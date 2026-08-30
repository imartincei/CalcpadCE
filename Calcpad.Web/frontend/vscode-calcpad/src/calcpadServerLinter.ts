import * as vscode from 'vscode';
import * as path from 'path';
import {
    CalcpadLintService,
    CalcpadApiClient,
    LintDiagnostic,
} from 'calcpad-frontend';
import { VSCodeLogger } from './adapters';
import { CalcpadSettingsManager } from './calcpadSettings';

/**
 * VS Code wrapper around CalcpadLintService from calcpad-frontend.
 * Converts platform-agnostic LintDiagnostic[] into vscode.Diagnostic[]
 * and manages a DiagnosticCollection for the editor.
 */
export class CalcpadServerLinter {
    private diagnosticCollection: vscode.DiagnosticCollection;
    private lintService: CalcpadLintService;
    private logger: VSCodeLogger;

    constructor(apiClient: CalcpadApiClient, debugChannel: vscode.OutputChannel) {
        this.diagnosticCollection = vscode.languages.createDiagnosticCollection('calcpad');
        this.logger = new VSCodeLogger(debugChannel);
        this.lintService = new CalcpadLintService(apiClient, this.logger);
    }

    public async lintDocument(document: vscode.TextDocument): Promise<void> {
        if (!document.fileName.endsWith('.cpd')) {
            this.diagnosticCollection.delete(document.uri);
            return;
        }

        const content = document.getText();

        try {
            const sourceFilePath = document.uri.fsPath;
            const lintResponse = await this.lintService.lintContent(content, sourceFilePath, document.uri.toString());

            if (lintResponse) {
                const diagnostics = this.convertToDiagnostics(lintResponse.diagnostics);
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

    private convertToDiagnostics(serverDiagnostics: LintDiagnostic[]): vscode.Diagnostic[] {
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
                    '[' + d.code + '] ' + d.message,
                    severity
                );
                diagnostic.code = d.code;
                diagnostic.source = d.source;
                return diagnostic;
            })
            .filter(d => d.severity <= minSeverity);
    }

    public dispose(): void {
        this.diagnosticCollection.clear();
        this.diagnosticCollection.dispose();
    }
}
