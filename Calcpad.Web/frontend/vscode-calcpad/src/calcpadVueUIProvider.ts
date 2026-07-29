import * as vscode from 'vscode';
import * as path from 'path';
import * as fs from 'fs';
import { parseHeadings, DEFAULT_PDF_SETTINGS, extractPlotsFromHtml, buildZip, serializeMetadataComment, serializeSettingsDirective, computeMetadataBlock, buildDefinitionResolver, findUiDirectiveBlock, serializeUiDirective } from 'calcpad-frontend';
import type { CalcpadError, ExtractedPlot, MetadataCommentBlock, MetadataCommentData, MetadataLayout, DefinitionResolver, DefinitionsResponse, SettingsValues, UiDirectiveData } from 'calcpad-frontend';
import { CalcpadSettingsManager } from './calcpadSettings';
import { CalcpadInsertManager } from './calcpadInsertManager';

export class CalcpadVueUIProvider implements vscode.WebviewViewProvider {
    public static readonly viewType = 'calcpadVueUI';

    private _view?: vscode.WebviewView;
    private _outputChannel: vscode.OutputChannel;
    private _cachedPlots: ExtractedPlot[] = [];
    private _cachedHtml: string = '';
    /**
     * Extension supplies a getter that combines `activeTextEditor` with the
     * remembered preview-source editor so plot fetching still works when the
     * preview panel is focused (which nulls out `activeTextEditor`).
     */
    public getSourceEditor?: () => vscode.TextEditor | undefined;
    public onPreviewThemeChanged?: () => void | Promise<void>;
    public onSettingsChanged?: () => void | Promise<void>;
    /** Real highlighter definitions for a document URI, used to resolve metadata context. */
    public getDefinitions?: (documentUri: string) => DefinitionsResponse | undefined;

    constructor(
        private readonly _extensionUri: vscode.Uri,
        private readonly _context: vscode.ExtensionContext,
        private readonly _settingsManager: CalcpadSettingsManager,
        private readonly _insertManager: CalcpadInsertManager
    ) {
        this._outputChannel = vscode.window.createOutputChannel('CalcpadCE Vue');
        this._outputChannel.appendLine('CalcPad Vue UI Provider initialized');

        // Register callback to refresh UI when snippets are loaded from server
        this._insertManager.onSnippetsLoaded(() => {
            this._outputChannel.appendLine('Snippets loaded - refreshing Vue UI');
            this._sendInitialData();
        });
    }

