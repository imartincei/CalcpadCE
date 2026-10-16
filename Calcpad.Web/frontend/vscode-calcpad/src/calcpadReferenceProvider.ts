import * as vscode from 'vscode';
import type { ILogger } from 'calcpad-frontend';
import { CalcpadApiClient, SymbolAtPositionResponse } from 'calcpad-frontend';
import { VSCodeFileSystem } from './adapters';
import { resolveSymbolLocation } from './calcpadLocationResolver';

/**
 * Provides "Find All References" (Shift+F12) functionality for CalcPad
 * variables, custom functions, and macros. Server resolves the cursor to a
 * symbol and returns every occurrence in one round-trip.
 */
export class CalcpadReferenceProvider implements vscode.ReferenceProvider {
    private apiClient: CalcpadApiClient;
    private outputChannel: ILogger;
    private logger: ILogger;
    private fileSystem: VSCodeFileSystem;

    constructor(apiClient: CalcpadApiClient, outputChannel: ILogger) {
        this.apiClient = apiClient;
        this.outputChannel = outputChannel;
        this.logger = outputChannel;
        this.fileSystem = new VSCodeFileSystem();
    }

    async provideReferences(
        document: vscode.TextDocument,
        position: vscode.Position,
        context: vscode.ReferenceContext,
        token: vscode.CancellationToken
    ): Promise<vscode.Location[] | null> {
        const sym = await this.fetchSymbol(document, position);
        if (!sym) {
            this.outputChannel.appendLine('[References] No symbol at cursor position', 'verbose');
            return null;
        }

        this.outputChannel.appendLine('[References] Finding references for: ' + sym.symbolName, 'verbose');

        const filtered = context.includeDeclaration
            ? sym.locations
            : sym.locations.filter(loc => !loc.isAssignment);

        this.outputChannel.appendLine(`[References] Found ${filtered.length} reference(s) (${sym.locations.length} total)`, 'verbose');

        const results: vscode.Location[] = [];
        for (const loc of filtered) {
            const vsLoc = await resolveSymbolLocation(document, loc, this.fileSystem, this.outputChannel, '[References]');
            if (vsLoc) results.push(vsLoc);
        }

        return results;
    }

    private async fetchSymbol(document: vscode.TextDocument, position: vscode.Position): Promise<SymbolAtPositionResponse | null> {
        try {
            return await this.apiClient.symbolAtPosition(
                document.getText(),
                position.line,
                position.character,
                document.uri.fsPath,
            );
        } catch (error) {
            this.outputChannel.appendLine(
                '[References] Error resolving symbol: ' + (error instanceof Error ? error.message : 'Unknown error')
            , 'verbose');
            return null;
        }
    }

    static register(
        apiClient: CalcpadApiClient,
        outputChannel: ILogger
    ): vscode.Disposable {
        const provider = new CalcpadReferenceProvider(apiClient, outputChannel);
        return vscode.languages.registerReferenceProvider(
            { language: 'calcpad' },
            provider
        );
    }
}
