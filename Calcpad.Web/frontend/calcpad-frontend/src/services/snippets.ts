import type { CalcpadApiClient } from '../api/client';
import type { ILogger } from '../types/interfaces';
import type {
    SnippetDto,
    InsertItem,
    InsertDataTree,
    SnippetsLoadedCallback,
} from '../types/snippets';
import type { UserCustomizations } from './customizations';
import { DEFAULT_QUICK_TYPES, applySnippetCustomizations, buildQuickTypeMap } from './customizations';
import { buildInsertSnippet, hasSnippetPlaceholders } from '../text/snippet-insert';

/**
 * Manages loading, caching, and searching of CalcPad snippets/insert items.
 * Adapted from CalcpadInsertManager with ILogger replacing vscode.OutputChannel.
 */
export class CalcpadSnippetService {
    private apiClient: CalcpadApiClient;
    private logger?: ILogger;
    private _allItems: InsertItem[] = [];
    private _insertDataTree: InsertDataTree = {};
    private _isLoaded: boolean = false;
    private _loadPromise: Promise<void> | null = null;
    private _retryInterval: ReturnType<typeof setInterval> | null = null;
    private _onSnippetsLoadedCallbacks: SnippetsLoadedCallback[] = [];
    private _customizationsProvider: (() => UserCustomizations) | null = null;
    private static readonly RETRY_INTERVAL_MS = 3000;

    constructor(apiClient: CalcpadApiClient, logger?: ILogger) {
        this.apiClient = apiClient;
        this.logger = logger;
    }

    /**
     * Load snippets from the server, reusing the existing promise when called again. Starts a
     * background retry if the initial load fails.
     */
    public async loadSnippets(): Promise<void> {
        if (this._isLoaded) return;
        if (this._loadPromise) return this._loadPromise;

        this._loadPromise = this.fetchSnippetsFromServer();

        try {
            await this._loadPromise;
        } catch {
            this.startRetryInterval();
        }
    }

    private startRetryInterval(): void {
        if (this._retryInterval) return;

        this.log('Starting background retry every ' + CalcpadSnippetService.RETRY_INTERVAL_MS + 'ms');

        this._retryInterval = setInterval(async () => {
            if (this._isLoaded) {
                this.stopRetryInterval();
                return;
            }

            this.log('Retrying snippet fetch...');
            this._loadPromise = null;

            try {
                await this.fetchSnippetsFromServer();
                this.log('Retry successful - snippets loaded');
                this.stopRetryInterval();
            } catch {
                // Will retry on next interval
            }
        }, CalcpadSnippetService.RETRY_INTERVAL_MS);
    }

    private stopRetryInterval(): void {
        if (this._retryInterval) {
            clearInterval(this._retryInterval);
            this._retryInterval = null;
            this.log('Stopped background retry');
        }
    }

    private async fetchSnippetsFromServer(): Promise<void> {
        const response = await this.apiClient.snippets();
        if (!response) {
            this._loadPromise = null;
            throw new Error('Failed to fetch snippets');
        }

        const { snippets } = response;

        this.log('Received ' + snippets.length + ' snippets from server');

        this._allItems = snippets.map(snippet => this.convertSnippetToInsertItem(snippet));
        this._insertDataTree = this.buildTreeFromSnippets(snippets);
        this._isLoaded = true;

        this.log('Snippets loaded and cached successfully');
        this.notifySnippetsLoaded();
    }

    public onSnippetsLoaded(callback: SnippetsLoadedCallback): void {
        this._onSnippetsLoadedCallbacks.push(callback);
    }

    private notifySnippetsLoaded(): void {
        for (const callback of this._onSnippetsLoadedCallbacks) {
            try {
                callback();
            } catch (error) {
                this.log('Error in snippets loaded callback: ' + (error instanceof Error ? error.message : String(error)));
            }
        }
    }

    /** Supplies the user's custom quick types and snippets for the quick-type map. */
    public setCustomizationsProvider(provider: () => UserCustomizations): void {
        this._customizationsProvider = provider;
    }

    public dispose(): void {
        this.stopRetryInterval();
    }

    private convertSnippetToInsertItem(snippet: SnippetDto): InsertItem {
        return {
            tag: snippet.insert,
            label: snippet.label,
            description: snippet.description,
            documentation: snippet.documentation,
            example: snippet.example,
            categoryPath: snippet.category.replace(/\//g, ' > '),
            quickType: snippet.quickType,
            keywordType: snippet.keywordType,
            returnType: snippet.returnType,
            returnTypeDescription: snippet.returnTypeDescription,
            isElementWise: snippet.isElementWise,
            acceptsAnyCount: snippet.acceptsAnyCount,
            parameters: snippet.parameters
        };
    }

    private buildTreeFromSnippets(snippets: SnippetDto[]): InsertDataTree {
        const tree: InsertDataTree = {};

        for (const snippet of snippets) {
            const parts = snippet.category.split('/');
            let current: InsertDataTree = tree;

            for (let i = 0; i < parts.length; i++) {
                const part = parts[i];
                const isLast = i === parts.length - 1;

                if (isLast) {
                    if (!current[part]) {
                        current[part] = [];
                    }
                    const items = current[part];
                    if (Array.isArray(items)) {
                        items.push(this.convertSnippetToInsertItem(snippet));
                    }
                } else {
                    if (!current[part]) {
                        current[part] = {};
                    }
                    const next = current[part];
                    if (!Array.isArray(next)) {
                        current = next;
                    }
                }
            }
        }

        return tree;
    }

    public isLoaded(): boolean {
        return this._isLoaded;
    }

    public getCustomizations(): UserCustomizations | null {
        return this._customizationsProvider?.() ?? null;
    }

    public getAllItems(): InsertItem[] {
        return [...this._allItems];
    }

    public getInsertData(): InsertDataTree {
        return this._insertDataTree;
    }

    public searchItems(searchTerm: string): InsertItem[] {
        if (!searchTerm.trim()) return [];

        const term = searchTerm.toLowerCase();

        const itemMatches = this._allItems.filter(item =>
            item.label?.toLowerCase().includes(term) ||
            item.tag?.toLowerCase().includes(term) ||
            item.description?.toLowerCase().includes(term)
        );

        const categoryMatches = this._allItems.filter(item =>
            item.categoryPath?.toLowerCase().includes(term) &&
            !itemMatches.includes(item)
        );

        return [...itemMatches, ...categoryMatches];
    }

    /** `~letters` → inserted text, from the `quickTypes` setting; `§` placeholders are kept for {@link quickTypeSnippet}. */
    public buildQuickTypeMap(): Map<string, string> {
        return buildQuickTypeMap(this._customizationsProvider?.().quickTypes ?? DEFAULT_QUICK_TYPES, true);
    }

    /** Snippet body for quick-type text with `§`, its tab stops named like the matching item's parameters. */
    public quickTypeSnippet(text: string): string | null {
        if (!text.includes('§')) return null;
        const c = this.getCustomizations();
        const items = c ? applySnippetCustomizations(this._allItems, c) : this._allItems;
        const item = items.find(i => i.tag === text && hasSnippetPlaceholders(i));
        return buildInsertSnippet({ ...(item ?? { description: '' }), tag: text });
    }

    public async reloadSnippets(): Promise<void> {
        this._isLoaded = false;
        this._loadPromise = null;
        this._allItems = [];
        this._insertDataTree = {};
        return this.loadSnippets();
    }

    private log(message: string): void {
        this.logger?.appendLine('[Snippets] ' + message);
    }
}
