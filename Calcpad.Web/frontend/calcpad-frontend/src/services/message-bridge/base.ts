import { CalcpadApiClient } from '../../api/client';
import { CalcpadSnippetService } from '../snippets';
import { CalcpadDefinitionsService } from '../definitions';
import { parseHeadings } from '../headings';
import { serializeMetadataComment, findSettingsDirectiveLine, serializeSettingsDirective, computeMetadataBlock, buildDefinitionResolver } from '../../text/metadata-comment';
import type { MetadataCommentData, MetadataCommentBlock, DefinitionResolver, SettingsValues } from '../../text/metadata-comment';
import type { DefinitionsResponse } from '../../types/api';
import { getDefaultSettings, buildApiSettings } from '../../types/settings';
import type { CalcpadSettings } from '../../types/settings';
import { DEFAULT_PDF_SETTINGS } from '../../types/pdf-settings';
import { buildImageCommentLine, bytesToBase64 } from '../image-utils';
import type { ImageStorageMode, PickedImage } from '../image-utils';
import { extractPlotsFromHtml, type ExtractedPlot } from '../plot-extract';
import { buildZip } from '../zip-writer';
import type { ILogger } from '../../types/interfaces';

export interface ExportRequest {
    defaultName: string;
    data: string | ArrayBuffer | Uint8Array;
    mime: string;
    extensions: string[];
    dialogTitle: string;
}

export interface QuickPickOption<T> {
    label: string;
    detail?: string;
    value: T;
}

/**
 * Present a modal list of choices and resolve with the chosen value, or null
 * if the user dismissed it. Injected by the host (see `setQuickPick`) so the
 * platform-agnostic bridge can prompt without depending on the app shell.
 */
export type QuickPickFn = <T>(opts: {
    title: string;
    placeholder?: string;
    options: QuickPickOption<T>[];
}) => Promise<T | null>;

/** Base64 payloads above this size prompt a "save to file instead?" warning. */
const BASE64_WARN_BYTES = 250 * 1024;

const BUILTIN_THEMES = [
    { id: 'calcpad-dark',  label: 'Dark',  kind: 'dark'  as const },
    { id: 'calcpad-light', label: 'Light', kind: 'light' as const },
];

/**
 * Shared message routing and handlers for the web and Tauri bridges.
 *
 * Subclasses inject platform-specific behavior via the abstract hooks
 * (settings storage, file save/pick, image pick, source-file resolution)
 * and can add platform-only message cases by overriding
 * `handlePlatformMessage`.
 */
export abstract class BaseMessageBridge {
    protected apiClient: CalcpadApiClient;
    protected snippetService: CalcpadSnippetService;
    protected definitionsService: CalcpadDefinitionsService;
    protected settings: CalcpadSettings;
    protected _onInsertText: ((text: string) => void) | null = null;
    protected quickPick: QuickPickFn | null = null;
    private _cachedPlots: ExtractedPlot[] = [];

    constructor(serverUrl: string, logger?: ILogger) {
        const log: ILogger = logger ?? { appendLine: (msg: string) => console.debug('[CalcPad]', msg) };
        this.apiClient = new CalcpadApiClient(serverUrl, log);
        this.snippetService = new CalcpadSnippetService(this.apiClient, log);
        this.definitionsService = new CalcpadDefinitionsService(this.apiClient);
        this.settings = getDefaultSettings();
        this.snippetService.loadSnippets();
    }

    get api(): CalcpadApiClient { return this.apiClient; }
    get snippets(): CalcpadSnippetService { return this.snippetService; }
    get definitions(): CalcpadDefinitionsService { return this.definitionsService; }

    getSettings(): CalcpadSettings { return this.settings; }

    /** Read an "extra" (non-CalcpadSettings) preference. */
    abstract getExtraSetting(key: string): string | undefined;
    /** Persist an arbitrary extra preference. */
    abstract setExtraSetting(key: string, value: string): void;

    set onInsertText(handler: (text: string) => void) {
        this._onInsertText = handler;
    }

    /** Host injects a modal list picker used by the image-storage prompt. */
    setQuickPick(fn: QuickPickFn): void {
        this.quickPick = fn;
    }

