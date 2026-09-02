import * as vscode from 'vscode';
import type { ILogger } from 'calcpad-frontend';
import {
    isOperatorTriggerChar,
    isInsideStringOrComment,
    findOperatorReplacement,
} from 'calcpad-frontend';

/**
 * Handles automatic replacement of C-style operators with Unicode equivalents.
 * Logic is provided by calcpad-frontend; this class handles VS Code event wiring.
 */
export class OperatorReplacer {
    private outputChannel: ILogger;

    constructor(outputChannel: ILogger) {
        this.outputChannel = outputChannel;
    }

    public async processTextChange(
        document: vscode.TextDocument,
        change: vscode.TextDocumentContentChangeEvent
    ): Promise<void> {
        if (change.text.length !== 1) {
            return;
        }

        const insertedChar = change.text;
        const position = change.range.start;

        if (!isOperatorTriggerChar(insertedChar)) {
            return;
        }

        const line = document.lineAt(position.line);
        const lineText = line.text;

        if (isInsideStringOrComment(lineText, position.character)) {
            return;
        }

        const replacement = findOperatorReplacement(lineText, position.character + 1);
        if (replacement) {
            await this.replaceOperator(document, position, replacement);
        }
    }

    private async replaceOperator(
        document: vscode.TextDocument,
        insertPosition: vscode.Position,
        replacement: { startPos: number; endPos: number; replacement: string }
    ): Promise<void> {
        const edit = new vscode.WorkspaceEdit();
        const range = new vscode.Range(
            insertPosition.line,
            replacement.startPos,
            insertPosition.line,
            replacement.endPos
        );
        edit.replace(document.uri, range, replacement.replacement);

        this.outputChannel.appendLine(
            `[OPERATOR REPLACE] ${document.lineAt(insertPosition.line).text.substring(replacement.startPos, replacement.endPos)} → ${replacement.replacement}`
        );

        await vscode.workspace.applyEdit(edit);
    }

    public registerDocumentChangeListener(context: vscode.ExtensionContext): vscode.Disposable {
        return vscode.workspace.onDidChangeTextDocument(async (event) => {
            if (event.document.languageId !== 'calcpad' && event.document.languageId !== 'plaintext') {
                return;
            }
            for (const change of event.contentChanges) {
                await this.processTextChange(event.document, change);
            }
        });
    }
}
