import * as monaco from 'monaco-editor';
import { createApp, nextTick } from 'vue';
import App from './App.vue';
import pkg from '../package.json';
import CalcpadAppVue from 'calcpad-frontend/vue/components/CalcpadApp.vue';
import { initMessaging } from 'calcpad-frontend/vue/services/messaging';
import { MessageBridge } from './services/message-bridge';
import { buildApiSettings } from 'calcpad-frontend/types/settings';
import {
    findMetadataCommentBlock,
    serializeMetadataComment,
    buildDefinitionResolver,
    UiOverrideStore,
    writeUiOverrides,
    extractUiControls,
    isCompiledPath,
    documentHasUiDirectives,
} from 'calcpad-frontend';
import { registerCalcpadLanguage, registerCalcpadTheme, remeasureEditorFontsWhenReady, resolveEditorFontFamily } from './editor/setup';
import { setAppTheme, coerceAppTheme } from './editor/app-theme';
import { registerSemanticTokensProvider } from './editor/semantic-tokens';
import { setupDiagnostics } from './editor/diagnostics';
import { registerCompletionProvider } from './editor/completions';
import { registerIncludeCompletionProvider } from './editor/include-completions';
import { registerHoverProvider } from './editor/hover';
import {
    registerDefinitionProvider,
    registerIncludeLinkProvider,
    registerReferenceProvider,
    registerRenameProvider,
    type IncludeFileOpener,
    type IncludeUriResolver,
} from './editor/references';
import { attachQuickTyper } from './editor/quick-type';
import { attachOperatorReplacer } from './editor/operator-replacer';
import { attachAutoIndenter } from './editor/auto-indent';
import { registerFormattingCommands } from './editor/formatting-commands';
import { registerFormatDocumentProvider } from './editor/format-document';
import { setActiveDocumentKeyResolver, getActiveDocumentKey, type EditorBridge } from './editor/bridge';
import { EditorGroup } from './editor/editor-group';
import type { TabManager } from './tabs/tab-manager';
import type { UiControl } from 'calcpad-frontend';
import './editor/vscode-variables.css';
import 'calcpad-frontend/vue/styles/base.css';
import './styles/app.css';

// Monaco worker setup — must run before editor creation
import './editor/workers';

/** Runtime check: are we running inside a Tauri webview? */
const isTauri = typeof (window as any).__TAURI_INTERNALS__ !== 'undefined';

// Determine server URL:
// 1. ?server= query param
// 2. VITE_SERVER_URL env var
// 3. Default to same origin
function getServerUrl(): string {
    const params = new URLSearchParams(window.location.search);
    const fromParam = params.get('server');
    if (fromParam) return fromParam;

    if (import.meta.env.VITE_SERVER_URL) return import.meta.env.VITE_SERVER_URL;

    return window.location.origin;
}

/** Idle-state preview HTML — same content the VS Code extension shows when
 *  the editor buffer is empty. Quick reference for formatting hotkeys. */
function getEmptyPreviewHtml(theme: 'light' | 'dark'): string {
    const c = theme === 'light'
        ? { fg: '#6e6e6e', bg: '#ffffff', link: '#0066cc' }
        : { fg: '#858585', bg: '#1e1e1e', link: '#4FC1FF' };
    return `<!DOCTYPE html>
<html>
<head>
    <meta charset="UTF-8">
    <title>CalcpadCE Preview</title>
    <style>
        body { color: ${c.fg}; background: ${c.bg}; padding: 20px; font-family: var(--vscode-font-family, system-ui, sans-serif); }
        h3 { text-align: center; }
        p { text-align: center; }
        table { margin: 1em auto; border-collapse: collapse; text-align: left; font-size: 0.9em; }
        th, td { padding: 4px 12px; }
        th { text-align: right; font-weight: normal; opacity: 0.7; }
        td { font-family: var(--vscode-editor-font-family, monospace); }
        h4 { text-align: center; margin-top: 1.5em; margin-bottom: 0.3em; }
        a { color: ${c.link}; }
    </style>
</head>
<body>
    <h3>Empty Document</h3>
    <p>Start typing CalcpadCE code to see the preview.</p>
    <h4>Formatting Hotkeys</h4>
    <table>
        <tr><th>Bold</th><td>Ctrl+B</td></tr>
        <tr><th>Italic</th><td>Ctrl+I</td></tr>
        <tr><th>Underline</th><td>Ctrl+U</td></tr>
        <tr><th>Subscript</th><td>Ctrl+=</td></tr>
        <tr><th>Superscript</th><td>Ctrl+Shift+=</td></tr>
        <tr><th>Heading 1-6</th><td>Ctrl+1 ... Ctrl+6</td></tr>
        <tr><th>Paragraph</th><td>Ctrl+L</td></tr>
        <tr><th>Line Break</th><td>Ctrl+R</td></tr>
        <tr><th>Bulleted List</th><td>Ctrl+Shift+L</td></tr>
        <tr><th>Numbered List</th><td>Ctrl+Shift+N</td></tr>
        <tr><th>Toggle Comment</th><td>Ctrl+Q</td></tr>
    </table>
    <h4>Resources</h4>
    <p><a href="https://github.com/imartincei/CalcpadCE">CalcpadCE on GitHub</a></p>
    <p><a href="https://calcpad-ce.org/">calcpad-ce.org</a></p>
    <p><a href="https://imartincei.github.io/CalcpadCE/">CalcpadCE Documentation</a></p>
</body>
</html>`;
}

function getSampleContent(): string {
    return `'CalcpadCE Web Editor
'Enter your calculations below

a = 3
b = 4
c = √(a² + b²)
`;
}

/**
 * Encode raw RGBA pixels (as returned by Tauri's native clipboard readImage)
 * to PNG bytes via an offscreen canvas, so a pasted image can be embedded or
 * saved like a file-picked one.
 */
async function rgbaToPng(rgba: Uint8Array, width: number, height: number): Promise<Uint8Array | null> {
    const canvas = document.createElement('canvas');
    canvas.width = width;
    canvas.height = height;
    const ctx = canvas.getContext('2d');
    if (!ctx) return null;
    ctx.putImageData(new ImageData(new Uint8ClampedArray(rgba), width, height), 0, 0);
    const blob = await new Promise<Blob | null>(resolve => canvas.toBlob(resolve, 'image/png'));
    if (!blob) return null;
    return new Uint8Array(await blob.arrayBuffer());
}

/**
 * Native message box shown when the calculation server never becomes ready.
 * The editor itself keeps working; only server-backed features (preview,
 * linting, export) need it.
 */
async function showServerBlockedDialog(details: string): Promise<void> {
    const { message: dialogMessage } = await import('@tauri-apps/plugin-dialog');
    const body =
        "CalcpadCE's calculation server started but never became ready.\n\n"
        + 'The editor still works, but preview, linting, and PDF/Word export '
        + 'need the server. Choose Server → Restart Server to try again.\n\n'
        + `Details: ${details}`;
    try {
        await dialogMessage(body, {
            title: 'CalcpadCE server unavailable',
            kind: 'warning',
            okLabel: 'OK',
        });
    } catch {
        // dialog can throw if the runtime is tearing down — the buffered log
        // line in the Output panel is the fallback.
    }
}

type ResultMode = 'preview' | 'unwrapped' | 'ui' | 'report';

