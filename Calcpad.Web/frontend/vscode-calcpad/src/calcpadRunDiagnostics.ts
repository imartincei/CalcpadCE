import * as vscode from 'vscode';
import type { CalcpadError } from 'calcpad-frontend';

/** Shows each document's last-run errors in the Problems panel, beside the linter's. */
export class CalcpadRunDiagnostics implements vscode.Disposable {
    private readonly collection = vscode.languages.createDiagnosticCollection('calcpad-run');

    public update(uri: vscode.Uri, errors: CalcpadError[]): void {
        const document = vscode.workspace.textDocuments.find(d => d.uri.toString() === uri.toString());
        this.collection.set(uri, errors.map(err => {
            const diagnostic = new vscode.Diagnostic(
                this.range(document, err),
                err.code ? `[Runtime] [${err.code}] ${err.message}` : `[Runtime] ${err.message}`,
                vscode.DiagnosticSeverity.Error);
            diagnostic.code = err.code;
            return diagnostic;
        }));
    }

    public delete(uri: vscode.Uri): void {
        this.collection.delete(uri);
    }

    public dispose(): void {
        this.collection.dispose();
    }

    private range(document: vscode.TextDocument | undefined, err: CalcpadError): vscode.Range {
        const line = Math.max(err.sourceLine - 1, 0);
        const start = err.startColumn ?? -1;
        const end = err.endColumn ?? -1;
        if (!document) {
            return start >= 0 && end > start
                ? new vscode.Range(line, start, line, end)
                : new vscode.Range(line, 0, line, Number.MAX_SAFE_INTEGER);
        }
        const text = document.lineAt(Math.min(line, document.lineCount - 1));
        if (start >= 0 && end > start && end <= text.range.end.character)
            return new vscode.Range(text.lineNumber, start, text.lineNumber, end);
        return new vscode.Range(text.lineNumber, text.firstNonWhitespaceCharacterIndex, text.lineNumber, text.range.end.character);
    }
}