    public resolveWebviewView(
        webviewView: vscode.WebviewView,
        context: vscode.WebviewViewResolveContext,
        _token: vscode.CancellationToken,
    ) {
        this._view = webviewView;
        this._outputChannel.appendLine('Resolving Vue webview view');

        webviewView.webview.options = {
            // Allow scripts in the webview
            enableScripts: true,
            localResourceRoots: [
                this._extensionUri
            ]
        };

        webviewView.webview.html = this._getHtmlForWebview(webviewView.webview);
        this._outputChannel.appendLine('Webview HTML set');

        // Handle messages from the webview
        webviewView.webview.onDidReceiveMessage(async (data) => {
            this._outputChannel.appendLine(`Received message: ${data.type}`);
            switch (data.type) {
                case 'insertText':
                    const insertEditor = vscode.window.activeTextEditor;
                    if (insertEditor) {
                        const position = insertEditor.selection.active;
                        await insertEditor.edit(editBuilder => {
                            editBuilder.insert(position, data.text);
                        });
                    }
                    break;

                case 'insertImage':
                    vscode.commands.executeCommand('vscode-calcpad.insertImage');
                    break;

                case 'getSettings':
                    await this._settingsManager.ready;
                    webviewView.webview.postMessage(await this._buildSettingsResponse());
                    break;

                case 'updateSettings':
                    this._settingsManager.updateSettings(data.settings);
                    void this.onSettingsChanged?.();
                    break;

                case 'resetSettings':
                    await this._settingsManager.resetSettings();
                    webviewView.webview.postMessage({
                        type: 'settingsReset',
                        settings: this._settingsManager.getSettings(),
                    });
                    webviewView.webview.postMessage(await this._buildSettingsResponse());
                    break;

                case 'saveNamedConfig': {
                    const result = await this._settingsManager.savePreset(data.name);
                    if (!result.ok) {
                        webviewView.webview.postMessage({
                            type: 'saveNamedConfigError',
                            message: result.message,
                        });
                    } else {
                        webviewView.webview.postMessage(await this._buildSettingsResponse());
                    }
                    break;
                }

                case 'switchConfig':
                    await this._settingsManager.loadPreset(data.name);
                    webviewView.webview.postMessage(await this._buildSettingsResponse());
                    break;

                case 'openSettingsFolder':
                    await this._settingsManager.openSettingsFolder();
                    break;

                case 'updatePreviewTheme':
                    this._settingsManager.setExtra('previewTheme', data.theme);
                    void this.onPreviewThemeChanged?.();
                    break;

                case 'updateColorTheme':
                    await vscode.workspace.getConfiguration('workbench').update('colorTheme', data.theme, vscode.ConfigurationTarget.Global);
                    break;

                case 'updateCommentFormat':
                    this._settingsManager.setExtra('commentFormat', data.format);
                    break;

                case 'updateFormattingHotkeys':
                    this._settingsManager.setExtra('formattingHotkeys', data.enabled);
                    break;

                case 'updateQuickTyping':
                    this._settingsManager.setExtra('quickTyping', data.enabled);
                    break;

                case 'updatePreviewCursorSync':
                    this._settingsManager.setExtra('previewCursorSync', data.enabled);
                    break;

                case 'updateAutoRun':
                    this._settingsManager.setExtra('autoRun', data.enabled);
                    break;

                case 'updateDarkBackground':
                    this._settingsManager.setExtra('darkBackground', data.color);
                    break;

                case 'updateLinterMinSeverity':
                    this._settingsManager.setExtra('linterMinSeverity', data.severity);
                    break;

                case 'updateLibraryPath':
                    this._settingsManager.setExtra('libraryPath', data.path);
                    break;

                case 'updatePdfSettings': {
                    const current = this._settingsManager.getExtraObject<Record<string, unknown>>('pdfSettings', {});
                    this._settingsManager.setExtra('pdfSettings', { ...current, ...data.settings });
                    break;
                }

                case 'resetPdfSettings': {
                    this._settingsManager.setExtra('pdfSettings', {});
                    const resetPdfSettings = { ...DEFAULT_PDF_SETTINGS };
                    webviewView.webview.postMessage({
                        type: 'pdfSettingsReset',
                        settings: resetPdfSettings,
                    });
                    break;
                }

                case 'openLogsFolder': {
                    // Resolve the same logs directory the server manager uses.
                    // Folder may not exist yet on a fresh install — create it so the
                    // OS file explorer has something to open instead of erroring.
                    const logsDir = path.join(this._context.extensionPath, 'bin', 'logs');
                    try { fs.mkdirSync(logsDir, { recursive: true }); } catch { /* best-effort */ }
                    try {
                        await vscode.env.openExternal(vscode.Uri.file(logsDir));
                        this._outputChannel.appendLine(`Opened logs folder: ${logsDir}`);
                    } catch (err) {
                        const msg = err instanceof Error ? err.message : String(err);
                        this._outputChannel.appendLine(`Failed to open logs folder: ${msg}`);
                        vscode.window.showErrorMessage(`Could not open logs folder: ${msg}`);
                    }
                    break;
                }

                case 'getPdfSettings': {
                    const stored = this._settingsManager.getExtraObject<Partial<typeof DEFAULT_PDF_SETTINGS>>('pdfSettings', {});
                    const pdfSettings = { ...DEFAULT_PDF_SETTINGS, ...stored };
                    webviewView.webview.postMessage({
                        type: 'pdfSettingsResponse',
                        settings: pdfSettings,
                    });
                    break;
                }

                // The Export tab names which rendering it wants; the PDF tab's Generate
                // button sends none, and the commands default to the report.
                case 'generatePdf':
                    vscode.commands.executeCommand('vscode-calcpad.printToPdf', data.variant);
                    break;

                case 'saveSourceHtml':
                    vscode.commands.executeCommand('vscode-calcpad.saveSourceHtml', data.variant);
                    break;

                case 'saveDocx':
                    vscode.commands.executeCommand('vscode-calcpad.saveDocx', data.variant);
                    break;

                case 'getPlots':
                    await this._handleGetPlots();
                    break;

                case 'savePlot':
                    await this._handleSavePlot(data.index);
                    break;

                case 'savePlotsZip':
                    await this._handleSavePlotsZip();
                    break;

                case 'getInsertData':
                    this._sendInitialData();
                    break;

                case 'getVariables':
                    // Trigger a refresh of variables from the current document
                    const editor = this.getSourceEditor?.() ?? vscode.window.activeTextEditor;
                    if (editor && (editor.document.languageId === 'calcpad' || editor.document.languageId === 'plaintext')) {
                        vscode.commands.executeCommand('calcpad.refreshVariables');
                    }
                    break;

                case 'getHeadings':
                    {
                        const headingsEditor = vscode.window.activeTextEditor;
                        if (headingsEditor && (headingsEditor.document.languageId === 'calcpad' || headingsEditor.document.languageId === 'plaintext')) {
                            const text = headingsEditor.document.getText();
                            const headings = parseHeadings(text);
                            webviewView.webview.postMessage({
                                type: 'updateHeadings',
                                headings
                            });
                        } else {
                            webviewView.webview.postMessage({
                                type: 'updateHeadings',
                                headings: []
                            });
                        }
                    }
                    break;

                case 'goToLine':
                    {
                        const goToEditor = vscode.window.activeTextEditor;
                        if (goToEditor && typeof data.line === 'number') {
                            const lineIndex = Math.max(0, data.line - 1);
                            const lineEnd = goToEditor.document.lineAt(lineIndex).range.end;
                            goToEditor.selection = new vscode.Selection(lineEnd, lineEnd);
                            goToEditor.revealRange(goToEditor.document.lineAt(lineIndex).range, vscode.TextEditorRevealType.InCenter);
                            vscode.window.showTextDocument(goToEditor.document, goToEditor.viewColumn);
                        }
                    }
                    break;

                case 'refreshDocument':
                    this._outputChannel.appendLine('[Vue UI] Refresh document requested');
                    vscode.commands.executeCommand('calcpad.refreshDocument');
                    break;

                case 'prettifyDocument':
                    this._outputChannel.appendLine('[Vue UI] Prettify document requested');
                    vscode.commands.executeCommand('vscode-calcpad.prettifyDocument');
                    break;

                case 'getPrettifySettings': {
                    webviewView.webview.postMessage({
                        type: 'prettifySettingsResponse',
                        indentStyle: this._settingsManager.getExtra('prettifyIndentStyle', 'tab'),
                        indentSize: this._settingsManager.getExtraNumber('prettifyIndentSize', 4),
                        trimTrailingWhitespace: this._settingsManager.getExtraBool('prettifyTrimTrailingWhitespace', true),
                    });
                    break;
                }

                case 'updatePrettifyIndentStyle':
                    this._settingsManager.setExtra('prettifyIndentStyle', data.value);
                    break;

                case 'updatePrettifyIndentSize':
                    this._settingsManager.setExtra('prettifyIndentSize', data.value);
                    break;

                case 'updatePrettifyTrim':
                    this._settingsManager.setExtra('prettifyTrimTrailingWhitespace', data.value);
                    break;

                case 'getMetadataContext':
                    this.updateMetadataContext(this._computeMetadataBlock());
                    break;

                case 'updateMetadata':
                    await this._handleUpdateMetadata(data);
                    break;

                case 'debug':
                    this._outputChannel.appendLine(`[Vue Debug] ${data.message}`);
                    break;
            }
        });

        // Send initial data
        this._sendInitialData();

        // Refresh headings when the user switches editor tabs
        vscode.window.onDidChangeActiveTextEditor(() => {
            this._sendHeadings();
        });

        // Debounced refresh of headings when the document content changes
        let tocTimer: ReturnType<typeof setTimeout> | undefined;
        vscode.workspace.onDidChangeTextDocument((e) => {
            const editor = this.getSourceEditor?.() ?? vscode.window.activeTextEditor;
            if (editor && e.document === editor.document) {
                if (tocTimer) clearTimeout(tocTimer);
                tocTimer = setTimeout(() => this._sendHeadings(), 800);
            }
        });
    }

