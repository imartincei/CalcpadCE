import * as vscode from 'vscode';
import {
    buildInsertSnippet,
    hasSnippetPlaceholders,
    formatInsertLabel,
    formatMacroCompletion,
    formatFunctionCompletion,
    formatVariableCompletion,
    formatCustomUnitCompletion,
    type CompletionData,
    type CompletionKind,
    METADATA_SETTINGS_KEYS,
    type MetadataSettingKey,
} from 'calcpad-frontend';
import { CalcpadInsertManager, InsertItem } from './calcpadInsertManager';
import { CalcpadDefinitionsService } from './calcpadDefinitionsService';
import { buildBuiltinDocMarkdown } from './calcpadBuiltinDocs';

/**
 * Build the tab-stop snippet for a setting's value from its catalog entry.
 * Booleans and enums become choice lists; numeric enums (e.g. degrees) and
 * numbers stay unquoted, strings are quoted.
 */
function settingSnippetValue(s: MetadataSettingKey): string {
    if (s.type === 'boolean') return '${1|true,false|}';
    if (s.type === 'enum' && s.options?.length) {
        const choices = s.options.map(o => o.value).join(',');
        return typeof s.def === 'number' ? `\${1|${choices}|}` : `"\${1|${choices}|}"`;
    }
    if (s.type === 'string') return `"\${1:${s.def}}"`;
    return `\${1:${s.def}}`;
}

const VSCODE_KIND_MAP: Record<CompletionKind, vscode.CompletionItemKind> = {
    macro: vscode.CompletionItemKind.Class,
    function: vscode.CompletionItemKind.Function,
    variable: vscode.CompletionItemKind.Variable,
    unit: vscode.CompletionItemKind.Unit,
    snippet: vscode.CompletionItemKind.Snippet,
};

function toVscodeCompletion(data: CompletionData): vscode.CompletionItem {
    const item = new vscode.CompletionItem(data.label, VSCODE_KIND_MAP[data.kind]);
    item.detail = data.detail;
    item.documentation = new vscode.MarkdownString(data.documentation);
    item.insertText = data.isSnippet ? new vscode.SnippetString(data.insertText) : data.insertText;
    item.sortText = data.sortText;
    return item;
}

export class CalcpadCompletionProvider implements vscode.CompletionItemProvider {
    private insertManager: CalcpadInsertManager;
    private definitionsService: CalcpadDefinitionsService;
    private outputChannel: vscode.OutputChannel;

    constructor(definitionsService: CalcpadDefinitionsService, insertManager: CalcpadInsertManager, outputChannel: vscode.OutputChannel) {
        this.insertManager = insertManager;
        this.definitionsService = definitionsService;
        this.outputChannel = outputChannel;
    }

    async provideCompletionItems(
        document: vscode.TextDocument,
        position: vscode.Position,
        token: vscode.CancellationToken,
        context: vscode.CompletionContext
    ): Promise<vscode.CompletionItem[]> {
        // Check if we're inside a metadata comment JSON block first
        const lineText = document.lineAt(position.line).text;
        const beforeCursor = lineText.substring(0, position.character);
        const metadataCompletions = this.provideMetadataJsonCompletions(beforeCursor, position);
        if (metadataCompletions) {
            return metadataCompletions;
        }

        const completionItems: vscode.CompletionItem[] = [];

        // Get the word being typed
        const wordRange = document.getWordRangeAtPosition(position);
        const word = wordRange ? document.getText(wordRange) : '';

        this.outputChannel.appendLine('[COMPLETION] Word: "' + word + '" at position ' + position.line + ':' + position.character);

        // Ensure snippets are loaded
        if (!this.insertManager.isLoaded()) {
            try {
                await this.insertManager.loadSnippets();
            } catch (error) {
                this.outputChannel.appendLine('[COMPLETION] Failed to load snippets: ' + error);
            }
        }

        // Get user-defined content from cached definitions (highest priority).
        // Items pulled in via #include are surfaced here too — the linter
        // service tags them with source !== 'local' so the shared formatters
        // can show their origin file in the docs.
        try {
            const definitions = this.definitionsService.getCachedDefinitions(document.uri.toString());

            if (definitions) {
                const matches = (name: string) => !word || name.toLowerCase().includes(word.toLowerCase());

                for (const variable of definitions.variables) {
                    if (matches(variable.name)) completionItems.push(toVscodeCompletion(formatVariableCompletion(variable)));
                }
                for (const func of definitions.functions) {
                    if (matches(func.name)) completionItems.push(toVscodeCompletion(formatFunctionCompletion(func)));
                }
                for (const macro of definitions.macros) {
                    if (matches(macro.name)) completionItems.push(toVscodeCompletion(formatMacroCompletion(macro)));
                }
                for (const unit of definitions.customUnits) {
                    if (matches(unit.name)) completionItems.push(toVscodeCompletion(formatCustomUnitCompletion(unit)));
                }
            }

        } catch (error) {
            this.outputChannel.appendLine('[COMPLETION ERROR] ' + error);
        }

        // Add built-in content from insert manager — only snippets that define a keyword
        // (function, command, unit, constant, operator, setting, control keyword). UI-only
        // entries like HTML tags, markdown syntax, symbols, and block templates are excluded.
        const allInsertItems = this.insertManager.getAllItems().filter(item => !!item.keywordType);
        for (const insertItem of allInsertItems) {
            // Filter by search term if provided
            if (!word || this.matchesSearchTerm(insertItem, word)) {
                const completionItem = this.convertInsertItemToCompletionItem(insertItem);
                if (completionItem) {
                    // Sort built-ins after user-defined content
                    completionItem.sortText = `1_${completionItem.label}`;
                    completionItems.push(completionItem);
                }
            }
        }

        this.outputChannel.appendLine(`[COMPLETION] Returning ${completionItems.length} items`);
        return completionItems;
    }

