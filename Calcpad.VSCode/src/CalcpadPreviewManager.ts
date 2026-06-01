import * as vscode from 'vscode';
import * as path from 'path';
import { CalcpadRenderer, ExportFormat } from './CalcpadRenderer';
import { CalcpadPreviewPanel, ExportState } from './CalcpadPreviewPanel';

function isCalcpad(doc: vscode.TextDocument): boolean {
  return doc.languageId === 'calcpad' || /\.cpdz?$/i.test(doc.uri.fsPath);
}

// Owns the shared renderer and a single dynamic preview panel that follows the
// active .cpd editor (like the built-in Markdown preview).
export class CalcpadPreviewManager implements vscode.Disposable {
  private readonly renderer: CalcpadRenderer;
  private preview: CalcpadPreviewPanel | undefined;
  private previewColumn: vscode.ViewColumn = vscode.ViewColumn.Beside;
  private debounceTimer: NodeJS.Timeout | undefined;

  constructor(private readonly context: vscode.ExtensionContext) {
    this.renderer = new CalcpadRenderer(context);
  }

  openPreview(uri: vscode.Uri, options: { side: boolean }): void {
    this.previewColumn = options.side ? vscode.ViewColumn.Beside : vscode.ViewColumn.Active;
    this.show(uri, true);
  }

  // Follow the active editor: retarget the existing preview to the newly focused .cpd.
  onActiveEditorChanged(editor: vscode.TextEditor | undefined): void {
    if (!this.preview || !editor || !isCalcpad(editor.document)) {
      return;
    }
    if (editor.document.uri.toString() !== this.preview.sourceUri.toString()) {
      this.show(editor.document.uri, false);
    }
  }

  onSourceChanged(doc: vscode.TextDocument, immediate = false): void {
    if (!this.preview || doc.uri.toString() !== this.preview.sourceUri.toString()) {
      return;
    }
    const config = vscode.workspace.getConfiguration('calcpad');
    if (config.get<boolean>('preview.updateOnSaveOnly') && !immediate) {
      return;
    }
    if (immediate) {
      void this.renderNow();
      return;
    }
    const delay = config.get<number>('preview.debounceMs') ?? 300;
    if (this.debounceTimer) {
      clearTimeout(this.debounceTimer);
    }
    this.debounceTimer = setTimeout(() => {
      this.debounceTimer = undefined;
      void this.renderNow();
    }, delay);
  }

  // Show `uri` in the preview, creating/retargeting/recreating the panel as needed.
  private show(uri: vscode.Uri, reveal: boolean): void {
    if (this.preview) {
      if (this.preview.sourceUri.toString() === uri.toString()) {
        if (reveal) {
          this.preview.reveal(this.previewColumn);
        }
        void this.renderNow();
        return;
      }
      if (this.preview.coversUri(uri)) {
        this.preview.retarget(uri);
        if (reveal) {
          this.preview.reveal(this.previewColumn);
        }
        void this.renderNow();
        return;
      }
      // Roots don't cover the new document — recreate the panel.
      this.preview.dispose();
      this.preview = undefined;
    }

    this.preview = new CalcpadPreviewPanel(
      this.context,
      this.renderer,
      uri,
      this.previewColumn,
      () => {
        this.preview = undefined;
      },
      () => {
        /* activated hook (unused) */
      },
      (exportUri, format, state) => void this.doExport(exportUri, format, state)
    );
    void this.renderNow();
  }

  // Export command entry point: uses the preview's current state when it targets
  // the same document, so the exported file matches what is shown.
  async exportCommand(uri: vscode.Uri | undefined, format: ExportFormat): Promise<void> {
    const target = uri ?? vscode.window.activeTextEditor?.document.uri;
    if (!target) {
      void vscode.window.showWarningMessage('Open a .cpd file to export.');
      return;
    }
    const state: ExportState =
      this.preview && this.preview.sourceUri.toString() === target.toString()
        ? this.preview.exportState
        : { inputValues: [], units: undefined };
    await this.doExport(target, format, state);
  }

  private async doExport(uri: vscode.Uri, format: ExportFormat, state: ExportState): Promise<void> {
    const ext = format === 'docx' ? 'docx' : 'html';
    const defaultUri = vscode.Uri.file(uri.fsPath.replace(/\.[^.\\/]+$/, '') + '.' + ext);
    const filters: Record<string, string[]> =
      format === 'docx' ? { 'Word document': ['docx'] } : { HTML: ['html', 'htm'] };
    const target = await vscode.window.showSaveDialog({ defaultUri, filters });
    if (!target) {
      return;
    }

    const doc = vscode.workspace.textDocuments.find((d) => d.uri.toString() === uri.toString());
    const sourceText = doc ? doc.getText() : (await vscode.workspace.openTextDocument(uri)).getText();

    try {
      await this.renderer.export({
        sourcePath: uri.fsPath,
        sourceText,
        units: state.units,
        inputValues: state.inputValues,
        export: { format, outPath: target.fsPath }
      });
      const open = 'Open';
      const reveal = 'Reveal in Explorer';
      const choice = await vscode.window.showInformationMessage(
        `Calcpad: exported to ${path.basename(target.fsPath)}`,
        open,
        reveal
      );
      if (choice === open) {
        void vscode.env.openExternal(target);
      } else if (choice === reveal) {
        void vscode.commands.executeCommand('revealFileInOS', target);
      }
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err);
      void vscode.window.showErrorMessage(`Calcpad export failed: ${message}`);
    }
  }

  private async renderNow(): Promise<void> {
    const panel = this.preview;
    if (!panel) {
      return;
    }
    const uri = panel.sourceUri;
    const doc = vscode.workspace.textDocuments.find((d) => d.uri.toString() === uri.toString());
    const text = doc ? doc.getText() : (await vscode.workspace.openTextDocument(uri)).getText();
    await panel.update(text);
  }

  dispose(): void {
    if (this.debounceTimer) {
      clearTimeout(this.debounceTimer);
    }
    this.preview?.dispose();
    this.preview = undefined;
    this.renderer.dispose();
  }
}