    private _getInstalledThemes(): Array<{ label: string; id: string; kind: 'dark' | 'light' }> {
        const themes: Array<{ label: string; id: string; kind: 'dark' | 'light' }> = [];
        for (const ext of vscode.extensions.all) {
            const contributed = ext.packageJSON?.contributes?.themes;
            if (!Array.isArray(contributed)) continue;
            for (const t of contributed) {
                if (!t?.label) continue;
                const uiTheme: string = t.uiTheme ?? 'vs-dark';
                const kind: 'dark' | 'light' = uiTheme === 'vs' || uiTheme === 'hc-light' ? 'light' : 'dark';
                themes.push({ label: t.label, id: t.id ?? t.label, kind });
            }
        }
        themes.sort((a, b) => a.label.localeCompare(b.label));
        return themes;
    }

    private async _sendInitialData() {
        if (!this._view) return;

        // Ensure snippets are loaded
        if (!this._insertManager.isLoaded()) {
            try {
                await this._insertManager.loadSnippets();
            } catch (error) {
                this._outputChannel.appendLine('[Vue UI] Failed to load snippets: ' + error);
            }
        }

        // Send insert items as flat array
        const insertItems = this._insertManager.getAllItems();
        this._outputChannel.appendLine('Sending ' + insertItems.length + ' insert items');
        this._view.webview.postMessage({
            type: 'insertDataResponse',
            items: insertItems
        });
    }