    /** Send updated TOC headings to the Vue sidebar. */
    refreshHeadings(): void {
        const content = this.getActiveEditorContent();
        const headings = parseHeadings(content);
        this.postToVue({ type: 'updateHeadings', headings });
    }

    /** Return the (coerced) stored color-theme label. */
    getStoredColorTheme(): string {
        return this.coerceColorTheme(this.getExtraSetting('colorTheme'));
    }

    handleMessage(message: any): void {
        if (this.handlePlatformMessage(message)) return;

        switch (message.type) {
            case 'getInsertData':
                this.handleGetInsertData();
                break;
            case 'getSettings':
                this.handleGetSettings();
                break;
            case 'updateSettings':
                this.handleUpdateSettings(message.settings);
                break;
            case 'resetSettings':
                this.handleResetSettings();
                break;
            case 'getVariables':
                this.handleGetVariables();
                break;
            case 'insertText':
                if (this._onInsertText) this._onInsertText(message.text);
                break;
            case 'insertImage':
                this.handleInsertImage();
                break;
            case 'updatePreviewTheme':
                this.setExtraSetting('previewTheme', message.theme);
                this.postToVue({ type: 'previewThemeChanged', theme: message.theme });
                break;
            case 'updateColorTheme':
                this.setExtraSetting('colorTheme', message.theme);
                this.applyColorTheme(message.theme);
                break;
            case 'updateQuickTyping':
                this.setExtraSetting('quickTyping', String(message.enabled));
                break;
            case 'updateCommentFormat':
                this.setExtraSetting('commentFormat', message.format);
                break;
            case 'updateFormattingHotkeys':
                this.setExtraSetting('formattingHotkeys', String(message.enabled));
                break;
            case 'updatePreviewCursorSync':
                this.setExtraSetting('previewCursorSync', String(message.enabled));
                break;
            case 'updateAutoRun':
                this.setExtraSetting('autoRun', String(message.enabled));
                this.postToVue({ type: 'autoRunChanged', enabled: !!message.enabled });
                break;
            case 'updateLinterMinSeverity':
                this.setExtraSetting('linterMinSeverity', message.severity);
                this.postToVue({ type: 'linterMinSeverityChanged', severity: message.severity });
                break;
            case 'updateMaxOutputLines':
                this.setExtraSetting('maxOutputLines', String(message.value));
                this.postToVue({ type: 'maxOutputLinesChanged', value: message.value });
                break;
            case 'getPdfSettings':
                this.handleGetPdfSettings();
                break;
            case 'updatePdfSettings':
                this.setExtraSetting('pdfSettings', JSON.stringify(message.settings));
                break;
            case 'resetPdfSettings':
                this.setExtraSetting('pdfSettings', '');
                this.handleGetPdfSettings();
                break;
            case 'generatePdf':
                this.handleGeneratePdf();
                break;
            case 'saveSourceHtml':
                this.handleSaveSourceHtml();
                break;
            case 'saveDocx':
                this.handleSaveDocx();
                break;
            case 'getPlots':
                this.handleGetPlots();
                break;
            case 'savePlot':
                this.handleSavePlot(message.index);
                break;
            case 'savePlotsZip':
                this.handleSavePlotsZip();
                break;
            case 'getHeadings':
                this.refreshHeadings();
                break;
            case 'getMetadataContext':
                this.handleGetMetadataContext();
                break;
            case 'updateMetadata':
                this.handleUpdateMetadata(message);
                break;
            case 'goToLine':
                this.handleGoToLine(message.line);
                break;
            case 'openLogsFolder':
                this.onOpenLogsFolder();
                break;
            case 'openFontsFolder':
                this.onOpenFontsFolder();
                break;
            case 'refreshFonts':
                this.onRefreshFonts();
                break;
            case 'updateEditorFontFamily':
                this.setExtraSetting('editorFontFamily', message.family ?? '');
                this.postToVue({ type: 'editorFontFamilyChanged', family: message.family ?? '' });
                break;
            case 'debug':
                break;
        }
    }