    /**
     * Check if insert item matches the search term
     */
    private matchesSearchTerm(item: InsertItem, searchTerm: string): boolean {
        const term = searchTerm.toLowerCase();
        return (
            item.tag.toLowerCase().includes(term) ||
            (item.label?.toLowerCase().includes(term) ?? false) ||
            item.description.toLowerCase().includes(term) ||
            (item.categoryPath?.toLowerCase().includes(term) ?? false)
        );
    }

    /**
     * Convert an InsertItem to a CompletionItem
     */
    private convertInsertItemToCompletionItem(item: InsertItem): vscode.CompletionItem | null {
        // Determine completion item kind based on content
        let kind = vscode.CompletionItemKind.Text;
        let insertText = item.tag;
        
        // Categorize based on tag content and category path
        if (item.tag.includes('(') || item.categoryPath?.toLowerCase().includes('function')) {
            kind = vscode.CompletionItemKind.Function;
            if (hasSnippetPlaceholders(item)) {
                insertText = buildInsertSnippet(item);
            } else {
                const funcMatch = item.tag.match(/^([a-zA-Z_][a-zA-Z0-9_]*)/);
                insertText = funcMatch ? `${funcMatch[1]}()` : item.tag;
            }
        } else if (item.categoryPath?.toLowerCase().includes('constant') ||
                   item.tag.match(/^[A-Za-z_][A-Za-z0-9_]*\\s*=/) ||
                   ['π', 'e', 'φ', 'γ'].includes(item.tag)) {
            kind = vscode.CompletionItemKind.Constant;
            // For constants with assignments, just use the name part
            const constMatch = item.tag.match(/^([A-Za-z_π][A-Za-z0-9_]*)/);
            if (constMatch) {
                insertText = constMatch[1];
            }
        } else if (item.categoryPath?.toLowerCase().includes('operator')) {
            kind = vscode.CompletionItemKind.Operator;
        }

        const completionItem = new vscode.CompletionItem(
            formatInsertLabel(item),
            kind
        );
        
        completionItem.detail = item.categoryPath || 'Built-in';
        completionItem.documentation = buildBuiltinDocMarkdown(item);
        
        // Use snippet string for functions, plain text for others
        if (kind === vscode.CompletionItemKind.Function && insertText.includes('${')) {
            completionItem.insertText = new vscode.SnippetString(insertText);
        } else {
            completionItem.insertText = insertText;
        }

        return completionItem;
    }

    // ===== Metadata comment JSON autocomplete =====

    /** Valid paramType values for custom functions */
    private static readonly FUNCTION_PARAM_TYPES = ['value', 'vector', 'matrix', 'any'];

    /** Valid paramType values for macros (TokenType names) */
    private static readonly MACRO_PARAM_TYPES = [
        'None', 'Const', 'Operator', 'Bracket', 'LineContinuation',
        'Variable', 'LocalVariable', 'Function', 'Macro', 'MacroParameter',
        'Units', 'Setting',
        'Keyword', 'ControlBlockKeyword', 'EndKeyword', 'Command',
        'Include', 'FilePath', 'DataExchangeKeyword',
        'Comment', 'HtmlComment', 'Tag', 'HtmlContent', 'JavaScript', 'Css', 'Svg',
        'Input', 'Format',
    ];

