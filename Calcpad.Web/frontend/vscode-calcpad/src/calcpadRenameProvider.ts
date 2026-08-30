import * as vscode from 'vscode';
import { CalcpadApiClient, SymbolAtPositionResponse } from 'calcpad-frontend';
import { VSCodeLogger, VSCodeFileSystem } from './adapters';

/**
 * Provides "Rename Symbol" (F2) for CalcPad variables, functions, and macros by asking the
 * server for the symbol at the cursor and rewriting every local occurrence. Cross-file rename
 * is intentionally rejected — users must rename inside the include file itself.
 */
export class CalcpadRenameProvider implements vscode.RenameProvider {
    private apiClient: CalcpadApiClient;
    private outputChannel: vscode.OutputChannel;
    private logger: VSCodeLogger;
    private fileSystem: VSCodeFileSystem;

    constructor(apiClient: CalcpadApiClient, outputChannel: vscode.OutputChannel) {
        this.apiClient = apiClient;
        this.outputChannel = outputChannel;
        this.logger = new VSCodeLogger(outputChannel);
        this.fileSystem = new VSCodeFileSystem();
    }

    async prepareRename(
        document: vscode.TextDocument,
        position: vscode.Position,
        token: vscode.CancellationToken
    ): Promise<vscode.Range | { range: vscode.Range; placeholder: string }> {
        const sym = await this.fetchSymbol(document, position);
        if (!sym) {
            throw new Error('No renameable symbol found at cursor position');
        }

        const anchor = this.anchorAtCursor(sym, position);
        if (!anchor) {
            throw new Error(`'${sym.symbolName}' is defined in an include file and cannot be renamed here`);
        }

        const range = new vscode.Range(
            new vscode.Position(anchor.line, anchor.column),
            new vscode.Position(anchor.line, anchor.column + anchor.length),
        );
        return { range, placeholder: sym.symbolName };
    }

    async provideRenameEdits(
        document: vscode.TextDocument,
        position: vscode.Position,
        newName: string,
        token: vscode.CancellationToken
    ): Promise<vscode.WorkspaceEdit | null> {
        const sym = await this.fetchSymbol(document, position);
        if (!sym) {
            this.outputChannel.appendLine('[Rename] No symbol at cursor position');
            return null;
        }
        if (sym.symbolName === newName) return null;

        this.outputChannel.appendLine(`[Rename] Renaming '${sym.symbolName}' to '${newName}'`);

        const localLocations = sym.locations.filter(loc => loc.source === 'local');
        this.outputChannel.appendLine(`[Rename] Found ${localLocations.length} local occurrence(s) (${sym.locations.length} total)`);

        const edit = new vscode.WorkspaceEdit();
        for (const loc of localLocations) {
            const range = new vscode.Range(
                new vscode.Position(loc.line, loc.column),
                new vscode.Position(loc.line, loc.column + loc.length),
            );
            edit.replace(document.uri, range, newName);
        }
        return edit;
    }

    /** Local occurrence covering the cursor, or the first local occurrence as fallback. */
    private anchorAtCursor(sym: SymbolAtPositionResponse, position: vscode.Position) {
        const line = position.line;
        const col = position.character;
        return sym.locations.find(l =>
            l.source === 'local' &&
            l.line === line &&
            col >= l.column &&
            col <= l.column + l.length,
        ) ?? sym.locations.find(l => l.source === 'local');
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
                '[Rename] Error resolving symbol: ' + (error instanceof Error ? error.message : 'Unknown error')
            );
            return null;
        }
    }

    static register(
        apiClient: CalcpadApiClient,
        outputChannel: vscode.OutputChannel
    ): vscode.Disposable {
        const provider = new CalcpadRenameProvider(apiClient, outputChannel);
        return vscode.languages.registerRenameProvider(
            { language: 'calcpad' },
            provider
        );
    }
}