    // ---- Platform hooks (subclasses override) ----

    protected abstract persistSettings(settings: CalcpadSettings): void | Promise<void>;
    protected abstract resetSettingsBackend(): void | Promise<void>;
    protected abstract coerceColorTheme(raw: string | undefined | null): string;
    protected abstract applyColorTheme(theme: string): void;
    /** File-picker insert: returns the `src` to reference the chosen image (a path or data URI), or null if cancelled. */
    protected abstract pickImageSrc(): Promise<string | null>;
    /** True when this platform can write image files to disk (desktop). Web is base64-only. */
    protected canSaveImageToDisk(): boolean { return false; }
    /** True when the active document can host relative image files (has a path on disk). */
    protected canSaveImageRelativeToDocument(): boolean { return false; }
    /** Copy the image into an `images/` folder beside the document; return its relative src. */
    protected async saveImageToImagesFolder(_img: PickedImage): Promise<string | null> { return null; }
    /** Prompt for a save location; return the src (relative to the document) to reference it by. */
    protected async saveImageToCustomPath(_img: PickedImage): Promise<string | null> { return null; }
    /** Persist an export; returns the saved path when the platform has one, else null. */
    protected abstract saveExportedFile(req: ExportRequest): Promise<string | null>;
    protected abstract buildFileContext(content: string): Promise<{ sourceFilePath?: string }>;
    protected abstract getVariablesOrigin(): string;

    /**
     * The cached highlighter definitions for the active document, used to resolve
     * definition kinds/param counts for the metadata panel. Subclasses key the
     * definitions cache differently, so each supplies the correct lookup.
     */
    protected abstract getActiveDefinitions(): DefinitionsResponse | undefined;

    /** Definition resolver over the active document's real highlighter results. */
    private definitionResolver(): DefinitionResolver {
        const defs = this.getActiveDefinitions();
        return buildDefinitionResolver(defs ?? { functions: [], macros: [], variables: [], customUnits: [] });
    }
    protected abstract generatePdfBytes(
        content: string,
        apiSettings: unknown,
        sourceFilePath: string | undefined,
    ): Promise<ArrayBuffer | null>;

    protected buildSettingsResponseExtras(): Record<string, unknown> | Promise<Record<string, unknown>> { return {}; }
    protected async runPdfPreflight(): Promise<boolean> { return true; }
    protected async onPdfError(_err: unknown): Promise<void> { /* default no-op */ }
    /** Called after a PDF is successfully written, with the saved path (platforms that have one). */
    protected async onPdfSaved(_filePath: string): Promise<void> { /* default no-op */ }
    protected handlePlatformMessage(_message: any): boolean { return false; }
    protected onOpenLogsFolder(): void {
        console.warn('Open Logs Folder is only available in the desktop build — server logs live on the host running CalcPad.');
    }
    protected onOpenFontsFolder(): void {
        console.warn('Open Fonts Folder is only available in the desktop build.');
    }
    protected onRefreshFonts(): void { /* default no-op; desktop overrides */ }
    protected afterResetSettings(): void | Promise<void> { /* default no-op */ }

    // ---- Shared handlers ----

    protected postToVue(message: unknown): void {
        window.dispatchEvent(new MessageEvent('message', { data: message }));
    }

    protected getActiveEditorContent(): string {
        const tabs = (window as { calcpadTabs?: { activeModel?: { getValue(): string } } }).calcpadTabs;
        const fromTabs = tabs?.activeModel?.getValue();
        if (typeof fromTabs === 'string') return fromTabs;
        const m = (window as { monaco?: MonacoLike }).monaco;
        if (!m) return '';
        const editor = m.editor.getEditors()[0];
        const model = editor?.getModel() ?? m.editor.getModels()[0];
        return model?.getValue() ?? '';
    }

    private async handleGetInsertData(): Promise<void> {
        const items = this.snippetService.getAllItems();
        if (items.length > 0) {
            this.postToVue({ type: 'insertDataResponse', items });
        } else {
            this.snippetService.onSnippetsLoaded(() => {
                this.postToVue({
                    type: 'insertDataResponse',
                    items: this.snippetService.getAllItems(),
                });
            });
        }
    }

