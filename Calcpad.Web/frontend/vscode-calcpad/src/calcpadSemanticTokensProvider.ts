import * as vscode from 'vscode';
import * as path from 'path';
import {
    CalcpadApiClient,
    CalcpadTokenType,
    SEMANTIC_TOKEN_TYPES,
    mapTokenTypeToIndex,
    truncateBase64Content,
} from 'calcpad-frontend';
import { VSCodeLogger, VSCodeFileSystem } from './adapters';

const SEMANTIC_TOKEN_MODIFIERS: string[] = [];

export const semanticTokensLegend = new vscode.SemanticTokensLegend(
    SEMANTIC_TOKEN_TYPES,
    SEMANTIC_TOKEN_MODIFIERS
);

/**
 * Semantic token provider that fetches tokens from the Calcpad server
 * via CalcpadApiClient from calcpad-frontend.
 */
export class CalcpadSemanticTokensProvider implements vscode.DocumentSemanticTokensProvider {
    private debugChannel: vscode.OutputChannel;
    private apiClient: CalcpadApiClient;
    private fileSystem = new VSCodeFileSystem();
    private requestId = 0;
    private _onDidChangeSemanticTokens = new vscode.EventEmitter<void>();
    public readonly onDidChangeSemanticTokens = this._onDidChangeSemanticTokens.event;

    constructor(apiClient: CalcpadApiClient, debugChannel: vscode.OutputChannel) {
        this.apiClient = apiClient;
        this.debugChannel = debugChannel;
    }

    public refresh(): void {
        this.debugChannel.appendLine('[Highlight] Manual refresh triggered');
        this._onDidChangeSemanticTokens.fire();
    }

    async provideDocumentSemanticTokens(
        document: vscode.TextDocument,
        cancellationToken: vscode.CancellationToken
    ): Promise<vscode.SemanticTokens | null> {
        const content = document.getText();
        const reqId = ++this.requestId;
        const startTime = Date.now();

        this.debugChannel.appendLine('[Highlight #' + reqId + '] Request started for ' + document.fileName + ' (' + content.length + ' chars)');

        if (!content.trim()) {
            this.debugChannel.appendLine('[Highlight #' + reqId + '] Skipped - empty document');
            return null;
        }

        try {
            const truncatedContent = truncateBase64Content(content);
            const sourceFilePath = document.uri.fsPath;
            const tokens = await this.apiClient.highlight(truncatedContent, false, sourceFilePath, {
                key: `highlight:${document.uri.toString()}`,
            });

            if (cancellationToken.isCancellationRequested) {
                this.debugChannel.appendLine('[Highlight #' + reqId + '] Cancelled after ' + (Date.now() - startTime) + 'ms');
                return null;
            }

            if (!tokens) {
                this.debugChannel.appendLine('[Highlight #' + reqId + '] No tokens returned after ' + (Date.now() - startTime) + 'ms');
                return null;
            }

            this.debugChannel.appendLine('[Highlight #' + reqId + '] Received ' + tokens.length + ' tokens in ' + (Date.now() - startTime) + 'ms');

            const builder = new vscode.SemanticTokensBuilder(semanticTokensLegend);

            tokens.sort((a, b) => {
                if (a.line !== b.line) {
                    return a.line - b.line;
                }
                return a.column - b.column;
            });

            let validCount = 0;
            for (const tok of tokens) {
                if (tok.typeId === CalcpadTokenType.None) {
                    continue;
                }
                const tokenType = mapTokenTypeToIndex(tok.typeId);
                if (tokenType >= 0) {
                    builder.push(tok.line, tok.column, tok.length, tokenType, 0);
                    validCount++;
                }
            }

            this.debugChannel.appendLine('[Highlight #' + reqId + '] Built ' + validCount + ' semantic tokens, total time: ' + (Date.now() - startTime) + 'ms');
            return builder.build();
        } catch (error) {
            this.debugChannel.appendLine('[Highlight #' + reqId + '] Error after ' + (Date.now() - startTime) + 'ms: ' + (error instanceof Error ? error.message : 'Unknown error'));
            return null;
        }
    }
}
