import * as vscode from 'vscode';
import type { ILogger } from 'calcpad-frontend';
import * as path from 'path';
import {
    CalcpadLintService,
    CalcpadApiClient,
    LintDiagnostic,
} from 'calcpad-frontend';
import { CalcpadSettingsManager } from './calcpadSettings';

/**
 * VS Code wrapper around CalcpadLintService from calcpad-frontend.
 * Converts platform-agnostic LintDiagnostic[] into vscode.Diagnostic[]
 * and manages a DiagnosticCollection for the editor.
 */
export class CalcpadServerLinter {
    private diagnosticCollection: vscode.DiagnosticCollection;
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
            } else {
                this.diagnosticCollection.set(document.uri, []);
            }
        } catch (error) {
            this.logger.appendLine(
                '[Lint] Error: ' + (error instanceof Error ? error.message : 'Unknown error')
            );
            this.diagnosticCollection.set(document.uri, []);
        }
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
                const severity = d.severityId === 0
                    ? vscode.DiagnosticSeverity.Error
                    : d.severityId === 1
                        ? vscode.DiagnosticSeverity.Warning
                        : vscode.DiagnosticSeverity.Information;

                const diagnostic = new vscode.Diagnostic(
                    range,
                    '[Linter] [' + d.code + '] ' + d.message,
                    severity
                );
                diagnostic.code = d.code;
                diagnostic.source = d.source;
                diagnostic.relatedInformation = d.related?.map(r => new vscode.DiagnosticRelatedInformation(
                    new vscode.Location(uri, new vscode.Range(r.line, r.column, r.line, r.endColumn)),
                    r.message));
                return diagnostic;
            })
            .filter(d => d.severity <= minSeverity);
    }

    public dispose(): void {
        this.diagnosticCollection.clear();
        this.diagnosticCollection.dispose();
    }
}