    protected async handleGetSettings(): Promise<void> {
        const extras = await this.buildSettingsResponseExtras();
        this.postToVue({
            type: 'settingsResponse',
            settings: this.settings,
            previewTheme: this.getExtraSetting('previewTheme') || 'system',
            colorTheme: this.getStoredColorTheme(),
            availableThemes: BUILTIN_THEMES,
            commentFormat: this.getExtraSetting('commentFormat') || 'auto',
            enableFormattingHotkeys: this.getExtraSetting('formattingHotkeys') !== 'false',
            enablePreviewCursorSync: this.getExtraSetting('previewCursorSync') === 'true',
            enableAutoRun: this.getExtraSetting('autoRun') !== 'false',
            linterMinSeverity: this.getExtraSetting('linterMinSeverity') || 'information',
            maxOutputLines: Number(this.getExtraSetting('maxOutputLines')) || 1000,
            editorFontFamily: this.getExtraSetting('editorFontFamily') ?? 'JuliaMono',
            ...extras,
        });
    }

    private async handleUpdateSettings(newSettings: any): Promise<void> {
        this.settings = { ...this.settings, ...newSettings };
        await this.persistSettings(this.settings);
        if (newSettings.server?.url) {
            this.apiClient.setBaseUrl(newSettings.server.url);
        }
        this.postToVue({ type: 'settingsChanged' });
    }

    private async handleResetSettings(): Promise<void> {
        await this.resetSettingsBackend();
        this.postToVue({ type: 'settingsReset', settings: this.settings });
        await this.afterResetSettings();
    }

    private async handleGetVariables(): Promise<void> {
        const content = this.getActiveEditorContent();
        const { sourceFilePath } = await this.buildFileContext(content);
        const response = await this.definitionsService.refreshDefinitions(
            content,
            this.getVariablesOrigin(),
            sourceFilePath,
        );
        this.postToVue({
            type: 'updateVariables',
            data: {
                macros: (response?.macros ?? []).map(m => ({
                    name: m.name,
                    params: m.parameters.length > 0 ? m.parameters.join('; ') : undefined,
                    definition: m.content.join('\n'),
                    source: m.source,
                    sourceFile: m.sourceFile,
                    description: m.description,
                    paramTypes: m.paramTypes,
                    paramDescriptions: m.paramDescriptions,
                    defaults: m.defaults,
                })),
                variables: (response?.variables ?? []).map(v => ({
                    name: v.name,
                    definition: v.expression,
                    expression: v.expression,
                    type: v.type,
                    source: v.source,
                    sourceFile: v.sourceFile,
                    description: v.description,
                })),
                functions: (response?.functions ?? []).map(f => ({
                    name: f.name,
                    params: f.parameters.join('; '),
                    definition: f.expression,
                    expression: f.expression,
                    returnType: f.returnType,
                    source: f.source,
                    sourceFile: f.sourceFile,
                    description: f.description,
                    paramTypes: f.paramTypes,
                    paramDescriptions: f.paramDescriptions,
                    defaults: f.defaults,
                })),
                customUnits: (response?.customUnits ?? []).map(u => ({
                    name: u.name,
                    definition: u.expression,
                    expression: u.expression,
                    source: u.source,
                    sourceFile: u.sourceFile,
                    description: u.description,
                })),
            },
        });
    }

    private handleGetPdfSettings(): void {
        const stored = this.getExtraSetting('pdfSettings');
        const settings = stored ? JSON.parse(stored) : { ...DEFAULT_PDF_SETTINGS };
        this.postToVue({ type: 'pdfSettingsResponse', settings });
    }

    private async handleInsertImage(): Promise<void> {
        // File picker: the image already exists on disk, so reference it in
        // place. The storage-mode prompt is only for pasted in-memory images.
        // On web (no real file path) this returns a base64 data URI.
        const src = await this.pickImageSrc();
        if (src && this._onInsertText) {
            this._onInsertText(buildImageCommentLine(src));
        }
    }

