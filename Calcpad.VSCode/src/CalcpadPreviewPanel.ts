import * as vscode from 'vscode';
import * as path from 'path';
import { CalcpadRenderer, InputValue } from './CalcpadRenderer';

function getNonce(): string {
  let text = '';
  const chars = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789';
  for (let i = 0; i < 32; i++) {
    text += chars.charAt(Math.floor(Math.random() * chars.length));
  }
  return text;
}

// Collects directories that the webview is allowed to load images from: the
// extension media folder, all workspace folders, and the document folder plus a
// few ancestors (worksheets reference images via paths like ../../Images/...).
function computeRoots(extensionUri: vscode.Uri, sourceUri: vscode.Uri): vscode.Uri[] {
  const roots: vscode.Uri[] = [vscode.Uri.joinPath(extensionUri, 'media')];
  for (const folder of vscode.workspace.workspaceFolders ?? []) {
    roots.push(folder.uri);
  }
  let dir = path.dirname(sourceUri.fsPath);
  for (let i = 0; i < 6; i++) {
    roots.push(vscode.Uri.file(dir));
    const parent = path.dirname(dir);
    if (parent === dir) {
      break;
    }
    dir = parent;
  }
  return roots;
}

// One dynamic preview panel that can be retargeted to follow the active .cpd editor.
export class CalcpadPreviewPanel {
  public readonly panel: vscode.WebviewPanel;
  private readonly disposables: vscode.Disposable[] = [];
  private readonly rootPaths: string[];
  private units: string | undefined;
  private inputValues: InputValue[] = [];
  private renderToken = 0;
  public sourceUri: vscode.Uri;

  constructor(
    private readonly context: vscode.ExtensionContext,
    private readonly renderer: CalcpadRenderer,
    sourceUri: vscode.Uri,
    viewColumn: vscode.ViewColumn,
    private readonly onDispose: (panel: CalcpadPreviewPanel) => void,
    private readonly onActivated: (panel: CalcpadPreviewPanel) => void
  ) {
    this.sourceUri = sourceUri;
    const roots = computeRoots(context.extensionUri, sourceUri);
    this.rootPaths = roots.map((r) => r.fsPath);

    this.panel = vscode.window.createWebviewPanel(
      'calcpadPreview',
      this.title(),
      { viewColumn, preserveFocus: true },
      { enableScripts: true, retainContextWhenHidden: true, localResourceRoots: roots }
    );

    this.panel.webview.html = this.buildShell();
    this.panel.webview.onDidReceiveMessage((msg) => this.onMessage(msg), null, this.disposables);
    this.panel.onDidChangeViewState(
      (e) => {
        if (e.webviewPanel.active) {
          this.onActivated(this);
        }
      },
      null,
      this.disposables
    );
    this.panel.onDidDispose(() => this.dispose(), null, this.disposables);
  }

  private title(): string {
    return `Preview ${path.basename(this.sourceUri.fsPath)}`;
  }

  // True if this panel's localResourceRoots already cover the given document
  // (so it can be retargeted without recreating the webview).
  coversUri(uri: vscode.Uri): boolean {
    const dir = path.dirname(uri.fsPath);
    return this.rootPaths.some((root) => {
      const rel = path.relative(root, dir);
      return rel === '' || (!rel.startsWith('..') && !path.isAbsolute(rel));
    });
  }

  reveal(viewColumn?: vscode.ViewColumn): void {
    this.panel.reveal(viewColumn, true);
  }

  // Point this preview at a different document (clears the interactive state).
  retarget(uri: vscode.Uri): void {
    this.sourceUri = uri;
    this.units = undefined;
    this.inputValues = [];
    this.panel.title = this.title();
  }

  async update(sourceText: string): Promise<void> {
    const token = ++this.renderToken;
    try {
      const html = await this.renderer.render({
        sourcePath: this.sourceUri.fsPath,
        sourceText,
        units: this.units,
        inputValues: this.inputValues
      });
      if (token !== this.renderToken) {
        return;
      }
      this.panel.webview.postMessage({ type: 'update', html, imgBase: this.imgBase() });
    } catch (err) {
      if (token !== this.renderToken) {
        return;
      }
      const message = err instanceof Error ? err.message : String(err);
      this.panel.webview.postMessage({ type: 'error', message });
    }
  }