    /**
     * Build the payload used by `settingsResponse` messages. Single source of
     * truth for the getSettings/resetSettings/saveNamedConfig/switchConfig
     * handlers so their payloads can't drift out of sync.
     */
    private async _buildSettingsResponse(): Promise<Record<string, unknown>> {
        const sm = this._settingsManager;
        return {
            type: 'settingsResponse',
            settings: sm.getSettings(),
            previewTheme: sm.getExtra('previewTheme', 'system'),
            colorTheme: vscode.workspace.getConfiguration('workbench').get<string>('colorTheme', ''),
            availableThemes: this._getInstalledThemes(),
            commentFormat: sm.getExtra('commentFormat', 'auto'),
            enableFormattingHotkeys: sm.getExtraBool('formattingHotkeys', true),
            enableQuickTyping: sm.getExtraBool('quickTyping', true),
            enablePreviewCursorSync: sm.getExtraBool('previewCursorSync', false),
            enableAutoRun: sm.getExtraBool('autoRun', true),
            darkBackground: sm.getExtra('darkBackground', '#1e1e1e'),
            linterMinSeverity: sm.getExtra('linterMinSeverity', 'information'),
            libraryPath: sm.getExtra('libraryPath', ''),
            activeConfig: sm.getActivePresetName(),
            availableConfigs: await sm.listPresets(),
        };
    }

    public setCachedHtml(html: string): void {
        this._cachedHtml = html;
        this._cachedPlots = extractPlotsFromHtml(html);
        if (this._view) {
            this._view.webview.postMessage({
                type: 'plotsResponse',
                plots: this._cachedPlots.map(p => ({
                    index: p.index,
                    ext: p.ext,
                    dataUri: p.dataUri,
                    sizeBytes: p.bytes.length,
                })),
            });
        }
    }

    private async _handleGetPlots(): Promise<void> {
        if (!this._view) return;
        if (this._cachedHtml) {
            this._cachedPlots = extractPlotsFromHtml(this._cachedHtml);
        } else {
            this._cachedPlots = [];
        }
        this._view.webview.postMessage({
            type: 'plotsResponse',
            plots: this._cachedPlots.map(p => ({
                index: p.index,
                ext: p.ext,
                dataUri: p.dataUri,
                sizeBytes: p.bytes.length,
            })),
        });
    }

    private async _handleSavePlot(index: number): Promise<void> {
        const plot = this._cachedPlots[index];
        if (!plot) return;
        const defaultName = `plot-${index + 1}.${plot.ext}`;
        const defaultDir = this._defaultSaveDir();
        const saveUri = await vscode.window.showSaveDialog({
            defaultUri: vscode.Uri.file(path.join(defaultDir, defaultName)),
            filters: { [plot.ext.toUpperCase()]: [plot.ext] },
        });
        if (!saveUri) return;
        await vscode.workspace.fs.writeFile(saveUri, plot.bytes);
    }

    private async _handleSavePlotsZip(): Promise<void> {
        if (this._cachedPlots.length === 0) return;
        const zipBytes = buildZip(
            this._cachedPlots.map(p => ({
                name: `plot-${p.index + 1}.${p.ext}`,
                bytes: p.bytes,
            })),
        );
        const defaultDir = this._defaultSaveDir();
        const saveUri = await vscode.window.showSaveDialog({
            defaultUri: vscode.Uri.file(path.join(defaultDir, 'calcpad-plots.zip')),
            filters: { 'ZIP Archive': ['zip'] },
        });
        if (!saveUri) return;
        await vscode.workspace.fs.writeFile(saveUri, zipBytes);
    }

