import * as monaco from 'monaco-editor';
import { createApp, nextTick } from 'vue';
import App from './App.vue';
import CalcpadAppVue from 'calcpad-frontend/vue/components/CalcpadApp.vue';
import { initMessaging } from 'calcpad-frontend/vue/services/messaging';
import { discardMetadataDraft } from 'calcpad-frontend/vue/metadata-drafts';
import { MessageBridge } from './services/message-bridge';
import type { TauriServerManager } from './services/server-manager';
import { WorkspaceStateStore, isResultMode, type ResultMode, type WorkspaceLayout } from './services/workspace-state';
import { buildApiSettings, DEFAULT_COMPLETION_DELAY_MS } from 'calcpad-frontend/types/settings';
import { ConnectionMonitor, setLogLevel, coerceLogLevel, stripCpdSnippetWrapper } from 'calcpad-frontend';
import {
    findMetadataCommentBlock,
    serializeMetadataComment,
    buildSourceDefinitionResolver,
    UiOverrideStore,
    UiPauseStore,
    writeUiOverrides,
    extractUiControls,
    isCompiledPath,
    documentHasUiDirectives,
    DEFAULT_PREVIEW_SIZE_MB,
    MIN_PREVIEW_SIZE_MB,
    MIN_CONSOLE_MESSAGES_PER_DOCUMENT,
    MAX_INLINE_IMAGE_TOTAL_BYTES,
    toRangeEdits,
    planFileInfoStamp,
    onlyReviewChanged,
    previewSizeLimitChars,
    previewLimitNoticeHtml,
    formatSize,
    type InlineImageBudget,
    type ExportVariant,
} from 'calcpad-frontend';
import { registerCalcpadLanguage, registerCalcpadTheme, remeasureEditorFontsWhenReady, resolveEditorFontFamily } from './editor/setup';
import { setAppTheme, coerceAppTheme, getResolvedAppTheme, onAppThemeChanged } from './editor/app-theme';
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
import { registerFormattingCommands, getParseMode } from './editor/formatting-commands';
import { registerFormatDocumentProvider } from './editor/format-document';
import { setActiveDocumentKeyResolver, getActiveDocumentKey, type EditorBridge } from './editor/bridge';
import { EditorGroup } from './editor/editor-group';
import { navigationDirection, type NavigationLocation } from './editor/navigation-history';
import { createBrowserNavigationHistory, createDesktopNavigationHistory, type NavigationHistoryClient } from './services/navigation-history';
import { UNTITLED_TITLE, type TabManager } from './tabs/tab-manager';
import { setupTabDragging } from './tabs/tab-drag';
import { toDisplayLogLevel } from 'calcpad-frontend';
import type { UiControl, CalcpadLogLevel, DisplayLogLevel, DraftForm } from 'calcpad-frontend';
import './editor/vscode-variables.css';
import 'calcpad-frontend/vue/styles/base.css';
import './styles/app.css';
import { MetadataFooterView } from './editor/metadata-footer-view';

// Monaco worker setup — must run before editor creation
import './editor/workers';

/** Runtime check: are we running inside a Tauri webview? */
const isTauri = typeof (window as any).__TAURI_INTERNALS__ !== 'undefined';

/** Link schemes a worksheet may ask to open outside the app. */
const EXTERNAL_LINK_SCHEMES = /^(https?|file):/i;

// Server URL: the ?server= query param, then VITE_SERVER_URL, then same origin.
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
        body { color: ${c.fg}; background: ${c.bg}; padding: 20px; font: var(--vscode-font-size, 13px)/1.5 var(--vscode-font-family, system-ui, sans-serif); }
        h3 { text-align: center; font-size: inherit; }
        p { text-align: center; }
        table { margin: 1em auto; border-collapse: collapse; text-align: left; }
        th, td { padding: 4px 12px; }
        th { text-align: right; font-weight: normal; }
        kbd { font-family: var(--vscode-editor-font-family, monospace); font-size: inherit; padding: 1px 4px; border: 1px solid; border-radius: 3px; }
        h4 { text-align: center; font-size: inherit; margin-top: 1.5em; margin-bottom: 0.3em; }
        a { color: ${c.link}; }
    </style>
</head>
<body>
    <h3>Empty Document</h3>
    <p>Start typing to see the preview.</p>
    <h4>Formatting Hotkeys</h4>
    <table>
        <tr><th>Bold</th><td><kbd>Ctrl</kbd>+<kbd>B</kbd></td></tr>
        <tr><th>Italic</th><td><kbd>Ctrl</kbd>+<kbd>I</kbd></td></tr>
        <tr><th>Underline</th><td><kbd>Ctrl</kbd>+<kbd>U</kbd></td></tr>
        <tr><th>Subscript</th><td><kbd>Ctrl</kbd>+<kbd>=</kbd></td></tr>
        <tr><th>Superscript</th><td><kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>=</kbd></td></tr>
        <tr><th>Heading 1-6</th><td><kbd>Ctrl</kbd>+<kbd>1</kbd> ... <kbd>Ctrl</kbd>+<kbd>6</kbd></td></tr>
        <tr><th>Paragraph</th><td><kbd>Ctrl</kbd>+<kbd>L</kbd></td></tr>
        <tr><th>Line Break</th><td><kbd>Ctrl</kbd>+<kbd>R</kbd></td></tr>
        <tr><th>Bulleted List</th><td><kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>L</kbd></td></tr>
        <tr><th>Numbered List</th><td><kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>N</kbd></td></tr>
        <tr><th>Toggle Comment</th><td><kbd>Ctrl</kbd>+<kbd>Q</kbd></td></tr>
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
 * Native message box shown when the calculation server could not start.
 * The editor itself keeps working; only server-backed features (preview,
 * linting, export) need it.
 */
