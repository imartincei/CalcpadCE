import * as vscode from 'vscode';
import type { ILogger } from 'calcpad-frontend';
import {
    CalcpadSnippetService,
    CalcpadApiClient,
    InsertItem,
    InsertDataTree,
    SnippetsLoadedCallback,
} from 'calcpad-frontend';

export type { InsertItem, InsertDataTree, SnippetsLoadedCallback };

/**
 * VS Code wrapper around CalcpadSnippetService from calcpad-frontend.
 * Instantiate with `new CalcpadInsertManager(apiClient, outputChannel)`.
 */
export class CalcpadInsertManager {
    private snippetService: CalcpadSnippetService;

    constructor(apiClient: CalcpadApiClient, outputChannel: ILogger) {
        const logger = outputChannel;
        this.snippetService = new CalcpadSnippetService(apiClient, logger);
    }

    public async loadSnippets(): Promise<void> {
        return this.snippetService.loadSnippets();
    }

    public getAllItems(): InsertItem[] {
        return this.snippetService.getAllItems();
    }

    public getInsertData(): InsertDataTree {
        return this.snippetService.getInsertData();
    }

    public searchItems(searchTerm: string): InsertItem[] {
        return this.snippetService.searchItems(searchTerm);
    }

    public async reloadSnippets(): Promise<void> {
        return this.snippetService.reloadSnippets();
    }

    public isLoaded(): boolean {
        return this.snippetService.isLoaded();
    }

    public onSnippetsLoaded(callback: SnippetsLoadedCallback): void {
        this.snippetService.onSnippetsLoaded(callback);
    }

    public buildQuickTypeMap(): Map<string, string> {
        return this.snippetService.buildQuickTypeMap();
    }

    public dispose(): void {
        this.snippetService.dispose();
    }
}
