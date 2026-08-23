import { CalcpadApiClient } from '../../api/client';
import { CalcpadSnippetService } from '../snippets';
import { CalcpadDefinitionsService } from '../definitions';
import { parseHeadings } from '../headings';
import { serializeMetadataComment, serializeSettingsDirective, computeMetadataBlock, buildDefinitionResolver, pdfSettingsFromDocument, hasMetadataContent } from '../../text/metadata-comment';
import type { MetadataCommentData, MetadataCommentBlock, MetadataLayout, DefinitionResolver, SettingsValues } from '../../text/metadata-comment';
import { findUiDirectiveBlock, serializeUiDirective } from '../../text/ui-directive';
import type { UiDirectiveData } from '../../text/ui-directive';
import type { DefinitionsResponse, ExportVariant } from '../../types/api';
import { getDefaultSettings, buildApiSettings, coerceWriteMode, writesAllowed } from '../../types/settings';
import type { CalcpadSettings, WriteMode } from '../../types/settings';
import { resolveStoredPdfSettings, resolveEffectivePdfSettings } from '../../types/pdf-settings';
import type { PdfSettings } from '../../types/pdf-settings';
import { buildImageCommentLine, bytesToBase64 } from '../image-utils';
import type { ImageStorageMode, PickedImage } from '../image-utils';
import { extractPlotsFromHtml, type ExtractedPlot } from '../plot-extract';
import { extractUiControls } from '../ui-overrides';
import type { UiControl } from '../ui-overrides';
import { COMPILED_MIME } from '../cpdz';
import { DEFAULT_PREVIEW_SIZE_MB, DEFAULT_CONSOLE_MESSAGES_PER_DOCUMENT } from '../preview-limits';
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

/**
 * The convert-request fields a given export variant renders with. `unwrapped` goes to a
 * different endpoint entirely, hence the flag rather than a field.
 *
 * No variant carries line anchors: an exported file is read, not navigated, so the per-line
 * ids and error-summary boxes the preview relies on are always suppressed.
 */
export interface VariantRender {
    forPrint: boolean;
    enableUi: boolean;
    unwrap: boolean;
    /** Whether the entered `#UI` values apply to this rendering. */
    useOverrides: boolean;
    /** Suffix for the save dialog's title, e.g. "Export Preview PDF". */
    label: string;
}

/**
 * `report` is the default: the print layout, with `#pre` hidden and the entered `#UI` values
 * applied. `preview` is what the results pane shows. `input` is the form itself — `enableUi`
 * needs `forPrint` false, since the server drops UI mode for print output.
 *
 * `previewAppliesUiOverrides` is the "Apply `#UI` Values in Preview" setting, and is the whole
 * of what Preview's `#UI` handling depends on: an exported Preview shows what the Preview pane
 * shows, so the setting has to reach both or the two disagree.
 */
export function variantRender(variant: ExportVariant, previewAppliesUiOverrides = false): VariantRender {
    switch (variant) {
        case 'preview':
            return { forPrint: false, enableUi: false, unwrap: false, useOverrides: previewAppliesUiOverrides, label: 'Preview' };
        case 'input':
            return { forPrint: false, enableUi: true, unwrap: false, useOverrides: true, label: 'Input Form' };
        case 'unwrapped':
            return { forPrint: false, enableUi: false, unwrap: true, useOverrides: false, label: 'Unwrapped' };
        default:
            return { forPrint: true, enableUi: false, unwrap: false, useOverrides: true, label: '' };
    }
}

/**
 * Save-dialog title naming the variant, so four PDF buttons don't all open an identically
 * titled dialog. The default report variant stays unqualified: "Export PDF".
 */