  private imgBase(): string {
    const docDir = vscode.Uri.file(path.dirname(this.sourceUri.fsPath));
    return this.panel.webview.asWebviewUri(docDir).toString() + '/';
  }

  private async onMessage(msg: any): Promise<void> {
    if (!msg || typeof msg.type !== 'string') {
      return;
    }
    if (msg.type === 'inputChange' || msg.type === 'unitChange') {
      this.inputValues = Array.isArray(msg.inputValues) ? msg.inputValues : [];
      this.units = typeof msg.units === 'string' && msg.units.length > 0 ? msg.units : this.units;
      const doc = await this.findOpenDocument();
      await this.update(doc?.getText() ?? '');
    }
  }

  private async findOpenDocument(): Promise<vscode.TextDocument | undefined> {
    const open = vscode.workspace.textDocuments.find((d) => d.uri.toString() === this.sourceUri.toString());
    if (open) {
      return open;
    }
    try {
      return await vscode.workspace.openTextDocument(this.sourceUri);
    } catch {
      return undefined;
    }
  }

  private buildShell(): string {
    const webview = this.panel.webview;
    const nonce = getNonce();
    const cssUri = webview.asWebviewUri(vscode.Uri.joinPath(this.context.extensionUri, 'media', 'calcpad.css'));
    const jqueryUri = webview.asWebviewUri(
      vscode.Uri.joinPath(this.context.extensionUri, 'media', 'jquery-3.6.3.min.js')
    );
    const mainUri = webview.asWebviewUri(vscode.Uri.joinPath(this.context.extensionUri, 'media', 'main.js'));

    const csp = [
      `default-src 'none'`,
      `img-src ${webview.cspSource} https: data:`,
      `font-src ${webview.cspSource}`,
      `style-src ${webview.cspSource} 'unsafe-inline'`,
      `script-src 'nonce-${nonce}' ${webview.cspSource}`
    ].join('; ');

    return `<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8">
  <meta http-equiv="Content-Security-Policy" content="${csp}">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <link rel="stylesheet" href="${cssUri}">
  <title>Calcpad Preview</title>
  <style>
    #cp-toolbar {
      position: sticky; top: 0; z-index: 10;
      display: flex; gap: 4px; padding: 4px 6px; margin: 0 0 6px 0;
      background: var(--vscode-editor-background);
      border-bottom: 1px solid var(--vscode-panel-border);
    }
    #cp-toolbar button {
      font: inherit; font-size: 12px; cursor: pointer;
      padding: 2px 10px; border: 1px solid var(--vscode-button-border, transparent);
      border-radius: 3px;
      color: var(--vscode-button-secondaryForeground);
      background: var(--vscode-button-secondaryBackground);
    }
    #cp-toolbar button.cp-active {
      color: var(--vscode-button-foreground);
      background: var(--vscode-button-background);
    }
    .calcpad-status { color: var(--vscode-descriptionForeground); padding: 1em; font-style: italic; }
    .calcpad-error { color: var(--vscode-errorForeground); padding: 1em; white-space: pre-wrap; }
  </style>
</head>
<body>
  <div id="cp-toolbar">
    <button id="cp-mode-interactive" title="Editable input fields, live recalculation">Interactive</button>
    <button id="cp-mode-final" title="Read-only calculated output">Final</button>
  </div>
  <div id="calcpad-root"><div class="calcpad-status">Rendering…</div></div>
  <script nonce="${nonce}" src="${jqueryUri}"></script>
  <script nonce="${nonce}" src="${mainUri}"></script>
</body>
</html>`;
  }

  dispose(): void {
    this.onDispose(this);
    this.panel.dispose();
    while (this.disposables.length) {
      this.disposables.pop()?.dispose();
    }
  }
}
