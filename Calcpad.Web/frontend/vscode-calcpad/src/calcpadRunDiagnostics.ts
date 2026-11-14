import * as vscode from 'vscode';
import { expandRunErrors } from 'calcpad-frontend';
import type { CalcpadError } from 'calcpad-frontend';

/** Shows each document's last-run errors in the Problems panel, beside the linter's. */
export class CalcpadRunDiagnostics implements vscode.Disposable {
    private readonly collection = vscode.languages.createDiagnosticCollection('calcpad-run');

    public update(uri: vscode.Uri, errors: CalcpadError[]): void {
        const document = vscode.workspace.textDocuments.find(d => d.uri.toString() === uri.toString());
        this.collection.set(uri, expandRunErrors(errors).map(err => {
            const diagnostic = new vscode.Diagnostic(
                this.range(document, err),
                err.code ? `[Runtime] [${err.code}] ${err.message}` : `[Runtime] ${err.message}`,
                vscode.DiagnosticSeverity.Error);
            diagnostic.code = err.code;
            if (err.related.length > 0) {
                diagnostic.relatedInformation = err.related.map(r => new vscode.DiagnosticRelatedInformation(
                    new vscode.Location(uri, this.lineRange(document, r.line, -1, -1)),
                    r.message));
            }
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
        const first = this.lineRange(document, err.sourceLine, err.startColumn ?? -1, err.endColumn ?? -1);
        const more = err.additionalRanges ?? [];
        if (more.length === 0)
            return first;
        const last = more[more.length - 1];
        return new vscode.Range(first.start, this.lineRange(document, last.sourceLine, last.startColumn, last.endColumn).end);
    }

    private lineRange(document: vscode.TextDocument | undefined, sourceLine: number, start: number, end: number): vscode.Range {
        const line = Math.max(sourceLine - 1, 0);
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