    private _defaultSaveDir(): string {
        const editor = this.getSourceEditor?.() ?? vscode.window.activeTextEditor;
        if (editor && !editor.document.isUntitled) {
            return path.dirname(editor.document.uri.fsPath);
        }
        const folder = vscode.workspace.workspaceFolders?.[0]?.uri.fsPath;
        return folder ?? '';
    }

    private _sendHeadings() {
        if (!this._view) return;

        const editor = this.getSourceEditor?.() ?? vscode.window.activeTextEditor;
        if (editor && (editor.document.languageId === 'calcpad' || editor.document.languageId === 'plaintext')) {
            const headings = parseHeadings(editor.document.getText());
            this._view.webview.postMessage({ type: 'updateHeadings', headings });
        } else {
            this._view.webview.postMessage({ type: 'updateHeadings', headings: [] });
        }
    }

    public updateVariables(data: { macros: any[], variables: any[], functions: any[], customUnits: any[] }) {
        if (this._view) {
            this._outputChannel.appendLine(`Updating variables: ${data.macros.length} macros, ${data.variables.length} variables, ${data.functions.length} functions, ${data.customUnits.length} custom units`);
            this._view.webview.postMessage({
                type: 'updateVariables',
                data: data
            });
        }
    }

    public updateConvertErrors(errors: CalcpadError[]) {
        this._view?.webview.postMessage({ type: 'updateConvertErrors', errors });
    }

    /** Push the metadata comment (or null) the cursor currently sits in to the panel. */
    public updateMetadataContext(block: MetadataCommentBlock | null) {
        this._view?.webview.postMessage({ type: 'metadataContext', block });
    }

    /** Detect the single-line metadata comment at the source editor's cursor. */
    private _computeMetadataBlock(): MetadataCommentBlock | null {
        const editor = this.getSourceEditor?.() ?? vscode.window.activeTextEditor;
        if (!editor || (editor.document.languageId !== 'calcpad' && editor.document.languageId !== 'plaintext')) {
            return null;
        }
        const lines = editor.document.getText().split(/\r?\n/);
        const line = editor.selection.active.line;
        return computeMetadataBlock(lines, line, this._definitionResolver(editor.document.uri.toString()));
    }

    /** Definition resolver over a document's real highlighter results. */
    private _definitionResolver(documentUri: string): DefinitionResolver {
        const defs = this.getDefinitions?.(documentUri);
        return buildDefinitionResolver(defs ?? { functions: [], macros: [], variables: [], customUnits: [] });
    }

    /** Ask the panel to switch to a given tab id. */
    public focusTab(tab: string) {
        this._view?.webview.postMessage({ type: 'focusTab', tab });
    }