async function showServerBlockedDialog(details: string): Promise<void> {
    const { message: dialogMessage } = await import('@tauri-apps/plugin-dialog');
    const body =
        "CalcpadCE's calculation server could not start.\n\n"
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

async function showConfigInvalidDialog(details: string): Promise<'retry' | 'ignore' | null> {
    const { message: dialogMessage } = await import('@tauri-apps/plugin-dialog');
    const body =
        "CalcpadCE's calculation server could not read its settings file.\n\n"
        + `${details}\n\n`
        + 'Fix the file, then choose Retry to load it again. Choose Ignore to start '
        + 'the server with default settings.';
    try {
        const choice = await dialogMessage(body, {
            title: 'Invalid appsettings.json',
            kind: 'error',
            buttons: { ok: 'Retry', cancel: 'Ignore' },
        });
        // Some platforms report the custom label, others the default button name.
        return choice === 'Retry' || choice === 'Ok' ? 'retry' : 'ignore';
    } catch {
        // The Output panel carries the same message.
        return null;
    }
}

/** A Retry that fails the same way raises onConfigInvalid again, so this loops until resolved. */
async function resolveConfigInvalid(manager: TauriServerManager, details: string): Promise<void> {
    const choice = await showConfigInvalidDialog(details);
    if (!choice) return;
    try {
        await manager.restart({ ignoreSettings: choice === 'ignore' });
    } catch {
        // restart() already logged it.
    }
}

async function bootstrap(): Promise<void> {
    let serverUrl: string;
    let isPrimaryWindow = true;
    let windowLabel = 'main';
    const appVersion = import.meta.env.VITE_APP_VERSION;
    let bridge: MessageBridge | null = null;
    let tauriBridge: import('./services/tauri-bridge').TauriMessageBridge | null = null;
    let serverManager: import('./services/server-manager').TauriServerManager | null = null;
    // Server-manager log lines that arrive before the Output panel mounts
    // get buffered here, then flushed when appInstance is ready.
    const pendingServerLogs: { msg: string; level?: CalcpadLogLevel }[] = [];
    // Raw stdout/stderr lines from the Calcpad.Server sidecar (Rust's
    // `server-log` event), buffered the same way for the same reason.
    const pendingServerRawLogs: { line: string; stream: 'stdout' | 'stderr' }[] = [];

    if (isTauri) {
        windowLabel = (await import('@tauri-apps/api/window')).getCurrentWindow().label;
        const { invoke } = await import('@tauri-apps/api/core');
        const { listen } = await import('@tauri-apps/api/event');
        const updatePrimaryWindow = (label: string) => {
            isPrimaryWindow = windowLabel === label;
            serverManager?.setAutoRestartEnabled(isPrimaryWindow);
        };
        await listen<string>('primary-window-changed', event => updatePrimaryWindow(event.payload));
        updatePrimaryWindow(await invoke<string>('primary_window'));
        // Tauri desktop: the Rust layer owns the Calcpad.Server sidecar
        // (spawn, kill on exit, port discovery). This manager just tracks
        // its URL and surfaces crashes to the Output panel.
        const { TauriServerManager } = await import('./services/server-manager');
        // Only one window may auto-restart a crashed sidecar — `server-crashed` is
        // broadcast, so both would race to respawn it.
        serverManager = new TauriServerManager({
            appendLine: (msg: string, level?: CalcpadLogLevel) => pendingServerLogs.push({ msg, level }),
        }, isPrimaryWindow);
        serverManager.onServerLog = (line: string, stream: 'stdout' | 'stderr') => {
            pendingServerRawLogs.push({ line, stream });
        };

        // The server is shared, so one dialog is enough; the manager already logged the reason.
        serverManager.onStartupBlocked = (details: string) => {
            if (isPrimaryWindow) void showServerBlockedDialog(details);
        };
        const manager = serverManager;
        serverManager.onConfigInvalid = (details: string) => {
            if (isPrimaryWindow) void resolveConfigInvalid(manager, details);
        };

        try {
            await serverManager.start();
        } catch (err) {
            if (!serverManager.startFailureReported) {
                const msg = err instanceof Error ? (err.stack ?? err.message) : String(err);
                pendingServerLogs.push({ msg: `[bootstrap] Server failed to start: ${msg}`, level: 'error' });
            }
            console.error('[bootstrap] Server failed to start:', err);
        }
        serverUrl = serverManager.getBaseUrl() || '';

        const { TauriMessageBridge } = await import('./services/tauri-bridge');
        tauriBridge = new TauriMessageBridge(serverUrl);
        tauriBridge.api.setAuthToken(serverManager.getAuthToken());
        (window as any).calcpadBridge = tauriBridge;
    } else {
        // Pure web: use in-process web bridge
        serverUrl = getServerUrl();
        bridge = new MessageBridge(serverUrl);
        (window as any).calcpadBridge = bridge;
    }

    const activeBridge = tauriBridge ?? bridge!;
    activeBridge.programVersion = appVersion;
    // Last session's panes, result mode and open files, stored apart from the settings.
    const workspace = await WorkspaceStateStore.load(isTauri);

    // Initialize the platform messaging (reads VITE_PLATFORM='web')
    initMessaging();

    // The base message bridge's handleGoToLine looks up the editor via
    // window.monaco (matches the vscode-webview convention). Expose it here so
    // sidebar tabs (TOC, Errors) can post `goToLine` and reach Monaco.
    (window as any).monaco = monaco;

    // Mount the main app layout
    const app = createApp(App, { isDesktop: isTauri });
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
    // Before the buffered startup lines are flushed into the Output panel below, so they are
    // filtered against the level the user actually chose rather than the default.
    setLogLevel(coerceLogLevel((activeBridge as unknown as EditorBridge).getExtraSetting('logLevel')));

    const WORD_WRAP_KEY = 'calcpad.wordWrap';
    const initialWordWrap: 'on' | 'off' =
        localStorage.getItem(WORD_WRAP_KEY) === 'off' ? 'off' : 'on';
    const initialEditorFontFamily = (activeBridge as unknown as EditorBridge).getExtraSetting('editorFontFamily') ?? 'JuliaMono';
    let completionDelayMs = Number(activeBridge.getExtraSetting('completionDelayMs') ?? DEFAULT_COMPLETION_DELAY_MS);

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
    const historyCallbacks = { confirm: confirmLeaveUiDoc, restore: restoreNavigation };
    let navigationHistory: NavigationHistoryClient | undefined = isTauri
        ? undefined : createBrowserNavigationHistory(historyCallbacks);

    function recordNavigation(group: EditorGroup, jump = false): void {
        if (group !== activeGroup) return;
        const tabId = group.tabs.activeId;
        const position = group.editor.getPosition();
        const selection = group.editor.getSelection() ?? undefined;
        if (tabId && position) {
            navigationHistory?.record({ groupId: group.id, tabId, position, selection }, jump);
        }
    }

    function restoreNavigation(location: NavigationLocation): boolean {
        const group = groups.get(location.groupId);
        if (!group?.tabs.modelForTab(location.tabId)) return false;
        if (activeGroup !== group) setActiveGroup(group);
        group.tabs.activate(location.tabId);
        if (location.selection) {
            group.editor.setSelection(location.selection);
            group.editor.revealRangeInCenterIfOutsideViewport(group.editor.getSelection()!);
        } else {
            const position = group.editor.getModel()!.validatePosition(location.position);
            group.editor.setPosition(position);
            group.editor.revealPositionInCenterIfOutsideViewport(position);
        }
        group.editor.focus();
        return true;
    }

    window.addEventListener('keydown', event => {
        const direction = navigationDirection(event, navigator.platform);
        if (!direction) return;
        event.preventDefault();
        event.stopImmediatePropagation();
        void navigationHistory?.go(direction);
    }, true);

    const handleHistoryMouse = (event: MouseEvent): void => {
        if (event.button !== 3 && event.button !== 4) return;
        event.preventDefault();
        event.stopImmediatePropagation();
        if (event.type === 'mousedown') void navigationHistory?.go(event.button === 3 ? -1 : 1);
    };
    for (const type of ['mousedown', 'mouseup', 'auxclick'] as const) {
        window.addEventListener(type, handleHistoryMouse, true);
    }

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

    window.addEventListener('message', (event) => {
        const msg = (event as MessageEvent).data;
        if (msg?.type !== 'completionDelayMsChanged' && msg?.type !== 'settingsResponse') return;
        if (event.source !== null) return;
        completionDelayMs = msg.completionDelayMs;
        for (const g of groups.values()) g.editor.updateOptions({ quickSuggestionsDelay: completionDelayMs });
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

    // Also hands the result to App.vue, which themes the loading overlay with it.
    function resolvePreviewTheme(): 'light' | 'dark' {
        const stored = editorBridge.getExtraSetting('previewTheme') ?? 'system';
        const resolved = stored === 'light' || stored === 'dark'
            ? stored
            : window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
        appInstance.setPreviewTheme(resolved);
        return resolved;
    }

    // Output line the next unwrapped refresh should scroll to, per group. Set
    // by the wrapped->unwrapped two-step in the 'navigateToLine' handler.
    const pendingPreviewScrollLine = new Map<string, number>();

    // Values entered into #UI controls. Held in memory so typing in a form never
    // dirties the file; "Save values" writes them into the document.
    const uiOverrides = new UiOverrideStore();
    // Documents whose in-memory values have not been written back yet.
    const uiOverridesDirty = new Set<string>();
    // The #pause step each input form is on.
    const uiPause = new UiPauseStore();
    // Documents with an unapplied Properties or Info form, and which, as reported by the panel.
    const metadataDirty = new Map<string, Set<DraftForm>>();
    // Controls each document's last input-form render produced, which is what tells the
    // Properties tab whether a saved value still applies to anything.
    const uiControls = new Map<string, UiControl[]>();

    /** Caps what a preview render will inline, and says so rather than dropping images silently. */
    function previewImageBudget(group: EditorGroup): InlineImageBudget {
        return {
            maxTotalBytes: MAX_INLINE_IMAGE_TOTAL_BYTES,
            // A view, so it degrades: refusing the render would be worse than an
            // under-illustrated one. The compiled export fails instead.
            onExceeded: 'skip',
            onSkip: (skipped) => appInstance.appendOutput('warn',
                `Image budget of ${formatSize(MAX_INLINE_IMAGE_TOTAL_BYTES)} reached — ${skipped} image(s) left unembedded in the preview.`,
                'preview', group.id),
        };
    }

    function previewSizeLimit(): number {
        const stored = Number(editorBridge.getExtraSetting('maxPreviewSizeMB'));
        return previewSizeLimitChars(Number.isFinite(stored) && stored > 0 ? stored : DEFAULT_PREVIEW_SIZE_MB);
    }

    /**
     * Whether this render is too big to show, and if so shows the notice in its place.
     * Called before anything copies the HTML: the pipeline below multiplies it several
     * times over, so passing here is what bounds every one of those copies.
     */
    function previewTooLarge(group: EditorGroup, html: string, uiPrint = false): boolean {
        const docKey = uiDocKeyFor(group);
        const limit = previewSizeLimit();
        if (html.length <= limit) return false;

        appInstance.appendOutput('warn',
            `Preview blocked: this document rendered to ${formatSize(html.length)}, over the ${formatSize(limit)} limit.`,
            'preview', group.id);
        const notice = previewLimitNoticeHtml({
            chars: html.length,
            limitChars: limit,
            theme: resolvePreviewTheme(),
        });
        if (uiPrint) void appInstance.setUiPrintHtml(group.id, notice, docKey);
        else void appInstance.setPreviewHtml(group.id, notice, undefined, docKey);
        return true;
    }

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
     * `#UI` controls is one to fill in. Decided once per file and recorded as decided even
     * when the answer is no, and split from {@link autoEnterUiMode} so the caller can skip
     * its own preview refresh.
     */
    function shouldAutoEnterUiMode(group: EditorGroup): boolean {
        if (!isTauri) return false;
        const activeId = group.tabs.activeId;
        if (!activeId) return false;

        const path = group.tabs.getFilePath(activeId);
        // Untitled documents are left alone: there is no file yet to remember the decision
        // against, and a #UI line typed into a scratch buffer is being written rather than
        // filled in.
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
     * Tells the sidebar whether the active document is being filled in rather than
     * edited, so the tabs that act on source can grey themselves out. Input mode hides
     * the editor; a compiled worksheet has no source to act on at all, however the
     * results pane happens to be set.
     */
    function syncInputMode(): void {
        const activeId = activeGroup.tabs.activeId;
        const path = activeId ? activeGroup.tabs.getFilePath(activeId) : null;
        const compiled = !!path && isCompiledPath(path);
        const active = compiled
            || (appInstance.isPreviewVisible() && appInstance.getResultMode() === 'ui');
        window.dispatchEvent(new MessageEvent('message', {
            data: { type: 'inputModeChanged', active, compiled },
        }));
    }

    /**
     * Key that #UI values are stored under: the file path, so the same document open in two
     * tabs or two groups shares one set of entered values. Unsaved documents fall back to
     * the tab key, which is the only thing that tells them apart.
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
    activeBridge.setUiOverridesProvider(() => {
        syncUiOverrides(activeGroup, activeGroup.editor.getValue());
        return uiOverrides.toRecord(activeUiDocKey());
    });
    activeBridge.setUiControlsProvider(() => uiControls.get(activeUiDocKey()) ?? null);
    activeBridge.setUiControlsSink((controls) => uiControls.set(activeUiDocKey(), controls));
    activeBridge.setMetadataDirtySink((docKey, dirty, form) => {
        const forms = metadataDirty.get(docKey) ?? new Set<DraftForm>();
        if (dirty) forms.add(form);
        else forms.delete(form);
        if (forms.size) metadataDirty.set(docKey, forms);
        else metadataDirty.delete(docKey);
    });

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
     * Sidebar line navigation (the TOC) while the input form is up: the editor it would
     * normally reveal is hidden, so the form and the report beside it are scrolled instead.
     * The cursor still moves without taking focus off the form.
     */
    activeBridge.onGoToLine = (line: number) => {
        if (!appInstance.isPreviewVisible() || appInstance.getResultMode() !== 'ui') return false;
        appInstance.scrollPreviewToSourceLine(activeGroup.id, line, true);
        activeGroup.editor.revealLineInCenter(line);
        activeGroup.editor.setPosition({ lineNumber: line, column: 1 }, 'code.navigation');
        return true;
    };

    /**
     * Whether the plain preview renders with the entered #UI values applied. Off by default,
     * since preview is where the document itself is read; turned on it becomes a debugging
     * view of the filled-in form.
     */
    function previewAppliesUiOverrides(): boolean {
        return editorBridge.getExtraSetting('previewUiOverrides') === 'true';
    }

    /**
     * Keeps a document's overrides in step with its saved uiOverrides comment, so entered
     * values survive reopening the file and an edit to that comment — a key renamed by hand
     * to follow a renamed variable — reaches the render instead of being shadowed by them.
     */
    function syncUiOverrides(group: EditorGroup, content: string): void {
        if (!uiOverrides.syncFromSource(uiDocKeyFor(group), content)) return;
        uiOverridesDirty.delete(uiDocKeyFor(group));
        refreshUiDirtyIndicator();
    }

    /**
     * Retires the controls cached for a document, so an edit that renames or removes a #UI
     * line is not judged against the list a render found before it. The panel asks for a
     * fresh one while it is showing saved values; anywhere else the next form render fills it.
     */
    function invalidateUiControls(group: EditorGroup): void {
        if (!uiControls.delete(uiDocKeyFor(group))) return;
        if (group === activeGroup) activeBridge.refreshUiControls();
    }

    const PREVIEW_LOADING_DELAY_MS = 400;
    const PREVIEW_LOADING_MIN_MS = 300;
    const previewLoadingSeq = new Map<string, number>();

    /**
     * Delayed "Calculating…" overlay, returning the call that takes it back down.
     * Shown only if the round-trip runs long, so fast renders never flash it, and held
     * for a minimum once shown so one finishing just past the delay does not strobe it.
     */
    function showPreviewLoading(group: EditorGroup): () => Promise<void> {
        // Held past the response, so a superseded render is still holding the overlay
        // when its replacement starts and would otherwise hide it on the way out.
        const seq = (previewLoadingSeq.get(group.id) ?? 0) + 1;
        previewLoadingSeq.set(group.id, seq);
        let shownAt = 0;
        const timer = window.setTimeout(() => {
            if (previewLoadingSeq.get(group.id) !== seq) return;
            shownAt = performance.now();
            appInstance.setPreviewLoading(group.id, true);
        }, PREVIEW_LOADING_DELAY_MS);
        return async () => {
            window.clearTimeout(timer);
            if (shownAt) {
                const held = performance.now() - shownAt;
                if (held < PREVIEW_LOADING_MIN_MS)
                    await new Promise(r => setTimeout(r, PREVIEW_LOADING_MIN_MS - held));
            }
            if (previewLoadingSeq.get(group.id) !== seq) return;
            appInstance.setPreviewLoading(group.id, false);
        };
    }

    async function refreshPreviewFor(group: EditorGroup): Promise<void> {
        if (!appInstance.isPreviewVisible()) return;
        if (appInstance.getResultMode() === 'ui' && group !== activeGroup) return;

        const content = group.editor.getValue();
        const settings = activeBridge.getSettings();
        const apiSettings = buildApiSettings(settings);
        const mode = appInstance.getResultMode() as ResultMode;
        const theme = resolvePreviewTheme();

        if (!content.trim()) {
            void appInstance.setPreviewHtml(group.id, getEmptyPreviewHtml(getResolvedAppTheme()));
            return;
        }

        const fileContext = getFileContext ? await getFileContext(content) : {};

        const hideLoading = showPreviewLoading(group);
        let result;
        try {
            // The report applies entered values just as the input form does, so both need
            // the document's saved ones seeded first. Preview joins them when the setting
            // asks it to, which is how an error that only shows up once the form is filled
            // in gets looked at against the source.
            const overrideMode = mode === 'ui' || mode === 'report'
                || (mode === 'preview' && previewAppliesUiOverrides());
            if (overrideMode) syncUiOverrides(group, content);
            // Unwrapped always shows the document as written.
            const ui = overrideMode
                ? {
                    enableUi: mode === 'ui',
                    uiOverrides: uiOverrides.toRecord(uiDocKeyFor(group)),
                    stepPauses: mode === 'ui',
                    pauseAfter: uiPause.pauseAfter(uiDocKeyFor(group)),
                }
                : undefined;
            const write = activeBridge.mayWrite(mode === 'report', mode === 'ui');
            result = mode === 'unwrapped'
                ? await activeBridge.api.convertUnwrapped(content, apiSettings, fileContext.sourceFilePath, theme, { key: `preview:${group.id}`, write })
                // The report is a print layout, but on screen, so it keeps the line
                // anchors that forPrint would otherwise suppress.
                : await activeBridge.api.convert(
                    content, apiSettings, 'html', mode === 'report', fileContext.sourceFilePath, theme, ui,
                    mode === 'report' ? true : undefined, { key: `preview:${group.id}`, write });
        } catch (err) {
            void hideLoading();
            throw err;
        }

        // Consume any pending two-step scroll target for this group: only the
        // unwrapped view it was set for should honor it, and only once.
        const scrollToLine = (mode === 'unwrapped' && pendingPreviewScrollLine.get(group.id) != null)
            ? pendingPreviewScrollLine.get(group.id)
            : undefined;
        pendingPreviewScrollLine.delete(group.id);

        if (result && !(result instanceof ArrayBuffer)) {
            // Before the image inlining and the injection passes each copy it. Showing a
            // render costs several times its own size, so the check that can hold the line
            // is the one in front of all of them.
            if (previewTooLarge(group, result.html)) {
                await hideLoading();
                return;
            }
            // Desktop: inline on-disk images so relative <img src> paths (from
            // the images-folder / custom-path insert options) render in the
            // sandboxed preview iframe, matching PDF export.
            const finalHtml = tauriBridge
                ? await tauriBridge.inlineDocumentImages(result.html, previewImageBudget(group))
                : result.html;
            const committed = appInstance.setPreviewHtml(group.id, finalHtml, scrollToLine, uiDocKeyFor(group));
            if (mode === 'ui') {
                uiControls.set(uiDocKeyFor(group), extractUiControls(result.html));
                uiPause.update(uiDocKeyFor(group), result.pause);
                appInstance.setUiPauseState(uiPause.state(uiDocKeyFor(group)) ?? null);
            }
            window.dispatchEvent(new MessageEvent('message', {
                data: { type: 'updateConvertErrors', errors: result.errors },
            }));
            await committed;
        }
        await hideLoading();

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
            // A report even though a form is open, so this is the half of input mode that writes.
            true, { key: `preview:${group.id}`, write: activeBridge.mayWrite(true) });
        if (!result || result instanceof ArrayBuffer) return;
        if (previewTooLarge(group, result.html, true)) return;

        const html = tauriBridge
            ? await tauriBridge.inlineDocumentImages(result.html, previewImageBudget(group))
            : result.html;
        await appInstance.setUiPrintHtml(group.id, html, uiDocKeyFor(group));
    }

    // Nothing else re-renders on an app-theme switch: `updateColorTheme` only calls
    // `applyColorTheme` and never announces `settingsChanged`.
    onAppThemeChanged(() => refreshAllPreviews());

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
        activeBridge.refreshDocumentMetadata();
        refreshUiDirtyIndicator();
        syncInputMode();
        if (appInstance.isPreviewVisible()) void refreshPreviewFor(group);
        refreshWindowTitle();
        recordNavigation(group);
    }

    // Assigned in the Tauri branch; the browser tab title is all there is on web.
    let setNativeTitle: ((title: string) => void) | null = null;

    function refreshWindowTitle(): void {
        const active = activeGroup?.tabs.activeTab;
        const title = active?.filePath
            ? `${active.title} – CalcpadCE ${appVersion}`
            : `CalcpadCE – ${appVersion}`;
        document.title = title;
        appInstance.setWindowTitle(active?.filePath ? active.title : title);
        setNativeTitle?.(title);
    }

    // ---- Per-group wiring (common to web + desktop) ----
    function wireGroupCommon(group: EditorGroup): void {
        const ed = group.editor;

        group.disposables.push(ed.onDidChangeCursorSelection(event => {
            if (event.source === 'restoreState'
                || (event.source === 'modelChange' && event.reason === monaco.editor.CursorChangeReason.NotSet)) return;
            recordNavigation(group, event.source === 'code.navigation' || event.source === 'code.jump');
        }));
        group.tabs.onTabRemoved(tabId => navigationHistory?.remove(group.id, tabId));

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

        // Manual "run" — re-renders all previews, for when Auto-Run Preview is off. The
        // Ctrl+Alt+X shortcut is bound both here and at the window level (Tauri) so it fires
        // from anywhere in the app.
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
                const resolve = buildSourceDefinitionResolver(model.getValue().split(/\r?\n/));
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
        // retire its cached #UI controls, and (only when this is the active group) the
        // sidebar TOC.
        let definitionsTimer: ReturnType<typeof setTimeout> | null = null;
        let previewTimer: ReturnType<typeof setTimeout> | null = null;
        let tocTimer: ReturnType<typeof setTimeout> | null = null;
        group.disposables.push(
            new MetadataFooterView(ed, () => editorBridge.getExtraSetting('showFileMetadata') === 'true'),
            ed.onDidChangeModelContent(() => {
                if (definitionsTimer) clearTimeout(definitionsTimer);
                definitionsTimer = setTimeout(() => {
                    invalidateUiControls(group);
                    void refreshDefinitionsFor(group);
                }, 800);
                if (appInstance.isPreviewVisible() && editorBridge.getExtraSetting('autoRun') !== 'false') {
                    if (previewTimer) clearTimeout(previewTimer);
                    previewTimer = setTimeout(() => void refreshPreviewFor(group), 800);
                }
                if (group === activeGroup) {
                    if (tocTimer) clearTimeout(tocTimer);
                    tocTimer = setTimeout(() => {
                        activeBridge.refreshHeadings();
                        activeBridge.refreshDocumentMetadata();
                    }, 800);
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
            if (group === activeGroup) refreshWindowTitle();
            syncOpenFiles();
        });

        // On tab switch within this group, re-emit markers + re-lint + repaint.
        group.tabs.onActiveModelChanged(() => {
            recordNavigation(group);
            applyCompiledWorksheetMode(group);
            const enteringUi = shouldAutoEnterUiMode(group);
            refreshProblemsFor(group);
            // Re-lint: content-change events don't fire on tab switch, so the
            // debounced lint in setupDiagnostics never re-runs for the new model.
            void group.diagnostics?.refresh();
            // The mode switch below renders the form itself, so rendering here first would
            // just be a preview nobody sees.
            if (!enteringUi && appInstance.isPreviewVisible()) void refreshPreviewFor(group);
            if (group === activeGroup) {
                activeBridge.refreshHeadings();
                activeBridge.refreshDocumentMetadata();
            }
            void refreshDefinitionsFor(group);
            if (enteringUi) autoEnterUiMode();
        });

        // Initial definitions population for the seeded tab.
        setTimeout(() => void refreshDefinitionsFor(group), 500);
    }

    /**
     * Create a group's editor in its App.vue container, wire it, seed a tab. If `linkFrom`
     * is given the new group's first tab shares that group's active tab's model instead of
     * starting blank, which is how a split defaults to a live-synced view of the current file.
     */
    async function createAndWireGroup(id: string, seedContent = '', linkFrom?: EditorGroup): Promise<EditorGroup> {
        appInstance.addGroup(id);
        await nextTick();
        const container = appInstance.getEditorContainer(id) as HTMLElement | null;
        if (!container) throw new Error(`Editor container for group ${id} not found`);
        const group = new EditorGroup(id, container, { wordWrap: initialWordWrap, fontFamily: initialEditorFontFamily, completionDelayMs });
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
    const primaryGroup = new EditorGroup('g0', g0Container, { wordWrap: initialWordWrap, fontFamily: initialEditorFontFamily, completionDelayMs });
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
    // Monotonic group-id allocator. Ids are never reused: after an unsplit the surviving
    // group may be the second one, so a fixed 'g1' would collide on the next split.
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
        syncOpenFiles();
        appInstance.removeGroup(groupId);
        other?.editor.focus();
    }

    // Set by the Tauri branch below; its absence marks a host that cannot quit.
    let quitApplication: (() => Promise<void>) | null = null;

    /** Native three-button prompt on desktop; the in-app modal on web. */
    function confirmThreeWay(opts: {
        title: string;
        message: string;
        yesLabel: string;
        noLabel: string;
    }): Promise<'yes' | 'no' | 'cancel'> {
        return editorBridge.confirmThreeWay?.(opts) ?? appInstance.showConfirm(opts);
    }

    /**
     * Settles the zero-tab state a close would otherwise leave behind: an emptied split
     * group is merged away (closeGroup keeps the survivor, so emptying the top group
     * promotes the bottom one), and emptying the only group quits — or on web, where
     * there is nothing to quit, starts a fresh untitled document.
     */
    async function handleEmptyGroup(group: EditorGroup): Promise<void> {
        if (group.tabs.count > 0) return;
        if (groups.size > 1) {
            await closeGroup(group.id);
            return;
        }
        if (quitApplication) await quitApplication();
        else group.tabs.newUntitled();
    }

    appInstance.onSplitRequest = () => { void splitEditor(); };
    appInstance.onCloseGroupRequest = (groupId: string) => { void closeGroup(groupId); };
    appInstance.onGroupFocusRequest = (groupId: string) => {
        const g = groups.get(groupId);
        if (g) setActiveGroup(g);
    };

    // ---- Include navigation (Go-to-Definition / Find All References) ----
    // Find All References needs the include files' models registered so the panel can render
    // their snippets, so this is only wired up on Tauri desktop where there is filesystem
    // access. All handlers act on the active group.
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

    // Go-to-Definition must stay side-effect free — Monaco calls provideDefinition on
    // Ctrl+hover just to draw the underline, so the provider gets a pure URI and the real
    // open happens in the editor opener. The resolved absolute path is stashed under the
    // exact URI string we mint, since fsPath would re-case the Windows drive letter and
    // break the tab lookup's strict path compare.
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

    // Registered before the include opener, which is unshifted ahead of it and so still
    // claims its own URIs first. Monaco's default would otherwise window.open() an http
    // link and navigate the app window itself for any other scheme.
    monaco.editor.registerLinkOpener({
        open(resource) {
            const url = resource.toString();
            if (!EXTERNAL_LINK_SCHEMES.test(url)) return false;
            return openExternalLink(url).then(() => true);
        },
    });

    function syncOpenFiles(): void {
        const paths = [...new Set([...groups.values()].flatMap(g =>
            g.tabs.all.flatMap(t => t.filePath ? [t.filePath] : [])))];
        void invokeTauri?.('set_open_files', { paths });
    }

    function activateOpenFile(path: string, position?: monaco.IPosition | null): boolean {
        for (const group of [activeGroup, ...[...groups.values()].filter(g => g !== activeGroup)]) {
            const existing = group.tabs.findByPath(path);
            if (!existing) continue;
            if (group !== activeGroup) setActiveGroup(group);
            group.tabs.activate(existing.id, position ?? undefined);
            group.editor.focus();
            return true;
        }
        return false;
    }

    monaco.editor.registerEditorOpener({
        openCodeEditor(source, resource, selectionOrPosition) {
            const group = [...groups.values()].find(group => group.editor === source);
            if (!group) return false;
            const tab = group.tabs.all.find(tab => group.tabs.modelForTab(tab.id)!.uri.toString() === resource.toString());
            if (!tab) return false;
            const position = selectionOrPosition && ('startLineNumber' in selectionOrPosition
                ? { lineNumber: selectionOrPosition.startLineNumber, column: selectionOrPosition.startColumn }
                : selectionOrPosition);
            group.tabs.activate(tab.id, position);
            if (selectionOrPosition && 'startLineNumber' in selectionOrPosition) {
                group.editor.setSelection(selectionOrPosition, 'code.jump');
            }
            return true;
        },
    });

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
                const position = selectionOrPosition
                    ? 'startLineNumber' in selectionOrPosition
                        ? { lineNumber: selectionOrPosition.startLineNumber, column: selectionOrPosition.startColumn }
                        : { lineNumber: selectionOrPosition.lineNumber, column: selectionOrPosition.column }
                    : null;
                if (activateOpenFile(absPath, position)) return;
                if (await invokeTauri!<boolean>('navigate_to_open_file', { path: absPath, position })) return;
                tabs.openFile(absPath, await bridge.readFile(absPath), position ?? undefined);
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
        if (e.data?.type === 'autoRunChanged') appInstance.setAutoRunEnabled(e.data.enabled);
        if (e.data?.type === 'settingsResponse') appInstance.setAutoRunEnabled(e.data.enableAutoRun);
        if (e.data?.type === 'linterMinSeverityChanged') {
            for (const g of groups.values()) void g.diagnostics?.refresh();
        }
        if (e.data?.type === 'maxOutputLinesChanged') {
            const n = Number(e.data.value);
            if (Number.isFinite(n)) appInstance.setMaxOutputLines(n);
        }
        // A raised limit should take effect on what is already on screen, and a document
        // blocked under the old one is showing the notice rather than a render.
        if (e.data?.type === 'maxPreviewSizeChanged') {
            const n = Number(e.data.value);
            if (Number.isFinite(n) && n >= MIN_PREVIEW_SIZE_MB) refreshAllPreviews();
        }
        // Baked into the scripts a render injects, so the previews are re-run to pick it up
        // rather than left relaying under the old cap.
        if (e.data?.type === 'maxPreviewConsoleMessagesChanged') {
            const n = Number(e.data.value);
            if (Number.isFinite(n) && n >= MIN_CONSOLE_MESSAGES_PER_DOCUMENT) {
                appInstance.setMaxPreviewConsoleMessages(n);
                refreshAllPreviews();
            }
        }
        if (e.data?.type === 'exportError') {
            appInstance.appendOutput('error', String(e.data.message ?? 'Export failed'));
        }
    });

    appInstance.setAutoRunEnabled(editorBridge.getExtraSetting('autoRun') !== 'false');

    // Apply persisted cap at startup — the sidebar's settingsResponse will
    // sync it too, but the log wiring below can fire before that arrives.
    {
        const stored = Number(editorBridge.getExtraSetting('maxOutputLines'));
        if (Number.isFinite(stored) && stored >= 10) appInstance.setMaxOutputLines(stored);
        const messages = Number(editorBridge.getExtraSetting('maxPreviewConsoleMessages'));
        if (Number.isFinite(messages) && messages >= MIN_CONSOLE_MESSAGES_PER_DOCUMENT)
            appInstance.setMaxPreviewConsoleMessages(messages);
    }

    // Wire the bridge's insertText handler to the active editor.
    activeBridge.onInsertText = (text: string) => {
        const selection = editor.getSelection();
        if (selection) {
            // Markup snippets are written for Calcpad mode; an #html/#markdown block takes
            // the same content without the #val wrapper or the ' quotes.
            const insert = getParseMode(editor, activeBridge) === 'cpd'
                ? text
                : stripCpdSnippetWrapper(text);
            editor.executeEdits('calcpad-insert', [{
                range: selection,
                text: insert,
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
        level: DisplayLogLevel,
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

    // Monaco cancels superseded provider work — completions, semantic tokens, hovers — on
    // essentially every keystroke, and unwinds it by rejecting with a CancellationError
    // ("Canceled: Canceled"). That is control flow, not a failure, and Monaco exports
    // isCancellationError for exactly this; VS Code's own error handler drops them the same way.
    // AbortError is the fetch-side equivalent, raised by our own superseded requests.
    const isCancellation = (reason: unknown): boolean =>
        reason instanceof DOMException ? reason.name === 'AbortError'
            : reason instanceof Error && reason.name === 'Canceled' && reason.message === 'Canceled';

    // Stack included: without it a bare "Canceled: Canceled" gives nothing to trace it back to.
    const describeError = (reason: unknown): string =>
        reason instanceof Error ? (reason.stack ?? `${reason.name}: ${reason.message}`) : String(reason);

    // Also capture unhandled errors
    window.addEventListener('error', (e) => {
        if (isCancellation(e.error)) return;
        appInstance.appendOutput('error', `Uncaught: ${e.message} (${e.filename}:${e.lineno})`);
    });
    window.addEventListener('unhandledrejection', (e) => {
        if (isCancellation(e.reason)) {
            e.preventDefault();
            return;
        }
        appInstance.appendOutput('error', `Unhandled rejection: ${describeError(e.reason)}`);
    });

    // Messages posted from the preview iframes (App.vue:injectPreviewConsole /
    // injectLineLinks). Each message carries `groupId` so it routes to the
    // group whose preview emitted it.
    window.addEventListener('message', (e: MessageEvent) => {
        const data = e.data;
        if (!data) return;

        // Two kinds of sender reach this listener. The host's own bridge announces settings
        // changes with a synthetic MessageEvent carrying no source; everything else comes
        // from a preview frame rendering untrusted worksheet HTML and drives editor
        // navigation and #UI values, so it is accepted only from a window that really is one
        // of our preview frames — they are sandboxed to an opaque origin, so e.origin is
        // "null" and window identity is the only usable check.
        const fromHost = e.source === null;
        const fromPreview = appInstance.isPreviewFrameSource(e.source);
        if (!fromHost && !fromPreview) return;

        // Forward console.* + uncaught errors to the "Preview Console" channel,
        // tagged with the originating group.
        if (data.type === 'previewConsole') {
            if (!fromPreview) return;
            const level: DisplayLogLevel =
                data.level === 'warn' ? 'warn'
                : data.level === 'error' ? 'error'
                : data.level === 'debug' ? 'debug'
                : 'info';
            appInstance.appendOutput(level, String(data.message ?? ''), 'preview', data.groupId);
            return;
        }

        // Settings announcements are the host talking to itself; a preview must not
        // be able to trigger a full re-render loop by forging one.
        if (data.type === 'previewThemeChanged' || data.type === 'settingsChanged'
            || data.type === 'previewUiOverridesChanged') {
            if (!fromHost) return;
            refreshAllPreviews();
            return;
        }

        // A #UI control was edited in the preview. Record the value and re-render
        // that group so dependent results recalculate.
        if (data.type === 'uiValueChange') {
            if (!fromPreview) return;
            const group = (data.groupId && groups.get(data.groupId)) || activeGroup;
            const docKey = uiDocKeyFor(group);
            if (!uiOverrides.set(docKey, String(data.varName), String(data.newValue))) return;
            uiOverridesDirty.add(docKey);
            refreshUiDirtyIndicator();
            void refreshPreviewFor(group);
            return;
        }

        // F5 pressed inside a preview frame (desktop only).
        if (data.type === 'previewRefresh') {
            if (!fromPreview) return;
            void runRefresh();
            return;
        }

        // Results -> editor navigation. An 'output' line only makes sense in the unwrapped
        // view when the document has macros/includes, so the pane flips there scrolled to it
        // (the two-step); a 'source' line navigates Monaco directly, and the message's
        // groupId selects the group.
        if (data.type === 'navigateToLine') {
            if (!fromPreview) return;
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
                group.editor.setPosition({ lineNumber: line, column: 1 }, 'code.navigation');
                group.editor.focus();
            }
            return;
        }

        // A link in the rendered worksheet. The frame has already blocked the
        // navigation; the scheme is re-checked here because the message crosses
        // from untrusted content.
        if (data.type === 'openExternal') {
            if (!fromPreview) return;
            void openExternalLink(String(data.url ?? ''));
            return;
        }
    });

    /** Prompts with the target, then hands it to the OS. Shared by the preview and the editor. */
    async function openExternalLink(url: string): Promise<void> {
        if (!EXTERNAL_LINK_SCHEMES.test(url)) return;
        const isFile = /^file:/i.test(url);
        // A browser cannot act on a file:// URL, so there is nothing to offer.
        if (isFile && !isTauri) return;
        try {
            // A malformed file: URL from the worksheet throws here and is reported below.
            const target = isFile ? fileUrlToPath(url) : url;
            if (!(await appInstance.showOpenLink(target, isFile ? 'file' : 'browser'))) return;
            if (isTauri) {
                const opener = await import('@tauri-apps/plugin-opener');
                if (isFile) await opener.openPath(target);
                else await opener.openUrl(url);
            } else {
                window.open(url, '_blank', 'noopener,noreferrer');
            }
        } catch (e) {
            appInstance.appendOutput('error', `Could not open ${url}: ${describeError(e)}`);
        }
    }

    function fileUrlToPath(url: string): string {
        const path = decodeURIComponent(new URL(url).pathname);
        // Windows paths arrive as /C:/... from the URL form.
        return /^\/[A-Za-z]:/.test(path) ? path.slice(1) : path;
    }

    appInstance.appendOutput('info', `CalcpadCE Web started — server: ${serverUrl}`);

    // Flush any server-manager log lines buffered before the Output panel mounted,
    // then redirect future ones straight into the panel.
    for (const { msg, level } of pendingServerLogs) appInstance.appendOutput(toDisplayLogLevel(level), msg);
    pendingServerLogs.length = 0;
    for (const { line, stream } of pendingServerRawLogs) {
        appInstance.appendOutput(stream === 'stderr' ? 'error' : 'info', line, 'server');
    }
    pendingServerRawLogs.length = 0;
    const connectionMonitor = new ConnectionMonitor({
        probe: (timeoutMs: number) => activeBridge.api.checkHealth(timeoutMs),
        onStatusChanged: (status) => appInstance.setServerStatus(status),
        onRecovered: () => { void runRefresh('Server reconnected — refreshing…'); },
        log: (msg: string) => appInstance.appendOutput('info', `[Server] ${msg}`),
    });

    if (serverManager) {
        serverManager.setLogger({
            appendLine: (msg: string, level?: CalcpadLogLevel) => appInstance.appendOutput(toDisplayLogLevel(level), msg),
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
        // 'running' only means Rust saw a bound port, so applyLifecycle probes before going green.
        serverManager.onStatusChanged = (state, detail) => connectionMonitor.applyLifecycle(state, detail);
    }

    // serverManager.start() ran before the callbacks above existed, so seed from what it knows.
    // markStopped leaves polling suspended, which is right: only a user restart can help.
    if (serverManager && !serverManager.isRunning) {
        connectionMonitor.markStopped(serverManager.isConfigInvalid ? 'invalid appsettings.json' : 'server did not start');
    } else {
        // A Retry/Ignore answered during boot can restart it before onUrlChanged was wired.
        if (serverManager) activeBridge.api.setBaseUrl(serverManager.getBaseUrl());
        connectionMonitor.start();
    }

    // A hidden webview throttles timers hard, so the poll can be well stale on restore.
    document.addEventListener('visibilitychange', () => {
        if (document.visibilityState === 'visible') connectionMonitor.probeSoon();
    });

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
        }, 'code.navigation');
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
        const g = groups.get(groupId);
        if (!g) return;
        g.tabs.close(id);
        void handleEmptyGroup(g);
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
        void handleEmptyGroup(g);
    };

    // Mount the Calcpad sidebar; Files is available on desktop.
    const versionConfig = {
        isVSCode: false,
        isWeb: !isTauri,
        isDesktop: isTauri,
        isWebOrDesktop: true,
    };
    const sidebarApp = createApp(CalcpadAppVue, { versionConfig, appVersion });
    const sidebarInstance = sidebarApp.mount('#vue-sidebar') as InstanceType<typeof CalcpadAppVue>;

    window.addEventListener('keydown', (event) => {
        if (event.key.toLowerCase() === 'i' && event.altKey && !event.ctrlKey && !event.metaKey && !event.shiftKey) {
            event.preventDefault();
            event.stopPropagation();
            appInstance.showSidebar();
            void sidebarInstance.focusInsertSearch();
        }
    }, true);

    // Reveal the sidebar before opening Settings.
    function openSettings(): void {
        appInstance.showSidebar();
        sidebarInstance.switchTab?.('settings');
    }

    // ---- Result mode and pane layout: restore, then keep persisted ----
    // Installs from before the workspace store kept the mode in the settings extras, under
    // `previewMode` with `wrapped` for what is now `preview`; carry that over once.
    const legacyMode = workspace.isFresh
        ? editorBridge.getExtraSetting('resultMode') ?? editorBridge.getExtraSetting('previewMode')
        : undefined;
    const restoredMode = legacyMode === 'wrapped' ? 'preview' : legacyMode;
    appInstance.setResultMode(isResultMode(restoredMode) ? restoredMode : workspace.get().resultMode);

    appInstance.setLayout(workspace.get().layout);
    // Coming up visible is not a toggle, so onPreviewToggled never fires for it and
    // nothing else would put the first render on screen.
    if (appInstance.isPreviewVisible()) setTimeout(refreshAllPreviews, 50);
    // Wired after the restore so it doesn't write the same values straight back.
    appInstance.onLayoutChanged = (layout: WorkspaceLayout) => {
        if (isPrimaryWindow) workspace.update({ layout });
    };

    // The restores above predate onResultModeChanged, and the sidebar has only just
    // mounted, so seed it with the mode and pane state the session came up in.
    syncInputMode();

    // The sidebar's own hide button, relayed by the bridge's 'hideSidebar'.
    window.addEventListener('calcpad-toggle-sidebar', () => { appInstance.toggleSidebar(); });

    // Manual refresh: re-lint, refresh definitions/headings, redraw previews, and re-extract
    // Export-tab plots. From the Server > Refresh menu, the Run action, and on reconnect.
    let refreshInFlight = false;
    let refreshQueued = false;
    async function runRefresh(reason: string = 'Refreshing…'): Promise<void> {
        // Coalesced rather than dropped: the reconnect refresh has no user to retry it, and
        // rendering against the server it just replaced is what it exists to correct.
        if (refreshInFlight) {
            refreshQueued = true;
            return;
        }
        refreshInFlight = true;
        appInstance.appendOutput('info', reason);
        try {
            for (const g of groups.values()) {
                await g.diagnostics?.refresh();
                await refreshDefinitionsFor(g);
                if (appInstance.isPreviewVisible()) await refreshPreviewFor(g);
            }
            activeBridge.refreshHeadings();
            // The Export tab's plot list caches independently of the preview.
            window.dispatchEvent(new MessageEvent('message', { data: { type: 'getPlots' } }));
        } finally {
            refreshInFlight = false;
        }
        if (refreshQueued) {
            refreshQueued = false;
            await runRefresh(reason);
        }
    }

    appInstance.onResultModeChanged = (mode: ResultMode) => {
        // Not persisted when a #UI document chose it rather than the user: the session would
        // otherwise come back up in a form, whatever document is opened next.
        if (!autoUiSwitchInFlight) workspace.update({ resultMode: mode });
        refreshUiDirtyIndicator();
        syncInputMode();
        refreshAllPreviews();
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

    appInstance.onUiPauseStepRequest = (direction: 'next' | 'previous') => {
        if (uiPause.step(activeUiDocKey(), direction)) void refreshPreviewFor(activeGroup);
    };

    /**
     * Leaving the input form discards the entered values — they only live in memory
     * until written into the document — so prompt for unwritten ones first. Returns
     * false to keep the form open when the user cancels.
     */
    async function leaveUiDoc(): Promise<boolean> {
        const docKey = activeUiDocKey();
        if (uiOverridesDirty.has(docKey)) {
            const choice = await confirmThreeWay({
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
        uiPause.clear(docKey);
        appInstance.setUiPauseState(null);
        return true;
    }

    appInstance.onExitUiModeRequest = leaveUiDoc;

    /**
     * The input form always shows the active document, so switching documents takes the
     * form's values with it — prompt as if the form were closing. Returns false when the
     * user cancels the switch.
     */
    async function confirmLeaveUiDoc(): Promise<boolean> {
        if (!appInstance.isPreviewVisible() || appInstance.getResultMode() !== 'ui') return true;
        return await leaveUiDoc();
    }

    /** The Properties panel's draft key for a tab: its file, so split panes share one draft. */
    function metadataDocKeyFor(group: EditorGroup, id: string): string {
        return group.tabs.getFilePath(id) || `tab:${id}`;
    }

    /**
     * A Properties form edited but not applied lives only in the panel, so closing the
     * document it belongs to throws it away. Returns false when the user backs out.
     */
    async function confirmDiscardMetadataDraft(docKey: string, title: string): Promise<boolean> {
        const forms = metadataDirty.get(docKey);
        if (!forms) return true;
        const names = [
            forms.has('info') && 'File Info',
            forms.has('uiOverrides') && 'UI Overrides',
            forms.has('properties') && 'Properties',
        ].filter(Boolean).join(', ');
        const choice = await confirmThreeWay({
            title: 'Unsaved properties',
            message: `Discard the unapplied ${names} changes to ${title}?`,
            yesLabel: 'Discard',
            noLabel: 'Keep Editing',
        });
        if (choice !== 'yes') return false;
        metadataDirty.delete(docKey);
        discardMetadataDraft(docKey);
        return true;
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

    let persistTransferredTab = async (_tabId: string): Promise<void> => {};
    const enableTabDragging = () => setupTabDragging(isTauri, {
        async capture(groupId, tabId) {
            const group = groups.get(groupId)!;
            if (group === activeGroup && !await confirmLeaveUiDoc()) return null;
            if (group.tabs.isLastReference(tabId)
                && !await confirmDiscardMetadataDraft(metadataDocKeyFor(group, tabId), group.tabs.getTitle(tabId)!)) return null;
            return group.tabs.exportTab(tabId);
        },
        async move(sourceGroupId, tabId, groupId, beforeId) {
            const source = groups.get(sourceGroupId)!;
            const target = groups.get(groupId)!;
            if (source === target) {
                source.tabs.reorder(tabId, beforeId);
                return;
            }
            if (!await confirmLeaveUiDoc()) return;
            if (!await confirmDiscardMetadataDraft(metadataDocKeyFor(source, tabId), source.tabs.getTitle(tabId)!)) return;
            const snapshot = source.tabs.exportTab(tabId);
            const model = source.tabs.modelForTab(tabId)!;
            const existing = target.tabs.all.find(tab => target.tabs.modelForTab(tab.id) === model);
            setActiveGroup(target);
            const id = existing?.id ?? target.tabs.importTab(snapshot, model);
            target.tabs.activate(id);
            target.tabs.reorder(id, beforeId);
            await persistTransferredTab(id);
            source.tabs.close(tabId);
            setActiveGroup(target);
            target.editor.focus();
        },
        async receive(snapshot, groupId, beforeId) {
            const target = groups.get(groupId);
            if (!target || !await confirmLeaveUiDoc()) return false;
            const existing = snapshot.filePath ? target.tabs.findByPath(snapshot.filePath) : null;
            const documentGroup = snapshot.filePath
                ? [...groups.values()].find(group => group.tabs.findByPath(snapshot.filePath!))
                : undefined;
            const openDocument = documentGroup?.tabs.findByPath(snapshot.filePath!) ?? null;
            if (openDocument && (documentGroup!.tabs.getContent(openDocument.id) !== snapshot.content
                || openDocument.dirty !== snapshot.dirty)) {
                appInstance.appendOutput('warn', `Cannot move ${snapshot.title}: this window already has a different version open.`);
                appInstance.showOutput();
                return false;
            }
            const scratch = target.tabs.activeTab;
            const replaceScratch = scratch?.filePath === null && !scratch.dirty
                && target.tabs.getContent(scratch.id) === ''
                && !metadataDirty.has(metadataDocKeyFor(target, scratch.id));
            setActiveGroup(target);
            const id = existing?.id ?? target.tabs.importTab(snapshot,
                openDocument ? documentGroup!.tabs.modelForTab(openDocument.id)! : undefined);
            target.tabs.activate(id);
            target.tabs.reorder(id, beforeId);
            await persistTransferredTab(id);
            if (replaceScratch && scratch.id !== id) target.tabs.close(scratch.id);
            if (isTauri) (await import('@tauri-apps/api/window')).getCurrentWindow().setFocus();
            return true;
        },
        remove(groupId, tabId, snapshot) {
            const group = groups.get(groupId);
            const model = group?.tabs.modelForTab(tabId);
            if (!model) return;
            if (model.getVersionId() !== snapshot.version
                || group!.tabs.getFilePath(tabId) !== snapshot.filePath
                || group!.tabs.isDirty(tabId) !== snapshot.dirty) {
                appInstance.appendOutput('warn', `${snapshot.title} changed during the move and was kept in this window.`);
                appInstance.showOutput();
                return;
            }
            group!.tabs.close(tabId);
        },
    });

    // Tauri-specific: native menu clicks + file operations
    if (isTauri && tauriBridge) {
        const [
            { listen: tauriListen },
            { getCurrentWindow },
            tauriClipboard,
            { invoke: tauriInvoke },
            { exists: fileExists },
        ] = await Promise.all([
            import('@tauri-apps/api/event'),
            import('@tauri-apps/api/window'),
            import('@tauri-apps/plugin-clipboard-manager'),
            import('@tauri-apps/api/core'),
            import('@tauri-apps/plugin-fs'),
        ]);
        invokeTauri = tauriInvoke;
        // An unscoped listener answers every `emit_to` whatever its target, so anything
        // addressed to one window has to name this window to stay out of the others.
        const listenHere = <T>(event: string, handler: (e: { payload: T }) => void) =>
            tauriListen<T>(event, handler, { target: windowLabel });
        navigationHistory = await createDesktopNavigationHistory(historyCallbacks);
        await getCurrentWindow().onFocusChanged(({ payload: focused }) => {
            if (focused) recordNavigation(activeGroup);
        });
        recordNavigation(activeGroup);
        // The seeded group was wired before this branch ran, so kick the first native title.
        setNativeTitle = (title: string) => { void getCurrentWindow().setTitle(title); };
        refreshWindowTitle();
        await listenHere<{ path: string; position: monaco.IPosition | null }>('navigate-to-file', evt => {
            activateOpenFile(evt.payload.path, evt.payload.position);
        });
        syncOpenFiles();
        // Route Ctrl+Shift+V "Paste as Comment" through the same Tauri-native
        // clipboard as the rest of the app (WebKitGTK workaround).
        editorBridge.readClipboardText = () => tauriClipboard.readText();
        // ---- Autosave drafts (10s debounce per tab) ----
        // Rust owns the on-disk drafts dir (<app_data>/drafts) and each tab is assigned a
        // stable UUID on first autosave. Tab ids are namespaced per group, so drafts never
        // collide across groups.
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
            const title = g.tabs.getTitle(tabId) ?? UNTITLED_TITLE;
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

        persistTransferredTab = writeDraft;

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

        await listenHere<DraftInfo[]>('drafts-recovered', async (evt) => {
            const drafts = evt.payload;
            if (!drafts || drafts.length === 0) return;
            const summary = drafts
                .map(d => `• ${d.filename}${d.filePath ? ` (${d.filePath})` : ''}`)
                .join('\n');
            const choice = await confirmThreeWay({
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

        /**
         * Open `path` in a tab: focuses it if the active group already holds that file, or
         * opens a second live-synced tab onto the same model when another group has it, which
         * is what lets one file be open in both split panes. Otherwise reads from disk.
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

        // ---- Session: reopen last launch's files ----
        function captureSession(): void {
            const openFiles: string[] = [];
            for (const g of groups.values()) {
                for (const t of g.tabs.all) {
                    if (t.filePath && !openFiles.includes(t.filePath)) openFiles.push(t.filePath);
                }
            }
            workspace.update({ openFiles, activeFile: activeGroup.tabs.activeTab?.filePath ?? '' });
        }

        /**
         * Reopens last launch's files into the primary group. Deliberately not routed
         * through loadFile, which would push every restored path back onto the
         * recent-files list — rebuilding the native menu once per file — on every launch.
         */
        async function restoreSession(): Promise<void> {
            // A new window comes up empty, and never overwrites the main window's session.
            if (windowLabel !== 'main') return;
            for (const path of workspace.get().openFiles) {
                // Files deleted or moved since last launch drop out of the session silently.
                try {
                    if (!await fileExists(path)) continue;
                    tabs.openFile(path, await tauriBridge!.readFile(path));
                } catch (err) {
                    appInstance.appendOutput('debug', `Session restore skipped ${path}: ${err}`);
                }
            }
            const activePath = workspace.get().activeFile;
            const restored = activePath ? tabs.findByPath(activePath) : null;
            if (restored) tabs.activate(restored.id);
            // The first file replaces the seeded untitled tab in place, which is not an
            // active-model change, so nothing else settles the mode for it.
            applyCompiledWorksheetMode(activeGroup);
            if (shouldAutoEnterUiMode(activeGroup)) autoEnterUiMode();
        }

        await restoreSession();

        // Drain any files handed to us at cold start by the OS's .cpd file
        // association. Runs after the listener above is wired so the bridge's
        // synchronous dispatch inside handleOpenFileByPath actually lands.
        // Only the main window drains them; a second window opened later would otherwise
        // steal files the launch handed to the first.
        if (isTauri && windowLabel === 'main') {
            try {
                const pending = await tauriInvoke<string[]>('take_pending_launch_files');
                for (const path of pending) await loadFile(path);
            } catch {
                /* older desktop builds may not expose the command; ignore */
            }
        }

        /**
         * Writes the file-info comment into the active model just before it is saved to
         * `filePath`, returning the content to write. `saved` is the file as it is on disk,
         * absent for a new one.
         */
        function stampedContentFor(filePath: string, saved?: string): string {
            const model = tabs.activeModel;
            if (!model) return '';
            const edits = planFileInfoStamp(model.getLinesContent(), {
                userName: editorBridge.getExtraSetting('userName') ?? '',
                programVersion: appVersion,
                fileName: filePath,
                reviewOnly: saved !== undefined && onlyReviewChanged(saved, model.getValue()),
            });
            const ranges = toRangeEdits(edits, model.getLineCount(), i => model.getLineMaxColumn(i + 1) - 1, model.getEOL());
            // Through the model: a compiled worksheet's editor is read-only and would reject executeEdits.
            model.pushEditOperations([], ranges.map(r => ({
                range: new monaco.Range(r.startLine + 1, r.startCharacter + 1, r.endLine + 1, r.endCharacter + 1),
                text: r.text,
            })), () => null);
            return model.getValue();
        }

        /**
         * Save the active tab, prompting for a path when it has none. Returns true if saved,
         * false if the user cancelled or there is no active tab.
         */
        async function saveActive(): Promise<boolean> {
            const active = tabs.activeTab;
            if (!active) return false;
            if (active.filePath) {
                // Saving an unchanged file is not an update.
                const content = tabs.isDirty(active.id)
                    ? stampedContentFor(active.filePath, await tauriBridge!.readFile(active.filePath).catch(() => undefined))
                    : tabs.activeModel?.getValue() ?? '';
                await tauriBridge!.saveFile(active.filePath, content);
                tabs.markActiveSaved();
                await deleteDraft(active.id);
                return true;
            }
            const newPath = await tauriBridge!.saveFileAs(stampedContentFor);
            if (!newPath) return false;
            tabs.markActiveSaved({ filePath: newPath });
            // Saving as .cpdz turns the tab into a compiled worksheet.
            applyCompiledWorksheetMode(activeGroup);
            await tauriBridge!.addRecentFile(newPath);
            await deleteDraft(active.id);
            return true;
        }

        persistActiveTab = saveActive;
        activeBridge.setDocumentSaver({
            isDirty: () => !!tabs.activeTab && tabs.isDirty(tabs.activeTab.id),
            save: saveActive,
        });

        async function saveAsActive(): Promise<boolean> {
            const active = tabs.activeTab;
            const newPath = await tauriBridge!.saveFileAs(stampedContentFor);
            if (!newPath) return false;
            tabs.markActiveSaved({ filePath: newPath });
            applyCompiledWorksheetMode(activeGroup);
            await tauriBridge!.addRecentFile(newPath);
            if (active) await deleteDraft(active.id);
            return true;
        }

        /**
         * Close a tab in a specific group, prompting if dirty; returns false if the user
         * cancelled. The prompt is skipped when another tab still references the same model,
         * since the content isn't actually being discarded.
         */
        async function tryCloseTab(group: EditorGroup, id: string): Promise<boolean> {
            const target = group.tabs.all.find(t => t.id === id);
            if (!target) return true;
            // Closing the document the input form is showing takes its values away.
            if (group === activeGroup && id === group.tabs.activeId && !await confirmLeaveUiDoc()) return false;
            if (!await confirmDiscardMetadataDraft(metadataDocKeyFor(group, id), target.title)) return false;
            // Activate the group + tab so the editor shows what's being asked about.
            if (activeGroup !== group) setActiveGroup(group);
            // Re-read the dirty flag: saving the input form's values above may have
            // written the file already.
            if (group.tabs.isDirty(id) && group.tabs.isLastReference(id)) {
                if (id !== group.tabs.activeId) group.tabs.activate(id);
                const choice = await confirmThreeWay({
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

        async function openNewWindow(): Promise<void> {
            try { await tauriInvoke('new_window'); }
            catch (err) { appInstance.appendOutput('error', `Could not open a new window: ${err}`); }
        }

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
            ed.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyMod.Alt | monaco.KeyCode.KeyW, () => {
                void openNewWindow();
            });
            ed.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.KeyP, () => {
                appInstance.togglePreview();
            });
            ed.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.Backslash, () => {
                void splitEditor();
            });
            ed.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.Comma, () => {
                openSettings();
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

            // Registered here rather than beside captureSession so it starts only once
            // restoreSession() has finished laying the session back out, and stops again
            // once the exit path has taken its snapshot.
            group.tabs.onTabsChanged(() => {
                if (isPrimaryWindow && !isExiting) captureSession();
            });

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

        /**
         * The user-initiated close. tryCloseTab itself deliberately does not settle: the
         * unsplit and exit paths call it to drain a group they are already tearing down.
         */
        async function closeTabAndSettle(group: EditorGroup, id: string): Promise<void> {
            if (await tryCloseTab(group, id)) await handleEmptyGroup(group);
        }

        // Override tab-strip close actions with save-prompt-aware versions.
        appInstance.onTabCloseRequest = (groupId: string, id: string) => {
            const g = groups.get(groupId);
            if (g) void closeTabAndSettle(g, id);
        };

        async function tryCloseTabsSequentially(group: EditorGroup, ids: string[]): Promise<void> {
            for (const id of ids) {
                const ok = await tryCloseTab(group, id);
                if (!ok) return;
            }
            await handleEmptyGroup(group);
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
            // A real DOM selection (text picked inside the hover panel, parameter hints or
            // output) takes priority over the editor's own model selection, since Monaco
            // renders the main text via its own selection overlay. Copy only, and only when
            // the editor lacks focus: Monaco mirrors its selection into a hidden textarea
            // whose value is a truncated screen-reader representation for large selections,
            // and a cut routed this way also wrote to the clipboard without touching the
            // document.
            if (action === 'copy' && !editor.hasTextFocus()) {
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
            // The menu accelerator beats the preview frame's own Ctrl+F handler, so the
            // frame never sees the key and the host has to open the find bar for it.
            if (action === 'find' && appInstance.openFindInFocusedPreview()) return;
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
        const runMenuCommand = async (id: string): Promise<void> => {

            // Result mode picker (View → Result Mode: Preview/Unwrapped/Input/Report)
            if (id.startsWith('result-mode:')) {
                const mode = id.split(':')[1] as ResultMode;
                appInstance.setResultMode(mode);
                return;
            }

            // File → Open Recent. The id carries the path itself, so the click opens
            // the file whose label was clicked even if the list has moved on since.
            if (id.startsWith('open-recent:')) {
                await loadFile(id.slice('open-recent:'.length));
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
                    tauriBridge.handleMessage({ type, variant: variant ?? 'report', openDirectly: format === 'pdf' || format === 'docx' });
                    return;
                }
            }

            switch (id) {
                case 'new':
                    tabs.newUntitled();
                    break;

                case 'new-window':
                    await openNewWindow();
                    break;

                case 'close-tab': {
                    const activeId = tabs.activeId;
                    if (activeId) await closeTabAndSettle(activeGroup, activeId);
                    break;
                }

                case 'open': {
                    const result = await tauriBridge!.openFile();
                    if (result) await loadFile(result.path);
                    break;
                }

                case 'clear-recent':
                    await tauriBridge!.clearRecentFiles();
                    break;

                case 'save':
                    await saveActive();
                    break;

                case 'save-as':
                    await saveAsActive();
                    break;

                // An export, so the tab keeps its own path and stays editable —
                // unlike Save As, which would turn it into a locked worksheet.
                case 'save-as-compiled':
                    await tauriBridge!.saveCompiled();
                    break;

                case 'save-as-portable':
                    await tauriBridge!.savePortable();
                    break;

                case 'toggle-sidebar':
                    appInstance.toggleSidebar();
                    break;

                case 'go-to-settings':
                    openSettings();
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
        };
        appInstance.onCommandRequest = (id: string) => { void runMenuCommand(id); };
        await listenHere<{ id: string }>('menu-click', (evt) => {
            void runMenuCommand(evt.payload.id).catch(err => console.error('menu action failed:', err));
        });

        interface ControlRequestPayload {
            id: number;
            command: string;
            args: Record<string, unknown>;
        }

        function controlRespond(id: number, ok: boolean, result?: unknown, error?: string): void {
            const response: Record<string, unknown> = { ok };
            if (result !== undefined) response.result = result;
            if (error !== undefined) response.error = error;
            void tauriInvoke('control_respond', { id, response })
                .catch(err => console.error('control response failed:', err));
        }

        function findOpenTab(path: string): {
            group: EditorGroup;
            tab: { id: string; title: string; filePath: string | null; dirty: boolean };
        } | null {
            for (const group of groups.values()) {
                const tab = group.tabs.findByPath(path);
                if (tab) return { group, tab };
            }
            return null;
        }

        function currentPreviewHtml(): string {
            const buffers = Array.from(document.querySelectorAll<HTMLElement>('.preview-buffers'));
            const target = document.querySelector<HTMLElement>('.preview-buffers.active-group') ?? buffers[0];
            return target?.querySelector<HTMLIFrameElement>('.preview-frame:not(.preview-frame-back)')?.srcdoc ?? '';
        }

        function activeTabOrThrow() {
            const tab = activeGroup.tabs.activeTab;
            if (!tab) throw new Error('no active tab');
            return tab;
        }

        async function saveActiveToPath(targetPath: string): Promise<string> {
            const saved = await tauriBridge!.saveFileAs(stampedContentFor, targetPath);
            if (!saved) throw new Error('save failed');
            tabs.markActiveSaved({ filePath: saved });
            applyCompiledWorksheetMode(activeGroup);
            await tauriBridge!.addRecentFile(saved);
            const active = tabs.activeTab;
            if (active) await deleteDraft(active.id);
            return saved;
        }

        async function setTabContent(path: string, content: string): Promise<void> {
            let found = findOpenTab(path);
            if (!found) {
                const disk = await tauriBridge!.readFile(path).catch(() => '');
                activeGroup.tabs.openFile(path, disk);
                found = findOpenTab(path);
            }
            if (!found) throw new Error(`could not open ${path}`);
            const model = found.group.tabs.modelForTab(found.tab.id);
            if (!model) throw new Error(`no editor model for ${path}`);
            model.setValue(content);
            if (found.group.id !== activeGroup.id) setActiveGroup(found.group);
            found.group.tabs.activate(found.tab.id);
        }

        function closeTabForControl(path: string | undefined, force: boolean): string | null {
            let group: EditorGroup;
            let tab: { id: string; title: string; filePath: string | null; dirty: boolean };
            if (path) {
                const found = findOpenTab(path);
                if (!found) throw new Error(`not open: ${path}`);
                group = found.group;
                tab = found.tab;
            } else {
                group = activeGroup;
                tab = activeTabOrThrow();
            }
            if (group.tabs.isDirty(tab.id) && group.tabs.isLastReference(tab.id) && !force) {
                throw new Error('the tab has unsaved changes; pass --force to discard them');
            }
            group.tabs.close(tab.id);
            return tab.filePath;
        }

        async function reloadTabForControl(path: string | undefined, force: boolean): Promise<string> {
            let group: EditorGroup;
            let tab: { id: string; title: string; filePath: string | null; dirty: boolean };
            if (path) {
                const found = findOpenTab(path);
                if (!found) throw new Error(`not open: ${path}`);
                group = found.group;
                tab = found.tab;
            } else {
                group = activeGroup;
                tab = activeTabOrThrow();
            }
            if (!tab.filePath) throw new Error('the tab has no file on disk');
            if (group.tabs.isDirty(tab.id) && !force) {
                throw new Error('the tab has unsaved changes; pass --force to discard them');
            }
            const content = await tauriBridge!.readFile(tab.filePath);
            const model = group.tabs.modelForTab(tab.id);
            if (!model) throw new Error(`no editor model for ${tab.filePath}`);
            if (group.id !== activeGroup.id) setActiveGroup(group);
            group.tabs.activate(tab.id);
            model.setValue(content);
            group.tabs.markActiveSaved();
            return tab.filePath;
        }

        function controlState(): Record<string, unknown> {
            const sidebar = document.querySelector<HTMLElement>('.sidebar-pane');
            const editor = (window as any).calcpadActiveEditor;
            return {
                window: windowLabel,
                windowTitle: document.title,
                activeGroup: activeGroup.id,
                activeId: activeGroup.tabs.activeId,
                activeFile: activeGroup.tabs.activeTab?.filePath ?? null,
                activeTitle: activeGroup.tabs.activeTab?.title ?? null,
                dirty: [...groups.values()].some(group => group.tabs.anyDirty()),
                resultMode: appInstance.getResultMode(),
                previewVisible: appInstance.isPreviewVisible(),
                sidebarVisible: !!sidebar && getComputedStyle(sidebar).display !== 'none',
                wordWrap: editor?.getRawOptions?.().wordWrap ?? null,
                serverUrl: ((window as any).calcpadBridge as { apiClient?: { getBaseUrl?(): string } })
                    ?.apiClient?.getBaseUrl?.() ?? null,
                groups: [...groups.values()].map(group => ({
                    id: group.id,
                    activeId: group.tabs.activeId,
                    active: group === activeGroup,
                    tabs: group.tabs.all.map(tab => ({
                        id: tab.id,
                        title: tab.title,
                        filePath: tab.filePath,
                        dirty: tab.dirty,
                        active: tab.id === group.tabs.activeId,
                    })),
                })),
            };
        }

        async function handleControlRequest(request: ControlRequestPayload): Promise<void> {
            const { id, command, args } = request;
            try {
                switch (command) {
                    case 'open': {
                        const path = String(args.path ?? '');
                        if (!path) throw new Error('missing path');
                        await loadFile(path);
                        const found = findOpenTab(path);
                        if (!found) throw new Error(`could not open ${path}`);
                        controlRespond(id, true, {
                            path,
                            activeId: found?.tab.id ?? null,
                            title: found?.tab.title ?? null,
                        });
                        return;
                    }
                    case 'close': {
                        const path = args.path ? String(args.path) : undefined;
                        const closed = closeTabForControl(path, Boolean(args.force));
                        controlRespond(id, true, { closed: true, path: closed });
                        return;
                    }
                    case 'save': {
                        const active = activeTabOrThrow();
                        if (!active.filePath) throw new Error('the active tab has no path; use save-as');
                        const saved = await saveActive();
                        if (!saved) throw new Error('save failed');
                        controlRespond(id, true, { saved: true, path: active.filePath });
                        return;
                    }
                    case 'save_as': {
                        const path = String(args.path ?? '');
                        if (!path) throw new Error('missing path');
                        const saved = await saveActiveToPath(path);
                        controlRespond(id, true, { saved: true, path: saved });
                        return;
                    }
                    case 'menu': {
                        const menuId = String(args.id ?? '');
                        if (!menuId) throw new Error('missing menu id');
                        await runMenuCommand(menuId);
                        controlRespond(id, true, { id: menuId });
                        return;
                    }
                    case 'refresh': {
                        await runRefresh('Control refresh');
                        while (refreshInFlight || refreshQueued) {
                            await new Promise(resolve => setTimeout(resolve, 50));
                        }
                        controlRespond(id, true, { refreshed: true });
                        return;
                    }
                    case 'state':
                        controlRespond(id, true, controlState());
                        return;
                    case 'content': {
                        const path = args.path ? String(args.path) : undefined;
                        const active = activeGroup.tabs.activeTab;
                        const found = path ? findOpenTab(path) : (active ? { group: activeGroup, tab: active } : null);
                        if (found) {
                            const content = found.group.tabs.modelForTab(found.tab.id)?.getValue() ?? '';
                            controlRespond(id, true, {
                                path: found.tab.filePath,
                                title: found.tab.title,
                                content,
                                dirty: found.tab.dirty,
                                open: true,
                            });
                            return;
                        }
                        if (!path) throw new Error('no active tab');
                        controlRespond(id, true, {
                            path,
                            title: null,
                            content: await tauriBridge!.readFile(path),
                            dirty: false,
                            open: false,
                        });
                        return;
                    }
                    case 'set_content': {
                        const content = String(args.content ?? '');
                        const path = args.path ? String(args.path) : activeTabOrThrow().filePath;
                        if (!path) throw new Error('the active tab has no path; pass a destination file');
                        if (isCompiledPath(path)) throw new Error('compiled worksheets are read-only');
                        await setTabContent(path, content);
                        const found = findOpenTab(path);
                        controlRespond(id, true, {
                            path,
                            dirty: found ? found.group.tabs.isDirty(found.tab.id) : true,
                        });
                        return;
                    }
                    case 'reload': {
                        const path = args.path ? String(args.path) : undefined;
                        const reloaded = await reloadTabForControl(path, Boolean(args.force));
                        controlRespond(id, true, { reloaded: true, path: reloaded });
                        return;
                    }
                    case 'result': {
                        if (!appInstance.isPreviewVisible()) appInstance.togglePreview();
                        await refreshPreviewFor(activeGroup);
                        let html = currentPreviewHtml();
                        for (let attempt = 0; attempt < 100 && !html; attempt += 1) {
                            await new Promise(resolve => setTimeout(resolve, 100));
                            html = currentPreviewHtml();
                        }
                        if (!html) throw new Error('the preview did not render in time');
                        controlRespond(id, true, {
                            html,
                            resultMode: appInstance.getResultMode(),
                            previewVisible: appInstance.isPreviewVisible(),
                        });
                        return;
                    }
                    case 'export': {
                        const format = String(args.format ?? '');
                        const path = String(args.path ?? '');
                        const variant = String(args.variant ?? 'report') as ExportVariant;
                        if (!path) throw new Error('missing output path');
                        if (!['pdf', 'html', 'docx', 'cpdz', 'zip'].includes(format)) {
                            throw new Error(`unsupported export format: ${format}`);
                        }
                        if (!['report', 'preview', 'input', 'unwrapped'].includes(variant)) {
                            throw new Error(`unsupported export variant: ${variant}`);
                        }
                        const saved = await activeBridge.exportActive(
                            format as 'pdf' | 'html' | 'docx' | 'cpdz' | 'zip', variant, path);
                        if (!saved) throw new Error('the export produced no file');
                        controlRespond(id, true, { path: saved });
                        return;
                    }
                    case 'plots': {
                        const plots = await activeBridge.collectPlots();
                        controlRespond(id, true, {
                            plots: plots.map((plot, index) => ({
                                index,
                                sourceIndex: plot.index,
                                ext: plot.ext,
                                mime: plot.mime,
                                sizeBytes: plot.bytes.length,
                            })),
                        });
                        return;
                    }
                    case 'save_plot': {
                        const index = Number(args.index);
                        const path = String(args.path ?? '');
                        if (!Number.isInteger(index) || index < 0) throw new Error('invalid plot index');
                        if (!path) throw new Error('missing output path');
                        const saved = await activeBridge.savePlotTo(index, path);
                        if (!saved) throw new Error('the plot was not saved');
                        controlRespond(id, true, { path: saved });
                        return;
                    }
                    case 'save_plots': {
                        const path = String(args.path ?? '');
                        if (!path) throw new Error('missing output path');
                        const saved = await activeBridge.savePlotsZipTo(path);
                        if (!saved) throw new Error('the plots were not saved');
                        controlRespond(id, true, { path: saved });
                        return;
                    }
                    case 'editor': {
                        const editor = (window as any).calcpadActiveEditor;
                        if (!editor) throw new Error('no active editor');
                        const model = editor.getModel();
                        const action = String(args.action ?? '');
                        if (action === 'focus') {
                            editor.focus();
                            controlRespond(id, true, { focused: true });
                            return;
                        }
                        if (!model) throw new Error('no active editor model');
                        const selection = editor.getSelection();
                        switch (action) {
                            case 'info': {
                                const position = editor.getPosition();
                                const current = editor.getSelection();
                                controlRespond(id, true, {
                                    position: position
                                        ? { line: position.lineNumber, column: position.column }
                                        : null,
                                    selection: current
                                        ? {
                                            startLine: current.startLineNumber,
                                            startColumn: current.startColumn,
                                            endLine: current.endLineNumber,
                                            endColumn: current.endColumn,
                                        }
                                        : null,
                                    lineCount: model.getLineCount(),
                                    valueLength: model.getValueLength(),
                                    readOnly: editor.getRawOptions?.().readOnly ?? false,
                                });
                                return;
                            }
                            case 'goto': {
                                const line = Math.max(1, Number(args.line ?? 1));
                                const column = Math.max(1, Number(args.column ?? 1));
                                const position = { lineNumber: line, column };
                                editor.setPosition(position, 'control');
                                editor.revealPositionInCenter(position);
                                editor.focus();
                                controlRespond(id, true, { position: { line, column } });
                                return;
                            }
                            case 'select': {
                                const range = new monaco.Range(
                                    Number(args.startLine ?? 1),
                                    Number(args.startColumn ?? 1),
                                    Number(args.endLine ?? args.startLine ?? 1),
                                    Number(args.endColumn ?? args.startColumn ?? 1),
                                );
                                editor.setSelection(range);
                                editor.focus();
                                controlRespond(id, true, {
                                    selection: {
                                        startLine: range.startLineNumber,
                                        startColumn: range.startColumn,
                                        endLine: range.endLineNumber,
                                        endColumn: range.endColumn,
                                    },
                                });
                                return;
                            }
                            case 'insert': {
                                const text = String(args.text ?? '');
                                if (!selection) throw new Error('no editor selection');
                                const applied = editor.executeEdits('control-insert', [
                                    { range: selection, text, forceMoveMarkers: true },
                                ]);
                                if (!applied) throw new Error('the editor is read-only');
                                editor.pushUndoStop();
                                controlRespond(id, true, { inserted: text.length });
                                return;
                            }
                            case 'undo':
                            case 'redo':
                                editor.trigger('control', action, null);
                                controlRespond(id, true, { action });
                                return;
                            case 'find': {
                                const query = String(args.query ?? '');
                                if (!query) throw new Error('missing query');
                                const wordSeparators = args.wholeWord
                                    ? editor.getOption(monaco.editor.EditorOption.wordSeparators)
                                    : null;
                                const matches = model.findMatches(
                                    query,
                                    false,
                                    Boolean(args.regex),
                                    Boolean(args.caseSensitive),
                                    wordSeparators,
                                    false,
                                    200,
                                );
                                controlRespond(id, true, {
                                    count: matches.length,
                                    matches: matches.map((match: monaco.editor.FindMatch) => ({
                                        startLine: match.range.startLineNumber,
                                        startColumn: match.range.startColumn,
                                        endLine: match.range.endLineNumber,
                                        endColumn: match.range.endColumn,
                                        text: match.matches ? match.matches[0] : model.getValueInRange(match.range),
                                    })),
                                });
                                return;
                            }
                            case 'replace_all': {
                                const query = String(args.query ?? '');
                                if (!query) throw new Error('missing query');
                                const replacement = String(args.replacement ?? '');
                                const wordSeparators = args.wholeWord
                                    ? editor.getOption(monaco.editor.EditorOption.wordSeparators)
                                    : null;
                                const all = model.findMatches(
                                    query,
                                    false,
                                    Boolean(args.regex),
                                    Boolean(args.caseSensitive),
                                    wordSeparators,
                                    true,
                                    10001,
                                );
                                const truncated = all.length > 10000;
                                const matches = truncated ? all.slice(0, 10000) : all;
                                const expand = (match: monaco.editor.FindMatch): string => {
                                    if (!args.regex || !match.matches) return replacement;
                                    return replacement.replace(/\$(\$|&|\d+)/g, (_, token: string) => {
                                        if (token === '$') return '$';
                                        if (token === '&') return match.matches?.[0] ?? '';
                                        return match.matches?.[Number(token)] ?? '';
                                    });
                                };
                                if (matches.length > 0) {
                                    const applied = editor.executeEdits('control-replace', matches
                                        .slice()
                                        .reverse()
                                        .map((match: monaco.editor.FindMatch) => ({ range: match.range, text: expand(match) })));
                                    if (!applied) throw new Error('the editor is read-only');
                                    editor.pushUndoStop();
                                }
                                controlRespond(id, true, { replaced: matches.length, truncated });
                                return;
                            }
                            default:
                                throw new Error(`unknown editor action: ${action}`);
                        }
                    }
                    case 'clipboard_read': {
                        controlRespond(id, true, { text: await tauriClipboard.readText() });
                        return;
                    }
                    case 'clipboard_write': {
                        const text = String(args.text ?? '');
                        await tauriClipboard.writeText(text);
                        controlRespond(id, true, { written: text.length });
                        return;
                    }
                    case 'message': {
                        const message = args.message;
                        if (!message || typeof message !== 'object') throw new Error('missing message object');
                        const waitFor = args.waitFor ? String(args.waitFor) : '';
                        let waiter: Promise<unknown> | null = null;
                        if (waitFor) {
                            waiter = new Promise((resolve, reject) => {
                                const timeout = window.setTimeout(() => {
                                    window.removeEventListener('message', onMessage);
                                    reject(new Error(`timed out waiting for ${waitFor}`));
                                }, Number(args.timeoutMs ?? 10000));
                                const onMessage = (event: MessageEvent) => {
                                    if (event.source !== null) return;
                                    const data = event.data as { type?: string } | null;
                                    if (data?.type === waitFor) {
                                        window.clearTimeout(timeout);
                                        window.removeEventListener('message', onMessage);
                                        resolve(data);
                                    }
                                };
                                window.addEventListener('message', onMessage);
                            });
                        }
                        try {
                            activeBridge.handleMessage(message as never);
                        } catch (error) {
                            waiter?.catch(() => {});
                            throw error;
                        }
                        controlRespond(id, true, waiter ? await waiter : { sent: true });
                        return;
                    }
                    case 'eval': {
                        const expression = String(args.expression ?? '');
                        if (!expression.trim()) throw new Error('missing expression');
                        const evaluate = new Function(`return (async () => (${expression}))();`);
                        let value: unknown = await evaluate();
                        if (value === undefined) value = null;
                        try {
                            value = JSON.parse(JSON.stringify(value));
                        } catch {
                            value = String(value);
                        }
                        controlRespond(id, true, { value });
                        return;
                    }
                    default:
                        throw new Error(`unknown command: ${command}`);
                }
            } catch (error) {
                controlRespond(id, false, undefined, error instanceof Error ? error.message : String(error));
            }
        }

        let controlQueue = Promise.resolve();
        await listenHere<ControlRequestPayload>('control-request', evt => {
            controlQueue = controlQueue
                .then(() => handleControlRequest(evt.payload))
                .catch(err => console.error('control request failed:', err));
        });
        void tauriInvoke('control_ready')
            .catch(err => console.error('control ready failed:', err));

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
            } else if (data.type === 'pdfInfo') {
                appInstance.appendOutput('info', String(data.message || ''));
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
            // F5 — run/refresh instead of reloading the webview.
            if (e.key === 'F5' && !e.ctrlKey && !e.altKey && !e.shiftKey && !e.metaKey) {
                e.preventDefault();
                void runRefresh();
                return;
            }
            if (!e.ctrlKey || e.metaKey) return;
            if ((e.key === 'w' || e.key === 'W') && e.altKey && !e.shiftKey) {
                e.preventDefault();
                void openNewWindow();
                return;
            }
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
                openSettings();
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
                if (id) void closeTabAndSettle(activeGroup, id);
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
        let allowWindowClose = false;

        /** Save prompts for everything unsaved in this window. False if the user cancelled. */
        async function confirmWindowClose(): Promise<boolean> {
            if (!await confirmLeaveUiDoc()) return false;
            // One dirty tab at a time, like VS Code on window-close. Reuses tryCloseTab so
            // the prompt copy and save-as fallback match a manual tab close.
            const dirty: { group: EditorGroup; id: string }[] = [];
            for (const g of groups.values()) {
                for (const t of g.tabs.all) {
                    // An unapplied Properties or Info form is as much unsaved work as a dirty buffer.
                    if (t.dirty || metadataDirty.has(metadataDocKeyFor(g, t.id))) dirty.push({ group: g, id: t.id });
                }
            }
            for (const { group, id } of dirty) {
                if (!await tryCloseTab(group, id)) return false;
            }
            return true;
        }

        async function closeWindow(): Promise<void> {
            connectionMonitor.stop();
            // Rust owns sidecar shutdown (kill-on-exit hook). This dispose only
            // tears down TS event listeners.
            if (serverManager) {
                try { await serverManager.dispose(); }
                catch (e) { appInstance.appendOutput('debug', `serverManager.dispose() rejected: ${e}`); }
            }
            try { if (isPrimaryWindow) await workspace.flush(); }
            catch (e) { appInstance.appendOutput('debug', `workspace.flush() rejected: ${e}`); }
            appInstance.appendOutput('debug', 'Closing window');
            allowWindowClose = true;
            await getCurrentWindow().close();
        }

        async function tryExit(): Promise<void> {
            if (isExiting) return;        // re-entry guard (multiple X clicks)
            isExiting = true;

            // Snapshot before the dirty-tab walk closes anything; `isExiting` keeps those
            // closes from overwriting it, so quitting with files open records them.
            if (isPrimaryWindow) captureSession();

            try {
                if (!await confirmWindowClose()) {
                    isExiting = false;
                    return;
                }
            } catch (e) {
                appInstance.appendOutput('debug', `Close prompts failed, closing anyway: ${e}`);
            }

            await closeWindow();
        }

        quitApplication = tryExit;

        // Intercept the window close button so unsaved tabs get their save prompt
        // before Tauri tears down the webview.
        await getCurrentWindow().onCloseRequested(async (event) => {
            if (allowWindowClose) return;
            event.preventDefault();
            void tryExit();
        });
    }
    await enableTabDragging();
}

// Nothing has mounted yet when bootstrap rejects, so an unhandled rejection here
// leaves a blank window with a working native menu and no clue what failed.
bootstrap().catch((err) => {
    const detail = err instanceof Error ? (err.stack ?? err.message) : String(err);
    const panel = document.createElement('pre');
    panel.setAttribute('style', [
        'position:fixed', 'inset:0', 'margin:0', 'padding:24px',
        'background:#1e1e1e', 'color:#f48771',
        'font-family:ui-monospace,SFMono-Regular,Menlo,monospace', 'line-height:1.5',
        'white-space:pre-wrap', 'overflow:auto', 'z-index:2147483647',
    ].join(';'));
    panel.textContent = `CalcpadCE failed to start.\n\n${detail}`;
    document.body.appendChild(panel);
});
