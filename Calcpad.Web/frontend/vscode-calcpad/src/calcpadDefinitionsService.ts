import * as vscode from 'vscode';
import * as path from 'path';
import {
    CalcpadDefinitionsService as FrontendDefinitionsService,
    CalcpadApiClient,
    DefinitionsResponse,
    ResolvedPathRoots,
} from 'calcpad-frontend';
import { VSCodeLogger, VSCodeFileSystem } from './adapters';

/**
 * VS Code wrapper around CalcpadDefinitionsService from calcpad-frontend.
 * Adapts the platform-agnostic definitions service for use with
 * vscode.TextDocument and vscode.OutputChannel.
 */
export class CalcpadDefinitionsService {
    private definitionsService: FrontendDefinitionsService;
    private logger: VSCodeLogger;
    private fileSystem: VSCodeFileSystem;

    constructor(apiClient: CalcpadApiClient, debugChannel: vscode.OutputChannel) {
        this.logger = new VSCodeLogger(debugChannel);
        this.fileSystem = new VSCodeFileSystem();
        this.definitionsService = new FrontendDefinitionsService(apiClient, this.logger);
    }

    public getCachedDefinitions(documentUri: string): DefinitionsResponse | undefined {
        return this.definitionsService.getCachedDefinitions(documentUri);
    }

    public getCachedPathRoots(documentUri: string): ResolvedPathRoots {
        return this.definitionsService.getCachedPathRoots(documentUri);
    }

    public async refreshDefinitions(document: vscode.TextDocument): Promise<DefinitionsResponse | null> {
        const content = document.getText();

        try {
            return await this.definitionsService.refreshDefinitions(
                content, document.uri.toString(), document.uri.fsPath
            );
        } catch (error) {
            this.logger.appendLine(
                '[Definitions] Error: ' + (error instanceof Error ? error.message : 'Unknown error')
            );
            return null;
        }
    }
}
