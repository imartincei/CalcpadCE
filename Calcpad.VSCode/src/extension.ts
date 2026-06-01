import * as vscode from 'vscode';
import { CalcpadPreviewManager } from './CalcpadPreviewManager';

let manager: CalcpadPreviewManager | undefined;

export function activate(context: vscode.ExtensionContext): void {
  manager = new CalcpadPreviewManager(context);

  const resolveUri = (arg?: vscode.Uri): vscode.Uri | undefined => {
    if (arg instanceof vscode.Uri) {
      return arg;
    }
    return vscode.window.activeTextEditor?.document.uri;
  };

  context.subscriptions.push(
    vscode.commands.registerCommand('calcpad.showPreview', (arg?: vscode.Uri) => {
      const uri = resolveUri(arg);
      if (uri) {
        manager!.openPreview(uri, { side: false });
      }
    }),
    vscode.commands.registerCommand('calcpad.showPreviewToSide', (arg?: vscode.Uri) => {
      const uri = resolveUri(arg);
      if (uri) {
        manager!.openPreview(uri, { side: true });
      }
    }),
    vscode.commands.registerCommand('calcpad.exportHtml', (arg?: vscode.Uri) => {
      void manager!.exportCommand(resolveUri(arg), 'html');
    }),
    vscode.commands.registerCommand('calcpad.exportDocx', (arg?: vscode.Uri) => {
      void manager!.exportCommand(resolveUri(arg), 'docx');
    }),
    vscode.workspace.onDidChangeTextDocument((e) => {
      if (e.document.languageId === 'calcpad' || e.document.uri.fsPath.endsWith('.cpd')) {
        manager!.onSourceChanged(e.document);
      }
    }),
    vscode.workspace.onDidSaveTextDocument((doc) => {
      if (doc.languageId === 'calcpad' || doc.uri.fsPath.endsWith('.cpd')) {
        manager!.onSourceChanged(doc, true);
      }
    }),
    vscode.window.onDidChangeActiveTextEditor((editor) => {
      manager!.onActiveEditorChanged(editor);
    })
  );
}

export function deactivate(): void {
  manager?.dispose();
  manager = undefined;
}