    /**
     * Rewrite the metadata comment line the panel edited. The panel sends the
     * 0-based line, its original indentation and trailing quote, and the new
     * data object; we serialize and replace the whole line.
     */
    private async _handleUpdateMetadata(data: {
        line: number;
        endLine?: number;
        indent?: string;
        trailingQuote?: string;
        layout?: MetadataLayout;
        data: MetadataCommentData;
        isNew?: boolean;
        settings?: SettingsValues;
        settingsLine?: number | null;
        settingsEndLine?: number | null;
        settingsLayout?: MetadataLayout;
        ui?: UiDirectiveData;
        uiLine?: number | null;
    }): Promise<void> {
        const editor = this.getSourceEditor?.() ?? vscode.window.activeTextEditor;
        if (!editor || typeof data.line !== 'number') return;

        const document = editor.document;
        const settingsText = data.settings ? serializeSettingsDirective(data.settings, data.settingsLayout) : '';
        let insertedSettings = false;

        await editor.edit(editBuilder => {
            // Metadata comment (desc/params/lint/no-print) — only when it has
            // content. A multi-line comment spans line..endLine; layout preserves
            // its shape (existing keys in place, new keys on the last line).
            if (Object.keys(data.data).length > 0 && data.line >= 0 && data.line < document.lineCount) {
                const newText = serializeMetadataComment(data.data, data.indent ?? '', data.trailingQuote ?? '', data.layout);
                if (data.isNew) {
                    editBuilder.insert(new vscode.Position(data.line, 0), newText + '\n');
                } else {
                    const endLine = Math.min(data.endLine ?? data.line, document.lineCount - 1);
                    const range = document.lineAt(data.line).range.with({ end: document.lineAt(endLine).range.end });
                    editBuilder.replace(range, newText);
                }
            }

            // #settings directive under the cursor. `settingsLine` (0-based) points
            // at the existing directive to rewrite, or null to create a new one at
            // the cursor — so multiple directives can coexist, each edited in place.
            if (data.settings) {
                const dirLine = data.settingsLine ?? null;
                const dirEndLine = Math.min(data.settingsEndLine ?? dirLine ?? 0, document.lineCount - 1);
                const hasExisting = dirLine !== null && dirLine >= 0 && dirLine < document.lineCount;
                const hasSettings = Object.keys(data.settings).length > 0;
                if (hasSettings) {
                    if (hasExisting) {
                        const range = document.lineAt(dirLine!).range.with({ end: document.lineAt(dirEndLine).range.end });
                        editBuilder.replace(range, settingsText);
                    } else {
                        editBuilder.insert(new vscode.Position(data.line, 0), settingsText + '\n');
                        insertedSettings = true;
                    }
                } else if (hasExisting) {
                    const start = document.lineAt(dirLine!).range.start;
                    editBuilder.delete(new vscode.Range(start, document.lineAt(dirEndLine).rangeIncludingLineBreak.end));
                }
            }

            // #UI directive at the cursor. Always a single-line replace — a #UI
            // line never shares a physical line with the comment or #settings
            // edits above, so this never overlaps them.
            if (data.ui && typeof data.uiLine === 'number' && data.uiLine >= 0 && data.uiLine < document.lineCount) {
                const currentLine = document.lineAt(data.uiLine);
                const uiBlock = findUiDirectiveBlock([currentLine.text], 0);
                if (uiBlock) editBuilder.replace(currentLine.range, serializeUiDirective(data.ui, uiBlock));
            }
        });

        // A freshly created directive: park the cursor on it so the re-emitted
        // context binds to it and a repeated Apply edits in place, not duplicates.
        if (insertedSettings) {
            const lines = editor.document.getText().split(/\r?\n/);
            const target = this._nearestLineMatching(lines, settingsText, data.line);
            if (target !== null)
                editor.selection = new vscode.Selection(target, lines[target].length, target, lines[target].length);
        }

        // Re-emit context (comment + refreshed settings) at the current cursor so a
        // repeated Apply edits in place instead of inserting a duplicate.
        const lines = editor.document.getText().split(/\r?\n/);
        const block = computeMetadataBlock(lines, editor.selection.active.line, this._definitionResolver(editor.document.uri.toString()));
        this.updateMetadataContext(block);
    }

    /** 0-based index of the line equal to `text`, closest to `near`; null if none. */
    private _nearestLineMatching(lines: string[], text: string, near: number): number | null {
        let best: number | null = null;
        for (let i = 0; i < lines.length; i++) {
            if (lines[i] !== text) continue;
            if (best === null || Math.abs(i - near) < Math.abs(best - near)) best = i;
        }
        return best;
    }

    public dispose() {
        this._outputChannel.dispose();
    }

    private _getHtmlForWebview(webview: vscode.Webview) {
        // Get the local path to main script run in the webview, then convert it to a uri we can use in the webview.
        const scriptUri = webview.asWebviewUri(vscode.Uri.joinPath(this._extensionUri, 'out', 'CalcpadVuePanel', 'main.js'));
        const styleUri = webview.asWebviewUri(vscode.Uri.joinPath(this._extensionUri, 'out', 'CalcpadVuePanel', 'main.css'));

        this._outputChannel.appendLine(`Script URI: ${scriptUri.toString()}`);
        this._outputChannel.appendLine(`Style URI: ${styleUri.toString()}`);

        // Use a nonce to only allow a specific script to be run.
        const nonce = getNonce();

        return `<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src ${webview.cspSource} data:; style-src ${webview.cspSource} 'unsafe-inline'; script-src 'nonce-${nonce}';">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <link href="${styleUri}" rel="stylesheet">
    <title>CalcpadCE Vue UI</title>
</head>
<body>
    <div id="app">
        <div style="padding: 20px; text-align: center; color: #666; font-size: 12px;">
            Loading Vue.js CalcpadCE UI...
            <br><small>If this message persists, check the developer console for errors</small>
        </div>
    </div>
    <script nonce="${nonce}" src="${scriptUri}"></script>
</body>
</html>`;
    }
}

function getNonce() {
    let text = '';
    const possible = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789';
    for (let i = 0; i < 32; i++) {
        text += possible.charAt(Math.floor(Math.random() * possible.length));
    }
    return text;
}