function exportDialogTitle(verb: string, format: string, variant: ExportVariant): string {
    const label = variantRender(variant).label;
    return label ? `${verb} ${label} ${format}` : `${verb} ${format}`;
}

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
    protected _onGoToLine: ((line: number) => boolean) | null = null;
    protected quickPick: QuickPickFn | null = null;
    private _uiOverridesProvider: (() => Record<string, string> | undefined) | null = null;
    private _uiControlsProvider: (() => UiControl[] | null) | null = null;
    private _uiControlsSink: ((controls: UiControl[]) => void) | null = null;
    private _uiOverridesSink: ((overrides: Record<string, string>) => void) | null = null;
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

    writeMode(): WriteMode {
        return coerceWriteMode(this.getExtraSetting('writeMode'));
    }

    /** Whether a render with these flags may run `#write`/`#append`. */
    mayWrite(forPrint: boolean, enableUi = false): boolean {
        return writesAllowed(this.writeMode(), forPrint, enableUi);
    }

    /** Read an "extra" (non-CalcpadSettings) preference. */
    abstract getExtraSetting(key: string): string | undefined;
    /** Persist an arbitrary extra preference. */
    abstract setExtraSetting(key: string, value: string): void;

    set onInsertText(handler: (text: string) => void) {
        this._onInsertText = handler;
    }

    /**
     * Host hook for sidebar line navigation, returning true when it handled the jump
     * itself. Only the host knows which results pane is on screen: input mode hides the
     * editor, so the navigation has to land in the rendered form instead.
     */
    set onGoToLine(handler: (line: number) => boolean) {
        this._onGoToLine = handler;
    }

    /** Host injects a modal list picker used by the image-storage prompt. */
    setQuickPick(fn: QuickPickFn): void {
        this.quickPick = fn;
    }

    /**
     * Host injects a lookup for the active document's entered `#UI` values. The store lives
     * with the editor groups (it is keyed per document), so the bridge can't own it — but
     * report and input-form exports need it to render what the user actually typed.
     */
    setUiOverridesProvider(fn: () => Record<string, string> | undefined): void {
        this._uiOverridesProvider = fn;
    }

    protected activeUiOverrides(): Record<string, string> | undefined {
        return this._uiOverridesProvider?.() ?? undefined;
    }

    /**
     * Host injects the controls of the active document's last input-form render, and a way
     * to record a fresh set. Null means the document has not been rendered as a form yet,
     * which is what makes the Properties tab withhold its used/unused verdicts rather than
     * declare every saved value orphaned.
     */
    setUiControlsProvider(fn: () => UiControl[] | null): void {
        this._uiControlsProvider = fn;
    }

    /** Host injects where a render made here records its controls, keyed by its own document. */
    setUiControlsSink(fn: (controls: UiControl[]) => void): void {
        this._uiControlsSink = fn;
    }

    /** Host injects a sink for the entered `#UI` values, so an edit made in the panel sticks. */
    setUiOverridesSink(fn: (overrides: Record<string, string>) => void): void {
        this._uiOverridesSink = fn;
    }

    /** Pushes the cached controls, so the panel follows document and cursor changes. */
    refreshUiControls(): void {
        this.postToVue({ type: 'uiControls', controls: this._uiControlsProvider?.() ?? null });
    }

    /**
     * Answers the panel's request for the live controls by rendering the document as a
     * form. Deliberately not served from the cache: this is what the panel asks when it
     * has no answer or wants a fresh one, and the cache is only as new as the last time
     * the form itself was shown.
     */
    private async handleGetUiControls(): Promise<void> {
        const content = this.getActiveEditorContent();
        const { sourceFilePath } = await this.buildFileContext(content);
        const rendered = await this.renderForExport(content, buildApiSettings(this.settings), sourceFilePath, 'input');
        // A failed render leaves the panel unresolved rather than empty - "no controls"
        // and "could not tell" must not read the same to a purge button.
        if (rendered == null) return;
        const controls = extractUiControls(rendered);
        this._uiControlsSink?.(controls);
        this.postToVue({ type: 'uiControls', controls });
    }

    /** The `#UI` options an export of `variant` should render with. */
    /** The "Apply `#UI` Values in Preview" setting: Preview renders entered values, not declared ones. */
    protected previewAppliesUiOverrides(): boolean {
        return this.getExtraSetting('previewUiOverrides') === 'true';
    }

    protected uiOptionsFor(variant: ExportVariant): { enableUi: boolean; uiOverrides?: Record<string, string> } {
        const render = variantRender(variant, this.previewAppliesUiOverrides());
        return {
            enableUi: render.enableUi,
            uiOverrides: render.useOverrides ? this.activeUiOverrides() : undefined,
        };
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
            // Read per open rather than cached, so there is nothing to broadcast.
            case 'updateAutoInputMode':
                this.setExtraSetting('autoInputMode', String(message.enabled));
                break;
            case 'updatePreviewUiOverrides':
                this.setExtraSetting('previewUiOverrides', String(message.enabled));
                this.postToVue({ type: 'previewUiOverridesChanged', enabled: !!message.enabled });
                break;
            case 'updateLinterMinSeverity':
                this.setExtraSetting('linterMinSeverity', message.severity);
                this.postToVue({ type: 'linterMinSeverityChanged', severity: message.severity });
                break;
            case 'updateMaxOutputLines':
                this.setExtraSetting('maxOutputLines', String(message.value));
                this.postToVue({ type: 'maxOutputLinesChanged', value: message.value });
                break;
            case 'updateMaxPreviewSize':
                this.setExtraSetting('maxPreviewSizeMB', String(message.value));
                this.postToVue({ type: 'maxPreviewSizeChanged', value: message.value });
                break;
            case 'updateMaxPreviewConsoleMessages':
                this.setExtraSetting('maxPreviewConsoleMessages', String(message.value));
                this.postToVue({ type: 'maxPreviewConsoleMessagesChanged', value: message.value });
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
                this.handleGeneratePdf(message.variant);
                break;
            case 'saveSourceHtml':
                this.handleSaveSourceHtml(message.variant);
                break;
            case 'saveDocx':
                this.handleSaveDocx(message.variant);
                break;
            case 'saveCompiled':
                this.saveCompiled();
                break;
            case 'savePortable':
                this.savePortable();
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
            case 'updateWriteMode':
                this.setExtraSetting('writeMode', coerceWriteMode(message.mode));
                this.postToVue({ type: 'writeModeChanged', mode: this.writeMode() });
                break;
            case 'writeFilesNow':
                this.handleWriteFilesNow();
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
            case 'getUiControls':
                this.handleGetUiControls();
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
    /**
     * Embeds locally-referenced images so a compiled worksheet travels as one file.
     * Hosts with no filesystem hand the source back untouched — a browser has no
     * relative paths to resolve in the first place.
     */
    protected async buildCompiledSource(content: string): Promise<string> { return content; }
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
        variant: ExportVariant,
    ): Promise<ArrayBuffer | null>;

    protected buildSettingsResponseExtras(): Record<string, unknown> | Promise<Record<string, unknown>> { return {}; }
    protected async runPdfPreflight(): Promise<boolean> { return true; }
    /** Return `true` to have the export retried once — hosts use this after installing a browser. */
    protected async onPdfError(_err: unknown): Promise<boolean> { return false; }
    /** Called after a PDF is successfully written, with the saved path (platforms that have one). */
    protected async onPdfSaved(_filePath: string): Promise<void> { /* default no-op */ }
    /**
     * Report an export that could not be produced. The message reaches the Output panel
     * everywhere; platforms with a native dialog put one up on top of that.
     */
    protected async onExportError(message: string): Promise<void> {
        this.postToVue({ type: 'exportError', message });
    }
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
            enableAutoInputMode: this.getExtraSetting('autoInputMode') !== 'false',
            enablePreviewUiOverrides: this.previewAppliesUiOverrides(),
            linterMinSeverity: this.getExtraSetting('linterMinSeverity') || 'information',
            maxOutputLines: Number(this.getExtraSetting('maxOutputLines')) || 1000,
            maxPreviewSizeMB: Number(this.getExtraSetting('maxPreviewSizeMB')) || DEFAULT_PREVIEW_SIZE_MB,
            maxPreviewConsoleMessages: Number(this.getExtraSetting('maxPreviewConsoleMessages'))
                || DEFAULT_CONSOLE_MESSAGES_PER_DOCUMENT,
            editorFontFamily: this.getExtraSetting('editorFontFamily') ?? 'JuliaMono',
            writeMode: this.writeMode(),
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
        this.postToVue({ type: 'pdfSettingsResponse', settings: resolveStoredPdfSettings(this.getStoredPdfOptions()) });
    }

    /** The raw persisted PDF options, however much of `PdfSettings` was actually stored. */
    protected getStoredPdfOptions(): Partial<PdfSettings> {
        const stored = this.getExtraSetting('pdfSettings');
        if (!stored) return {};
        try {
            return JSON.parse(stored);
        } catch {
            return {};
        }
    }

    /**
     * The PDF options an export should actually use: the stored defaults with the
     * document's own `pdf` metadata comment layered over them, key by key. The
     * Settings tab keeps editing the stored set alone — this merge is only for the
     * request that generates a PDF.
     */
    protected getEffectivePdfOptions(content: string): PdfSettings {
        return resolveEffectivePdfSettings(this.getStoredPdfOptions(), pdfSettingsFromDocument(content.split('\n')));
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

    private async handleSaveSourceHtml(variant: ExportVariant = 'report'): Promise<void> {
        const content = this.getActiveEditorContent();
        const apiSettings = buildApiSettings(this.settings);
        const { sourceFilePath } = await this.buildFileContext(content);
        const rendered = await this.renderForExport(content, apiSettings, sourceFilePath, variant);
        if (rendered == null) return;
        await this.saveExportedFile({
            defaultName: 'calcpad-output.html',
            data: rendered,
            mime: 'text/html;charset=utf-8',
            extensions: ['html', 'htm'],
            dialogTitle: exportDialogTitle('Save', 'HTML', variant),
        });
    }

    private async handleSaveDocx(variant: ExportVariant = 'report'): Promise<void> {
        const render = variantRender(variant, this.previewAppliesUiOverrides());
        // A form and a code listing have no meaningful Word rendering, so the Export tab
        // doesn't offer them; guard anyway rather than emit a nonsense document.
        if (render.enableUi || render.unwrap) return;
        const content = this.getActiveEditorContent();
        const apiSettings = buildApiSettings(this.settings);
        const { sourceFilePath } = await this.buildFileContext(content);
        const buf = await this.apiClient.convertDocx(content, apiSettings, sourceFilePath, {
            forPrint: render.forPrint,
            uiOverrides: render.useOverrides ? this.activeUiOverrides() : undefined,
            write: this.mayWrite(render.forPrint, render.enableUi),
        });
        if (!buf) return;
        await this.saveExportedFile({
            defaultName: 'calcpad-output.docx',
            data: buf,
            mime: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
            extensions: ['docx'],
            dialogTitle: exportDialogTitle('Save', 'Word Document', variant),
        });
    }

    /**
     * Compiles the active document to a `.cpdz` and prompts for a location. This is an
     * export, not a Save As: the open document keeps its own path and stays editable.
     * Returns the saved path where the platform has one.
     *
     * The worksheet is bundled first — includes expanded, `#read` data inlined — then its
     * images are embedded, in that order: an included file's images only resolve once the
     * server has rewritten their paths.
     */
    async saveCompiled(): Promise<string | null> {
        const content = this.getActiveEditorContent();
        const { sourceFilePath } = await this.buildFileContext(content);
        const bundled = await this.apiClient.bundlePortable(content, sourceFilePath);
        if (bundled.content == null) {
            await this.onExportError(`This worksheet cannot be compiled:\n${bundled.errors.join('\n')}`);
            return null;
        }
        // Embedding the images can refuse the whole export, the same way the server refuses
        // one carrying too much `#read` data, so it is reported like that refusal rather
        // than thrown at whatever invoked the save.
        let compiled: string;
        try {
            compiled = await this.buildCompiledSource(bundled.content);
        } catch (error) {
            await this.onExportError(
                `This worksheet cannot be compiled:\n${error instanceof Error ? error.message : String(error)}`);
            return null;
        }
        const bytes = await this.apiClient.encodeCpdz(compiled);
        if (!bytes) return null;
        return this.saveExportedFile({
            defaultName: 'calcpad-worksheet.cpdz',
            data: bytes,
            mime: COMPILED_MIME,
            extensions: ['cpdz'],
            dialogTitle: 'Save Compiled Worksheet',
        });
    }

    /**
     * Packs the active document and everything it references into a portable archive, then
     * prompts for a location. Unlike a compiled worksheet this stays text: what comes out is
     * the document as written, with only its paths pointing somewhere else — at the folder of
     * references packed beside it. The recipient can read and edit it.
     *
     * The server does the packing, since resolving the references means reading them.
     */
    async savePortable(): Promise<string | null> {
        const content = this.getActiveEditorContent();
        const { sourceFilePath } = await this.buildFileContext(content);
        const packaged = await this.apiClient.packagePortable(content, sourceFilePath);
        if (!packaged.zip) {
            await this.onExportError(
                `This worksheet cannot be packaged:\n${packaged.errors.join('\n')}`);
            return null;
        }
        return this.saveExportedFile({
            defaultName: packaged.name ?? 'calcpad-worksheet.zip',
            data: packaged.zip,
            mime: 'application/zip',
            extensions: ['zip'],
            dialogTitle: 'Export Portable Package',
        });
    }

    /**
     * Render `variant` to HTML for writing to a file: never with line anchors, and with the
     * entered `#UI` values only where the variant calls for them. Also the first half of the
     * PDF pipeline, which feeds this HTML to `/pdf`.
     */
    protected async renderForExport(
        content: string,
        apiSettings: unknown,
        sourceFilePath: string | undefined,
        variant: ExportVariant,
    ): Promise<string | null> {
        const render = variantRender(variant);
        const write = this.mayWrite(render.forPrint, render.enableUi);
        const result = render.unwrap
            ? await this.apiClient.convertUnwrapped(content, apiSettings, sourceFilePath, undefined, { write })
            : await this.apiClient.convert(
                content, apiSettings, 'html', render.forPrint, sourceFilePath, undefined,
                this.uiOptionsFor(variant), false, { write });
        return result && !(result instanceof ArrayBuffer) ? result.html : null;
    }

    /**
     * Runs the document's `#write`/`#append` directives now, whatever the write-mode setting
     * says. The report render is the one used: it is the authoritative output, so `#post`
     * blocks and entered `#UI` values are included exactly as a saved report would have them.
     *
     * Rendered with line anchors on, since that is what makes the parser record its errors —
     * the HTML is discarded, only the errors are read.
     */
    private async handleWriteFilesNow(): Promise<void> {
        const content = this.getActiveEditorContent();
        if (!content.trim()) return;
        const { sourceFilePath } = await this.buildFileContext(content);
        const result = await this.apiClient.convert(
            content, buildApiSettings(this.settings), 'html', true, sourceFilePath, undefined,
            this.uiOptionsFor('report'), true, { write: true });
        if (result == null || result instanceof ArrayBuffer) {
            await this.onExportError('The document could not be run, so nothing was written.');
            return;
        }
        const errors = result.errors ?? [];
        this.postToVue({ type: 'updateConvertErrors', errors });
        this.postToVue({
            type: 'writeFilesResult',
            ok: errors.length === 0,
            message: errors.length === 0
                ? 'Data written successfully'
                : `Ran with ${errors.length} error${errors.length === 1 ? '' : 's'} — some output may be missing.`,
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

    private async handleGeneratePdf(variant: ExportVariant = 'report'): Promise<void> {
        if (!(await this.runPdfPreflight())) return;

        const content = this.getActiveEditorContent();
        const apiSettings = buildApiSettings(this.settings);
        const { sourceFilePath } = await this.buildFileContext(content);

        // Two attempts at most: onPdfError asks for a retry only when it resolved the
        // cause (installing a browser), so the second attempt either works or reports.
        for (let attempt = 0; attempt < 2; attempt++) {
            try {
                const pdfBytes = await this.generatePdfBytes(content, apiSettings, sourceFilePath, variant);
                if (!pdfBytes) return;
                const savedPath = await this.saveExportedFile({
                    defaultName: 'calcpad-output.pdf',
                    data: pdfBytes,
                    mime: 'application/pdf',
                    extensions: ['pdf'],
                    dialogTitle: exportDialogTitle('Export', 'PDF', variant),
                });
                if (savedPath) await this.onPdfSaved(savedPath);
                return;
            } catch (err) {
                const retry = await this.onPdfError(err);
                if (!retry || attempt === 1) return;
            }
        }
    }

    private handleGoToLine(line: number): void {
        if (typeof line !== 'number') return;
        if (this._onGoToLine?.(line)) return;
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
        // Sent with the block so the panel's saved-values list follows the document the
        // cursor is actually in, rather than keeping the last one it asked about.
        this.refreshUiControls();
    }

    /**
     * Rewrite the metadata comment line the panel edited. The panel sends the
     * 0-based line, its original indentation and trailing quote, and the new
     * data object; we serialize and replace the whole line.
     */
    private handleUpdateMetadata(msg: {
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
    }): void {
        const editor = this.getActiveMonacoEditor();
        const model = editor?.getModel();
        if (!editor || !model || typeof msg.line !== 'number') return;

        const edits: { range: MonacoRangeLike; text: string }[] = [];

        // Metadata comment (desc/params/lint/no-print) — only when it has content.
        // A multi-line comment spans line..endLine; layout preserves its shape.
        const lineNumber = msg.line + 1;
        if (lineNumber >= 1 && lineNumber <= model.getLineCount()) {
            const endLineNumber = (msg.endLine ?? msg.line) + 1;
            if (hasMetadataContent(msg.data)) {
                const newText = serializeMetadataComment(msg.data, msg.indent ?? '', msg.trailingQuote ?? '', msg.layout);
                edits.push(msg.isNew
                    ? { range: { startLineNumber: lineNumber, startColumn: 1, endLineNumber: lineNumber, endColumn: 1 }, text: newText + '\n' }
                    : { range: { startLineNumber: lineNumber, startColumn: 1, endLineNumber: endLineNumber, endColumn: model.getLineMaxColumn(endLineNumber) }, text: newText });
            } else if (!msg.isNew) {
                // Every key removed - clearing the last field, or purging the last saved
                // value - leaves an empty comment, so the comment goes with them.
                for (let ln = endLineNumber; ln >= lineNumber; ln--)
                    edits.push(this.deleteLineEdit(model, ln));
            }
        }

        // #settings directive under the cursor. `settingsLine` (0-based) points at
        // the existing directive to rewrite, or null to create a new one at the
        // cursor — so multiple directives can coexist, each edited where it lives.
        const settingsText = msg.settings ? serializeSettingsDirective(msg.settings, msg.settingsLayout) : '';
        let insertedSettings = false;
        if (msg.settings) {
            const dirLine = msg.settingsLine ?? null;
            const dirEndLine = msg.settingsEndLine ?? dirLine;
            const hasExisting = dirLine !== null && dirLine >= 0 && dirLine < model.getLineCount();
            const hasSettings = Object.keys(msg.settings).length > 0;
            if (hasSettings) {
                edits.push(hasExisting
                    ? { range: { startLineNumber: dirLine! + 1, startColumn: 1, endLineNumber: dirEndLine! + 1, endColumn: model.getLineMaxColumn(dirEndLine! + 1) }, text: settingsText }
                    : { range: { startLineNumber: lineNumber, startColumn: 1, endLineNumber: lineNumber, endColumn: 1 }, text: settingsText + '\n' });
                insertedSettings = !hasExisting;
            } else if (hasExisting) {
                for (let ln = dirEndLine! + 1; ln >= dirLine! + 1; ln--)
                    edits.push(this.deleteLineEdit(model, ln));
            }
        }

        // #UI directive at the cursor. Always a single-line replace — a #UI
        // line never shares a physical line with the comment or #settings
        // edits above, so this never overlaps them.
        if (msg.ui && typeof msg.uiLine === 'number' && msg.uiLine >= 0 && msg.uiLine < model.getLineCount()) {
            const uiLineNumber = msg.uiLine + 1;
            const uiLineText = model.getValue().split(/\r?\n/)[msg.uiLine];
            const uiBlock = findUiDirectiveBlock([uiLineText], 0);
            if (uiBlock) {
                edits.push({
                    range: { startLineNumber: uiLineNumber, startColumn: 1, endLineNumber: uiLineNumber, endColumn: model.getLineMaxColumn(uiLineNumber) },
                    text: serializeUiDirective(msg.ui, uiBlock),
                });
            }
        }

        if (edits.length === 0) return;
        editor.executeEdits('calcpad-metadata', edits);

        // Entered values are held in memory and written out on demand, so an edit to the
        // saved ones has to reach the store too - otherwise the next "Save values" puts
        // the purged keys straight back.
        const overrides = msg.data.uiOverrides;
        if (this._uiOverridesSink && overrides && typeof overrides === 'object' && !Array.isArray(overrides))
            this._uiOverridesSink(overrides as Record<string, string>);

        // A freshly created directive: park the cursor on it so the re-emitted
        // context binds to it and a repeated Apply edits in place, not duplicates.
        if (insertedSettings) {
            const target = this.nearestLineMatching(model.getValue().split(/\r?\n/), settingsText, msg.line);
            if (target !== null)
                editor.setPosition({ lineNumber: target + 1, column: model.getLineMaxColumn(target + 1) });
        }

        // Re-emit context (comment + refreshed settings) at the current cursor so a
        // repeated Apply edits in place instead of inserting a duplicate.
        const pos = editor.getPosition();
        const lines = model.getValue().split(/\r?\n/);
        const block = pos ? computeMetadataBlock(lines, pos.lineNumber - 1, this.definitionResolver()) : null;
        this.postToVue({ type: 'metadataContext', block });
    }

    /** 0-based index of the line equal to `text`, closest to `near`; null if none. */
    private nearestLineMatching(lines: string[], text: string, near: number): number | null {
        let best: number | null = null;
        for (let i = 0; i < lines.length; i++) {
            if (lines[i] !== text) continue;
            if (best === null || Math.abs(i - near) < Math.abs(best - near)) best = i;
        }
        return best;
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