    /**
     * Store an already-captured image (e.g. a clipboard paste) and insert its
     * comment line, prompting for storage mode just like the file-picker path.
     */
    async insertImageData(image: PickedImage): Promise<void> {
        await this.storeAndInsertImage(image);
    }

    private async storeAndInsertImage(image: PickedImage): Promise<void> {
        const mode = await this.resolveImageStorageMode(image);
        if (!mode) return;

        let src: string | null = null;
        switch (mode) {
            case 'base64':
                src = `data:${image.mimeType};base64,${bytesToBase64(image.data)}`;
                break;
            case 'imagesFolder':
                src = await this.saveImageToImagesFolder(image);
                break;
            case 'customPath':
                src = await this.saveImageToCustomPath(image);
                break;
        }

        if (src && this._onInsertText) {
            this._onInsertText(buildImageCommentLine(src));
        }
    }

    /**
     * Decide how the image should be stored. On desktop we offer the base64 /
     * images-folder / custom-path choice (matching the VS Code extension) — the
     * images-folder option only when the document is saved (it needs a folder
     * to sit beside). Without disk access (pure web) base64 is the only option.
     * Large base64 embeds get a follow-up warning with a save-to-file escape hatch.
     */
    private async resolveImageStorageMode(image: PickedImage): Promise<ImageStorageMode | null> {
        if (!this.canSaveImageToDisk() || !this.quickPick) return 'base64';

        const canSaveRelative = this.canSaveImageRelativeToDocument();
        const options: QuickPickOption<ImageStorageMode>[] = [
            { label: 'Embed as Base64', detail: 'Inline the image data directly in the document', value: 'base64' },
        ];
        if (canSaveRelative) {
            options.push({ label: 'Save to ./images/ folder', detail: 'Copy the image into an images subfolder beside this document', value: 'imagesFolder' });
        }
        options.push({ label: 'Save to custom path…', detail: 'Choose where to save the image file', value: 'customPath' });

        const mode = await this.quickPick<ImageStorageMode>({
            title: 'Insert Image',
            placeholder: 'How should the image be stored?',
            options,
        });
        if (!mode) return null;

        if (mode === 'base64' && image.data.length > BASE64_WARN_BYTES) {
            const sizeKB = Math.round(image.data.length / 1024);
            const fallback: ImageStorageMode = canSaveRelative ? 'imagesFolder' : 'customPath';
            const saveLabel = canSaveRelative ? 'Save to ./images/ folder instead' : 'Save to a file instead';
            const choice = await this.quickPick<'embed' | 'save'>({
                title: 'Large image',
                placeholder: `This image is ${sizeKB} KB — embedding it inflates the document and slows processing.`,
                options: [
                    { label: saveLabel, value: 'save' },
                    { label: 'Embed anyway', value: 'embed' },
                ],
            });
            if (!choice) return null;
            if (choice === 'save') return fallback;
        }
        return mode;
    }

    private async handleSaveSourceHtml(): Promise<void> {
        const content = this.getActiveEditorContent();
        const apiSettings = buildApiSettings(this.settings);
        const { sourceFilePath } = await this.buildFileContext(content);
        const result = await this.apiClient.convert(content, apiSettings, 'html', false, sourceFilePath);
        if (!result || result instanceof ArrayBuffer) return;
        await this.saveExportedFile({
            defaultName: 'calcpad-output.html',
            data: result.html,
            mime: 'text/html;charset=utf-8',
            extensions: ['html', 'htm'],
            dialogTitle: 'Save HTML',
        });
    }

    private async handleSaveDocx(): Promise<void> {
        const content = this.getActiveEditorContent();
        const apiSettings = buildApiSettings(this.settings);
        const { sourceFilePath } = await this.buildFileContext(content);
        const buf = await this.apiClient.convertDocx(content, apiSettings, sourceFilePath);
        if (!buf) return;
        await this.saveExportedFile({
            defaultName: 'calcpad-output.docx',
            data: buf,
            mime: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
            extensions: ['docx'],
            dialogTitle: 'Save Word Document',
        });
    }