async function bootstrap(): Promise<void> {
    let serverUrl: string;
    let bridge: MessageBridge | null = null;
    let tauriBridge: import('./services/tauri-bridge').TauriMessageBridge | null = null;
    let serverManager: import('./services/server-manager').TauriServerManager | null = null;
    // Server-manager log lines that arrive before the Output panel mounts
    // get buffered here, then flushed when appInstance is ready.
    const pendingServerLogs: string[] = [];
    // Raw stdout/stderr lines from the Calcpad.Server sidecar (Rust's
    // `server-log` event), buffered the same way for the same reason.
    const pendingServerRawLogs: { line: string; stream: 'stdout' | 'stderr' }[] = [];

    if (isTauri) {
        // Tauri desktop: the Rust layer owns the Calcpad.Server sidecar
        // (spawn, kill on exit, port discovery). This manager just tracks
        // its URL and surfaces crashes to the Output panel.
        const { TauriServerManager } = await import('./services/server-manager');
        serverManager = new TauriServerManager({
            appendLine: (msg: string) => pendingServerLogs.push(msg),
        });
        serverManager.onServerLog = (line: string, stream: 'stdout' | 'stderr') => {
            pendingServerRawLogs.push({ line, stream });
        };

        serverManager.onStartupBlocked = (details: string) => {
            pendingServerLogs.push(`Server did not start — ${details}`);
            void showServerBlockedDialog(details);
        };

        try {
            await serverManager.start();
        } catch (err) {
            const msg = err instanceof Error ? (err.stack ?? err.message) : String(err);
            pendingServerLogs.push(`[bootstrap] Server failed to start: ${msg}`);
            console.error('[bootstrap] Server failed to start:', err);
        }
        serverUrl = serverManager.getBaseUrl() || '';

        const { TauriMessageBridge } = await import('./services/tauri-bridge');
        tauriBridge = new TauriMessageBridge(serverUrl);
        (window as any).calcpadBridge = tauriBridge;
    } else {
        // Pure web: use in-process web bridge
        serverUrl = getServerUrl();
        bridge = new MessageBridge(serverUrl);
        (window as any).calcpadBridge = bridge;
    }

    const activeBridge = tauriBridge ?? bridge!;

    // Initialize the platform messaging (reads VITE_PLATFORM='web')
    initMessaging();

    // The base message bridge's handleGoToLine looks up the editor via
    // window.monaco (matches the vscode-webview convention). Expose it here so
    // sidebar tabs (TOC, Errors) can post `goToLine` and reach Monaco.
    (window as any).monaco = monaco;

    // Mount the main app layout
    const app = createApp(App);
    const appInstance = app.mount('#app') as any;

    // Let the bridge prompt via the in-app quick-pick modal (image storage mode).
    activeBridge.setQuickPick(async ({ title, placeholder, options }) => {
        const index = await appInstance.showQuickPick({
            title,
            placeholder,
            options: options.map((o: { label: string; detail?: string }) => ({ label: o.label, detail: o.detail })),
        });
        return index == null ? null : options[index].value;
    });

    // Wait for DOM to render, then set up the editor group(s)
    await nextTick();

    registerCalcpadLanguage();
    registerCalcpadTheme();

    // Apply the persisted app theme before Monaco initializes so the editor
    // renders with the right theme first paint. The desktop bridge loads its
    // settings asynchronously, so wait for it; the web bridge is synchronous.
    if (tauriBridge) await tauriBridge.ready;
    setAppTheme(coerceAppTheme(activeBridge.getStoredColorTheme()));

    const WORD_WRAP_KEY = 'calcpad.wordWrap';
    const initialWordWrap: 'on' | 'off' =
        localStorage.getItem(WORD_WRAP_KEY) === 'off' ? 'off' : 'on';
    const initialEditorFontFamily = (activeBridge as unknown as EditorBridge).getExtraSetting('editorFontFamily') ?? 'JuliaMono';

    // ---- Editor groups ----
    // The desktop supports a single top/bottom split into two editor groups.
    // Each group owns a Monaco editor + a TabManager; `activeGroup`/`editor`/
    // `tabs` track the focused group and are reassigned on focus change so the
    // shared command/save/clipboard closures below always act on it.
    const groups = new Map<string, EditorGroup>();
    // Per-group wiring applied to every new group after the common wiring
    // (populated by the Tauri block: save commands, draft autosave, drop).
    const groupWireHooks: ((g: EditorGroup) => void)[] = [];
    let activeGroup!: EditorGroup;
    let editor!: monaco.editor.IStandaloneCodeEditor;
    let tabs!: TabManager;

    const editorBridge = activeBridge as unknown as EditorBridge;
    const getFileContext = 'buildFileContext' in activeBridge
        ? (content: string) => (activeBridge as any).buildFileContext(content)
        : undefined;

    function docKeyFor(group: EditorGroup): string {
        return `tab:${group.tabs.activeId ?? 'none'}`;
    }
    function activeDocumentKey(): string {
        return docKeyFor(activeGroup);
    }

    remeasureEditorFontsWhenReady(initialEditorFontFamily);

    // Hot-swap the editor's font family when the user picks a different one in
    // the Settings tab. Also nudge Monaco to re-measure so the glyph grid stays
    // aligned when switching to/from an async web font.
    window.addEventListener('message', (event) => {
        const msg = (event as MessageEvent).data;
        if (msg?.type !== 'editorFontFamilyChanged') return;
        const family = typeof msg.family === 'string' ? msg.family : '';
        const resolved = resolveEditorFontFamily(family);
        for (const g of groups.values()) g.editor.updateOptions({ fontFamily: resolved });
        remeasureEditorFontsWhenReady(family);
    });

    // ---- Group-scoped refresh helpers ----
    function markerToSeverityInfo(severity: monaco.MarkerSeverity) {
        switch (severity) {
            case monaco.MarkerSeverity.Error:
                return { severityClass: 'lintError', icon: '✕' };
            case monaco.MarkerSeverity.Warning:
                return { severityClass: 'warning', icon: '⚠' };
            default:
                return { severityClass: 'info', icon: 'ℹ' };
        }
    }

    function refreshProblemsFor(group: EditorGroup): void {
        const model = group.editor.getModel();
        if (!model) {
            appInstance.setProblems(group.id, []);
            return;
        }
        const markers = monaco.editor.getModelMarkers({ resource: model.uri });
        const items = markers.map(m => ({
            severity: m.severity,
            ...markerToSeverityInfo(m.severity),
            message: m.message,
            code: typeof m.code === 'string' ? m.code : m.code?.value ?? '',
            startLineNumber: m.startLineNumber,
            startColumn: m.startColumn,
            endLineNumber: m.endLineNumber,
            endColumn: m.endColumn,
        }));
        items.sort((a, b) => b.severity - a.severity);
        appInstance.setProblems(group.id, items);
    }

    async function refreshDefinitionsFor(group: EditorGroup): Promise<void> {
        const content = group.editor.getValue();
        const ctx = getFileContext ? await getFileContext(content) : {};
        editorBridge.definitions.refreshDefinitions(content, docKeyFor(group), ctx.sourceFilePath, `defs:${group.id}`);
    }

    function resolvePreviewTheme(): 'light' | 'dark' {
        const stored = editorBridge.getExtraSetting('previewTheme') ?? 'system';
        if (stored === 'light' || stored === 'dark') return stored;
        return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
    }

    // Output line the next unwrapped refresh should scroll to, per group. Set
    // by the wrapped->unwrapped two-step in the 'navigateToLine' handler.
    const pendingPreviewScrollLine = new Map<string, number>();

    // Values entered into #UI controls. Held in memory so typing in a form never
    // dirties the file; "Save values" writes them into the document.
    const uiOverrides = new UiOverrideStore();
    // Documents whose in-memory values have not been written back yet.
    const uiOverridesDirty = new Set<string>();
    // Controls each document's last input-form render produced, which is what tells the
    // Properties tab whether a saved value still applies to anything.
    const uiControls = new Map<string, UiControl[]>();

    // Tauri's `invoke`, once the desktop-only block below has imported it. Null in the
    // web build, where there is no native menu to keep in step.
    let invokeTauri: (<T>(cmd: string, args?: Record<string, unknown>) => Promise<T>) | null = null;

    /**
     * A .cpdz is a compiled worksheet: it is distributed to be filled in, not read
     * or edited, so it opens straight into the input form with the editor locked.
     * Entered values can still be saved back — the file is decoded and re-encoded
     * around the uiOverrides comment, so no source is exposed on the way through.
     */
    function applyCompiledWorksheetMode(group: EditorGroup): void {
        const activeId = group.tabs.activeId;
        const path = activeId ? group.tabs.getFilePath(activeId) : null;
        const compiled = !!path && isCompiledPath(path);
        group.editor.updateOptions({ readOnly: compiled });
        if (group === activeGroup) {
            syncSourceModeMenuItems(!compiled);
            syncInputMode();
        }
        if (compiled && appInstance.getResultMode() !== 'ui') {
            if (!appInstance.isPreviewVisible()) appInstance.togglePreview();
            appInstance.setResultMode('ui');
        }
    }

    // Files already judged for input mode. Held per path for the session: the point of the
    // whole thing is that a tab switch, or a #UI line added while editing, never drags the
    // user back into a form they deliberately left.
    const autoUiSeenPaths = new Set<string>();
    // Set while the auto-switch drives setResultMode, so the mode it picks is not persisted
    // as the session's own — one #UI document would otherwise pin every later launch to it.
    let autoUiSwitchInFlight = false;

    /**
     * Whether opening this tab should go straight to the input form: a document declaring
     * `#UI` controls is one to fill in. Decided once per file, and recorded as decided even
     * when the answer is no, so the answer cannot change under the user later.
     *
     * Split from {@link autoEnterUiMode} so the caller can skip its own preview refresh —
     * switching the result mode triggers one of its own.
     */
    function shouldAutoEnterUiMode(group: EditorGroup): boolean {
        if (!isTauri) return false;
        const activeId = group.tabs.activeId;
        if (!activeId) return false;

        const path = group.tabs.getFilePath(activeId);
        // Untitled documents are left alone: there is no file yet, so nothing to remember
        // the decision against, and a #UI line typed into a scratch buffer is being written
        // rather than filled in. A compiled worksheet is already forced to the form above.
        if (!path || isCompiledPath(path)) return false;
        if (autoUiSeenPaths.has(path)) return false;
        autoUiSeenPaths.add(path);

        // A recovered draft comes back dirty, and hiding it behind a form right after the
        // recovery prompt is the last thing the user wants to see.
        if (group.tabs.isDirty(activeId)) return false;
        if (editorBridge.getExtraSetting('autoInputMode') === 'false') return false;
        // Toggling the preview off leaves the mode at 'ui', so the pane's visibility is part
        // of "already there" — otherwise this would do nothing and show nothing.
        if (appInstance.getResultMode() === 'ui' && appInstance.isPreviewVisible()) return false;

        return documentHasUiDirectives(group.editor.getValue());
    }

    function autoEnterUiMode(): void {
        autoUiSwitchInFlight = true;
        if (!appInstance.isPreviewVisible()) appInstance.togglePreview();
        void appInstance.setResultMode('ui');
        autoUiSwitchInFlight = false;
    }

    /**
     * The result modes a compiled worksheet has nothing to render for. App.vue leaves
     * their toolbar buttons out entirely, so the native View menu drops the matching
     * entries rather than greying them: a menu that offers a mode the toolbar doesn't
     * have reads as a bug. Only the desktop build has a menu — elsewhere `invokeTauri`
     * stays null and the App.vue guard is the whole story.
     */
    let sourceModeMenuShown = true;
    function syncSourceModeMenuItems(shown: boolean): void {
        if (shown === sourceModeMenuShown) return;
        sourceModeMenuShown = shown;
        void invokeTauri?.('set_source_result_modes_visible', { visible: shown });
    }

    /**
     * Tells the sidebar whether the active document is being filled in rather than
     * edited, so the tabs that act on source can grey themselves out. Input mode hides
     * the editor; a compiled worksheet has no source to act on at all, however the
     * results pane happens to be set.
     */
    function syncInputMode(): void {
        const activeId = activeGroup.tabs.activeId;
        const path = activeId ? activeGroup.tabs.getFilePath(activeId) : null;
        const active = (!!path && isCompiledPath(path))
            || (appInstance.isPreviewVisible() && appInstance.getResultMode() === 'ui');
        window.dispatchEvent(new MessageEvent('message', {
            data: { type: 'inputModeChanged', active },
        }));
    }

    /**
     * Key that #UI values are stored under. The file path, so the same document
     * open in two tabs or two groups shares one set of entered values rather than
     * silently diverging. Unsaved documents fall back to the tab key, which is the
     * only thing that tells them apart.
     */
    function uiDocKeyFor(group: EditorGroup): string {
        const activeId = group.tabs.activeId;
        return (activeId && group.tabs.getFilePath(activeId)) || docKeyFor(group);
    }

    function activeUiDocKey(): string {
        return uiDocKeyFor(activeGroup);
    }

    function refreshUiDirtyIndicator(): void {
        appInstance.setUiOverridesDirty(uiOverridesDirty.has(activeUiDocKey()));
    }

    // The store is keyed per document and owned here, so the bridge is handed a lookup
    // rather than the store: report and input-form exports render the entered values.
    activeBridge.setUiOverridesProvider(() => uiOverrides.toRecord(activeUiDocKey()));
    activeBridge.setUiControlsProvider(() => uiControls.get(activeUiDocKey()) ?? null);
    activeBridge.setUiControlsSink((controls) => uiControls.set(activeUiDocKey(), controls));
    // An edit made in the Properties tab is an edit to the entered values, so the store
    // follows it - otherwise the next "Save values" would write the old ones back.
    activeBridge.setUiOverridesSink((overrides) => {
        const docKey = activeUiDocKey();
        uiOverrides.replace(docKey, overrides);
        uiOverridesDirty.delete(docKey);
        refreshUiDirtyIndicator();
        void refreshPreviewFor(activeGroup);
    });

    /**
     * Sidebar line navigation (the TOC) while the input form is up. The editor it would
     * normally reveal is hidden there, so the form and the report beside it are scrolled
     * instead. The cursor still moves, without taking focus off the form, so leaving
     * input mode lands on the heading last navigated to.
     */
    activeBridge.onGoToLine = (line: number) => {
        if (!appInstance.isPreviewVisible() || appInstance.getResultMode() !== 'ui') return false;
        appInstance.scrollPreviewToSourceLine(activeGroup.id, line);
        activeGroup.editor.revealLineInCenter(line);
        activeGroup.editor.setPosition({ lineNumber: line, column: 1 });
        return true;
    };

    /**
     * Whether the plain preview renders with the entered #UI values applied. Off by
     * default: preview is where the document itself is read, and seeing its own values
     * is the point. Turned on it becomes a debugging view of the filled-in form.
     */
    function previewAppliesUiOverrides(): boolean {
        return editorBridge.getExtraSetting('previewUiOverrides') === 'true';
    }

    /**
     * Seeds a document's overrides from a saved uiOverrides comment the first time
     * it is rendered in UI mode, so entered values survive reopening the file.
     */
    function seedUiOverrides(group: EditorGroup, content: string): void {
        const docKey = uiDocKeyFor(group);
        if (uiOverrides.has(docKey) || uiOverridesDirty.has(docKey)) return;
        uiOverrides.readFromSource(docKey, content);
    }

    const PREVIEW_LOADING_DELAY_MS = 400;

    async function refreshPreviewFor(group: EditorGroup): Promise<void> {
        if (!appInstance.isPreviewVisible()) return;
        if (appInstance.getResultMode() === 'ui' && group !== activeGroup) return;

        const content = group.editor.getValue();
        const settings = activeBridge.getSettings();
        const apiSettings = buildApiSettings(settings);
        const mode = appInstance.getResultMode() as ResultMode;
        const theme = resolvePreviewTheme();

        if (!content.trim()) {
            appInstance.setPreviewHtml(group.id, getEmptyPreviewHtml(theme));
            return;
        }

        const fileContext = getFileContext ? await getFileContext(content) : {};

        // Show the "Calculating…" overlay only if the round-trip runs long, so
        // fast renders never flash it (mirrors Calcpad.Wpf's delayed spinner).
        const loadingTimer = window.setTimeout(
            () => appInstance.setPreviewLoading(group.id, true),
            PREVIEW_LOADING_DELAY_MS,
        );
        let result;
        try {
            // The report applies entered values just as the input form does, so both need
            // the document's saved ones seeded first. Preview joins them when the setting
            // asks it to, which is how an error that only shows up once the form is filled
            // in gets looked at against the source.
            const overrideMode = mode === 'ui' || mode === 'report'
                || (mode === 'preview' && previewAppliesUiOverrides());
            if (overrideMode) seedUiOverrides(group, content);
            // Unwrapped always shows the document as written.
            const ui = overrideMode
                ? { enableUi: mode === 'ui', uiOverrides: uiOverrides.toRecord(uiDocKeyFor(group)) }
                : undefined;
            result = mode === 'unwrapped'
                ? await activeBridge.api.convertUnwrapped(content, apiSettings, fileContext.sourceFilePath, theme, { key: `preview:${group.id}` })
                // The report is a print layout, but on screen, so it keeps the line
                // anchors that forPrint would otherwise suppress.
                : await activeBridge.api.convert(
                    content, apiSettings, 'html', mode === 'report', fileContext.sourceFilePath, theme, ui,
                    mode === 'report' ? true : undefined, { key: `preview:${group.id}` });
        } finally {
            window.clearTimeout(loadingTimer);
            appInstance.setPreviewLoading(group.id, false);
        }

        // Consume any pending two-step scroll target for this group: only the
        // unwrapped view it was set for should honor it, and only once.
        const scrollToLine = (mode === 'unwrapped' && pendingPreviewScrollLine.get(group.id) != null)
            ? pendingPreviewScrollLine.get(group.id)
            : undefined;
        pendingPreviewScrollLine.delete(group.id);

        if (result && !(result instanceof ArrayBuffer)) {
            // Desktop: inline on-disk images so relative <img src> paths (from
            // the images-folder / custom-path insert options) render in the
            // sandboxed preview iframe, matching PDF export.
            const finalHtml = tauriBridge
                ? await tauriBridge.inlineDocumentImages(result.html)
                : result.html;
            appInstance.setPreviewHtml(group.id, finalHtml, scrollToLine);
            if (mode === 'ui') uiControls.set(uiDocKeyFor(group), extractUiControls(result.html));
            window.dispatchEvent(new MessageEvent('message', {
                data: { type: 'updateConvertErrors', errors: result.errors },
            }));
        }

        if (mode === 'ui' && appInstance.isUiPrintVisible())
            await refreshUiPrintFor(group, content, apiSettings, fileContext.sourceFilePath, theme);
    }

    /**
     * Renders the report that sits beside the input form: the print layout, with
     * the entered values applied, so #post content and the results they produce
     * are visible while filling the form in.
     */
    async function refreshUiPrintFor(
        group: EditorGroup,
        content: string,
        apiSettings: unknown,
        sourceFilePath: string | undefined,
        theme: 'light' | 'dark',
    ): Promise<void> {
        const result = await activeBridge.api.convert(
            content, apiSettings, 'html', true, sourceFilePath, theme,
            { uiOverrides: uiOverrides.toRecord(uiDocKeyFor(group)), hideErrorLines: true },
            true, { key: `preview:${group.id}` });
        if (!result || result instanceof ArrayBuffer) return;

        const html = tauriBridge
            ? await tauriBridge.inlineDocumentImages(result.html)
            : result.html;
        appInstance.setUiPrintHtml(group.id, html);
    }

    function refreshAllPreviews(): void {
        // UI mode renders the active group only — the other has no iframe.
        if (appInstance.getResultMode() === 'ui') {
            void refreshPreviewFor(activeGroup);
            return;
        }
        for (const g of groups.values()) void refreshPreviewFor(g);
    }

    // Editor -> preview sync: scroll a group's preview to its cursor's source
    // line. `force` opens the preview if it's closed (right-click action);
    // the automatic path (cursor move) only runs when the preview is open.
    const syncPreviewToCursorFor = (group: EditorGroup, force: boolean): void => {
        const pos = group.editor.getPosition();
        if (!pos) return;
        if (!appInstance.isPreviewVisible()) {
            if (!force) return;
            appInstance.togglePreview();
            // Wait for the first preview render + iframe listener before posting.
            setTimeout(() => appInstance.scrollPreviewToSourceLine(group.id, pos.lineNumber), 600);
            return;
        }
        appInstance.scrollPreviewToSourceLine(group.id, pos.lineNumber);
    };

    function toggleWordWrap(): void {
        const current = editor.getOption(monaco.editor.EditorOption.wordWrap);
        const next: 'on' | 'off' = current === 'on' ? 'off' : 'on';
        for (const g of groups.values()) g.editor.updateOptions({ wordWrap: next });
        localStorage.setItem(WORD_WRAP_KEY, next);
    }

    // ---- Active group tracking ----
    function setActiveGroup(group: EditorGroup): void {
        activeGroup = group;
        editor = group.editor;
        tabs = group.tabs;
        (window as any).calcpadTabs = tabs;
        (window as any).calcpadActiveEditor = editor;
        appInstance.setActiveGroup(group.id);
        // Refresh active-group-scoped UI (Problems panel, sidebar TOC, preview).
        refreshProblemsFor(group);
        activeBridge.refreshHeadings();
        refreshUiDirtyIndicator();
        syncInputMode();
        if (appInstance.isPreviewVisible()) void refreshPreviewFor(group);
    }

    // ---- Per-group wiring (common to web + desktop) ----
    function wireGroupCommon(group: EditorGroup): void {
        const ed = group.editor;

        // Focus tracking — the focused group becomes active.
        group.disposables.push(
            ed.onDidFocusEditorText(() => {
                if (activeGroup !== group) setActiveGroup(group);
            }),
        );

        // Word wrap (Alt+Z) + duplicate line (Ctrl+D) per editor. Ctrl+D
        // overrides Monaco's default "add selection to next find match".
        ed.addCommand(monaco.KeyMod.Alt | monaco.KeyCode.KeyZ, toggleWordWrap);
        ed.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.KeyD, () => {
            ed.trigger('keyboard', 'editor.action.copyLinesDownAction', null);
        });

        attachQuickTyper(ed, editorBridge);
        attachOperatorReplacer(ed);
        attachAutoIndenter(ed);
        registerFormattingCommands(ed, editorBridge);

        // Per-group diagnostics.
        group.diagnostics = setupDiagnostics(ed, activeBridge.api, () => {
            const sev = editorBridge.getExtraSetting('linterMinSeverity');
            return (sev === 'error' || sev === 'warning') ? sev : 'information';
        }, getFileContext, `lint:${group.id}`);

        // Focus-the-preview-to-line context action (targets this group).
        ed.addAction({
            id: 'calcpad.focusPreviewToLine',
            label: 'Focus Preview to Line',
            keybindings: [monaco.KeyMod.CtrlCmd | monaco.KeyCode.Backquote],
            contextMenuGroupId: 'navigation',
            contextMenuOrder: 1.5,
            run: () => syncPreviewToCursorFor(group, true),
        });

        // Manual "run" — re-renders all previews. Useful when Auto-Run Preview
        // is off. The Ctrl+Alt+X shortcut is bound both here (works when the
        // editor has focus) and at the window level (Tauri) so it fires from
        // anywhere in the app.
        ed.addAction({
            id: 'calcpad.runPreview',
            label: 'Run Preview',
            keybindings: [monaco.KeyMod.CtrlCmd | monaco.KeyMod.Alt | monaco.KeyCode.KeyX],
            contextMenuGroupId: 'navigation',
            contextMenuOrder: 1.4,
            run: () => { void runRefresh(); },
        });

        // Edit-metadata context action: open the Metadata tab for the comment at
        // the cursor, moving onto (or seeding) one when needed. Mirrors the VS
        // Code `editMetadataProperties` command.
        ed.addAction({
            id: 'calcpad.editMetadata',
            label: 'Edit Metadata Properties',
            contextMenuGroupId: 'navigation',
            contextMenuOrder: 1.6,
            run: (edEditor) => {
                const model = edEditor.getModel();
                const pos = edEditor.getPosition();
                if (!model || !pos) return;
                const curLine = pos.lineNumber;
                const curText = model.getLineContent(curLine);

                const focusMetadata = () => {
                    sidebarInstance.switchView?.('calcpad');
                    // switchTab('metadata') posts getMetadataContext, which the
                    // bridge resolves against the (now-updated) cursor line.
                    sidebarInstance.switchTab?.('metadata');
                };

                // Already on a metadata comment — just open the editor for it.
                if (findMetadataCommentBlock([curText], 0)) {
                    focusMetadata();
                    return;
                }

                // A metadata comment already sits directly above — move onto it.
                if (curLine > 1 && findMetadataCommentBlock([model.getLineContent(curLine - 1)], 0)) {
                    const above = curLine - 1;
                    edEditor.setPosition({ lineNumber: above, column: model.getLineMaxColumn(above) });
                    focusMetadata();
                    return;
                }

                // On a definition, the panel shows a virtual block from real
                // highlighter results (correct params) and Apply creates the
                // comment — no seeding, so definition line numbers stay valid.
                const resolve = buildDefinitionResolver(
                    editorBridge.definitions.getCachedDefinitions(docKeyFor(group))
                    ?? { functions: [], macros: [], variables: [], customUnits: [] });
                if (resolve(curLine - 1)) {
                    focusMetadata();
                    return;
                }

                // Otherwise seed an empty comment so settings/lint markers can be
                // added on a non-definition line.
                const indent = curText.match(/^[ \t]*/)?.[0] ?? '';
                const newLineText = serializeMetadataComment({}, indent, '');
                edEditor.executeEdits('calcpad-metadata-seed', [{
                    range: new monaco.Range(curLine, 1, curLine, 1),
                    text: newLineText + '\n',
                }]);
                // The inserted comment now occupies the original line index.
                edEditor.setPosition({ lineNumber: curLine, column: model.getLineMaxColumn(curLine) });
                focusMetadata();
            },
        });

        // Content changes: refresh this group's definitions cache + preview,
        // and (only when this is the active group) the sidebar TOC.
        let definitionsTimer: ReturnType<typeof setTimeout> | null = null;
        let previewTimer: ReturnType<typeof setTimeout> | null = null;
        let tocTimer: ReturnType<typeof setTimeout> | null = null;
        group.disposables.push(
            ed.onDidChangeModelContent(() => {
                if (definitionsTimer) clearTimeout(definitionsTimer);
                definitionsTimer = setTimeout(() => void refreshDefinitionsFor(group), 800);
                if (appInstance.isPreviewVisible() && editorBridge.getExtraSetting('autoRun') !== 'false') {
                    if (previewTimer) clearTimeout(previewTimer);
                    previewTimer = setTimeout(() => void refreshPreviewFor(group), 800);
                }
                if (group === activeGroup) {
                    if (tocTimer) clearTimeout(tocTimer);
                    tocTimer = setTimeout(() => activeBridge.refreshHeadings(), 800);
                }
            }),
        );

        // Cursor moves: preview sync (only when this group is active/visible),
        // plus a debounced metadata-context refresh so the Metadata tab tracks
        // the comment under the cursor (mirrors the VS Code selection handler).
        let cursorSyncTimer: ReturnType<typeof setTimeout> | null = null;
        let metadataContextTimer: ReturnType<typeof setTimeout> | null = null;
        group.disposables.push(
            ed.onDidChangeCursorPosition(() => {
                if (group === activeGroup) {
                    if (metadataContextTimer) clearTimeout(metadataContextTimer);
                    metadataContextTimer = setTimeout(() => {
                        activeBridge.handleMessage({ type: 'getMetadataContext' });
                    }, 150);
                }
                if (editorBridge.getExtraSetting('previewCursorSync') !== 'true') return;
                if (!appInstance.isPreviewVisible()) return;
                if (cursorSyncTimer) clearTimeout(cursorSyncTimer);
                cursorSyncTimer = setTimeout(() => syncPreviewToCursorFor(group, false), 150);
            }),
        );

        // Tab list -> App.vue tab strip for this group.
        group.tabs.onTabsChanged((snapshots) => {
            appInstance.setTabs(group.id, snapshots);
        });

        // On tab switch within this group, re-emit markers + re-lint + repaint.
        group.tabs.onActiveModelChanged(() => {
            applyCompiledWorksheetMode(group);
            const enteringUi = shouldAutoEnterUiMode(group);
            refreshProblemsFor(group);
            // Re-lint: content-change events don't fire on tab switch, so the
            // debounced lint in setupDiagnostics never re-runs for the new model.
            void group.diagnostics?.refresh();
            // The mode switch below renders the form itself, so rendering here first would
            // just be a preview nobody sees.
            if (!enteringUi && appInstance.isPreviewVisible()) void refreshPreviewFor(group);
            if (group === activeGroup) activeBridge.refreshHeadings();
            void refreshDefinitionsFor(group);
            if (enteringUi) autoEnterUiMode();
        });

        // Initial definitions population for the seeded tab.
        setTimeout(() => void refreshDefinitionsFor(group), 500);
    }

    /**
     * Create a group's editor in its App.vue container, wire it, seed a tab.
     * If `linkFrom` is given, the new group's first tab shares that group's
     * active tab's model instead of starting blank — used by splitEditor() so
     * a split defaults to a second, live-synced view of the current file.
     */
    async function createAndWireGroup(id: string, seedContent = '', linkFrom?: EditorGroup): Promise<EditorGroup> {
        appInstance.addGroup(id);
        await nextTick();
        const container = appInstance.getEditorContainer(id) as HTMLElement | null;
        if (!container) throw new Error(`Editor container for group ${id} not found`);
        const group = new EditorGroup(id, container, { wordWrap: initialWordWrap, fontFamily: initialEditorFontFamily });
        groups.set(id, group);
        wireGroupCommon(group);
        for (const hook of groupWireHooks) hook(group);
        const linkModel = linkFrom?.tabs.activeId ? linkFrom.tabs.modelForTab(linkFrom.tabs.activeId) : null;
        if (linkModel) group.tabs.openLinked(linkModel);
        else group.tabs.newUntitled(seedContent);
        return group;
    }

    // ---- Seed the primary group (g0 already rendered by App.vue) ----
    const g0Container = appInstance.getEditorContainer('g0') as HTMLElement | null;
    if (!g0Container) throw new Error('Primary editor container not found');
    const primaryGroup = new EditorGroup('g0', g0Container, { wordWrap: initialWordWrap, fontFamily: initialEditorFontFamily });
    groups.set('g0', primaryGroup);
    setActiveGroup(primaryGroup);
    wireGroupCommon(primaryGroup);

    // Editor providers + hover/definitions cache scope per-tab via the active
    // group's active tab.
    setActiveDocumentKeyResolver(() => activeDocumentKey());

    // Seed the first tab. On web we put the sample in it; on desktop it's
    // an empty Untitled-1 ready to receive an Open or paste.
    primaryGroup.tabs.newUntitled(isTauri ? '' : getSampleContent());

    // ---- Split / merge / focus wiring ----
    let confirmCloseGroup: (g: EditorGroup) => Promise<boolean> = async () => true;
    // Monotonic group-id allocator. Never reuse ids: after an unsplit the
    // surviving group may be the second one (g1), so a fixed 'g1' would collide
    // on the next split and silently no-op. 'g0' is the primary (seeded above).
    let groupSeq = 0;

    async function splitEditor(): Promise<void> {
        if (groups.size >= 2) {
            activeGroup.editor.focus();
            return;
        }
        // The input form owns the window and shows one document; a second group
        // would have nowhere to render and would share this one's entered values.
        if (appInstance.getResultMode() === 'ui' && appInstance.isPreviewVisible()) {
            appInstance.appendOutput('info', 'Exit input mode to split the editor.');
            return;
        }
        const source = activeGroup;
        const group = await createAndWireGroup(`g${++groupSeq}`, '', source);
        setActiveGroup(group);
        group.editor.focus();
    }

    async function closeGroup(groupId: string): Promise<void> {
        if (groups.size < 2) return;
        const group = groups.get(groupId);
        if (!group) return;
        const ok = await confirmCloseGroup(group);
        if (!ok) return;
        const other = [...groups.values()].find(g => g !== group);
        if (activeGroup === group && other) setActiveGroup(other);
        groups.delete(groupId);
        group.dispose();
        appInstance.removeGroup(groupId);
        other?.editor.focus();
    }

    appInstance.onSplitRequest = () => { void splitEditor(); };
    appInstance.onCloseGroupRequest = (groupId: string) => { void closeGroup(groupId); };
    appInstance.onGroupFocusRequest = (groupId: string) => {
        const g = groups.get(groupId);
        if (g) setActiveGroup(g);
    };

    // ---- Include navigation (Go-to-Definition / Find All References) ----
    // Find All References needs the include files' models registered so the
    // panel can render their snippets. Only wire this up on Tauri desktop, where
    // we have filesystem access; in the pure-web build the provider silently
    // skips include locations. All handlers act on the active group.
    const openIncludeFile: IncludeFileOpener | undefined = tauriBridge
        ? async (rawFileName: string) => {
            try {
                const absPath = await tauriBridge.resolveIncludePath(rawFileName);
                let model = tabs.findModelByPath(absPath);
                if (!model) {
                    const content = await tauriBridge.readFile(absPath);
                    const tabId = tabs.openFile(absPath, content);
                    model = tabs.findModelByPath(absPath);
                    if (!model) {
                        console.warn(`[references] opened ${absPath} as ${tabId} but no model was registered`);
                        return null;
                    }
                }
                return model.uri;
            } catch (err) {
                console.warn(`[references] failed to open include ${rawFileName}: ${err instanceof Error ? err.message : String(err)}`);
                return null;
            }
        }
        : undefined;

    // Go-to-Definition must stay side-effect free — Monaco calls provideDefinition
    // on Ctrl+hover just to draw the underline, so opening a file or moving the
    // cursor there would navigate on hover with no click. The provider gets a
    // pure URI for the include (below); the real open + cursor move happens in
    // the editor opener, which Monaco invokes only on an actual click / F12.
    // We stash the resolved absolute path keyed by the exact URI string we mint
    // so the opener recovers it verbatim (fsPath would re-case the Windows drive
    // letter and break the tab lookup's strict path compare).
    const includeUriToPath = new Map<string, string>();
    const resolveIncludeUri: IncludeUriResolver | undefined = tauriBridge
        ? async (rawFileName: string): Promise<monaco.Uri | null> => {
            try {
                const absPath = await tauriBridge.resolveIncludePath(rawFileName);
                const uri = monaco.Uri.parse(`calcpad-include:${encodeURIComponent(absPath)}`);
                includeUriToPath.set(uri.toString(), absPath);
                return uri;
            } catch {
                return null;
            }
        }
        : undefined;

    if (tauriBridge) {
        const bridge = tauriBridge;
        // Shared by both openers below — the definition-provider's Ctrl+click/F12
        // path (registerEditorOpener) and the always-visible link path
        // (registerLinkOpener, for the underlined #include path text).
        const openIncludeAt = async (
            absPath: string,
            selectionOrPosition?: monaco.IRange | monaco.IPosition,
        ): Promise<void> => {
            try {
                const existing = tabs.findByPath(absPath);
                if (existing) {
                    tabs.activate(existing.id);
                } else {
                    tabs.openFile(absPath, await bridge.readFile(absPath));
                }
                if (selectionOrPosition) {
                    const pos = 'startLineNumber' in selectionOrPosition
                        ? { lineNumber: selectionOrPosition.startLineNumber, column: selectionOrPosition.startColumn }
                        : { lineNumber: selectionOrPosition.lineNumber, column: selectionOrPosition.column };
                    editor.setPosition(pos);
                    editor.revealPositionInCenter(pos);
                }
            } catch (err) {
                console.warn(`[references] failed to open include ${absPath}: ${err instanceof Error ? err.message : String(err)}`);
            }
        };

        monaco.editor.registerEditorOpener({
            openCodeEditor(_source, resource, selectionOrPosition) {
                const absPath = includeUriToPath.get(resource.toString());
                if (absPath === undefined) return false; // not an include jump — let Monaco handle it
                return openIncludeAt(absPath, selectionOrPosition).then(() => true);
            },
        });

        monaco.editor.registerLinkOpener({
            open(resource) {
                const absPath = includeUriToPath.get(resource.toString());
                if (absPath === undefined) return false; // not one of ours — let Monaco's default opener handle it
                return openIncludeAt(absPath).then(() => true);
            },
        });
    }

    // ---- Global (per-language) Monaco providers ----
    registerSemanticTokensProvider(activeBridge.api, getFileContext);
    registerCompletionProvider(editorBridge);
    if (tauriBridge) {
        registerIncludeCompletionProvider({
            listDirectory: (p) => tauriBridge.listDirectory(p),
            getCurrentFilePath: () => tabs.activeTab?.filePath ?? null,
            getOpenedFolder: () => tauriBridge.getOpenedFolder(),
            expandEnvVars: (raw) => tauriBridge.expandEnvVars(raw),
            getHomeDir: () => tauriBridge.getHomeDir(),
            getServerPathRoots: () => editorBridge.definitions.getCachedPathRoots(getActiveDocumentKey()),
        });
    }
    registerHoverProvider(editorBridge);
    registerDefinitionProvider(editorBridge, getFileContext, resolveIncludeUri);
    registerIncludeLinkProvider(resolveIncludeUri);
    registerReferenceProvider(editorBridge, getFileContext, openIncludeFile);
    registerRenameProvider(editorBridge, getFileContext);
    registerFormatDocumentProvider(editorBridge);

    window.addEventListener('message', (e: MessageEvent) => {
        if (e.data?.type === 'linterMinSeverityChanged') {
            for (const g of groups.values()) void g.diagnostics?.refresh();
        }
        if (e.data?.type === 'maxOutputLinesChanged') {
            const n = Number(e.data.value);
            if (Number.isFinite(n)) appInstance.setMaxOutputLines(n);
        }
        if (e.data?.type === 'exportError') {
            appInstance.appendOutput('error', String(e.data.message ?? 'Export failed'));
        }
    });

    // Apply persisted cap at startup — the sidebar's settingsResponse will
    // sync it too, but the log wiring below can fire before that arrives.
    {
        const stored = Number(editorBridge.getExtraSetting('maxOutputLines'));
        if (Number.isFinite(stored) && stored >= 10) appInstance.setMaxOutputLines(stored);
    }

    // Wire the bridge's insertText handler to the active editor.
    activeBridge.onInsertText = (text: string) => {
        const selection = editor.getSelection();
        if (selection) {
            editor.executeEdits('calcpad-insert', [{
                range: selection,
                text,
                forceMoveMarkers: true,
            }]);
        }
        editor.focus();
    };

    // Wire Output panel: intercept console methods to pipe into the panel.
    // Covers log/info/debug/warn/error so any call from app code lands in the
    // CalcPad output channel.
    function fmtConsoleArg(a: unknown): string {
        if (typeof a === 'string') return a;
        if (a instanceof Error) return a.stack ?? a.message;
        try {
            return JSON.stringify(a);
        } catch {
            return String(a);
        }
    }
    const origLog = console.log;
    const origInfo = console.info;
    const origDebug = console.debug;
    const origWarn = console.warn;
    const origError = console.error;

    const wrap = (
        orig: (...args: any[]) => void,
        level: 'info' | 'debug' | 'warn' | 'error',
    ) => (...args: any[]) => {
        orig.apply(console, args);
        // Monaco's ConsoleLogger emits styled `%c INFO`/`%c  ERR` lines whose CSS
        // argument only produces noise in the panel; leave those to devtools.
        if (typeof args[0] === 'string' && args[0].startsWith('%c')) return;
        appInstance.appendOutput(level, args.map(fmtConsoleArg).join(' '));
    };

    console.log = wrap(origLog, 'info');
    console.info = wrap(origInfo, 'info');
    console.debug = wrap(origDebug, 'debug');
    console.warn = wrap(origWarn, 'warn');
    console.error = wrap(origError, 'error');

    // Also capture unhandled errors
    window.addEventListener('error', (e) => {
        appInstance.appendOutput('error', `Uncaught: ${e.message} (${e.filename}:${e.lineno})`);
    });
    window.addEventListener('unhandledrejection', (e) => {
        appInstance.appendOutput('error', `Unhandled rejection: ${e.reason}`);
    });

    // Messages posted from the preview iframes (App.vue:injectPreviewConsole /
    // injectLineLinks). Each message carries `groupId` so it routes to the
    // group whose preview emitted it.
    window.addEventListener('message', (e: MessageEvent) => {
        const data = e.data;
        if (!data) return;

        // Forward console.* + uncaught errors to the "Preview Console" channel,
        // tagged with the originating group.
        if (data.type === 'previewConsole') {
            const level: 'info' | 'warn' | 'error' | 'debug' =
                data.level === 'warn' ? 'warn'
                : data.level === 'error' ? 'error'
                : data.level === 'debug' ? 'debug'
                : 'info';
            appInstance.appendOutput(level, String(data.message ?? ''), 'preview', data.groupId);
            return;
        }

        if (data.type === 'previewThemeChanged' || data.type === 'settingsChanged'
            || data.type === 'previewUiOverridesChanged') {
            refreshAllPreviews();
            return;
        }

        // A #UI control was edited in the preview. Record the value and re-render
        // that group so dependent results recalculate.
        if (data.type === 'uiValueChange') {
            const group = (data.groupId && groups.get(data.groupId)) || activeGroup;
            const docKey = uiDocKeyFor(group);
            if (!uiOverrides.set(docKey, String(data.varName), String(data.newValue))) return;
            uiOverridesDirty.add(docKey);
            refreshUiDirtyIndicator();
            void refreshPreviewFor(group);
            return;
        }

        // Results -> editor navigation. An 'output' line comes from a rendered
        // view; when the document has macros/includes that line only makes
        // sense in the unwrapped view, so flip the pane to unwrapped scrolled
        // there (the two-step). A 'source' line navigates Monaco directly. The
        // message's groupId selects which group to act on.
        if (data.type === 'navigateToLine') {
            const line = Number(data.line);
            if (!Number.isFinite(line) || line < 1) return;
            const group = (data.groupId && groups.get(data.groupId)) || activeGroup;
            if (group !== activeGroup) setActiveGroup(group);
            const isOutputLine = data.lineType === 'output';
            const hasMacros = /^\s*#(def|include)\b/im.test(group.editor.getValue());
            const mode = appInstance.getResultMode() as ResultMode;
            // A compiled worksheet has no unwrapped view to route through, so the
            // click falls through to revealing the line in the editor instead.
            if (isOutputLine && (mode === 'preview' || mode === 'report') && hasMacros
                && appInstance.resultModeAvailable('unwrapped')) {
                // Bake the target into the unwrapped refresh (avoids an
                // iframe-reload postMessage race); setResultMode triggers
                // onResultModeChanged -> refresh all previews.
                pendingPreviewScrollLine.set(group.id, line);
                appInstance.setResultMode('unwrapped');
            } else {
                group.editor.revealLineInCenter(line);
                group.editor.setPosition({ lineNumber: line, column: 1 });
                group.editor.focus();
            }
            return;
        }
    });

    appInstance.appendOutput('info', `CalcpadCE Web started — server: ${serverUrl}`);

    // Flush any server-manager log lines buffered before the Output panel mounted,
    // then redirect future ones straight into the panel.
    for (const msg of pendingServerLogs) appInstance.appendOutput('info', msg);
    pendingServerLogs.length = 0;
    for (const { line, stream } of pendingServerRawLogs) {
        appInstance.appendOutput(stream === 'stderr' ? 'error' : 'info', line, 'server');
    }
    pendingServerRawLogs.length = 0;
    if (serverManager) {
        serverManager.setLogger({
            appendLine: (msg: string) => appInstance.appendOutput('info', msg),
        });
        serverManager.onServerLog = (line: string, stream: 'stdout' | 'stderr') => {
            appInstance.appendOutput(stream === 'stderr' ? 'error' : 'info', line, 'server');
        };
        serverManager.onUrlChanged = (newUrl: string) => {
            activeBridge.api.setBaseUrl(newUrl);
            appInstance.appendOutput('info', `Server URL updated: ${newUrl}`);
        };
        serverManager.onCrashExhausted = (crashOutput: string) => {
            appInstance.appendOutput('error',
                'CalcpadCE server crashed repeatedly — auto-restart disabled. ' +
                'Use Server → Restart Server to try again.');
            if (crashOutput) appInstance.appendOutput('error', crashOutput);
        };
    }

    // Problems panel: markers can change for any group's model (background
    // lint). Dispatch to whichever group owns the affected resource.
    monaco.editor.onDidChangeMarkers((resources) => {
        for (const g of groups.values()) {
            const model = g.editor.getModel();
            if (!model) continue;
            if (resources.some(r => r.toString() === model.uri.toString())) {
                refreshProblemsFor(g);
            }
        }
    });

    // Handle click-to-navigate from problems panel (targets the active group).
    appInstance.onGotoProblem = (problem: any) => {
        editor.revealLineInCenter(problem.startLineNumber);
        editor.setPosition({
            lineNumber: problem.startLineNumber,
            column: problem.startColumn,
        });
        editor.focus();
    };

    // ---- Tab-strip user actions (dispatched by group id) ----
    // The Tauri branch overrides the close handlers with save-prompt-aware
    // versions; on web there's nothing to save, so a plain close is correct.
    async function activateTab(groupId: string, id: string): Promise<void> {
        const g = groups.get(groupId);
        if (!g) return;
        if (g === activeGroup && g.tabs.activeId === id) return;
        if (!await confirmLeaveUiDoc()) return;
        if (activeGroup !== g) setActiveGroup(g);
        g.tabs.activate(id);
    }

    appInstance.onTabActivate = (groupId: string, id: string) => { void activateTab(groupId, id); };
    appInstance.onTabCloseRequest = (groupId: string, id: string) => {
        groups.get(groupId)?.tabs.close(id);
    };
    appInstance.onNewTabRequest = (groupId: string) => {
        groups.get(groupId)?.tabs.newUntitled();
    };
    // Preview right-click "Open Full HTML": raw HTML in a new unsaved tab in
    // the same group, mirroring vscode-calcpad's "View Webview Source".
    appInstance.onOpenFullHtmlRequest = (groupId: string, html: string) => {
        groups.get(groupId)?.tabs.newUntitled(html, 'HTML Preview Source.html');
    };
    appInstance.onTabCloseOthersRequest = (groupId: string, id: string) => {
        const g = groups.get(groupId);
        if (!g) return;
        for (const t of g.tabs.all) {
            if (t.id !== id) g.tabs.close(t.id);
        }
    };
    appInstance.onTabCloseAllRequest = (groupId: string) => {
        const g = groups.get(groupId);
        if (!g) return;
        for (const t of g.tabs.all) g.tabs.close(t.id);
    };

    // Mount the CalcPad Vue sidebar. Desktop (Tauri) shows the Files view
    // + activity icons; web mode keeps the original single-panel look.
    const versionConfig = {
        isVSCode: false,
        isWeb: !isTauri,
        isDesktop: isTauri,
        isWebOrDesktop: true,
    };
    const sidebarApp = createApp(CalcpadAppVue, { versionConfig, appVersion: pkg.version });
    const sidebarInstance = sidebarApp.mount('#vue-sidebar') as {
        switchTab?: (id: string) => void;
        switchView?: (id: string) => void;
    };

    // Initialize the result mode from the saved extra setting (Tauri) or default (web).
    // The key was `previewMode` and the rendered view was `wrapped`, so fall back to the
    // old key and translate the old value — otherwise an existing install loses its mode.
    const savedMode = editorBridge.getExtraSetting('resultMode')
        ?? editorBridge.getExtraSetting('previewMode');
    const restoredMode = savedMode === 'wrapped' ? 'preview' : savedMode;
    if (restoredMode === 'preview' || restoredMode === 'unwrapped'
        || restoredMode === 'ui' || restoredMode === 'report') {
        appInstance.setResultMode(restoredMode);
    }
    // The restore above predates onResultModeChanged, and the sidebar has only just
    // mounted, so seed it with the mode the session came up in.
    syncInputMode();

    // Manual refresh: re-lint with current settings, refresh definitions/
    // headings, redraw previews, and re-extract Export-tab plots. Called from
    // the Server > Refresh menu item and the editor's Run action.
    async function runRefresh(): Promise<void> {
        appInstance.appendOutput('info', 'Refreshing…');
        for (const g of groups.values()) {
            await g.diagnostics?.refresh();
            await refreshDefinitionsFor(g);
            if (appInstance.isPreviewVisible()) await refreshPreviewFor(g);
        }
        activeBridge.refreshHeadings();
        // Refresh the Export tab's plot list — it caches independently of the
        // preview and would otherwise show stale plots until the user clicks
        // "Refresh Plots" manually.
        window.dispatchEvent(new MessageEvent('message', { data: { type: 'getPlots' } }));
    }

    appInstance.onResultModeChanged = (mode: ResultMode) => {
        // Not persisted when a #UI document chose it rather than the user: the session would
        // otherwise come back up in a form, whatever document is opened next.
        if (!autoUiSwitchInFlight) editorBridge.setExtraSetting('resultMode', mode);
        refreshUiDirtyIndicator();
        syncInputMode();
        refreshAllPreviews();
    };

    // "Print PDF" on the report/input toolbar. The report is the default export variant,
    // so this is the same render the Export tab's Report group produces.
    appInstance.onPrintReportRequest = () => {
        activeBridge.handleMessage({ type: 'generatePdf' });
    };

    // The report pane renders on demand; showing it needs a fresh convert.
    appInstance.onUiPrintToggled = () => {
        // The new iframe only exists after Vue has patched the DOM.
        void nextTick(refreshAllPreviews);
    };

    // Writes the active tab to disk. Set by the Tauri branch below; on web there
    // is no file behind the document, so the model edit is all there is.
    let persistActiveTab: (() => Promise<boolean>) | null = null;

    // "Save values": write the entered #UI values into the active document as a
    // uiOverrides metadata comment, so they are restored the next time it opens.
    async function saveUiOverrides(): Promise<void> {
        const docKey = activeUiDocKey();
        const overrides = uiOverrides.toRecord(docKey);
        if (!overrides) return;

        const model = activeGroup.editor.getModel();
        if (!model) return;

        const updated = writeUiOverrides(model.getValue(), overrides);
        if (updated !== model.getValue()) {
            // Edited through the model rather than the editor: a compiled worksheet
            // locks the editor, which would reject executeEdits. Undo is preserved.
            model.pushStackElement();
            model.pushEditOperations([], [{ range: model.getFullModelRange(), text: updated }], () => null);
            model.pushStackElement();
        }
        uiOverridesDirty.delete(docKey);
        refreshUiDirtyIndicator();
        // The values are only "saved" once they reach the file — a form filled in
        // and left dirty in the editor is exactly what the user asked to avoid.
        await persistActiveTab?.();
        appInstance.appendOutput('info', `Saved ${Object.keys(overrides).length} #UI value(s) to the document.`);
    }

    appInstance.onSaveUiOverridesRequest = () => { void saveUiOverrides(); };

    /**
     * Leaving the input form discards the entered values — they only live in memory
     * until written into the document — so prompt for unwritten ones first. Returns
     * false to keep the form open when the user cancels.
     */
    async function leaveUiDoc(): Promise<boolean> {
        const docKey = activeUiDocKey();
        if (uiOverridesDirty.has(docKey)) {
            const choice = await appInstance.showConfirm({
                title: 'Unsaved input values',
                message: 'Save the values entered in the input form before exiting? They are discarded otherwise.',
                yesLabel: 'Save',
                noLabel: "Don't Save",
            });
            if (choice === 'cancel') return false;
            if (choice === 'yes') await saveUiOverrides();
        }
        uiOverrides.clear(docKey);
        uiOverridesDirty.delete(docKey);
        refreshUiDirtyIndicator();
        return true;
    }

    appInstance.onExitUiModeRequest = leaveUiDoc;

    /**
     * The input form always shows the active document, so switching documents takes
     * the form's values with it. Prompt as if the form were closing, since for that
     * document it is. Returns false when the user cancels the switch.
     */
    async function confirmLeaveUiDoc(): Promise<boolean> {
        if (!appInstance.isPreviewVisible() || appInstance.getResultMode() !== 'ui') return true;
        return await leaveUiDoc();
    }

    // Refresh all previews when the preview pane is first opened.
    appInstance.onPreviewToggled = (visible: boolean) => {
        syncInputMode();
        if (visible) {
            setTimeout(refreshAllPreviews, 50);
        }
    };

    // Toolbar "Run" button.
    appInstance.onRunRequest = () => { void runRefresh(); };

    // Tauri-specific: native menu clicks + file operations
    if (isTauri && tauriBridge) {
        const [
            { listen: tauriListen },
            { getCurrentWindow },
            { exit: processExit },
            tauriClipboard,
            { invoke: tauriInvoke },
        ] = await Promise.all([
            import('@tauri-apps/api/event'),
            import('@tauri-apps/api/window'),
            import('@tauri-apps/plugin-process'),
            import('@tauri-apps/plugin-clipboard-manager'),
            import('@tauri-apps/api/core'),
        ]);
        invokeTauri = tauriInvoke;
        // The seeded tab decided the menu state before `invoke` existed, so the cached flag
        // is inverted here to defeat the no-op guard and let the real state through.
        const sourceModesShown = appInstance.resultModeAvailable('unwrapped');
        sourceModeMenuShown = !sourceModesShown;
        syncSourceModeMenuItems(sourceModesShown);

        // ---- Autosave drafts (10s debounce per tab) ----
        // Rust owns the on-disk drafts dir (<app_data>/drafts). Each tab is
        // assigned a stable UUID on first autosave. Tab ids are namespaced per
        // group (see TabManager), so drafts never collide across groups.
        const AUTOSAVE_DEBOUNCE_MS = 10_000;
        const draftTimers = new Map<string, ReturnType<typeof setTimeout>>();
        const draftIds = new Map<string, string>();

        // Look up which group owns a given (namespaced) tab id.
        function groupForTab(tabId: string): EditorGroup | null {
            for (const g of groups.values()) {
                if (g.tabs.all.some(t => t.id === tabId)) return g;
            }
            return null;
        }

        function draftIdFor(tabId: string): string {
            let id = draftIds.get(tabId);
            if (!id) {
                id = crypto.randomUUID();
                draftIds.set(tabId, id);
            }
            return id;
        }

        async function writeDraft(tabId: string): Promise<void> {
            const g = groupForTab(tabId);
            if (!g || !g.tabs.isDirty(tabId)) return;
            const content = g.tabs.getContent(tabId);
            if (content == null) return;
            const filePath = g.tabs.getFilePath(tabId);
            const title = g.tabs.getTitle(tabId) ?? 'Untitled';
            const filename = filePath ? title : `${title}.cpd`;
            try {
                await tauriInvoke('draft_write', {
                    id: draftIdFor(tabId),
                    filename,
                    filePath,
                    content,
                });
            } catch (err) {
                appInstance.appendOutput('warn',
                    `Autosave failed for ${title}: ${err instanceof Error ? err.message : String(err)}`);
            }
        }

        async function deleteDraft(tabId: string): Promise<void> {
            const id = draftIds.get(tabId);
            if (!id) return;
            draftIds.delete(tabId);
            const timer = draftTimers.get(tabId);
            if (timer) {
                clearTimeout(timer);
                draftTimers.delete(tabId);
            }
            try {
                await tauriInvoke('draft_delete', { id });
            } catch { /* swallow — draft may not exist yet */ }
        }

        // ---- Draft recovery ----
        // Rust emits `drafts-recovered` shortly after startup if orphan drafts
        // exist from a prior session. Prompt once, then either restore each
        // draft as a dirty tab or discard them all.
        interface DraftInfo {
            id: string;
            filename: string;
            filePath: string | null;
            savedAt: number;
            size: number;
        }
        interface DraftContent extends DraftInfo { content: string; }

        async function restoreDraft(info: DraftInfo): Promise<void> {
            try {
                const drafted = await tauriInvoke<DraftContent | null>('draft_read', { id: info.id });
                if (!drafted) return;
                const displayTitle = drafted.filePath
                    ? drafted.filename
                    : drafted.filename.replace(/\.cpd$/i, '');
                // Recovered drafts land in the active group (the primary group
                // at startup; a live lookup so it's never a disposed group).
                const newTabId = activeGroup.tabs.openDraft({
                    filePath: drafted.filePath,
                    title: displayTitle,
                    content: drafted.content,
                });
                // Reuse the draft id so subsequent autosaves overwrite it in place.
                draftIds.set(newTabId, drafted.id);
            } catch (err) {
                appInstance.appendOutput('warn',
                    `Draft recovery failed for ${info.filename}: ${err instanceof Error ? err.message : String(err)}`);
            }
        }

        await tauriListen<DraftInfo[]>('drafts-recovered', async (evt) => {
            const drafts = evt.payload;
            if (!drafts || drafts.length === 0) return;
            const summary = drafts
                .map(d => `• ${d.filename}${d.filePath ? ` (${d.filePath})` : ''}`)
                .join('\n');
            const choice = await appInstance.showConfirm({
                title: 'Recover unsaved changes?',
                message:
                    `CalcpadCE found ${drafts.length} unsaved draft${drafts.length === 1 ? '' : 's'} `
                    + `from a previous session:\n\n${summary}\n\n`
                    + `Restore them into new tabs? Choose "Don't Restore" to discard.`,
                yesLabel: 'Restore',
                noLabel: "Don't Restore",
            });
            if (choice === 'yes') {
                for (const d of drafts) await restoreDraft(d);
                appInstance.appendOutput('info', `Recovered ${drafts.length} draft(s).`);
            } else if (choice === 'no') {
                for (const d of drafts) {
                    try { await tauriInvoke('draft_delete', { id: d.id }); }
                    catch { /* ignored */ }
                }
                appInstance.appendOutput('info', `Discarded ${drafts.length} draft(s).`);
            }
            // 'cancel' leaves the drafts on disk — surfaced again on next launch.
        });

        // Menu is built in Rust (src-tauri/src/lib.rs:build_menu). The frontend
        // just tracks recents in the plugin-store; there is no dynamic menu
        // rebuild. Recent files remain accessible via the sidebar's Files tab.
        void tauriBridge.getRecentFiles();

        /**
         * Open `path` in a tab. If the active group already holds that file,
         * just focuses it. If another group has it open, opens a second,
         * live-synced tab onto the same model in the active group instead of
         * jumping away — this is what lets the same file be open in both
         * split panes at once. Otherwise reads from disk into the active
         * group.
         */
        async function loadFile(path: string): Promise<void> {
            const inActive = tabs.findByPath(path);
            if (inActive && inActive.id === tabs.activeId) return;
            if (!await confirmLeaveUiDoc()) return;
            if (inActive) {
                tabs.activate(inActive.id);
                return;
            }
            for (const g of groups.values()) {
                if (g === activeGroup) continue;
                const existing = g.tabs.findByPath(path);
                if (existing) {
                    const model = g.tabs.modelForTab(existing.id);
                    if (model) {
                        tabs.openLinked(model);
                        return;
                    }
                }
            }
            try {
                const content = await tauriBridge!.readFile(path);
                tabs.openFile(path, content);
                // Opening into the seeded empty tab replaces it in place, which is not an
                // active-model change, so the listener that normally settles the mode for a
                // newly opened file never runs — and that is the first file of every session.
                applyCompiledWorksheetMode(activeGroup);
                if (shouldAutoEnterUiMode(activeGroup)) autoEnterUiMode();
                await tauriBridge!.addRecentFile(path);
            } catch (err) {
                appInstance.appendOutput('error', 'Failed to open file: ' + (err instanceof Error ? err.message : String(err)));
            }
        }

        // Files-tab clicks arrive via a custom event dispatched by the bridge.
        window.addEventListener('calcpad-open-file', (e: Event) => {
            const detail = (e as CustomEvent<{ path: string }>).detail;
            if (detail?.path) void loadFile(detail.path);
        });

        // Drain any files handed to us at cold start by the OS's .cpd file
        // association. Runs after the listener above is wired so the bridge's
        // synchronous dispatch inside handleOpenFileByPath actually lands.
        if (isTauri) {
            try {
                const pending = await tauriInvoke<string[]>('take_pending_launch_files');
                for (const path of pending) await loadFile(path);
            } catch {
                /* older desktop builds may not expose the command; ignore */
            }
        }

        /**
         * Save the active tab. If it has no file path, prompts for one.
         * Returns true if saved, false if the user cancelled / no active tab.
         */
        async function saveActive(): Promise<boolean> {
            const active = tabs.activeTab;
            if (!active) return false;
            const content = tabs.activeModel?.getValue() ?? '';
            if (active.filePath) {
                await tauriBridge!.saveFile(active.filePath, content);
                tabs.markActiveSaved();
                await deleteDraft(active.id);
                return true;
            }
            const newPath = await tauriBridge!.saveFileAs(content);
            if (!newPath) return false;
            tabs.markActiveSaved({ filePath: newPath });
            // Saving as .cpdz turns the tab into a compiled worksheet.
            applyCompiledWorksheetMode(activeGroup);
            await tauriBridge!.addRecentFile(newPath);
            await deleteDraft(active.id);
            return true;
        }

        persistActiveTab = saveActive;

        async function saveAsActive(): Promise<boolean> {
            const active = tabs.activeTab;
            const content = tabs.activeModel?.getValue() ?? '';
            const newPath = await tauriBridge!.saveFileAs(content);
            if (!newPath) return false;
            tabs.markActiveSaved({ filePath: newPath });
            applyCompiledWorksheetMode(activeGroup);
            await tauriBridge!.addRecentFile(newPath);
            if (active) await deleteDraft(active.id);
            return true;
        }

        /**
         * Close a tab in a specific group, prompting if dirty. Returns true on
         * close, false if the user cancelled the prompt. Skips the prompt when
         * another tab (in this or another group) still references the same
         * model — the content isn't actually being discarded.
         */
        async function tryCloseTab(group: EditorGroup, id: string): Promise<boolean> {
            const target = group.tabs.all.find(t => t.id === id);
            if (!target) return true;
            // Closing the document the input form is showing takes its values away.
            if (group === activeGroup && id === group.tabs.activeId && !await confirmLeaveUiDoc()) return false;
            // Activate the group + tab so the editor shows what's being asked about.
            if (activeGroup !== group) setActiveGroup(group);
            // Re-read the dirty flag: saving the input form's values above may have
            // written the file already.
            if (group.tabs.isDirty(id) && group.tabs.isLastReference(id)) {
                if (id !== group.tabs.activeId) group.tabs.activate(id);
                const choice = await appInstance.showConfirm({
                    title: 'Unsaved changes',
                    message: `Save changes to ${target.title} before closing?`,
                    yesLabel: 'Save',
                    noLabel: "Don't Save",
                });
                if (choice === 'cancel') return false;
                if (choice === 'yes') {
                    const saved = await saveActive();
                    if (!saved) return false;
                }
            }
            group.tabs.close(id);
            return true;
        }

        // Prompt to save dirty tabs before a group is merged away (unsplit).
        confirmCloseGroup = async (group: EditorGroup): Promise<boolean> => {
            const dirty = group.tabs.all.filter(t => t.dirty);
            for (const t of dirty) {
                const ok = await tryCloseTab(group, t.id);
                if (!ok) return false;
            }
            return true;
        };

        // ---- Per-group Tauri wiring (commands + drafts + drop) ----
        function wireGroupTauri(group: EditorGroup): void {
            const ed = group.editor;

            // Monaco swallows several Ctrl+ keys as internal commands, so the
            // Tauri menu accelerators never fire while the editor has focus.
            // Bind the file-management ones directly on each group's editor.
            ed.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.KeyS, () => { void saveActive(); });
            ed.addCommand(
                monaco.KeyMod.CtrlCmd | monaco.KeyMod.Shift | monaco.KeyCode.KeyS,
                () => { void saveAsActive(); },
            );
            ed.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.KeyO, async () => {
                const result = await tauriBridge!.openFile();
                if (result) await loadFile(result.path);
            });
            ed.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.KeyN, () => {
                group.tabs.newUntitled();
            });
            ed.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.KeyP, () => {
                appInstance.togglePreview();
            });
            ed.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.Backslash, () => {
                void splitEditor();
            });
            ed.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.Comma, () => {
                sidebarInstance.switchTab?.('settings');
            });
            ed.addCommand(monaco.KeyCode.F5, () => { void runRefresh(); });
            // Clipboard via Tauri's native clipboard API (WebKitGTK workaround).
            ed.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.KeyC, () => { void runClipboardAction('copy'); });
            ed.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.KeyX, () => { void runClipboardAction('cut'); });
            ed.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.KeyV, () => { void runClipboardAction('paste'); });

            // Autosave drafts for this group's tabs.
            group.tabs.onTabContentChanged((tabId) => {
                const existing = draftTimers.get(tabId);
                if (existing) clearTimeout(existing);
                draftTimers.set(tabId, setTimeout(() => {
                    draftTimers.delete(tabId);
                    void writeDraft(tabId);
                }, AUTOSAVE_DEBOUNCE_MS));
            });
            group.tabs.onTabRemoved((tabId) => { void deleteDraft(tabId); });

            // Drag-drop file open — each dropped file opens/focuses a tab in
            // this group.
            const dropTarget = appInstance.getEditorContainer(group.id) as HTMLElement | null;
            if (dropTarget) {
                dropTarget.addEventListener('dragover', e => {
                    e.preventDefault();
                    if (e.dataTransfer) e.dataTransfer.dropEffect = 'copy';
                });
                dropTarget.addEventListener('drop', async e => {
                    e.preventDefault();
                    if (activeGroup !== group) setActiveGroup(group);
                    const files = e.dataTransfer?.files;
                    if (!files || files.length === 0) return;
                    for (const file of Array.from(files)) {
                        const dropped = file as File & { path?: string };
                        if (dropped.path) {
                            await loadFile(dropped.path);
                        } else {
                            const text = await dropped.text();
                            group.tabs.newUntitled(text);
                        }
                    }
                });
            }
        }

        // Apply to the primary group + register so future splits get it too.
        wireGroupTauri(primaryGroup);
        groupWireHooks.push(wireGroupTauri);

        // Override tab-strip close actions with save-prompt-aware versions.
        appInstance.onTabCloseRequest = (groupId: string, id: string) => {
            const g = groups.get(groupId);
            if (g) void tryCloseTab(g, id);
        };

        async function tryCloseTabsSequentially(group: EditorGroup, ids: string[]): Promise<void> {
            for (const id of ids) {
                const ok = await tryCloseTab(group, id);
                if (!ok) return;
            }
        }

        appInstance.onTabCloseOthersRequest = (groupId: string, id: string) => {
            const g = groups.get(groupId);
            if (!g) return;
            const ids = g.tabs.all.filter(t => t.id !== id).map(t => t.id);
            void tryCloseTabsSequentially(g, ids);
        };
        appInstance.onTabCloseAllRequest = (groupId: string) => {
            const g = groups.get(groupId);
            if (!g) return;
            const ids = g.tabs.all.map(t => t.id);
            void tryCloseTabsSequentially(g, ids);
        };
        appInstance.onTabOpenContainingFolderRequest = (groupId: string, id: string) => {
            const g = groups.get(groupId);
            const t = g?.tabs.all.find(t => t.id === id);
            if (t?.filePath) {
                tauriBridge.handleMessage({ type: 'openContainingFolder', path: t.filePath });
            }
        };

        // Clipboard-copy helpers for the tab context menu. Route through
        // Tauri's native clipboard so the value ends up on the system clipboard.
        const writeClipboardText = async (text: string) => {
            try {
                await tauriClipboard.writeText(text);
            } catch (err) {
                appInstance.appendOutput('error', `Copy failed: ${err instanceof Error ? err.message : String(err)}`);
            }
        };

        appInstance.onCopyTextRequest = (text: string) => { void writeClipboardText(text); };
        appInstance.onClipboardReadRequest = async () => {
            try {
                return await tauriClipboard.readText();
            } catch {
                return '';
            }
        };

        appInstance.onTabCopyFullPathRequest = (groupId: string, id: string) => {
            const g = groups.get(groupId);
            const t = g?.tabs.all.find(t => t.id === id);
            if (t?.filePath) void writeClipboardText(t.filePath);
        };
        appInstance.onTabCopyRelativePathRequest = async (groupId: string, id: string) => {
            const g = groups.get(groupId);
            const t = g?.tabs.all.find(t => t.id === id);
            if (!t?.filePath) return;
            const folder = await tauriBridge.getOpenedFolder();
            if (!folder) {
                void writeClipboardText(t.filePath);
                return;
            }
            const rootNorm = folder.replace(/[\\/]+$/, '');
            const sep = rootNorm.includes('\\') ? '\\' : '/';
            const rootWithSep = rootNorm + sep;
            const rel = t.filePath.startsWith(rootWithSep)
                ? t.filePath.substring(rootWithSep.length)
                : t.filePath;
            void writeClipboardText(rel);
        };

        /**
         * Read an image off the system clipboard (Tauri native, no WebView2
         * prompt) and run it through the image-insert flow. Returns true if an
         * image was inserted.
         */
        async function tryPasteClipboardImage(): Promise<boolean> {
            let pngBytes: Uint8Array | null = null;
            try {
                const image = await tauriClipboard.readImage();
                const rgba = await image.rgba();
                const { width, height } = await image.size();
                if (!width || !height || rgba.length === 0) return false;
                pngBytes = await rgbaToPng(rgba, width, height);
            } catch {
                // readImage throws when the clipboard has no image — nothing to paste.
                return false;
            }
            if (!pngBytes) return false;
            await tauriBridge!.insertImageData({
                data: pngBytes,
                mimeType: 'image/png',
                filename: 'pasted-image.png',
            });
            return true;
        }

        /**
         * Route a clipboard / edit action from the native menu to the active
         * group's editor (or a focused sidebar input).
         */
        async function runClipboardAction(
            action: 'cut' | 'copy' | 'paste' | 'select-all' | 'undo' | 'redo' | 'find' | 'replace',
        ): Promise<void> {
            // A real DOM selection (e.g. text picked inside the hover panel,
            // parameter hints, or output) takes priority over the editor's own
            // model selection, since Monaco renders the main text via its own
            // selection overlay rather than native browser selection.
            // Copy only: WebKit reports the selection of a focused text control
            // here as well, and Monaco keeps the editor selection in a hidden
            // textarea - so a cut routed this way wrote the text to the
            // clipboard and left the document untouched.
            if (action === 'copy') {
                const domText = window.getSelection()?.toString() ?? '';
                if (domText) {
                    try { await tauriClipboard.writeText(domText); } catch { /* ignored */ }
                    return;
                }
            }
            const editorHasFocus = editor.hasTextFocus();
            if (editorHasFocus) {
                if (action === 'copy' || action === 'cut') {
                    const sel = editor.getSelection();
                    const model = editor.getModel();
                    if (!sel || !model) return;
                    if (sel.isEmpty()) {
                        // Empty selection: copy/cut the whole current line, matching
                        // Monaco's default. Cut removes the line including its newline.
                        const line = sel.startLineNumber;
                        const text = model.getLineContent(line) + '\n';
                        try { await tauriClipboard.writeText(text); } catch { /* ignored */ }
                        if (action === 'cut') {
                            const lineCount = model.getLineCount();
                            const range = line < lineCount
                                ? new monaco.Range(line, 1, line + 1, 1)
                                : new monaco.Range(line, 1, line, model.getLineMaxColumn(line));
                            editor.executeEdits('menu-cut', [{ range, text: '', forceMoveMarkers: true }]);
                        }
                    } else {
                        const text = model.getValueInRange(sel);
                        try { await tauriClipboard.writeText(text); } catch { /* ignored */ }
                        if (action === 'cut') {
                            editor.executeEdits('menu-cut', [{ range: sel, text: '', forceMoveMarkers: true }]);
                        }
                    }
                    return;
                }
                if (action === 'paste') {
                    let text = '';
                    try { text = await tauriClipboard.readText(); } catch { /* ignored */ }
                    if (text) {
                        const sel = editor.getSelection();
                        if (!sel) return;
                        editor.executeEdits('menu-paste', [{ range: sel, text, forceMoveMarkers: true }]);
                        editor.pushUndoStop();
                        return;
                    }
                    // No text on the clipboard — try a native image paste.
                    await tryPasteClipboardImage();
                    return;
                }
                const cmd = {
                    'select-all': 'editor.action.selectAll',
                    undo: 'undo',
                    redo: 'redo',
                    find: 'actions.find',
                    replace: 'editor.action.startFindReplaceAction',
                }[action];
                editor.focus();
                editor.trigger('menu', cmd, null);
                return;
            }
            // A focused preview frame (the #UI input form, above all) handles the
            // action against whichever control it is sitting in.
            if (action === 'cut' || action === 'copy' || action === 'paste') {
                if (appInstance.runFocusedPreviewClipboardAction(action)) return;
            }
            // Fallback for sidebar / preview / etc.
            const el = document.activeElement as HTMLInputElement | HTMLTextAreaElement | null;
            if (action === 'paste') {
                let text = '';
                try { text = await tauriClipboard.readText(); } catch { /* ignored */ }
                if (!text) return;
                if (el && 'setRangeText' in el) {
                    const start = el.selectionStart ?? el.value.length;
                    const end = el.selectionEnd ?? start;
                    el.setRangeText(text, start, end, 'end');
                    el.dispatchEvent(new Event('input', { bubbles: true }));
                }
                return;
            }
            if (action === 'copy' || action === 'cut') {
                if (el && 'selectionStart' in el) {
                    const start = el.selectionStart ?? 0;
                    const end = el.selectionEnd ?? start;
                    if (end > start) {
                        const text = el.value.substring(start, end);
                        try { await tauriClipboard.writeText(text); } catch { /* ignored */ }
                        if (action === 'cut') {
                            el.setRangeText('', start, end, 'end');
                            el.dispatchEvent(new Event('input', { bubbles: true }));
                        }
                    }
                }
                return;
            }
            if (action === 'select-all') {
                if (el && 'select' in el && typeof el.select === 'function') el.select();
                return;
            }
            // undo / redo work via execCommand in inputs.
            if (action === 'undo' || action === 'redo') {
                document.execCommand(action);
            }
        }

        // Native menu clicks arrive as Tauri events emitted by the Rust menu handler.
        await tauriListen<{ id: string }>('menu-click', async (evt) => {
            const id: string = evt.payload.id;

            // Result mode picker (View → Result Mode: Preview/Unwrapped/Input/Report)
            if (id.startsWith('result-mode:')) {
                const mode = id.split(':')[1] as ResultMode;
                appInstance.setResultMode(mode);
                return;
            }

            // File → Export. A bare `export-pdf` is the report (the default variant);
            // `export-pdf:preview` and friends name one explicitly.
            if (id.startsWith('export-')) {
                const [format, variant] = id.slice('export-'.length).split(':');
                const type = format === 'pdf' ? 'generatePdf'
                    : format === 'html' ? 'saveSourceHtml'
                    : format === 'docx' ? 'saveDocx'
                    : null;
                if (type) {
                    tauriBridge.handleMessage({ type, variant: variant ?? 'report' });
                    return;
                }
            }

            switch (id) {
                case 'new':
                    tabs.newUntitled();
                    break;

                case 'close-tab': {
                    const activeId = tabs.activeId;
                    if (activeId) await tryCloseTab(activeGroup, activeId);
                    break;
                }

                case 'open': {
                    const result = await tauriBridge.openFile();
                    if (result) await loadFile(result.path);
                    break;
                }

                case 'save':
                    await saveActive();
                    break;

                case 'save-as':
                    await saveAsActive();
                    break;

                // An export, so the tab keeps its own path and stays editable —
                // unlike Save As, which would turn it into a locked worksheet.
                case 'save-as-compiled':
                    await tauriBridge.saveCompiled();
                    break;

                case 'save-as-portable':
                    await tauriBridge.savePortable();
                    break;

                case 'toggle-sidebar':
                    appInstance.toggleSidebar();
                    break;

                case 'toggle-preview':
                    appInstance.togglePreview();
                    break;

                case 'toggle-word-wrap':
                    toggleWordWrap();
                    break;

                case 'split-editor':
                    await splitEditor();
                    break;

                case 'unsplit-editor': {
                    // Always close the bottom group; keep the top (primary).
                    const all = [...groups.values()];
                    const bottom = all[all.length - 1];
                    if (all.length > 1 && bottom) await closeGroup(bottom.id);
                    break;
                }

                case 'quit':
                    await tryExit();
                    break;

                case 'refresh':
                    await runRefresh();
                    break;

                case 'show-server-log':
                    // Server stdout/stderr is streamed live into the Output
                    // panel's 'server' channel via the `server-log` Tauri
                    // event, so we just reveal that channel.
                    appInstance.showOutput('server');
                    break;

                case 'stop-server':
                    if (serverManager) {
                        appInstance.appendOutput('info', 'Stopping server…');
                        try {
                            await serverManager.forceStop();
                            appInstance.appendOutput('info', 'Server stopped. Use Restart Server to start it again.');
                        } catch (err) {
                            appInstance.appendOutput('error', `Stop failed: ${err instanceof Error ? err.message : String(err)}`);
                        }
                    }
                    break;

                case 'restart-server':
                    if (serverManager) {
                        appInstance.appendOutput('info', 'Restarting server…');
                        try {
                            await serverManager.restart();
                            appInstance.appendOutput('info', `Server restarted at ${serverManager.getBaseUrl()}`);
                        } catch (err) {
                            appInstance.appendOutput('error', `Restart failed: ${err instanceof Error ? err.message : String(err)}`);
                        }
                    }
                    break;

                case 'undo':
                    runClipboardAction('undo');
                    break;
                case 'redo':
                    runClipboardAction('redo');
                    break;
                case 'cut':
                    await runClipboardAction('cut');
                    break;
                case 'copy':
                    await runClipboardAction('copy');
                    break;
                case 'paste':
                    await runClipboardAction('paste');
                    break;
                case 'select-all':
                    runClipboardAction('select-all');
                    break;
                case 'find':
                    runClipboardAction('find');
                    break;
                case 'replace':
                    runClipboardAction('replace');
                    break;

                case 'help-documentation': {
                    const { openUrl } = await import('@tauri-apps/plugin-opener');
                    await openUrl('https://imartincei.github.io/CalcpadCE/');
                    break;
                }
            }
        });

        // Server stderr (captured by start-server.sh) and PDF errors flow
        // through bridge → window message → Output panel.
        window.addEventListener('message', (e) => {
            const data = (e as MessageEvent).data;
            if (!data || typeof data !== 'object') return;
            if (data.type === 'serverLogResponse') {
                if (data.error) {
                    appInstance.appendOutput('warn',
                        `Server log unavailable (${data.path || '<unknown>'}): ${data.error}`);
                    return;
                }
                const text = (data.content || '').trim();
                if (!text) {
                    appInstance.appendOutput('info',
                        `Server log is empty: ${data.path}`);
                    return;
                }
                appInstance.appendOutput('info', `--- Server log (${data.path}) ---`);
                for (const line of text.split('\n')) {
                    if (!line.trim()) continue;
                    const level = /\[(INFO|WARN|WARNING|ERROR|CRASH)\]/i.exec(line)?.[1]?.toUpperCase();
                    const sev = level === 'ERROR' || level === 'CRASH' ? 'error'
                        : level === 'WARN' || level === 'WARNING' ? 'warn'
                        : level === 'INFO' ? 'info'
                        : 'error';
                    appInstance.appendOutput(sev, line);
                }
                appInstance.appendOutput('info', '--- end server log ---');
            } else if (data.type === 'pdfError') {
                appInstance.appendOutput('error', String(data.message || 'PDF export failed'));
            }
        });

        // ---- Keyboard shortcuts (window-level) ----
        // These catch shortcuts when focus is outside the editor (sidebar,
        // preview iframe parent, etc.). Editor-focused variants are bound per
        // group in wireGroupTauri.
        window.addEventListener('keydown', (e) => {
            // Ctrl+Alt+X — run/refresh (bound here so it fires from any focus).
            if ((e.key === 'x' || e.key === 'X') && e.ctrlKey && e.altKey && !e.shiftKey && !e.metaKey) {
                e.preventDefault();
                void runRefresh();
                return;
            }
            if (!e.ctrlKey || e.metaKey) return;
            // Ctrl+S / Ctrl+Shift+S — fallback when focus is outside the editor.
            if ((e.key === 's' || e.key === 'S') && !e.altKey) {
                e.preventDefault();
                if (e.shiftKey) void saveAsActive(); else void saveActive();
                return;
            }
            // Ctrl+O — open file picker.
            if ((e.key === 'o' || e.key === 'O') && !e.shiftKey && !e.altKey) {
                e.preventDefault();
                void (async () => {
                    const result = await tauriBridge.openFile();
                    if (result) await loadFile(result.path);
                })();
                return;
            }
            // Ctrl+N — new tab.
            if ((e.key === 'n' || e.key === 'N') && !e.shiftKey && !e.altKey) {
                e.preventDefault();
                tabs.newUntitled();
                return;
            }
            // Ctrl+P — toggle preview.
            if ((e.key === 'p' || e.key === 'P') && !e.shiftKey && !e.altKey) {
                e.preventDefault();
                appInstance.togglePreview();
                return;
            }
            // Ctrl+\ — split / focus editor down.
            if (e.key === '\\' && !e.shiftKey && !e.altKey) {
                e.preventDefault();
                void splitEditor();
                return;
            }
            // Ctrl+, — open Settings tab in sidebar (VS Code convention).
            if (e.key === ',' && !e.shiftKey && !e.altKey) {
                e.preventDefault();
                sidebarInstance.switchTab?.('settings');
                return;
            }
            // Ctrl+Shift+B → toggle sidebar (Ctrl+B is reserved for Bold formatting).
            if (e.shiftKey && (e.key === 'B' || e.key === 'b') && !e.altKey) {
                e.preventDefault();
                appInstance.toggleSidebar();
                return;
            }
            if (e.key === 'Tab') {
                e.preventDefault();
                if (e.shiftKey) activeGroup.tabs.activatePrev(); else activeGroup.tabs.activateNext();
                return;
            }
            if (e.key === 't' && !e.shiftKey && !e.altKey) {
                e.preventDefault();
                activeGroup.tabs.newUntitled();
                return;
            }
            if (e.key === 'w' && !e.shiftKey && !e.altKey) {
                e.preventDefault();
                const id = activeGroup.tabs.activeId;
                if (id) void tryCloseTab(activeGroup, id);
                return;
            }
            // Ctrl+1..9 → activate Nth tab in the active group (Ctrl+9 = last).
            if (e.key >= '1' && e.key <= '9' && !e.shiftKey && !e.altKey) {
                const n = parseInt(e.key, 10);
                const index = n === 9 ? activeGroup.tabs.count - 1 : n - 1;
                const target = activeGroup.tabs.all[index];
                if (target) void activateTab(activeGroup.id, target.id);
                e.preventDefault();
            }
        });

        // ---- Close-with-unsaved guard ----
        let isExiting = false;

        async function tryExit(): Promise<void> {
            if (isExiting) return;        // re-entry guard (multiple X clicks)
            isExiting = true;

            try {
                if (!await confirmLeaveUiDoc()) {
                    isExiting = false;
                    return;
                }
                // Walk every dirty tab across all groups one at a time, like VS
                // Code does on window-close. Reuses tryCloseTab so the prompt
                // copy + save-as fallback are identical to manual tab close.
                const dirty: { group: EditorGroup; id: string }[] = [];
                for (const g of groups.values()) {
                    for (const t of g.tabs.all) {
                        if (t.dirty) dirty.push({ group: g, id: t.id });
                    }
                }
                for (const { group, id } of dirty) {
                    const closed = await tryCloseTab(group, id);
                    if (!closed) {
                        // User cancelled — abort exit.
                        isExiting = false;
                        return;
                    }
                }
            } finally {
                if (isExiting) {
                    // Rust owns sidecar shutdown (kill-on-exit hook). This
                    // dispose only tears down TS event listeners.
                    if (serverManager) {
                        try { await serverManager.dispose(); }
                        catch (e) { appInstance.appendOutput('debug', `serverManager.dispose() rejected: ${e}`); }
                    }
                    appInstance.appendOutput('debug', 'Exit path: calling process.exit()');
                    void processExit(0);
                }
            }
        }

        // Intercept the window close button so unsaved tabs get their save prompt
        // before Tauri tears down the webview. tryExit() calls processExit() on
        // confirmation; if the user cancels, the window stays open.
        await getCurrentWindow().onCloseRequested(async (event) => {
            event.preventDefault();
            void tryExit();
        });
    }
}

bootstrap();