    /**
     * Provide completions when inside a metadata comment JSON block.
     * Returns null if the cursor is not in a metadata comment context.
     */
    private provideMetadataJsonCompletions(beforeCursor: string, position: vscode.Position): vscode.CompletionItem[] | null {
        const trimmed = beforeCursor.trimStart();

        // Must be a comment line (starts with ' or ")
        if (!trimmed.startsWith("'") && !trimmed.startsWith('"')) {
            return null;
        }

        // Must contain the <!--  marker (HTML comment start)
        const markerIdx = trimmed.indexOf('<!--');
        if (markerIdx < 0) {
            return null;
        }

        // Must not already have --> before cursor (cursor would be outside the block)
        const afterMarker = trimmed.substring(markerIdx + 4);
        if (afterMarker.includes('-->')) {
            return null;
        }

        // We're inside a '<!--{...  block (no closing --> yet)
        // The insert range is from cursor to cursor (insert, don't replace)
        const insertRange = new vscode.Range(position, position);

        // Determine what to complete: property names or paramType values

        // Check if we're inside a "paramTypes" array value
        if (this.isInsideParamTypesArray(afterMarker)) {
            const replaceRange = this.getArrayValueReplaceRange(beforeCursor, position);
            return this.getParamTypeValueCompletions(replaceRange);
        }

        // Check if we're inside a "settings" object
        if (this.isInsideSettingsObject(afterMarker)) {
            return this.getSettingsKeyCompletions(insertRange);
        }

        // Check if we're at a property name position (after { or ,)
        if (this.isAtPropertyNamePosition(afterMarker)) {
            const replaceRange = this.getArrayValueReplaceRange(beforeCursor, position);
            return this.getPropertyNameCompletions(replaceRange);
        }

        return null;
    }

    /**
     * Check if cursor is inside a "paramTypes": [...] array value
     */
    private isInsideParamTypesArray(afterMarker: string): boolean {
        const ptIdx = afterMarker.indexOf('"paramTypes"');
        if (ptIdx < 0) return false;

        const afterPt = afterMarker.substring(ptIdx + '"paramTypes"'.length);
        const bracketOpen = afterPt.indexOf('[');
        if (bracketOpen < 0) return false;

        const bracketClose = afterPt.indexOf(']', bracketOpen);
        return bracketClose < 0;
    }

    /**
     * Check if cursor is inside a "settings": {...} object value
     */
    private isInsideSettingsObject(afterMarker: string): boolean {
        const sIdx = afterMarker.indexOf('"settings"');
        if (sIdx < 0) return false;

        const afterS = afterMarker.substring(sIdx + '"settings"'.length);
        const braceOpen = afterS.indexOf('{');
        if (braceOpen < 0) return false;

        // Count braces to see if we're still inside
        let depth = 0;
        for (let i = braceOpen; i < afterS.length; i++) {
            if (afterS[i] === '{') depth++;
            else if (afterS[i] === '}') depth--;
            if (depth === 0) return false; // Closed
        }
        return true; // Still inside
    }

    /**
     * Check if cursor is at a JSON property name position (after { or , at depth 0).
     * Uses forward scanning to correctly handle string quoting.
     */
    private isAtPropertyNamePosition(afterMarker: string): boolean {
        if (afterMarker.length === 0) return false;

        let inString = false;
        let arrayDepth = 0;
        let braceDepth = 0;
        let lastSignificant = '';

        for (let i = 0; i < afterMarker.length; i++) {
            const c = afterMarker[i];

            if (inString) {
                if (c === '"' && afterMarker[i - 1] !== '\\') {
                    inString = false;
                }
                continue;
            }

            if (c === '"') { inString = true; continue; }
            if (c === '{') { braceDepth++; lastSignificant = '{'; continue; }
            if (c === '}') { braceDepth--; lastSignificant = '}'; continue; }
            if (c === '[') { arrayDepth++; continue; }
            if (c === ']') { arrayDepth--; continue; }

            if (braceDepth === 1 && arrayDepth === 0) {
                if (c === ',') lastSignificant = ',';
                else if (c === ':') lastSignificant = ':';
            }
        }

        // At a property name position if we're inside the top-level object,
        // not inside an array, and the last token was { or ,
        // Also allow when inString is true (user started typing a quoted property name)
        return braceDepth >= 1 && arrayDepth === 0 && (lastSignificant === '{' || lastSignificant === ',');
    }

    /**
     * Helper to set filterText and range on a completion item so VS Code shows it
     * even when there's no word at the cursor position.
     */
    private applyInsertFix(item: vscode.CompletionItem, range: vscode.Range): void {
        item.filterText = ' ';
        item.range = range;
    }