    private async handleGetPlots(): Promise<void> {
        const content = this.getActiveEditorContent();
        if (!content.trim()) {
            this._cachedPlots = [];
            this.postToVue({ type: 'plotsResponse', plots: [] });
            return;
        }
        const apiSettings = buildApiSettings(this.settings);
        const { sourceFilePath } = await this.buildFileContext(content);
        const result = await this.apiClient.convert(content, apiSettings, 'html', false, sourceFilePath);
        const html = result && !(result instanceof ArrayBuffer) ? result.html : '';
        this._cachedPlots = extractPlotsFromHtml(html);
        this.postToVue({
            type: 'plotsResponse',
            plots: this._cachedPlots.map(p => ({
                index: p.index,
                ext: p.ext,
                dataUri: p.dataUri,
                sizeBytes: p.bytes.length,
            })),
        });
    }

    private async handleSavePlot(index: number): Promise<void> {
        const plot = this._cachedPlots[index];
        if (!plot) return;
        const name = `plot-${index + 1}.${plot.ext}`;
        await this.saveExportedFile({
            defaultName: name,
            data: plot.bytes,
            mime: plot.mime,
            extensions: [plot.ext],
            dialogTitle: 'Save Plot',
        });
    }

    private async handleSavePlotsZip(): Promise<void> {
        if (this._cachedPlots.length === 0) return;
        const zipBytes = buildZip(
            this._cachedPlots.map(p => ({
                name: `plot-${p.index + 1}.${p.ext}`,
                bytes: p.bytes,
            })),
        );
        await this.saveExportedFile({
            defaultName: 'calcpad-plots.zip',
            data: zipBytes,
            mime: 'application/zip',
            extensions: ['zip'],
            dialogTitle: 'Save Plots ZIP',
        });
    }

    private async handleGeneratePdf(): Promise<void> {
        if (!(await this.runPdfPreflight())) return;

        const content = this.getActiveEditorContent();
        const apiSettings = buildApiSettings(this.settings);
        const { sourceFilePath } = await this.buildFileContext(content);

        try {
            const pdfBytes = await this.generatePdfBytes(content, apiSettings, sourceFilePath);
            if (!pdfBytes) return;
            const savedPath = await this.saveExportedFile({
                defaultName: 'calcpad-output.pdf',
                data: pdfBytes,
                mime: 'application/pdf',
                extensions: ['pdf'],
                dialogTitle: 'Export PDF',
            });
            if (savedPath) await this.onPdfSaved(savedPath);
        } catch (err) {
            await this.onPdfError(err);
        }
    }

    private handleGoToLine(line: number): void {
        if (typeof line !== 'number') return;
        const editor = this.getActiveMonacoEditor();
        if (editor) {
            editor.revealLineInCenter(line);
            editor.setPosition({ lineNumber: line, column: 1 });
            editor.focus();
        }
    }

    /**
     * Prefer the host's active editor (set per focused editor group in the
     * desktop split layout); fall back to the first registered editor.
     */
    private getActiveMonacoEditor(): MonacoEditorLike | undefined {
        const active = (window as { calcpadActiveEditor?: MonacoEditorLike }).calcpadActiveEditor;
        const editors = (window as { monaco?: MonacoLike }).monaco?.editor?.getEditors?.();
        return active ?? editors?.[0];
    }

    /**
     * Detect the single-line metadata comment at the active editor's cursor and
     * push it (with its definition context) to the Vue panel's Metadata tab.
     * Mirrors the VS Code provider's `_computeMetadataBlock`.
     */
    private handleGetMetadataContext(): void {
        const editor = this.getActiveMonacoEditor();
        const model = editor?.getModel();
        const pos = editor?.getPosition();
        let block: MetadataCommentBlock | null = null;
        if (model && pos) {
            const lines = model.getValue().split(/\r?\n/);
            block = computeMetadataBlock(lines, pos.lineNumber - 1, this.definitionResolver());
        }
        this.postToVue({ type: 'metadataContext', block });
    }

