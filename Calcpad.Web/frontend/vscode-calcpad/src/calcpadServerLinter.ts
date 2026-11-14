import * as vscode from 'vscode';
import type { ILogger } from 'calcpad-frontend';
import * as path from 'path';
import {
    CalcpadLintService,
    CalcpadApiClient,
    LintDiagnostic,
    IncludedLintStore,
    includePathKey,
} from 'calcpad-frontend';
import { CalcpadSettingsManager } from './calcpadSettings';

/**
 * VS Code wrapper around CalcpadLintService from calcpad-frontend.
 * Converts platform-agnostic LintDiagnostic[] into vscode.Diagnostic[]
 * and manages a DiagnosticCollection for the editor.
 */
export class CalcpadServerLinter {
    private diagnosticCollection: vscode.DiagnosticCollection;
    private includedCollection = vscode.languages.createDiagnosticCollection('calcpad-included');
    private included = new IncludedLintStore();
    private lintService: CalcpadLintService;
    private logger: ILogger;

    constructor(apiClient: CalcpadApiClient, debugChannel: ILogger) {
        this.diagnosticCollection = vscode.languages.createDiagnosticCollection('calcpad');
        this.logger = debugChannel;
        this.lintService = new CalcpadLintService(apiClient, this.logger);
    }

    public async lintDocument(document: vscode.TextDocument, useTrace = false): Promise<void> {
        if (!document.fileName.endsWith('.cpd')) {
            this.diagnosticCollection.delete(document.uri);
            return;
        }

        const content = document.getText();

        try {
            const sourceFilePath = document.uri.fsPath;
            const lintResponse = await this.lintService.lintContent(content, sourceFilePath, `lint:${document.uri.toString()}`, useTrace);

            if (lintResponse) {
                const diagnostics = this.convertToDiagnostics(lintResponse.diagnostics, document.uri);
                this.diagnosticCollection.set(document.uri, diagnostics);
                this.included.set(document.uri.toString(), sourceFilePath,
                    [...lintResponse.diagnostics, ...lintResponse.includeOnly ?? []]);
            } else {
                this.diagnosticCollection.set(document.uri, []);
                this.included.delete(document.uri.toString());
            }
        } catch (error) {
            this.logger.appendLine(
                '[Lint] Error: ' + (error instanceof Error ? error.message : 'Unknown error')
            );
            this.diagnosticCollection.set(document.uri, []);
            this.included.delete(document.uri.toString());
        }
        this.publishIncluded();
    }

    /** Where an error this document reports at `position` is, in an included file named like `sourceFile`. */
    public includedErrorAt(uri: vscode.Uri, position: vscode.Position, sourceFile: string): vscode.Uri | undefined {
        const error = this.included.errorAt(uri.toString(), position.line, position.character, sourceFile);
        return error && vscode.Uri.file(error.filePath).with({ fragment: `L${error.line + 1},${Math.max(error.column, 0) + 1}` });
    }

    public forgetDocument(uri: vscode.Uri): void {
        this.included.delete(uri.toString());
        this.publishIncluded();
    }

    /** Shows each open base file's errors in the files it includes that are open in tabs. */
    public publishIncluded(): void {
        const open = new Map<string, vscode.Uri>();
        for (const group of vscode.window.tabGroups.all)
            for (const tab of group.tabs)
                if (tab.input instanceof vscode.TabInputText && tab.input.uri.scheme === 'file')
                    open.set(includePathKey(tab.input.uri.fsPath), tab.input.uri);
        const openBases = new Set([...open.values()].map(uri => uri.toString()));
        const minSeverity = this.getMinimumSeverity();

        this.includedCollection.clear();
        for (const uri of open.values()) {
            const document = vscode.workspace.textDocuments.find(d => d.uri.toString() === uri.toString());
            const diagnostics = this.included.get(uri.fsPath, base => openBases.has(base) && base !== uri.toString())
                .map(d => {
                    const diagnostic = new vscode.Diagnostic(
                        this.includedRange(document, d.line, d.column, d.endColumn),
                        '[Linter] [' + d.code + '] ' + d.message,
                        this.toSeverity(d.severityId)
                    );
                    diagnostic.code = d.code;
                    diagnostic.source = d.source;
                    const base = vscode.Uri.parse(d.base);
                    diagnostic.relatedInformation = d.related.map(r => new vscode.DiagnosticRelatedInformation(
                        new vscode.Location(base, new vscode.Range(r.line, r.column, r.line, r.endColumn)),
                        r.message));
                    return diagnostic;
                })
                .filter(d => d.severity <= minSeverity);
            if (diagnostics.length > 0) this.includedCollection.set(uri, diagnostics);
        }
    }

    private includedRange(document: vscode.TextDocument | undefined, line: number, column: number, endColumn: number): vscode.Range {
        if (column >= 0) return new vscode.Range(line, column, line, endColumn);
        if (!document || line >= document.lineCount) return new vscode.Range(line, 0, line, Number.MAX_SAFE_INTEGER);
        const text = document.lineAt(line);
        return new vscode.Range(line, text.firstNonWhitespaceCharacterIndex, line, text.range.end.character);
    }

    private toSeverity(severityId: number): vscode.DiagnosticSeverity {
        return severityId === 0
            ? vscode.DiagnosticSeverity.Error
            : severityId === 1
                ? vscode.DiagnosticSeverity.Warning
                : vscode.DiagnosticSeverity.Information;
    }

    private getMinimumSeverity(): vscode.DiagnosticSeverity {
        const level = CalcpadSettingsManager.getInstance().getExtra('linterMinSeverity', 'information');
        switch (level) {
            case 'error': return vscode.DiagnosticSeverity.Error;
            case 'warning': return vscode.DiagnosticSeverity.Warning;
            default: return vscode.DiagnosticSeverity.Information;
        }
    }

    private convertToDiagnostics(serverDiagnostics: LintDiagnostic[], uri: vscode.Uri): vscode.Diagnostic[] {
        const minSeverity = this.getMinimumSeverity();

        return serverDiagnostics
            .map(d => {
                const range = new vscode.Range(d.line, d.column, d.line, d.endColumn);
                const severity = this.toSeverity(d.severityId);

                const diagnostic = new vscode.Diagnostic(
                    range,
                    '[Linter] [' + d.code + '] ' + d.message,
                    severity
                );
                diagnostic.code = d.code;
                diagnostic.source = d.source;
                diagnostic.relatedInformation = [
                    ...d.related?.map(r => new vscode.DiagnosticRelatedInformation(
                        new vscode.Location(uri, new vscode.Range(r.line, r.column, r.line, r.endColumn)),
                        r.message)) ?? [],
                    ...d.includeOrigins?.map(o => new vscode.DiagnosticRelatedInformation(
                        new vscode.Location(vscode.Uri.file(o.filePath),
                            new vscode.Range(o.line, Math.max(o.column, 0), o.line, Math.max(o.endColumn, 0))),
                        'Error in the included file')) ?? [],
                ];
                return diagnostic;
            })
            .filter(d => d.severity <= minSeverity);
    }

    public dispose(): void {
        this.diagnosticCollection.clear();
        this.diagnosticCollection.dispose();
        this.includedCollection.dispose();
    }
}
