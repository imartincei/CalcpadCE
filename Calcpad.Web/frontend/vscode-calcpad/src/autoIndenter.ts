import * as vscode from 'vscode';
import type { ILogger } from 'calcpad-frontend';
import {
    shouldIncreaseIndent,
    shouldDecreaseIndent,
    getIndentation,
    couldCompleteDedentKeyword,
    calculateExpectedIndent,
} from 'calcpad-frontend';

/**
 * Handles automatic indentation for Calcpad control blocks.
 * Logic is provided by calcpad-frontend; this class handles VS Code event wiring.
 */
export class AutoIndenter {
    private outputChannel: ILogger;

    constructor(outputChannel: ILogger) {
        this.outputChannel = outputChannel;
    }

    public async processTextChange(
        document: vscode.TextDocument,
        change: vscode.TextDocumentContentChangeEvent
    ): Promise<void> {
        if (!change.text.includes('\n')) {
            return;
        }

        const editor = vscode.window.activeTextEditor;
        if (!editor || editor.document !== document) {
            return;
        }

        const newLinePos = change.range.start.line + 1;
        if (newLinePos >= document.lineCount) {
            return;
        }

        const previousLineText = document.lineAt(change.range.start.line).text;
        const currentLineText = document.lineAt(newLinePos).text;

        const previousIndent = getIndentation(previousLineText);
        const indentUnit = this.getIndentUnit();

        let targetIndent = previousIndent;
        if (shouldIncreaseIndent(previousLineText)) {
            targetIndent = previousIndent + indentUnit;
        }

        const currentIndent = getIndentation(currentLineText);

        if (currentIndent !== targetIndent) {
            const edit = new vscode.WorkspaceEdit();
            const indentRange = new vscode.Range(
                newLinePos, 0,
                newLinePos, currentIndent.length
            );
            edit.replace(document.uri, indentRange, targetIndent);
            await vscode.workspace.applyEdit(edit);

            const newPosition = new vscode.Position(newLinePos, targetIndent.length);
            editor.selection = new vscode.Selection(newPosition, newPosition);
        }
    }

    public async processKeywordTyped(
        document: vscode.TextDocument,
        change: vscode.TextDocumentContentChangeEvent
    ): Promise<void> {
        const position = change.range.start;
        const lineText = document.lineAt(position.line).text;

        if (!shouldDecreaseIndent(lineText)) {
            return;
        }

        const alsoIncreases = shouldIncreaseIndent(lineText);
        const expectedIndent = calculateExpectedIndent(
            position.line,
            (i) => document.lineAt(i).text,
            alsoIncreases
        );
        const currentIndent = getIndentation(lineText);

        if (currentIndent !== expectedIndent) {
            const editor = vscode.window.activeTextEditor;
            if (!editor || editor.document !== document) {
                return;
            }

            const edit = new vscode.WorkspaceEdit();
            const indentRange = new vscode.Range(
                position.line, 0,
                position.line, currentIndent.length
            );
            edit.replace(document.uri, indentRange, expectedIndent);
            await vscode.workspace.applyEdit(edit);

            this.outputChannel.appendLine('[AUTO-INDENT] Adjusted indent for: ' + lineText.trim());
        }
    }

    private getIndentUnit(): string {
        const editor = vscode.window.activeTextEditor;
        if (editor) {
            const tabSize = editor.options.tabSize as number || 4;
            const insertSpaces = editor.options.insertSpaces as boolean;
            if (insertSpaces) {
                return ' '.repeat(tabSize);
            } else {
                return '\t';
            }
        }
        return '    ';
    }

    public registerDocumentChangeListener(_context: vscode.ExtensionContext): vscode.Disposable {
        return vscode.workspace.onDidChangeTextDocument(async (event) => {
            if (event.document.languageId !== 'calcpad') {
                return;
            }
            for (const change of event.contentChanges) {
                if (change.text.includes('\n')) {
                    await this.processTextChange(event.document, change);
                } else if (change.text.length > 0) {
                    const lineText = event.document.lineAt(change.range.start.line).text;
                    if (couldCompleteDedentKeyword(lineText)) {
                        await this.processKeywordTyped(event.document, change);
                    }
                }
            }
        });
    }
}