    /**
     * Rewrite the metadata comment line the panel edited. The panel sends the
     * 0-based line, its original indentation and trailing quote, and the new
     * data object; we serialize and replace the whole line.
     */
    private handleUpdateMetadata(msg: {
        line: number;
        indent?: string;
        trailingQuote?: string;
        data: MetadataCommentData;
        isNew?: boolean;
        settings?: SettingsValues;
    }): void {
        const editor = this.getActiveMonacoEditor();
        const model = editor?.getModel();
        if (!editor || !model || typeof msg.line !== 'number') return;

        const edits: { range: MonacoRangeLike; text: string }[] = [];

        // Metadata comment (desc/params/lint/no-print) — only when it has content.
        const lineNumber = msg.line + 1;
        if (Object.keys(msg.data).length > 0 && lineNumber >= 1 && lineNumber <= model.getLineCount()) {
            const newText = serializeMetadataComment(msg.data, msg.indent ?? '', msg.trailingQuote ?? '');
            edits.push(msg.isNew
                ? { range: { startLineNumber: lineNumber, startColumn: 1, endLineNumber: lineNumber, endColumn: 1 }, text: newText + '\n' }
                : { range: { startLineNumber: lineNumber, startColumn: 1, endLineNumber: lineNumber, endColumn: model.getLineMaxColumn(lineNumber) }, text: newText });
        }

        // Document-level #settings directive. Re-scan the current document so the
        // edit targets the real line even if the comment above shifted it.
        if (msg.settings) {
            const directive = findSettingsDirectiveLine(model.getValue().split(/\r?\n/));
            const hasSettings = Object.keys(msg.settings).length > 0;
            if (hasSettings) {
                const text = serializeSettingsDirective(msg.settings);
                edits.push(directive.line !== null
                    ? { range: { startLineNumber: directive.line + 1, startColumn: 1, endLineNumber: directive.line + 1, endColumn: model.getLineMaxColumn(directive.line + 1) }, text }
                    : { range: { startLineNumber: 1, startColumn: 1, endLineNumber: 1, endColumn: 1 }, text: text + '\n' });
            } else if (directive.line !== null) {
                edits.push(this.deleteLineEdit(model, directive.line + 1));
            }
        }

        if (edits.length === 0) return;
        editor.executeEdits('calcpad-metadata', edits);

        // Re-emit context (comment + refreshed settings) at the current cursor so a
        // repeated Apply edits in place instead of inserting a duplicate.
        const pos = editor.getPosition();
        const lines = model.getValue().split(/\r?\n/);
        const block = pos ? computeMetadataBlock(lines, pos.lineNumber - 1, this.definitionResolver()) : null;
        this.postToVue({ type: 'metadataContext', block });
    }

    /** Range edit that removes a whole 1-based line, handling the last-line case. */
    private deleteLineEdit(model: MonacoModelLike, ln: number): { range: MonacoRangeLike; text: string } {
        if (ln < model.getLineCount())
            return { range: { startLineNumber: ln, startColumn: 1, endLineNumber: ln + 1, endColumn: 1 }, text: '' };
        if (ln > 1)
            return { range: { startLineNumber: ln - 1, startColumn: model.getLineMaxColumn(ln - 1), endLineNumber: ln, endColumn: model.getLineMaxColumn(ln) }, text: '' };
        return { range: { startLineNumber: 1, startColumn: 1, endLineNumber: 1, endColumn: model.getLineMaxColumn(1) }, text: '' };
    }
}

interface MonacoModelLike {
    getValue(): string;
    getLineCount(): number;
    getLineMaxColumn(line: number): number;
}
interface MonacoRangeLike {
    startLineNumber: number;
    startColumn: number;
    endLineNumber: number;
    endColumn: number;
}
interface MonacoEditorLike {
    getModel(): MonacoModelLike | null;
    getPosition(): { lineNumber: number; column: number } | null;
    revealLineInCenter(line: number): void;
    setPosition(pos: { lineNumber: number; column: number }): void;
    executeEdits(source: string, edits: { range: MonacoRangeLike; text: string }[]): void;
    focus(): void;
}
interface MonacoLike {
    editor: {
        getEditors(): MonacoEditorLike[];
        getModels(): MonacoModelLike[];
    };
}
