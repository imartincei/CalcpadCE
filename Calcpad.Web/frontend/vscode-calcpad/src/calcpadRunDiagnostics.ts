import * as vscode from 'vscode';
import type { CalcpadError } from 'calcpad-frontend';

/** Shows each document's last-run errors in the Problems panel, beside the linter's. */
export class CalcpadRunDiagnostics implements vscode.Disposable {
    private readonly collection = vscode.languages.createDiagnosticCollection('calcpad-run');

    public update(uri: vscode.Uri, errors: CalcpadError[]): void {
        const document = vscode.workspace.textDocuments.find(d => d.uri.toString() === uri.toString());
        this.collection.set(uri, errors.map(err => {
            const diagnostic = new vscode.Diagnostic(
                this.lineRange(document, err.sourceLine - 1),
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

    private lineRange(document: vscode.TextDocument | undefined, line: number): vscode.Range {
        if (!document) return new vscode.Range(Math.max(line, 0), 0, Math.max(line, 0), Number.MAX_SAFE_INTEGER);
        const text = document.lineAt(Math.min(Math.max(line, 0), document.lineCount - 1));
        return new vscode.Range(text.lineNumber, text.firstNonWhitespaceCharacterIndex, text.lineNumber, text.range.end.character);
    }
}
