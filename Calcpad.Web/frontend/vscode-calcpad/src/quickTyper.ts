import * as vscode from 'vscode';
import type { ILogger } from 'calcpad-frontend';
import { findQuickTypeReplacement } from 'calcpad-frontend';
import type { CalcpadInsertManager } from './calcpadInsertManager';
import { CalcpadSettingsManager } from './calcpadSettings';

/**
 * Replaces quick typing shortcuts with Unicode symbols. The map is read from the
 * `quickTypes` setting when a space is typed, so edits apply at once.
 */
export class QuickTyper {
    private outputChannel: ILogger;
    private insertManager: CalcpadInsertManager;

    constructor(outputChannel: ILogger, insertManager: CalcpadInsertManager) {
        this.outputChannel = outputChannel;
        this.insertManager = insertManager;
    }

    public async processTextChange(
        document: vscode.TextDocument,
        change: vscode.TextDocumentContentChangeEvent
    ): Promise<void> {
        const enableQuickTyping = CalcpadSettingsManager.getInstance().getExtraBool('quickTyping', true);
        if (!enableQuickTyping) {
            return;
        }

        if (change.text.length !== 1 || change.text !== ' ') {
            return;
        }

        const position = change.range.start;
        const line = document.lineAt(position.line);
        const lineText = line.text;

        const replacement = findQuickTypeReplacement(lineText, position.character, this.insertManager.buildQuickTypeMap());
        if (replacement) {
            await this.replaceQuickType(document, position, replacement);
        }
    }

    private async replaceQuickType(
        document: vscode.TextDocument,
        insertPosition: vscode.Position,
        replacement: { startPos: number; endPos: number; replacement: string }
    ): Promise<void> {
        const range = new vscode.Range(
            insertPosition.line,
            replacement.startPos,
            insertPosition.line,
            insertPosition.character + 1
        );
        const shortcut = document.lineAt(insertPosition.line).text.substring(
            replacement.startPos,
            replacement.endPos
        );
        this.outputChannel.appendLine(`[QUICK TYPER] ${shortcut} → ${replacement.replacement}`);

        // Placeholders become tab stops, like an accepted completion.
        const snippet = this.insertManager.quickTypeSnippet(replacement.replacement);
        const editor = vscode.window.visibleTextEditors.find(e => e.document === document);
        if (snippet && editor) {
            await editor.insertSnippet(new vscode.SnippetString(snippet), range);
            return;
        }
        const edit = new vscode.WorkspaceEdit();
        edit.replace(document.uri, range, replacement.replacement.replace(/§/g, ''));
        await vscode.workspace.applyEdit(edit);
    }

    public registerDocumentChangeListener(context: vscode.ExtensionContext): vscode.Disposable {
        return vscode.workspace.onDidChangeTextDocument(async (event) => {
            if (event.document.languageId !== 'calcpad' && event.document.languageId !== 'plaintext') {
                return;
            }
            if (event.contentChanges.length === 1) {
                await this.processTextChange(event.document, event.contentChanges[0]);
            }
        });
    }
}
