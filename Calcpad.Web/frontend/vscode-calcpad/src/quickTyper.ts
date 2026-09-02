import * as vscode from 'vscode';
import type { ILogger } from 'calcpad-frontend';
import { findQuickTypeReplacement } from 'calcpad-frontend';
import type { CalcpadInsertManager } from './calcpadInsertManager';
import { CalcpadSettingsManager } from './calcpadSettings';

/**
 * Handles automatic replacement of quick typing shortcuts with Unicode symbols.
 * The quick type map is built dynamically from server-provided snippet data.
 */
export class QuickTyper {
    private outputChannel: ILogger;
    private insertManager: CalcpadInsertManager;
    private quickTypeMap: Map<string, string> = new Map();

    constructor(outputChannel: ILogger, insertManager: CalcpadInsertManager) {
        this.outputChannel = outputChannel;
        this.insertManager = insertManager;

        // Build map immediately if snippets are already loaded
        if (insertManager.isLoaded()) {
            this.rebuildMap();
        }

        // Rebuild when snippets load (or reload)
        insertManager.onSnippetsLoaded(() => this.rebuildMap());
    }

    private rebuildMap(): void {
        this.quickTypeMap = this.insertManager.buildQuickTypeMap();
        this.outputChannel.appendLine(
            '[QUICK TYPER] Loaded ' + this.quickTypeMap.size + ' quick type shortcuts from server'
        );
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

        if (this.quickTypeMap.size === 0) {
            return;
        }

        const position = change.range.start;
        const line = document.lineAt(position.line);
        const lineText = line.text;

        const replacement = findQuickTypeReplacement(lineText, position.character, this.quickTypeMap);
        if (replacement) {
            await this.replaceQuickType(document, position, replacement);
        }
    }

    private async replaceQuickType(
        document: vscode.TextDocument,
        insertPosition: vscode.Position,
        replacement: { startPos: number; endPos: number; replacement: string }
    ): Promise<void> {
        const edit = new vscode.WorkspaceEdit();
        const range = new vscode.Range(
            insertPosition.line,
            replacement.startPos,
            insertPosition.line,
            insertPosition.character + 1
        );
        edit.replace(document.uri, range, replacement.replacement);

        const shortcut = document.lineAt(insertPosition.line).text.substring(
            replacement.startPos,
            replacement.endPos
        );
        this.outputChannel.appendLine(`[QUICK TYPER] ${shortcut} → ${replacement.replacement}`);

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