    /**
     * Find the replacement range for the current value being typed inside a JSON context.
     * Scans backwards from the cursor to find the start of the current token
     * (after opening quote, comma, bracket, brace, or whitespace).
     */
    private getArrayValueReplaceRange(beforeCursor: string, position: vscode.Position): vscode.Range {
        let startCol = position.character;
        for (let i = beforeCursor.length - 1; i >= 0; i--) {
            const ch = beforeCursor[i];
            if (ch === '"') {
                // Include the opening quote since insertText starts with "
                startCol = i;
                break;
            }
            if (ch === '[' || ch === ',' || ch === '{') {
                startCol = i + 1;
                break;
            }
        }
        return new vscode.Range(position.line, startCol, position.line, position.character);
    }

    /**
     * Get completion items for top-level HTML comment JSON property names
     */
    private getPropertyNameCompletions(range: vscode.Range): vscode.CompletionItem[] {
        const items: vscode.CompletionItem[] = [];

        const props: Array<{label: string; detail: string; snippet: string; sort: string}> = [
            { label: '"desc"', detail: 'Description of the definition', snippet: '"desc": "${1:description}"', sort: '0_desc' },
            { label: '"paramTypes"', detail: 'Type hints per parameter', snippet: '"paramTypes": [${1}]', sort: '0_paramTypes' },
            { label: '"paramDesc"', detail: 'Descriptions per parameter', snippet: '"paramDesc": [${1}]', sort: '0_paramDesc' },
            { label: '"settings"', detail: 'File settings overrides', snippet: '"settings": {${1}}', sort: '0_settings' },
            { label: '"LintIgnore"', detail: 'Suppress linter diagnostics (start region)', snippet: '"LintIgnore": [${1}]', sort: '0_LintIgnore' },
            { label: '"EndLintIgnore"', detail: 'End lint suppression region', snippet: '"EndLintIgnore": []', sort: '0_EndLintIgnore' },
        ];

        for (const p of props) {
            const item = new vscode.CompletionItem(p.label, vscode.CompletionItemKind.Property);
            item.detail = p.detail;
            item.insertText = new vscode.SnippetString(p.snippet);
            item.sortText = p.sort;
            // Strip quotes for filterText so VS Code matches against typed text
            item.filterText = p.label.replace(/"/g, '');
            item.range = range;
            items.push(item);
        }

        return items;
    }

    /** Valid #settings keys, derived from the shared METADATA_SETTINGS_KEYS catalog. */
    private static readonly SETTINGS_KEYS: Array<{key: string; detail: string; value: string}> =
        METADATA_SETTINGS_KEYS.map(s => ({
            key: s.key,
            detail: s.detail,
            value: settingSnippetValue(s),
        }));

    /**
     * Get completion items for settings object keys
     */
    private getSettingsKeyCompletions(range: vscode.Range): vscode.CompletionItem[] {
        const items: vscode.CompletionItem[] = [];

        for (const s of CalcpadCompletionProvider.SETTINGS_KEYS) {
            const item = new vscode.CompletionItem('"' + s.key + '"', vscode.CompletionItemKind.Property);
            item.detail = s.detail;
            item.insertText = new vscode.SnippetString('"' + s.key + '": ' + s.value);
            item.sortText = '0_' + s.key;
            this.applyInsertFix(item, range);
            items.push(item);
        }

        return items;
    }

    /**
     * Get completion items for paramType values.
     * Offers both function types and macro types.
     */
    private getParamTypeValueCompletions(range: vscode.Range): vscode.CompletionItem[] {
        const items: vscode.CompletionItem[] = [];

        for (const type of CalcpadCompletionProvider.FUNCTION_PARAM_TYPES) {
            const item = new vscode.CompletionItem('"' + type + '"', vscode.CompletionItemKind.EnumMember);
            item.detail = 'Function param type';
            item.insertText = '"' + type + '"';
            item.sortText = '0_' + type;
            item.filterText = type;
            item.range = range;
            items.push(item);
        }

        for (const type of CalcpadCompletionProvider.MACRO_PARAM_TYPES) {
            if (CalcpadCompletionProvider.FUNCTION_PARAM_TYPES.includes(type.toLowerCase())) continue;
            const item = new vscode.CompletionItem('"' + type + '"', vscode.CompletionItemKind.EnumMember);
            item.detail = 'Macro param type (TokenType)';
            item.insertText = '"' + type + '"';
            item.sortText = '1_' + type;
            item.filterText = type;
            item.range = range;
            items.push(item);
        }

        return items;
    }

    /**
     * Register the completion provider
     */
    public static register(definitionsService: CalcpadDefinitionsService, insertManager: CalcpadInsertManager, outputChannel: vscode.OutputChannel): vscode.Disposable {
        const provider = new CalcpadCompletionProvider(definitionsService, insertManager, outputChannel);
        return vscode.languages.registerCompletionItemProvider(
            ['calcpad', 'plaintext'], // Language selector
            provider,
            '.', '(', '$', '{' // Trigger characters ('{' for '<!--{' metadata comments)
        );
    }
}