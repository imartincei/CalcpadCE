import * as vscode from 'vscode';
import * as path from 'path';
import * as os from 'os';
import { pdfResponseError, isBrowserNotFound, installPdfBrowser, CalcpadApiClient, combineSignals, resolveEffectivePdfSettings, pdfSettingsFromDocument, parseConvertErrorHeader, findMetadataCommentBlock, serializeMetadataComment, computeMetadataBlock, buildDefinitionResolver, extractBodyHtml, UiOverrideStore, writeUiOverrides, extractUiControls, variantRender, inlineImageSources, createReferenceResolver, isCompiledPath, documentHasUiDirectives, COMPILED_EXTENSION, MAX_COMPILED_IMAGE_TOTAL_BYTES, DEFAULT_PREVIEW_SIZE_MB, DEFAULT_CONSOLE_MESSAGES_PER_DOCUMENT, MAX_HTML_MIRROR_CHARS, MAX_INLINE_IMAGE_TOTAL_BYTES, previewSizeLimitChars, previewLimitNoticeHtml, formatSize, truncateForOutput, consoleRelayGuardScript } from 'calcpad-frontend';
import type { PdfSettings as FrontendPdfSettings, ExportVariant, UiControl, UiOverrides } from 'calcpad-frontend';
import { CalcpadServerLinter } from './calcpadServerLinter';
import { CalcpadSemanticTokensProvider, semanticTokensLegend } from './calcpadSemanticTokensProvider';
import { CalcpadVueUIProvider } from './calcpadVueUIProvider';
import { CalcpadSettingsManager } from './calcpadSettings';
import { OperatorReplacer } from './operatorReplacer';
import { QuickTyper } from './quickTyper';
import { CalcpadCompletionProvider } from './calcpadCompletionProvider';
import { CalcpadIncludeCompletionProvider } from './calcpadIncludeCompletionProvider';
import { CalcpadInsertManager } from './calcpadInsertManager';
import { CalcpadDefinitionsService } from './calcpadDefinitionsService';
import { AutoIndenter } from './autoIndenter';
import { ImageInserter } from './imageInserter';
import { CalcpadDefinitionProvider } from './calcpadDefinitionProvider';
import { CalcpadIncludeLinkProvider } from './calcpadIncludeLinkProvider';
import { CalcpadReferenceProvider } from './calcpadReferenceProvider';
import { CalcpadRenameProvider } from './calcpadRenameProvider';
import { CalcpadHoverProvider } from './calcpadHoverProvider';
import { CalcpadCompiledEditorProvider } from './calcpadCompiledEditorProvider';
import { CommentFormatter } from './commentFormatter';
import { CalcpadServerManager } from './calcpadServerManager';
import { DotnetRuntimeManager } from './dotnetRuntimeManager';
import { VSCodeLogger, VSCodeFileSystem } from './adapters';
import { expandEnvVars } from './calcpadLocationResolver';
import { installJuliaMonoCommand, maybePromptInstall } from './installFont';
import { renderIntoShell, setShellLoading, getFrameAgentScript, frameStateFor, handleFrameStateMessage, lastRenderedHtml } from './previewFrame';

// The wrapped ("regular") and unwrapped previews are independent panels that can
// coexist: the unwrapped one is stacked directly below the regular one so the
// error/line-link "two-step" navigation (regular → unwrapped → source) reads top-to-bottom.
let wrappedPanel: vscode.WebviewPanel | undefined = undefined;
let unwrappedPanel: vscode.WebviewPanel | undefined = undefined;
let previewUpdateTimeout: NodeJS.Timeout | unknown = undefined;
let previewSourceEditor: vscode.TextEditor | undefined = undefined;

// The #UI input form is its own panel, with the report preview opening to its
// right so entered values and their effect are visible side by side. Its
// presence is what "UI mode is on" means. Values stay in memory until the user
// runs "Save UI Values", which writes them into the document.
let uiPanel: vscode.WebviewPanel | undefined = undefined;
// The print report that accompanies the input form, showing what the entered
// values produce. Opened to the form's right and closable on its own.
let uiReportPanel: vscode.WebviewPanel | undefined = undefined;
const uiOverrides = new UiOverrideStore();
const uiOverridesDirty = new Set<string>();
// Controls each document's last input-form render produced, keyed like the store above.
const uiControls = new Map<string, UiControl[]>();
// Document the input form is currently showing, so its values can be prompted
// about and dropped when the form closes.
let uiPanelDocKey: string | undefined = undefined;


// The compiled-worksheet editor, which holds the decoded source of every open `.cpdz`:
// they never become text documents, so it is the only way to the content.
let compiledEditor: CalcpadCompiledEditorProvider | undefined = undefined;
// The compiled worksheet last worked in, for the same reason previewSourceEditor exists:
// a report or side panel holding focus leaves no active editor of any kind.
let compiledSourceUri: vscode.Uri | undefined = undefined;

type PreviewKind = 'regular' | 'unwrapped' | 'ui' | 'uiReport';

function previewPanelFor(kind: PreviewKind): vscode.WebviewPanel | undefined {
    switch (kind) {
        case 'regular': return wrappedPanel;
        case 'unwrapped': return unwrappedPanel;
        case 'ui': return uiPanel;
        default: return uiReportPanel;
    }
}

/** The compiled worksheet whose editor is the active tab, if that is what it is. */
function activeCompiledUri(): vscode.Uri | undefined {
    const input = vscode.window.tabGroups.activeTabGroup.activeTab?.input;
    return input instanceof vscode.TabInputCustom
        && input.viewType === CalcpadCompiledEditorProvider.viewType ? input.uri : undefined;
}

/**
 * Tells the side panel when the user is filling a worksheet in rather than editing it:
 * the input form is open, or the active editor is a compiled worksheet — which has no
 * text document behind it at all. The panel's source-editing tabs grey out for it.
 */
function syncInputMode(): void {
    const compiled = activeCompiledUri();
    if (compiled) compiledSourceUri = compiled;
    // The report beside the form is the other half of what a compiled worksheet shows, so
    // it is input mode too. A webview tab reports its view type behind a
    // `mainThreadWebview-` prefix, hence the suffix match.
    const input = vscode.window.tabGroups.activeTabGroup.activeTab?.input;
    const compiledReport = input instanceof vscode.TabInputWebview
        && input.viewType.endsWith(CalcpadCompiledEditorProvider.reportViewType);
    vueUiProvider?.setInputMode(!!uiPanel || !!compiled || compiledReport);
}

/** The content and identity of a document the preview and export paths work from. */
interface CalcpadSource {
    readonly text: string;
    readonly uri: vscode.Uri;
}

/**
 * The document the preview and export commands act on. A compiled worksheet has no text
 * document, so it can only be reached through its editor; the remembered editor and
 * worksheet cover the case where a preview, the report or the side panel holds focus and
 * there is no active editor of any kind.
 */
function activeCalcpadSource(): CalcpadSource | undefined {
    const editor = vscode.window.activeTextEditor;
    if (editor) return { text: editor.document.getText(), uri: editor.document.uri };

    const compiled = activeCompiledUri() ?? compiledSourceUri;
    const compiledText = compiled && compiledEditor?.sourceFor(compiled);
    if (compiled && compiledText !== undefined) return { text: compiledText, uri: compiled };

    return previewSourceEditor
        ? { text: previewSourceEditor.document.getText(), uri: previewSourceEditor.document.uri }
        : undefined;
}

/**
 * The values a render should apply, with the document's saved uiOverrides comment
 * re-read first: an edit to that comment is what the document now says its values are,
 * so it replaces the entered ones rather than being shadowed by them.
 */
function uiOverridesFor(docKey: string, content: string): UiOverrides | undefined {
    if (uiOverrides.syncFromSource(docKey, content)) uiOverridesDirty.delete(docKey);
    return uiOverrides.toRecord(docKey);
}

/**
 * Writes a document's entered #UI values into it as a uiOverrides metadata
 * comment. Returns how many were written, or null when none were entered.
 */
async function saveUiValuesFor(document: vscode.TextDocument): Promise<number | null> {
    const docKey = document.uri.toString();
    const overrides = uiOverrides.toRecord(docKey);
    if (!overrides) return null;

    const original = document.getText();
    const updated = writeUiOverrides(original, overrides);
    if (updated !== original) {
        const edit = new vscode.WorkspaceEdit();
        edit.replace(
            document.uri,
            new vscode.Range(document.positionAt(0), document.positionAt(original.length)),
            updated);
        await vscode.workspace.applyEdit(edit);
        // The values are only "saved" once they reach the file — a form filled in
        // and left dirty in the editor is exactly what the user asked to avoid.
        if (!document.isUntitled) await document.save();
    }
    uiOverridesDirty.delete(docKey);
    return Object.keys(overrides).length;
}

/**
 * Closing the input form discards the values entered into it — they only live in
 * memory until written into the document — so offer to save the unwritten ones
 * first, then drop them so the next session starts from the document again.
 *
 * `allowCancel` keeps the values when the modal is dismissed (Cancel / Escape) and
 * returns false, for callers that can still back out of what they were doing. Where
 * the form is already gone there is nothing to back out of, so dismissing it is
 * taken as "Don't Save".
 */
async function discardUiValues(docKey: string | undefined, allowCancel = false): Promise<boolean> {
    if (!docKey) return true;
    if (uiOverridesDirty.has(docKey)) {
        const choice = await vscode.window.showWarningMessage(
            'Save the values entered in the #UI input form?',
            {
                modal: true,
                detail: 'Input mode is closing. Unsaved values are discarded.',
            },
            'Save', "Don't Save");
        if (allowCancel && choice === undefined) return false;
        const document = vscode.workspace.textDocuments.find(d => d.uri.toString() === docKey);
        if (choice === 'Save' && document) {
            const count = await saveUiValuesFor(document);
            if (count !== null)
                vscode.window.showInformationMessage(`Saved ${count} #UI value(s) to the document.`);
        }
    }
    uiOverrides.clear(docKey);
    uiOverridesDirty.delete(docKey);
    return true;
}

// Documents whose first activation has already been judged for input mode.
const autoUiConsidered = new Set<string>();

/**
 * A document declaring `#UI` controls is one to fill in, so the first time one is looked at
 * its input form opens. Judged once per document per session: a tab switch back, or a `#UI`
 * line added while editing, must not re-open a form the user closed.
 *
 * The form is opened without taking the focus, so the caret stays where the user left it.
 */
async function maybeAutoEnterInputMode(document: vscode.TextDocument): Promise<void> {
    // Deliberately stricter than processDocument, which also lints plaintext: a .txt file
    // with a line starting '#ui' is not a worksheet to fill in.
    if (document.languageId !== 'calcpad') return;
    // Keeps git diffs, output views and other virtual documents out of it.
    if (document.uri.scheme !== 'file') return;

    const docKey = document.uri.toString();
    if (autoUiConsidered.has(docKey)) return;
    // Marked before the rest, so a document judged while the form was open or the setting off
    // is never judged again — turning the setting on affects what is opened after it.
    autoUiConsidered.add(docKey);

    // With the form already open, the active-editor handler retargets it at this document;
    // anything done here would fight it and prompt twice.
    if (uiPanel) return;
    if (document.isUntitled || document.isDirty) return;
    if (!CalcpadSettingsManager.getInstance(extensionContext).getExtraBool('autoInputMode', true)) return;
    if (!documentHasUiDirectives(document.getText())) return;

    outputChannel.appendLine(`[UI] Input mode opened for ${document.uri.fsPath} (#UI document)`);
    await showPreview('ui', undefined, true);
}

/** Re-renders every open preview panel from the given document. */
async function refreshPreviewPanels(document: vscode.TextDocument): Promise<void> {
    const text = document.getText();
    if (wrappedPanel) await updatePreviewContent(wrappedPanel, text, document.uri, false);
    if (unwrappedPanel) await updatePreviewContent(unwrappedPanel, text, document.uri, true);
    if (uiPanel) await updatePreviewContent(uiPanel, text, document.uri, false, undefined, true);
    if (uiReportPanel) await updatePreviewContent(uiReportPanel, text, document.uri, false, undefined, false, true);
}
let linter: CalcpadServerLinter;
let definitionsService: CalcpadDefinitionsService;
let serverManager: CalcpadServerManager | undefined;
let outputChannel: vscode.OutputChannel;
let calcpadOutputHtmlChannel: vscode.OutputChannel;
let calcpadWebviewConsoleChannel: vscode.OutputChannel;
let extensionContext: vscode.ExtensionContext;
let vueUiProvider: CalcpadVueUIProvider | undefined;
// Set in activate(); the module-level export commands need it outside that scope.
let sharedApiClient: CalcpadApiClient | undefined;

/**
 * Auth headers for the handful of requests that bypass the shared client and
 * call `fetch` directly. The local server answers 401 without them.
 */
function apiAuthHeaders(): Record<string, string> {
    return sharedApiClient?.authHeaders() ?? {};
}

const PREVIEW_BUSY_DELAY_MS = 400;

/**
 * Detect whether the cursor sits inside a metadata comment (`'<!--{...}-->`)
 * and push it to the Vue panel's Metadata tab. Also drives the
 * `calcpad.inMetadataComment` context key that gates the editor context-menu
 * command. Single-line only, matching the shared detector.
 */
function updateMetadataContext(editor: vscode.TextEditor | undefined): void {
    let block = null;
    if (editor && (editor.document.languageId === 'calcpad' || editor.document.languageId === 'plaintext')) {
        const lines = editor.document.getText().split(/\r?\n/);
        const line = editor.selection.active.line;
        const resolve = buildDefinitionResolver(
            definitionsService.getCachedDefinitions(editor.document.uri.toString())
            ?? { functions: [], macros: [], variables: [], customUnits: [] });
        block = computeMetadataBlock(lines, line, resolve);
    }
    vscode.commands.executeCommand('setContext', 'calcpad.inMetadataComment', block !== null && !block.isNew);
    vueUiProvider?.updateMetadataContext(block);
    // Sent with the block so the panel's saved-values list follows the document the
    // cursor is actually in, rather than keeping the last one it asked about.
    vueUiProvider?.updateUiControls(editor ? uiControls.get(editor.document.uri.toString()) ?? null : null);
}

/**
 * Retires the controls cached for a document, so an edit that renames or removes a `#UI`
 * line is not judged against the list a render found before it. The panel asks for a fresh
 * one while it is showing saved values; anywhere else the next form render fills it.
 */
function invalidateUiControls(document: vscode.TextDocument): void {
    if (!uiControls.delete(document.uri.toString())) return;
    if (vscode.window.activeTextEditor?.document === document) vueUiProvider?.updateUiControls(null);
}

function escapeHtml(text: string): string {
    return text
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;');
}

/** The colour behind a preview's frames, which is also what it fades to while loading. */
function previewBackground(): string {
    return getEffectivePreviewTheme() === 'light'
        ? '#ffffff'
        : CalcpadSettingsManager.getInstance().getExtra('darkBackground', '#1e1e1e');
}

/**
 * Raises the preview's "Calculating…" overlay only if the render outlasts
 * PREVIEW_BUSY_DELAY_MS, so fast renders never flash it (mirrors Calcpad.Wpf's
 * delayed spinner). Returns a function that must be called when the render ends.
 *
 * An overlay in the shell rather than a page of its own: a page would have to be the
 * webview's document, and replacing that would throw away the buffer holding the render
 * the user is still looking at.
 *
 * The returned function only stops the overlay being raised. Lowering it belongs to the
 * render that replaces it — a request that ends by being superseded has not finished
 * calculating, it has handed that off.
 */
function showPreviewLoading(panel: vscode.WebviewPanel): () => void {
    const background = previewBackground();
    const timer = setTimeout(() => {
        setShellLoading(panel, background, true);
    }, PREVIEW_BUSY_DELAY_MS);
    return () => clearTimeout(timer);
}

/**
 * The PDF options for an export: the stored host defaults, overridden per key by
 * the document's own `pdf` metadata comment. `documentContent` is optional so
 * callers with no document in hand still get the host defaults.
 */
function getPdfSettings(documentContent?: string): FrontendPdfSettings {
    const settingsManager = CalcpadSettingsManager.getInstance();
    const stored = settingsManager.getExtraObject('pdfSettings', {} as Partial<FrontendPdfSettings>);
    const fromDocument = documentContent
        ? pdfSettingsFromDocument(documentContent.split('\n'))
        : {};
    const activeEditor = vscode.window.activeTextEditor;

    const fileName = activeEditor
        ? path.basename(activeEditor.document.fileName, path.extname(activeEditor.document.fileName))
        : 'CalcpadCE Document';

    return resolveEffectivePdfSettings(stored, fromDocument, fileName);
}

function getEffectivePreviewTheme(): 'light' | 'dark' {
    const previewTheme = CalcpadSettingsManager.getInstance().getExtra('previewTheme', 'system');

    if (previewTheme === 'light') {
        return 'light';
    } else if (previewTheme === 'dark') {
        return 'dark';
    } else {
        // System - follow VS Code theme
        const colorTheme = vscode.window.activeColorTheme;
        return colorTheme.kind === vscode.ColorThemeKind.Dark ||
               colorTheme.kind === vscode.ColorThemeKind.HighContrast ? 'dark' : 'light';
    }
}

const IMAGE_MIME_MAP: Record<string, string> = {
    'png': 'image/png',
    'jpg': 'image/jpeg',
    'jpeg': 'image/jpeg',
    'gif': 'image/gif',
    'webp': 'image/webp',
    'svg': 'image/svg+xml'
};

/**
 * Scan rendered HTML for <img src="..."> tags with local file paths, read the files from
 * disk, and return a cache mapping original src values to base64 data URIs.
 * `Calcpad.Core.ImageReferences` has already expanded any `{project}`/`{library}`/`{user}`
 * token and env var (`%VAR%`/`$VAR`) into an absolute path, so only a relative src is left
 * to join against `documentDir`.
 *
 * `maxTotalBytes` bounds what a single render will read: a worksheet referencing a folder
 * of photographs would otherwise pull all of them into memory as base64, which costs a
 * third again on top of the files themselves. Images past the budget keep their original
 * src, which the sandboxed frame cannot load, so they show as broken rather than silently
 * looking fine. The export/PDF path passes no budget — a written file keeps every image.
 */
async function buildImageCache(
    html: string,
    documentDir: string,
    maxTotalBytes?: number,
): Promise<Record<string, string>> {
    const cache: Record<string, string> = {};
    const resolve = (src: string) => path.resolve(documentDir, src);
    const imgSrcRegex = /<img\s[^>]*?src\s*=\s*["']([^"']+)["'][^>]*>/gi;
    let match;
    let inlined = 0;
    let skipped = 0;

    while ((match = imgSrcRegex.exec(html)) !== null) {
        const src = match[1];

        // Skip data URIs and remote URLs
        if (src.startsWith('data:') || src.startsWith('http://') || src.startsWith('https://')) {
            continue;
        }

        // Skip if already cached (same src used multiple times)
        if (cache[src]) {
            continue;
        }

        if (maxTotalBytes !== undefined && inlined >= maxTotalBytes) {
            skipped++;
            continue;
        }

        try {
            const absolutePath = await resolve(src);
            const ext = path.extname(absolutePath).toLowerCase().replace('.', '');
            const mimeType = IMAGE_MIME_MAP[ext];

            if (!mimeType) {
                outputChannel.appendLine(`[IMAGE CACHE] Skipping unsupported image type: ${src}`);
                continue;
            }

            const fileUri = vscode.Uri.file(absolutePath);
            const imageData = await vscode.workspace.fs.readFile(fileUri);
            const b64 = Buffer.from(imageData).toString('base64');
            cache[src] = `data:${mimeType};base64,${b64}`;
            inlined += imageData.length;

            outputChannel.appendLine(`[IMAGE CACHE] Cached: ${src} (${imageData.length} bytes)`);
        } catch (error) {
            outputChannel.appendLine(`[IMAGE CACHE] Could not read image: ${src} (${error instanceof Error ? error.message : 'Unknown error'})`);
        }
    }

    if (skipped) {
        outputChannel.appendLine(
            `[IMAGE CACHE] Budget of ${formatSize(maxTotalBytes ?? 0)} reached — ${skipped} image(s) left unembedded`);
    }

    return cache;
}

/**
 * Directly replace local image src attributes in the HTML string with
 * cached base64 data URIs. Unlike getImageCacheScript (which injects a
 * <script> for webview runtime replacement), this performs a static
 * string replacement suitable for headless PDF rendering.
 */
function applyImageCache(html: string, imageCache: Record<string, string>): string {
    if (Object.keys(imageCache).length === 0) {
        return html;
    }

    return html.replace(/<img\s([^>]*?)src\s*=\s*["']([^"']+)["']([^>]*?)>/gi,
        (match, before, src, after) => {
            if (imageCache[src]) {
                return `<img ${before}src="${imageCache[src]}"${after}>`;
            }
            return match;
        });
}

/**
 * Generate a <script> that strips VS Code's auto-injected theme from the webview.
 * VS Code injects 400+ --vscode-* CSS variables on <html> and sets body classes
 * (vscode-dark/vscode-light) which override the server-generated theme CSS.
 * There is no API to disable this: https://github.com/microsoft/vscode/issues/209253
 * VS Code only re-injects on VS Code theme change, not continuously,
 * so stripping at DOMContentLoaded is stable.
 */
function getThemeOverrideScript(previewTheme: 'light' | 'dark'): string {
    const bodyClass = previewTheme === 'light' ? 'vscode-light' : 'vscode-dark';
    const themeKind = previewTheme === 'light' ? 'vscode-light' : 'vscode-dark';
    const darkBg = CalcpadSettingsManager.getInstance().getExtra('darkBackground', '#1e1e1e');
    const bg = previewTheme === 'light' ? '#ffffff' : darkBg;
    return `
        <script>
            (function() {
                // Remove VS Code's injected inline styles (--vscode-* variables) from <html>
                document.documentElement.removeAttribute('style');
                // Set explicit background to prevent the webview container's grey from showing through
                document.documentElement.style.backgroundColor = '${bg}';

                // Set body classes to match the selected preview theme
                document.body.classList.remove('vscode-light', 'vscode-dark', 'vscode-high-contrast');
                document.body.classList.add('${bodyClass}');
                document.body.setAttribute('data-vscode-theme-kind', '${themeKind}');
                document.body.style.backgroundColor = '${bg}';
            })();
        </script>
    `;
}

// Escape stray '<' that aren't part of complete HTML tags to prevent
// malformed user content (e.g. '<h' from Calcpad notes) from breaking the DOM.
// A complete tag is <...> where the content between < and > contains no nested <.
//
// This is a rendering repair, not a security control: <script>, <iframe> and event
// handler attributes are all complete tags and pass through untouched, by design —
// author HTML and JS are a Calcpad feature. What contains them is the sandboxed
// frame in previewFrame.ts.
function repairStrayAngleBrackets(html: string): string {
    const bodyOpen = html.indexOf('<body');
    const bodyClose = html.lastIndexOf('</body>');
    if (bodyOpen === -1 || bodyClose === -1) return html;

    const bodyStart = html.indexOf('>', bodyOpen) + 1;
    const body = html.substring(bodyStart, bodyClose);
    const sanitized = body.replace(/(<!--[\s\S]*?-->)|(<script[\s\S]*?<\/script>)|(<style[\s\S]*?<\/style>)|(<\/?[a-zA-Z][^<>]*>)|(<)/g,
        (_match, comment, script, style, tag) => comment ?? script ?? style ?? tag ?? '&lt;');

    return html.substring(0, bodyStart) + sanitized + html.substring(bodyClose);
}

function getErrorNavigationScript(maxConsoleMessages: number): string {
    return `
        <script>
            // Every relayed line goes through __calcpadRelayLine first: a worksheet script
            // logging a parsed data set, or logging in a loop, would otherwise push its whole
            // heap across the boundary and into an output channel that holds it. Clipping in
            // here is the point — the oversized string never crosses.
            ${consoleRelayGuardScript(maxConsoleMessages)}

            function relayConsole(level, text) {
                const line = window.__calcpadRelayLine(text);
                if (line !== null) window.__calcpadSend({ type: 'consoleMessage', level: level, message: line });
            }

            // Intercept console methods and relay to VS Code. The document runs a frame
            // deeper than the webview now, on an opaque origin with no acquireVsCodeApi
            // of its own, so everything leaves through the shell's relay
            // (window.__calcpadSend, published by the frame agent).
            (function() {
                const originalConsole = {
                    log: console.log,
                    warn: console.warn,
                    error: console.error,
                    info: console.info,
                    debug: console.debug
                };

                function sendConsoleMessage(level, args) {
                    const message = Array.from(args).map(arg => {
                        if (typeof arg === 'object') {
                            try {
                                return JSON.stringify(arg, null, 2);
                            } catch (e) {
                                return String(arg);
                            }
                        }
                        return String(arg);
                    }).join(' ');

                    relayConsole(level, message);
                }

                console.log = function() {
                    originalConsole.log.apply(console, arguments);
                    sendConsoleMessage('log', arguments);
                };

                console.warn = function() {
                    originalConsole.warn.apply(console, arguments);
                    sendConsoleMessage('warn', arguments);
                };

                console.error = function() {
                    originalConsole.error.apply(console, arguments);
                    sendConsoleMessage('error', arguments);
                };

                console.info = function() {
                    originalConsole.info.apply(console, arguments);
                    sendConsoleMessage('info', arguments);
                };

                console.debug = function() {
                    originalConsole.debug.apply(console, arguments);
                    sendConsoleMessage('debug', arguments);
                };
            })();

            // Catch uncaught errors and unhandled promise rejections
            window.onerror = function(message, source, lineno, colno, error) {
                const detail = error ? (error.message || String(error)) : message;
                relayConsole('error', '[Uncaught] ' + detail + ' (' + lineno + ':' + colno + ')');
            };
            window.onunhandledrejection = function(event) {
                const reason = event.reason;
                const detail = reason ? (reason.message || String(reason)) : String(reason);
                relayConsole('error', '[Unhandled Rejection] ' + detail);
            };

            // Test console interception
            console.log('CalcPad webview console interception initialized');

            // Handle error link clicks
            document.addEventListener('DOMContentLoaded', function() {
                // Find all error links with data-text attributes
                const errorLinks = document.querySelectorAll('a[data-text]');

                // The code view (unwrapped output, or the wrapped view's fallback when
                // parsing errors occur) renders .line-num anchors whose data-text is
                // already a source line. The true wrapped view has no .line-num and its
                // error links carry expanded *output* lines. Tag each click so the
                // extension only does the output->unwrapped two-step for real output lines.
                const isCodeView = !!document.querySelector('.line-num');

                errorLinks.forEach(link => {
                    link.addEventListener('click', function(e) {
                        e.preventDefault();
                        const lineNumber = this.getAttribute('data-text');
                        if (lineNumber) {
                            const lineType = (this.classList.contains('line-num') || isCodeView) ? 'source' : 'output';
                            window.__calcpadSend({
                                type: 'navigateToLine',
                                line: parseInt(lineNumber, 10),
                                lineType: lineType
                            });
                        }
                    });
                });
            });
        </script>
    `;
}

/**
 * Give the preview a clearly visible vertical scrollbar. VS Code webviews scroll
 * natively but the default scrollbar is nearly invisible, so we style it to match
 * the extension's Vue sidebar (calcpad-frontend/src/vue/styles/base.css). Reserving
 * the gutter with `overflow-y: scroll` keeps the layout from shifting.
 */
function getScrollbarStyleScript(): string {
    return `
        <style>
            html { overflow-y: scroll; }
            body { min-height: 100vh; }
            .code { overflow-y: auto; }
            ::-webkit-scrollbar { width: 12px; height: 12px; }
            ::-webkit-scrollbar-track { background: var(--vscode-scrollbar-shadow, transparent); }
            ::-webkit-scrollbar-thumb {
                background: var(--vscode-scrollbarSlider-background, rgba(121,121,121,0.4));
                border-radius: 6px;
            }
            ::-webkit-scrollbar-thumb:hover { background: var(--vscode-scrollbarSlider-hoverBackground, rgba(100,100,100,0.7)); }
            ::-webkit-scrollbar-thumb:active { background: var(--vscode-scrollbarSlider-activeBackground, rgba(85,85,85,0.9)); }
            ::-webkit-scrollbar-corner { background: transparent; }
            /* Pin the arrow to its own line and extend the anchor across the body's
               left margin so the whole gutter is a hover+click target. Each arrow is
               always in the DOM at opacity 0 so pointing at the margin reveals it
               directly — no need to hover the line text first. */
            .line { position: relative; }
            /* Brief flash when the preview is focused to the editor's cursor line. */
            .cpd-line-focus { background-color: var(--vscode-editor-findMatchHighlightBackground, rgba(120,170,255,0.28)) !important; transition: background-color 0.3s ease !important; }
            .lineLink {
                left: -3em !important;
                top: 0 !important;
                bottom: 0 !important;
                width: 3em !important;
                height: auto !important;
                font-size: 16pt !important;
                padding-right: 4pt !important;
                box-sizing: border-box !important;
                display: flex !important;
                align-items: center !important;
                justify-content: flex-end !important;
                opacity: 0 !important;
                transition: opacity 0.15s !important;
            }
            .lineLink:hover { opacity: 1 !important; }
        </style>
    `;
}

/**
 * Inject the line-link behaviour ported from the WPF preview (doc/template.html):
 *  - each wrapped-view output line (.line) gets a hover "←" link that navigates via
 *    postMessage('navigateToLine'). Its data-text is the output line; the extension
 *    resolves output→source (the two-step hop) when the document has macros/includes.
 *    The unwrapped view isn't decorated here: its .line-num anchors already carry the
 *    source line and are handled by getErrorNavigationScript's a[data-text] binding.
 *  - error-summary .roundBox chips scroll the preview to that output line.
 *  - a 'scrollToLine' target (set by the two-step navigation) is scrolled into
 *    view on load. Baking the target into the HTML avoids a postMessage race with
 *    the webview reload.
 * The arrows are created after DOMContentLoaded (so after getErrorNavigationScript
 * binds), hence they get their own click handler here.
 *
 * `includeLinks` turns just the arrows off; the chips, the scroll target and the
 * editor->preview sync stay. The input form drops them, and so does the report
 * while it accompanies that form — the form, not the source, is what you are
 * working in.
 */
function getLineLinkScript(scrollToLine?: number, includeLinks: boolean = true): string {
    const scrollTarget = typeof scrollToLine === 'number' ? String(scrollToLine) : 'null';
    const arrows = !includeLinks ? '' : `
                function hideAllLineLinks() {
                    document.querySelectorAll('.lineLink').forEach(function(l) { l.style.display = 'none'; });
                }
                document.querySelectorAll('.line').forEach(function(el) {
                    var id = el.id || '';
                    var n = id.indexOf('line-') === 0 ? id.slice(5) : '';
                    // Prefer data-source-line (set by Calcpad.Core when the line came from
                    // a macro/include expansion) so the arrow navigates straight to the
                    // source line and skips the wrapped->unwrapped two-step. Loop
                    // iterations past the first drop the id but keep data-source-line, so
                    // key off the source line here. Error links keep the 'output' path.
                    var src = el.getAttribute('data-source-line') || n;
                    if (!src) return;
                    var link = document.createElement('a');
                    link.className = 'lineLink';
                    link.href = '#0';
                    link.setAttribute('data-text', src);
                    link.title = 'Source line ' + src;
                    link.textContent = '\\u2190';
                    link.style.display = 'none';
                    link.addEventListener('click', function(e) {
                        e.preventDefault();
                        window.__calcpadSend({ type: 'navigateToLine', line: parseInt(src, 10), lineType: 'source' });
                    });
                    el.appendChild(link);
                    el.addEventListener('mouseenter', function() {
                        hideAllLineLinks();
                        link.style.display = 'inline-block';
                    });
                });
                window.addEventListener('scroll', hideAllLineLinks);
    `;
    return `
        <script>
            document.addEventListener('DOMContentLoaded', function() {
                ${arrows}

                // Anything that moves the page on purpose has to take it off the scroll
                // agent first, or the restore in progress pulls the user straight back.
                function goTo(target, block) {
                    if (!target) return;
                    if (window.__calcpadReleaseScroll) window.__calcpadReleaseScroll();
                    target.scrollIntoView({ block: block });
                }

                // Error-summary chips: scroll the preview to the referenced output line.
                // Prefer data-error's err-N id, since an error paragraph has no line-N id
                // of its own; fall back to data-line's line-N for a non-error output line.
                document.querySelectorAll('.roundBox').forEach(function(box) {
                    box.addEventListener('click', function() {
                        var errId = box.getAttribute('data-error');
                        var target = errId ? document.getElementById(errId) : null;
                        if (!target) {
                            var line = box.getAttribute('data-line');
                            target = line ? document.getElementById('line-' + line) : null;
                        }
                        goTo(target, 'start');
                    });
                });

                var scrollToLine = ${scrollTarget};
                if (scrollToLine !== null) goTo(document.getElementById('line-' + scrollToLine), 'center');

                // Editor -> preview sync. The extension posts
                // { type: 'scrollToSourceLine', line } (a source line) on cursor
                // move (when auto-sync is on) or via the 'Focus Preview to Line'
                // command. Match data-source-line first (wrapped view), then the
                // code view's line-num anchors, falling back to the nearest
                // preceding source line so blank/continuation lines still resolve.
                var focusTimer = null;
                // "exact" comes from a TOC click: skip the nearest-preceding-line fallback so a
                // heading hidden by #pre/#post (no element for it at all) does nothing instead of
                // landing on an unrelated line.
                function focusPreviewLine(line, exact) {
                    if (typeof line !== 'number' || isNaN(line)) return;
                    var target = document.querySelector('[data-source-line="' + line + '"]');
                    if (!target) {
                        var anchor = document.querySelector('a.line-num[data-text="' + line + '"]');
                        if (anchor) target = anchor.closest('.line-text') || anchor;
                    }
                    if (!target && !exact) {
                        var best = null, bestSrc = -1;
                        document.querySelectorAll('[data-source-line]').forEach(function(el) {
                            var s = parseInt(el.getAttribute('data-source-line'), 10);
                            if (!isNaN(s) && s <= line && s > bestSrc) { bestSrc = s; best = el; }
                        });
                        target = best;
                    }
                    if (!target) return;
                    goTo(target, 'center');
                    document.querySelectorAll('.cpd-line-focus').forEach(function(el) { el.classList.remove('cpd-line-focus'); });
                    target.classList.add('cpd-line-focus');
                    if (focusTimer) clearTimeout(focusTimer);
                    focusTimer = setTimeout(function() { target.classList.remove('cpd-line-focus'); }, 1200);
                }
                window.addEventListener('message', function(e) {
                    var d = e.data;
                    if (d && d.type === 'scrollToSourceLine') focusPreviewLine(d.line, d.exact);
                });
            });
        </script>
    `;
}

/**
 * `standalone` marks a panel that *is* the document rather than a preview of an open
 * text editor — the compiled-worksheet editor. Such a panel owns its own tab title,
 * must not claim the shared input-form slot that the toggle commands manage, and has
 * no `activeTextEditor` whose folder local images could be resolved against (a
 * compiled worksheet carries its images inline anyway).
 */
async function updatePreviewContent(panel: vscode.WebviewPanel, content: string, sourceFileUri: vscode.Uri, unwrapped: boolean = false, scrollToLine?: number, enableUi: boolean = false, forPrint: boolean = false, standalone: boolean = false) {
    const docKey = sourceFileUri.toString();
    // The plain preview shows the document as written, unless the setting asks it to
    // render the entered values too — a debugging view of the filled-in form.
    const applyOverrides = enableUi || forPrint
        || (!unwrapped && CalcpadSettingsManager.getInstance(extensionContext)
            .getExtraBool('previewUiOverrides', false));
    if (enableUi && !standalone) uiPanelDocKey = docKey;

    const mode = enableUi ? 'ui' : forPrint ? 'report' : unwrapped ? 'unwrapped' : 'wrapped';
    outputChannel.appendLine(`Starting updatePreviewContent (${mode})...`);
    outputChannel.appendLine(`Content length: ${content.length} characters`);

    // Update panel title with current file name
    const activeEditor = standalone ? undefined : (vscode.window.activeTextEditor ?? previewSourceEditor);
    if (activeEditor) {
        const fileName = activeEditor.document.fileName.split('/').pop() || 'CalcpadCE';
        panel.title = enableUi ? `CalcpadCE Input - ${fileName}`
            : forPrint ? `CalcpadCE Report - ${fileName}`
            : unwrapped ? `CalcpadCE Preview Unwrapped - ${fileName}`
            : `CalcpadCE Preview - ${fileName}`;
    }

    // Check if content is empty
    if (!content || content.trim().length === 0) {
        outputChannel.appendLine('Content is empty - showing empty state');
        const emptyTheme = getEffectivePreviewTheme();
        const c = emptyTheme === 'light'
            ? { fg: '#6e6e6e', bg: '#ffffff', link: '#0066cc' }
            : { fg: '#858585', bg: '#1e1e1e', link: '#4FC1FF' };
        panel.webview.html = `
            <!DOCTYPE html>
            <html>
            <head>
                <meta charset="UTF-8">
                <meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline';">
                <title>CalcpadCE Preview${unwrapped ? ' Unwrapped' : ''}</title>
                <style>
                    body { color: ${c.fg}; background: ${c.bg}; padding: 20px; font-family: var(--vscode-font-family); }
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
            </html>
        `;
        return;
    }

    const endPreviewLoading = showPreviewLoading(panel);
    try {
        outputChannel.appendLine('Getting settings...');
        const settingsManager = CalcpadSettingsManager.getInstance(extensionContext);

        const settings = await settingsManager.getApiSettings();
        const apiBaseUrl = settingsManager.getServerUrl();

        if (!apiBaseUrl) {
            outputChannel.appendLine('ERROR: Server URL not configured');
            throw new Error('Server URL not configured');
        }
        outputChannel.appendLine(`Server URL: ${apiBaseUrl}`);
        outputChannel.appendLine(`Settings retrieved: ${JSON.stringify(settings)}`);

        const endpoint = unwrapped ? '/api/calcpad/convert?unwrap=true' : '/api/calcpad/convert';
        outputChannel.appendLine(`Making API call to ${endpoint}...`);

        const theme = getEffectivePreviewTheme();
        const requestBody = JSON.stringify({
            content: content,
            settings: settings,
            theme: theme,
            forceUnwrappedCode: unwrapped,
            sourceFilePath: sourceFileUri.fsPath,
            enableUi: enableUi,
            forPrint: forPrint,
            uiOverrides: applyOverrides ? uiOverridesFor(docKey, content) : undefined,
            // The report is a print layout, but on screen beside the editor, so it
            // keeps the line links that forPrint would otherwise suppress.
            includeLineAnchors: forPrint ? true : undefined,
            // The input form has no source editor for "on line [N]" to point at, and
            // neither does the report while it's shown behind that form.
            hideErrorLines: forPrint && uiPanel !== undefined ? true : undefined
        });
        // Routed through the shared client (rather than a bare fetch) so a newer preview
        // request for the same document aborts this one via the `key` below — that's not
        // a real failure, so it resolves to null instead of throwing (a genuine fetch
        // error still throws).
        const runRequest = async (signal: AbortSignal): Promise<Response | null> => {
            try {
                return await fetch(`${apiBaseUrl}${endpoint}`, {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json', ...apiAuthHeaders() },
                    body: requestBody,
                    signal: combineSignals(AbortSignal.timeout(10000), signal),
                });
            } catch (error) {
                if (error instanceof DOMException && error.name === 'AbortError' && signal.aborted) return null;
                throw error;
            }
        };
        const response = sharedApiClient
            ? await sharedApiClient.runWithSupersession(runRequest, { key: `preview:${docKey}` })
            : await runRequest(new AbortController().signal);
        if (!response) {
            // Superseded by a newer preview request for this document — that
            // newer call owns updating the webview, so leave it as is.
            return;
        }
        if (!response.ok) {
            throw new Error(`Server returned ${response.status}`);
        }
        outputChannel.appendLine('API call successful');

        vueUiProvider?.updateConvertErrors(parseConvertErrorHeader(response));

        // Use the entire API response as the webview HTML
        const apiResponse = await response.text();

        // Before anything copies it. Showing a render costs several times its own size —
        // the image inlining, the injection passes, the message into the webview, and the
        // live documents built from it — so the one check that can hold the line is the one
        // in front of all of them. Everything below is bounded by having passed here.
        const sizeLimit = previewSizeLimitChars(
            CalcpadSettingsManager.getInstance(extensionContext)
                .getExtraNumber('maxPreviewSizeMB', DEFAULT_PREVIEW_SIZE_MB));
        if (apiResponse.length > sizeLimit) {
            outputChannel.appendLine(
                `Preview blocked: render is ${formatSize(apiResponse.length)}, limit is ${formatSize(sizeLimit)}`);
            const notice = previewLimitNoticeHtml({
                chars: apiResponse.length,
                limitChars: sizeLimit,
                theme: getEffectivePreviewTheme(),
            }).replace('</head>', getFrameAgentScript() + '</head>');
            renderIntoShell(panel, notice, { background: previewBackground() });
            return;
        }

        // Share the freshly-converted HTML with the Vue side panel so the
        // Export tab can extract plot bytes without another server round-trip.
        vueUiProvider?.setCachedHtml(apiResponse);

        // The controls this document really has, which is what tells the Properties tab
        // whether a saved #UI value still applies to anything.
        if (enableUi) {
            uiControls.set(docKey, extractUiControls(apiResponse));
            vueUiProvider?.updateUiControls(uiControls.get(docKey) ?? null);
        }

        // Log to dedicated HTML output channel (without stealing focus). Clipped: the channel
        // is for reading a render, and holding a whole one doubles what this panel costs.
        calcpadOutputHtmlChannel.clear();
        calcpadOutputHtmlChannel.appendLine(truncateForOutput(extractBodyHtml(apiResponse), MAX_HTML_MIRROR_CHARS));

        outputChannel.appendLine(`HTML Length: ${apiResponse.length} characters`);

        // Build image cache: read local image files and convert to base64 data URIs.
        // An untitled document has no folder to join a relative src against, but Core
        // resolved every token to an absolute path, so those still render.
        const documentDir = activeEditor && !activeEditor.document.isUntitled
            ? path.dirname(activeEditor.document.uri.fsPath) : '';
        const imageCache = await buildImageCache(apiResponse, documentDir, MAX_INLINE_IMAGE_TOTAL_BYTES);

        // Inject JavaScript for error link navigation and console interception. The relay cap
        // goes to both blocks that install the guard, since only the first to run sets it.
        const maxConsoleMessages = settingsManager.getExtraNumber(
            'maxPreviewConsoleMessages', DEFAULT_CONSOLE_MESSAGES_PER_DOCUMENT);
        const errorNavigationScript = getErrorNavigationScript(maxConsoleMessages);

        // Visible, styled vertical scrollbar for the preview
        const scrollbarStyleScript = getScrollbarStyleScript();

        // Hover line links + roundBox scroll + optional scroll-to-line target. The arrows
        // need a source editor to navigate to: a compiled worksheet has none, the input
        // form itself hides the editor behind it, and the report gives them up too while
        // that form is in front of it. The report gets them back when it stands alone
        // beside the editor (the 'ui' panel's disposal re-renders it, so closing input
        // mode restores them).
        const lineLinkScript = getLineLinkScript(
            scrollToLine, !standalone && !enableUi && !(forPrint && uiPanel !== undefined));

        // Override VS Code's injected theme to match the selected preview theme
        const themeOverrideScript = getThemeOverrideScript(theme);

        // Repair stray '<' that aren't part of valid tags. Not a security control —
        // see the function's own comment; containment is the sandboxed frame below.
        // The image cache is applied in the same pass rather than shipped as a script the
        // frame runs: a script would carry every data URI as source text *and* as a parsed
        // object, so the images would cost twice over inside the webview. This matches what
        // calcpad-web and the PDF path already do.
        const repairedResponse = applyImageCache(repairStrayAngleBrackets(apiResponse), imageCache);

        // Where this panel's frame was before the render that is replacing it. An
        // explicit line target is a navigation the user asked for, and outranks
        // returning them to where they were.
        const frameState = frameStateFor(panel, docKey);
        const agentScript = getFrameAgentScript({
            scroll: scrollToLine === undefined ? frameState.scroll : undefined,
            uiPosition: frameState.uiPosition,
            maxConsoleMessages,
        });
        // Consumed once, matching the #UI script's own semantics: a stale position must
        // not steal focus when a document is opened fresh rather than re-rendered.
        frameState.uiPosition = undefined;

        // The agent goes first so window.__calcpadSend exists before anything that
        // relays through it, user scripts in <body> included.
        let frameHtml = repairedResponse.replace('</head>',
            agentScript + errorNavigationScript + scrollbarStyleScript + '</head>');
        // Inject theme override + line links before closing body tag
        frameHtml = frameHtml.replace('</body>', themeOverrideScript + lineLinkScript + '</body>');

        // The rendered document is never the webview's own document: it is sandboxed
        // to an opaque origin inside the shell, which holds the only handle to VS Code.
        renderIntoShell(panel, frameHtml, { background: previewBackground() });

        outputChannel.appendLine('Preview document pushed to the shell (sandboxed frame)');

    } catch (error) {
        outputChannel.appendLine(`ERROR in updatePreviewContent: ${error instanceof Error ? error.message : 'Unknown error'}`);
        const settingsManager = CalcpadSettingsManager.getInstance(extensionContext);
        const errorApiBaseUrl = settingsManager.getServerUrl();
        const endpoint = unwrapped ? 'convert?unwrap=true' : 'convert';
        // Escaped because the message can carry server text. It goes into the frame like
        // any other document rather than replacing the shell, which would discard the
        // buffers along with the render still on screen.
        const errorHtml = `
            <!DOCTYPE html>
            <html>
            <head>
                <meta charset="UTF-8">
                <title>CalcpadCE Preview Error</title>
                ${getFrameAgentScript()}
            </head>
            <body>
                <div style="color: #d32f2f; background: #ffebee; padding: 15px; border-radius: 4px; margin: 20px;">
                    <h3>Preview Error${unwrapped ? ' (Unwrapped)' : ''}</h3>
                    <p>${escapeHtml(error instanceof Error ? error.message : 'Unknown error')}</p>
                    <p>Server URL: ${escapeHtml(`${errorApiBaseUrl}/api/calcpad/${endpoint}`)}</p>
                </div>
            </body>
            </html>
        `;

        renderIntoShell(panel, errorHtml, { background: previewBackground() });
    } finally {
        endPreviewLoading();
    }
}

/**
 * Render a document for export. `variant` decides what the file contains — see
 * `variantRender` in calcpad-frontend, which the desktop and web exports share, so all
 * three front ends agree on what "report" or "preview" means. Line anchors are always
 * off: an exported file is read, not navigated.
 */
async function renderForExport(
    documentContent: string,
    sourceFileUri: vscode.Uri,
    variant: ExportVariant,
): Promise<string> {
    const settingsManager = CalcpadSettingsManager.getInstance(extensionContext);
    const apiBaseUrl = settingsManager.getServerUrl();
    if (!apiBaseUrl) throw new Error('Server URL not configured');

    if (!documentContent || documentContent.trim().length === 0) {
        throw new Error('Document is empty. Please add some CalcpadCE content first.');
    }

    const settings = await settingsManager.getApiSettings();
    const render = variantRender(variant);
    const endpoint = render.unwrap ? '/api/calcpad/convert?unwrap=true' : '/api/calcpad/convert';

    // Routed through the shared client (rather than a bare fetch) for a consistent request
    // path. Unkeyed: an export is an explicit one-off action, never superseded by a later one.
    const runRequest = (signal: AbortSignal) => fetch(`${apiBaseUrl}${endpoint}`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', ...apiAuthHeaders() },
        body: JSON.stringify({
            content: documentContent,
            settings: settings,
            sourceFilePath: sourceFileUri.fsPath,
            forPrint: render.forPrint,
            enableUi: render.enableUi,
            uiOverrides: render.useOverrides ? uiOverridesFor(sourceFileUri.toString(), documentContent) : undefined,
            includeLineAnchors: false,
        }),
        signal: combineSignals(AbortSignal.timeout(30000), signal),
    });
    const response = sharedApiClient
        ? await sharedApiClient.runWithSupersession(runRequest)
        : await runRequest(new AbortController().signal);
    if (!response || !response.ok) {
        throw new Error(`Server returned ${response?.status ?? 'no response'}`);
    }
    return await response.text();
}

/**
 * Convert a Calcpad document to PDF on the server and write the bytes to
 * <paramref name="saveUri"/>. Pure I/O — no UI prompts. Used by
 * <see cref="runPdfExportCommand"/>, which handles the editor lookup, save
 * dialog, progress notification, and "Open PDF" follow-up.
 */
async function generatePdfToFile(
    documentContent: string,
    sourceFileUri: vscode.Uri,
    saveUri: vscode.Uri,
    variant: ExportVariant = 'report',
    progress?: vscode.Progress<{ increment?: number; message?: string }>
): Promise<void> {
    const settingsManager = CalcpadSettingsManager.getInstance(extensionContext);
    const apiBaseUrl = settingsManager.getServerUrl();
    if (!apiBaseUrl) throw new Error('Server URL not configured');

    progress?.report({ increment: 20, message: 'Converting to HTML...' });

    const sourceDir = path.dirname(sourceFileUri.fsPath);

    // Step 1: Convert calcpad content to HTML
    let html = await renderForExport(documentContent, sourceFileUri, variant);

    // Inline local images as base64 data URIs so the headless browser can
    // render them (it has no access to the local filesystem).
    html = applyImageCache(html, await buildImageCache(html, sourceDir));

    progress?.report({ increment: 50, message: 'Generating PDF...' });

    // Step 2: Generate PDF from HTML
    const pdfResponse = await fetch(`${apiBaseUrl}/api/calcpad/pdf`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', ...apiAuthHeaders() },
        body: JSON.stringify({
            html: html,
            options: getPdfSettings(documentContent)
        }),
        signal: AbortSignal.timeout(60000)
    });
    if (!pdfResponse.ok) {
        throw await pdfResponseError(pdfResponse);
    }

    progress?.report({ increment: 80, message: 'Saving PDF file...' });

    const pdfBuffer = await pdfResponse.arrayBuffer();
    await vscode.workspace.fs.writeFile(saveUri, new Uint8Array(pdfBuffer));

    progress?.report({ increment: 100, message: 'PDF generation complete!' });
}

/**
 * Default save target for an export: the document's own folder and base name, or the
 * workspace folder for a document that has never been saved.
 */
function defaultSavePath(uri: vscode.Uri, extension: string): string {
    const directory = uri.scheme === 'untitled'
        ? (vscode.workspace.workspaceFolders?.[0]?.uri.fsPath ?? '')
        : path.dirname(uri.fsPath);
    return path.join(directory, path.basename(uri.fsPath, path.extname(uri.fsPath)) + extension);
}

/**
 * Editor → save dialog → generate → "Open PDF" prompt. Shared entry point for
 * <c>vscode-calcpad.exportToPdf</c> and <c>vscode-calcpad.printToPdf</c>, which
 * both produce the report; the Export tab passes other variants through.
 */
async function runPdfExportCommand(variant: ExportVariant = 'report'): Promise<void> {
    const source = activeCalcpadSource();
    if (!source) {
        vscode.window.showErrorMessage('No active CalcpadCE document found');
        return;
    }

    const saveUri = await vscode.window.showSaveDialog({
        defaultUri: vscode.Uri.file(defaultSavePath(source.uri, '.pdf')),
        filters: { 'PDF Files': ['pdf'] }
    });
    if (!saveUri) return;

    const runExport = () => vscode.window.withProgress({
        location: vscode.ProgressLocation.Notification,
        title: 'Generating PDF...',
        cancellable: false
    }, async progress => {
        progress.report({ increment: 0, message: 'Starting PDF generation...' });
        await generatePdfToFile(
            source.text,
            source.uri,
            saveUri,
            variant,
            progress
        );
    });

    // Two attempts at most: the second only runs after the user accepted the
    // Chromium download, so it starts from a state where a browser exists.
    for (let attempt = 0; attempt < 2; attempt++) {
        try {
            await runExport();

            const openChoice = await vscode.window.showInformationMessage(
                `PDF saved to ${saveUri.fsPath}`,
                'Open PDF'
            );
            if (openChoice === 'Open PDF') {
                vscode.env.openExternal(saveUri);
            }
            return;
        } catch (error) {
            const msg = error instanceof Error ? error.message : String(error);
            outputChannel.appendLine(`[PDF] ${msg}`);

            if (attempt === 0 && isBrowserNotFound(error) && await offerChromiumDownload(error.downloadSizeMb)) {
                continue;
            }
            if (!isBrowserNotFound(error)) {
                vscode.window.showErrorMessage(`Failed to generate PDF: ${msg}`);
            }
            return;
        }
    }
}

/**
 * Offers the bundled headless Chromium download after PDF export found no usable
 * browser. Declining is a first-class outcome — installing Chrome/Edge/Chromium
 * themselves stays the recommended path, so that advice goes to the Output panel
 * either way. Resolves true when a browser is now installed.
 */
async function offerChromiumDownload(downloadSizeMb: number): Promise<boolean> {
    const size = downloadSizeMb > 0 ? `~${downloadSizeMb} MB` : 'a few hundred MB';
    const advice = 'PDF export needs a Chromium-family browser (Chrome, Edge or Chromium). '
        + 'Install one and it is picked up automatically, or point the '
        + '"BrowserPath" setting in the bundled server\'s appsettings.json at an existing install.';

    const choice = await vscode.window.showWarningMessage(
        'No Chromium-family browser was found for PDF export.',
        {
            modal: true,
            detail: `${advice}\n\nAlternatively, CalcpadCE can download a private headless Chromium (${size}) used only for exports.`,
        },
        `Download Chromium (${size})`,
    );

    if (choice === undefined) {
        outputChannel.appendLine(`[PDF] Export cancelled — no browser available. ${advice}`);
        return false;
    }

    const settingsManager = CalcpadSettingsManager.getInstance(extensionContext);
    const apiBaseUrl = settingsManager.getServerUrl();
    if (!apiBaseUrl) {
        vscode.window.showErrorMessage('Server URL not configured');
        return false;
    }

    try {
        const installedPath = await vscode.window.withProgress({
            location: vscode.ProgressLocation.Notification,
            title: 'Downloading headless Chromium (one time)...',
            cancellable: false
        }, () => installPdfBrowser(apiBaseUrl, apiAuthHeaders()));
        outputChannel.appendLine(`[PDF] Chromium installed: ${installedPath}`);
        return true;
    } catch (error) {
        const msg = error instanceof Error ? error.message : String(error);
        outputChannel.appendLine(`[PDF] Chromium download failed: ${msg}`);
        vscode.window.showErrorMessage(`Chromium download failed: ${msg}. ${advice}`);
        return false;
    }
}

/**
 * Convert the active CalcPad document to HTML on the server, then save
 * the result via a native Save dialog. Used by the Export tab's
 * "Save HTML…" buttons, which pick the variant.
 */
async function saveSourceHtml(variant: ExportVariant = 'report') {
    const source = activeCalcpadSource();
    if (!source) {
        vscode.window.showErrorMessage('No active CalcpadCE document found');
        return;
    }
    try {
        const saveUri = await vscode.window.showSaveDialog({
            defaultUri: vscode.Uri.file(defaultSavePath(source.uri, '.html')),
            filters: { 'HTML Files': ['html', 'htm'] },
        });
        if (!saveUri) return;

        await vscode.window.withProgress({
            location: vscode.ProgressLocation.Notification,
            title: 'Generating HTML…',
            cancellable: false,
        }, async () => {
            const html = await renderForExport(source.text, source.uri, variant);
            await vscode.workspace.fs.writeFile(saveUri, new TextEncoder().encode(html));
        });

        const openChoice = await vscode.window.showInformationMessage(
            `HTML saved to ${saveUri.fsPath}`,
            'Open HTML',
        );
        if (openChoice === 'Open HTML') {
            vscode.env.openExternal(saveUri);
        }
    } catch (error) {
        const msg = error instanceof Error ? error.message : 'Unknown error';
        outputChannel.appendLine(`ERROR in saveSourceHtml: ${msg}`);
        vscode.window.showErrorMessage(`Failed to save HTML: ${msg}`);
    }
}

/**
 * Convert the active CalcPad document to DOCX (Word) on the server and save the result.
 * Used by the Export tab's "Save Word…" buttons, which offer the report and the preview
 * only — a form and a code listing have no meaningful Word rendering.
 */
async function saveDocx(variant: ExportVariant = 'report') {
    const render = variantRender(variant);
    if (render.enableUi || render.unwrap) return;
    const source = activeCalcpadSource();
    if (!source) {
        vscode.window.showErrorMessage('No active CalcpadCE document found');
        return;
    }
    try {
        const settingsManager = CalcpadSettingsManager.getInstance(extensionContext);
        const apiBaseUrl = settingsManager.getServerUrl();
        if (!apiBaseUrl) {
            vscode.window.showErrorMessage('Server URL not configured');
            return;
        }

        const saveUri = await vscode.window.showSaveDialog({
            defaultUri: vscode.Uri.file(defaultSavePath(source.uri, '.docx')),
            filters: { 'Word Documents': ['docx'] },
        });
        if (!saveUri) return;

        await vscode.window.withProgress({
            location: vscode.ProgressLocation.Notification,
            title: 'Generating Word document…',
            cancellable: false,
        }, async () => {
            const settings = await settingsManager.getApiSettings();

            const buf = await sharedApiClient?.convertDocx(source.text, settings, source.uri.fsPath, {
                forPrint: render.forPrint,
                uiOverrides: render.useOverrides ? uiOverridesFor(source.uri.toString(), source.text) : undefined,
            });
            if (!buf) {
                throw new Error('Word document generation failed');
            }
            await vscode.workspace.fs.writeFile(saveUri, new Uint8Array(buf));
        });

        const openChoice = await vscode.window.showInformationMessage(
            `Word document saved to ${saveUri.fsPath}`,
            'Open',
        );
        if (openChoice === 'Open') {
            vscode.env.openExternal(saveUri);
        }
    } catch (error) {
        const msg = error instanceof Error ? error.message : 'Unknown error';
        outputChannel.appendLine(`ERROR in saveDocx: ${msg}`);
        vscode.window.showErrorMessage(`Failed to save Word document: ${msg}`);
    }
}

/**
 * Compile the active document to a `.cpdz` and save it. This is an export: the open
 * document keeps its own path and stays editable. The worksheet is bundled first —
 * includes expanded, `#read` data inlined — and its images embedded after, in that order:
 * an included file's images only resolve once the server has rewritten their paths.
 */
async function saveAsCompiled() {
    const source = activeCalcpadSource();
    if (!source) {
        vscode.window.showErrorMessage('No active CalcpadCE document found');
        return;
    }
    try {
        const untitled = source.uri.scheme === 'untitled';
        const defaultPath = untitled
            ? path.join(
                vscode.workspace.workspaceFolders?.[0]?.uri.fsPath ?? '',
                'worksheet' + COMPILED_EXTENSION)
            : defaultSavePath(source.uri, COMPILED_EXTENSION);
        const saveUri = await vscode.window.showSaveDialog({
            defaultUri: vscode.Uri.file(defaultPath),
            filters: { 'CalcpadCE Compiled': ['cpdz'] },
        });
        if (!saveUri) return;

        const bundled = await sharedApiClient?.bundlePortable(
            source.text, untitled ? undefined : source.uri.fsPath);
        if (bundled?.content == null) {
            const reasons = bundled?.errors.join('\n') ?? 'The server is not running';
            throw new Error(`the worksheet is not self-contained:\n${reasons}`);
        }

        // An untitled document has no folder for relative image paths to resolve against.
        const documentDir = untitled ? '' : path.dirname(source.uri.fsPath);
        // bundled.content is already self-contained — the server resolved any {project}/
        // {library} reference to an absolute path — so this resolver only needs to expand
        // env vars in whatever plain relative/absolute src the author wrote directly.
        const resolve = createReferenceResolver(bundled.content, documentDir, expandEnvVars, path.resolve, os.homedir);
        // Refused rather than trimmed: the images become the file, so a compiled worksheet
        // that quietly dropped them would be a lossy save. Mirrors the server's own limit on
        // embedded `#read` data. The catch below turns it into the failure message.
        const compiled = await inlineImageSources(bundled.content, new VSCodeFileSystem(), resolve, {
            maxTotalBytes: MAX_COMPILED_IMAGE_TOTAL_BYTES,
            onExceeded: 'fail',
        });
        const bytes = await sharedApiClient?.encodeCpdz(compiled);
        if (!bytes) throw new Error('The server could not encode the worksheet');
        await vscode.workspace.fs.writeFile(saveUri, bytes);

        vscode.window.showInformationMessage(`Compiled worksheet saved to ${saveUri.fsPath}`);
    } catch (error) {
        const msg = error instanceof Error ? error.message : 'Unknown error';
        outputChannel.appendLine(`ERROR in saveAsCompiled: ${msg}`);
        vscode.window.showErrorMessage(`Failed to save compiled worksheet: ${msg}`);
    }
}

/**
 * Packs the active document and everything it references into a portable archive. Unlike a
 * compiled worksheet this stays text: what comes out is the document as written, with only its
 * paths changed — each one pointing into the folder of references packed beside it. For a
 * recipient who has to read or edit the calculation rather than just fill it in.
 *
 * The server does the packing, since resolving the references means reading them; refusing is
 * part of the job, so what came back is reported rather than worked around.
 */
async function exportPortable() {
    const source = activeCalcpadSource();
    if (!source) {
        vscode.window.showErrorMessage('No active CalcpadCE document found');
        return;
    }
    if (source.uri.scheme === 'untitled') {
        vscode.window.showErrorMessage('A portable package resolves references against the '
            + "document's own folder, so save the document first.");
        return;
    }
    if (isCompiledPath(source.uri.fsPath)) {
        vscode.window.showErrorMessage('A compiled worksheet already carries everything it needs, '
            + 'and its source is not handed out — there is nothing to package.');
        return;
    }
    try {
        const saveUri = await vscode.window.showSaveDialog({
            defaultUri: vscode.Uri.file(defaultSavePath(source.uri, '.zip')),
            filters: { 'ZIP Archives': ['zip'] },
        });
        if (!saveUri) return;

        const packaged = await sharedApiClient?.packagePortable(source.text, source.uri.fsPath);
        if (!packaged?.zip) {
            const reasons = packaged?.errors ?? ['The server is not running'];
            // A collision or a list of unreadable references runs to several lines, which a
            // notification swallows: the first goes in the toast, the rest to the log.
            outputChannel.appendLine(`ERROR in exportPortable:\n${reasons.join('\n')}`);
            const choice = await vscode.window.showErrorMessage(
                `Cannot package this worksheet: ${reasons[0]}`,
                'Show Details');
            if (choice === 'Show Details') outputChannel.show(true);
            return;
        }

        await vscode.workspace.fs.writeFile(saveUri, packaged.zip);
        vscode.window.showInformationMessage(
            `Portable package saved to ${saveUri.fsPath} — ${packaged.bundled.length} reference(s) bundled`);
    } catch (error) {
        const msg = error instanceof Error ? error.message : 'Unknown error';
        outputChannel.appendLine(`ERROR in exportPortable: ${msg}`);
        vscode.window.showErrorMessage(`Failed to export the portable package: ${msg}`);
    }
}

// Detects whether the document uses macros or includes. When it does, the wrapped
// preview's line/error links point at *expanded output* lines, so we route the user
// through the unwrapped view (mirrors WPF's `_highlighter.Defined.HasMacros`).
function documentHasMacros(text: string): boolean {
    return /^\s*#(def|include)\b/im.test(text);
}

// Jump the source editor to a 1-based line.
function navigateEditorToLine(sourceEditor: vscode.TextEditor, line: number) {
    const lineIndex = Math.max(0, line - 1);
    outputChannel.appendLine(`Navigating to source line ${line}`);
    const position = new vscode.Position(lineIndex, 0);
    const selection = new vscode.Selection(position, position);
    sourceEditor.selection = selection;
    sourceEditor.revealRange(selection, vscode.TextEditorRevealType.InCenter);
    vscode.window.showTextDocument(sourceEditor.document, vscode.ViewColumn.One);
}

// Editor -> preview sync: tell any open preview panel(s) to scroll to a 1-based
// source line. Every view matches on data-source-line / line-num anchors, so the
// same source line works for the preview, unwrapped, input-form and report panels.
// The input form is included so sidebar (TOC) navigation lands there too -- it drops
// the hover arrows but keeps the sync listener.
// `exact` disables the nearest-preceding-line fallback in the preview's focusPreviewLine:
// a TOC heading that fell inside a hidden #pre/#post block has no element to land on, and
// the fallback would jump to an unrelated line, so TOC navigation should do nothing rather
// than land somewhere odd.
function postPreviewSourceLine(line: number, exact: boolean = false) {
    const msg = { type: 'scrollToSourceLine', line, exact };
    wrappedPanel?.webview.postMessage(msg);
    unwrappedPanel?.webview.postMessage(msg);
    uiPanel?.webview.postMessage(msg);
    uiReportPanel?.webview.postMessage(msg);
}

function handlePreviewMessage(message: any, kind: PreviewKind) {
    switch (message.type) {
        case 'navigateToLine': {
            const sourceEditor = previewSourceEditor;
            if (!sourceEditor || !message.line) break;
            // An 'output' line comes from the true wrapped view; when the document has
            // macros/includes that line only makes sense in the unwrapped view, so open
            // it (below the wrapped one) scrolled there — the user then clicks a line
            // number to reach the true source line. A 'source' line (code-view .line-num
            // anchors, or a macro-free document) navigates the editor directly.
            const isOutputLine = message.lineType === 'output';
            if (kind === 'regular' && isOutputLine && documentHasMacros(sourceEditor.document.getText())) {
                void showPreview('unwrapped', message.line);
            } else {
                navigateEditorToLine(sourceEditor, message.line);
            }
            break;
        }
        case 'consoleMessage': {
            const timestamp = new Date().toISOString();
            const level = message.level.toUpperCase();
            calcpadWebviewConsoleChannel.appendLine(`[${timestamp}] [${level}] ${message.message}`);
            break;
        }
        // A #UI control was edited in the preview. Record the value and re-render
        // so dependent results recalculate.
        case 'uiValueChange': {
            const sourceEditor = previewSourceEditor;
            if (!sourceEditor || kind !== 'ui') break;
            const docKey = sourceEditor.document.uri.toString();
            if (!uiOverrides.set(docKey, String(message.varName), String(message.newValue))) break;
            uiOverridesDirty.add(docKey);
            // Refreshes the form (so the control keeps the entered value) and the
            // report panel beside it (so the result updates).
            void refreshPreviewPanels(sourceEditor.document);
            break;
        }
        default:
            break;
    }
}

// Opens (or reveals) the wrapped or unwrapped preview. The unwrapped preview is
// stacked directly below the wrapped one so the two-step navigation reads top→bottom.
// `scrollToLine` (an output line) is baked into the rendered HTML so the unwrapped
// view scrolls to it on load without a postMessage race.
async function showPreview(kind: PreviewKind, scrollToLine?: number, preserveFocus = false) {
    // When invoked from a preview line-link click the webview is focused, so there is
    // no active *text* editor — fall back to the editor that spawned the preview.
    const activeEditor = vscode.window.activeTextEditor ?? previewSourceEditor;
    if (!activeEditor) {
        vscode.window.showErrorMessage('No active editor found');
        return;
    }

    // Store the source editor for navigation
    previewSourceEditor = activeEditor;

    const unwrapped = kind === 'unwrapped';
    const enableUi = kind === 'ui';
    const forPrint = kind === 'uiReport';
    const existing = previewPanelFor(kind);

    if (existing) {
        existing.reveal(existing.viewColumn ?? vscode.ViewColumn.Beside, true);
        await updatePreviewContent(existing, activeEditor.document.getText(), activeEditor.document.uri, unwrapped, scrollToLine, enableUi, forPrint);
        return;
    }

    // Focus the wrapped panel first so `moveEditorToBelowGroup` moves the new
    // unwrapped preview below the *wrapped* group. Without this reveal, when the
    // click originates from the wrapped webview the "active" group is whatever
    // ViewColumn.Beside just created — usually a column to the right of the
    // wrapped preview — and the split lands below that instead of below wrapped.
    if (unwrapped && wrappedPanel) {
        wrappedPanel.reveal(wrappedPanel.viewColumn, false);
    }

    const viewType = enableUi ? 'htmlPreviewUi'
        : forPrint ? 'htmlPreviewUiReport'
        : unwrapped ? 'htmlPreviewUnwrapped'
        : 'htmlPreview';
    const title = enableUi ? 'CalcpadCE Input'
        : forPrint ? 'CalcpadCE Report'
        : unwrapped ? 'CalcpadCE Preview Unwrapped'
        : 'CalcpadCE Preview';
    const panel = vscode.window.createWebviewPanel(
        viewType,
        title,
        {
            viewColumn: unwrapped && wrappedPanel ? vscode.ViewColumn.Active : vscode.ViewColumn.Beside,
            // A panel the user asked for should have the focus; one that opened on its own
            // (a #UI document's form) must leave the caret where it was.
            preserveFocus,
        },
        {
            enableScripts: true,
            // The rendered document lives in a frame the shell is handed by message, not
            // in `webview.html` (see previewFrame.ts). Letting VS Code tear the webview
            // down on hide would therefore restore an empty shell and make every reveal
            // pay for a fresh render — visible as a blank frame, then content. Holding the
            // context costs memory for at most four panels and keeps a tab switch free.
            retainContextWhenHidden: true,
            // No enableFindWidget: the workbench's find reaches the webview's own
            // document, and the report it would search is a sandboxed frame deeper.
            // previewFrame.ts carries a find widget that talks to the frame instead.
            //
            // Nothing local is loaded either — images arrive as data URIs — so the
            // default roots (every workspace folder) are given up rather than left open.
            localResourceRoots: []
        }
    );

    if (kind === 'regular') {
        wrappedPanel = panel;
    } else if (kind === 'ui') {
        uiPanel = panel;
        syncInputMode();
    } else if (kind === 'uiReport') {
        uiReportPanel = panel;
    } else {
        unwrappedPanel = panel;
        // Stack the unwrapped preview below the wrapped one when both are open.
        if (wrappedPanel) {
            await vscode.commands.executeCommand('workbench.action.moveEditorToBelowGroup');
        }
    }

    panel.onDidDispose(() => {
        if (kind === 'regular') {
            wrappedPanel = undefined;
        } else if (kind === 'ui') {
            uiPanel = undefined;
            syncInputMode();
            // Closing the panel directly leaves input mode too; the toggle command
            // has already dealt with the values when it comes through there. The report
            // stays open — it is a view of the document in its own right — but the values
            // it was showing are gone now, so re-render it from the document.
            const docKey = uiPanelDocKey;
            uiPanelDocKey = undefined;
            void discardUiValues(docKey).then(() => {
                const editor = previewSourceEditor;
                if (uiReportPanel && editor) void refreshPreviewPanels(editor.document);
            });
        } else if (kind === 'uiReport') {
            uiReportPanel = undefined;
        } else {
            unwrappedPanel = undefined;
        }
        if (!wrappedPanel && !unwrappedPanel && !uiPanel && !uiReportPanel) {
            previewSourceEditor = undefined;
        }
    });

    panel.webview.onDidReceiveMessage(message => {
        if (handleFrameStateMessage(panel, message)) return;
        handlePreviewMessage(message, kind);
    });

    await updatePreviewContent(panel, activeEditor.document.getText(), activeEditor.document.uri, unwrapped, scrollToLine, enableUi, forPrint);
}

function schedulePreviewUpdate() {
    if (!wrappedPanel && !unwrappedPanel && !uiPanel && !uiReportPanel) return;

    const activeEditor = vscode.window.activeTextEditor;
    if (!activeEditor) return;

    // Only update for .cpd files or plaintext files
    if (activeEditor.document.languageId !== 'calcpad' && activeEditor.document.languageId !== 'plaintext') {
        return;
    }

    if (previewUpdateTimeout) {
        clearTimeout(previewUpdateTimeout as NodeJS.Timeout);
    }

    previewUpdateTimeout = setTimeout(async () => {
        const editor = vscode.window.activeTextEditor;
        if (!editor) return;
        previewSourceEditor = editor;
        await refreshPreviewPanels(editor.document);
    }, 500);
}

export async function activate(context: vscode.ExtensionContext) {
    console.log('VS Code CalcPad extension is now active!');
    
    try {
        // Store extension context for global access
        extensionContext = context;
        
        // Create output channel for debugging
        outputChannel = vscode.window.createOutputChannel('CalcpadCE Extension');
        outputChannel.appendLine('CalcPad extension activated');

        // Create dedicated output channels for HTML
        calcpadOutputHtmlChannel = vscode.window.createOutputChannel('CalcpadCE Output HTML');
        calcpadWebviewConsoleChannel = vscode.window.createOutputChannel('CalcpadCE Webview Console');

        // Create debug channel for linter/highlighter
        const serverDebugChannel = vscode.window.createOutputChannel('CalcpadCE Server Debug');

        outputChannel.appendLine('Initializing settings manager...');
        const settingsManager = CalcpadSettingsManager.getInstance(context);

        // Create shared API client (uses remote URL initially, switches to local when server starts)
        const apiClient = new CalcpadApiClient(
            settingsManager.getServerUrl(),
            new VSCodeLogger(serverDebugChannel)
        );
        sharedApiClient = apiClient;

        // Start bundled server if available
        const serverMode = settingsManager.getSettings().server.mode || 'auto';
        outputChannel.appendLine(`Server mode: ${serverMode}`);

        if (serverMode === 'auto' || serverMode === 'local') {
            const dllExists = CalcpadServerManager.dllExists(context.extensionPath);
            const appHostExists = CalcpadServerManager.appHostExists(context.extensionPath);
            outputChannel.appendLine(`Bundled DLL exists: ${dllExists}`);
            outputChannel.appendLine(`Bundled apphost exists: ${appHostExists}`);

            if (dllExists) {
                const configuredDotnetPath = settingsManager.getExtra('dotnetPath', 'dotnet');
                const dotnetManager = new DotnetRuntimeManager(outputChannel);
                const globalStorage = context.globalStorageUri.fsPath;

                // The VSIX now ships framework-dependent: the apphost is
                // present but requires a .NET 10 runtime to be installed
                // somewhere on the user's machine. Always run the resolver
                // so we either find the system install, prompt the user to
                // install one locally, or fall back to the remote API.
                // (Calcpad-desktop's self-contained flow is unaffected — this
                // path only runs in vscode-calcpad.) When the resolver returns
                // a path under the extension's globalStorage, the server-manager
                // sets DOTNET_ROOT so the apphost can find that runtime.
                const dotnetPromise = dotnetManager.resolveDotnetPath(globalStorage, configuredDotnetPath, serverMode);

                dotnetPromise.then((resolvedDotnetPath) => {
                    if (!resolvedDotnetPath) {
                        if (serverMode === 'local') {
                            outputChannel.appendLine('.NET runtime not available, server cannot start');
                        } else {
                            outputChannel.appendLine('.NET runtime not available, falling back to remote API');
                        }
                        return;
                    }

                    if (appHostExists) {
                        outputChannel.appendLine('Using bundled apphost (self-contained, no system dotnet required)');
                    } else {
                        outputChannel.appendLine(`Using dotnet at: ${resolvedDotnetPath}`);
                    }
                    serverManager = new CalcpadServerManager(context.extensionPath, serverDebugChannel, resolvedDotnetPath, outputChannel);
                    context.subscriptions.push(serverManager);

                    // Notify user when server crashes repeatedly
                    serverManager.onCrashExhausted = (crashOutput: string) => {
                        serverDebugChannel.appendLine('[ServerManager] Server crashed 3 times — stopping auto-restart');
                        serverDebugChannel.appendLine('[ServerManager] Last crash output:\n' + crashOutput);
                        vscode.window.showErrorMessage(
                            'CalcpadCE server crashed repeatedly (possibly due to your file). Use the refresh button to restart.',
                            'Show Debug Output'
                        ).then(choice => {
                            if (choice === 'Show Debug Output') {
                                serverDebugChannel.show();
                            }
                        });
                    };

                    // Start the server in the background so activation isn't blocked
                    serverManager.start().then(() => {
                        const serverUrl = serverManager!.getBaseUrl();
                        settingsManager.setLocalServerUrl(serverUrl);
                        apiClient.setBaseUrl(serverUrl);
                        apiClient.setAuthToken(serverManager!.getAuthToken());
                        outputChannel.appendLine(`Local server started at ${serverUrl}`);
                        void refreshAllComponents();
                    }).catch((err) => {
                        const message = err instanceof Error ? err.message : String(err);
                        outputChannel.appendLine(`Failed to start local server: ${message}`);
                        // Keep `serverManager` around when Windows blocked the exe —
                        // the user can unblock the file and click refresh to retry.
                        // Discarding it here would leave refresh with nothing to call.
                        const blocked = /Windows blocked the executable|EACCES|EPERM/i.test(message);
                        if (!blocked) {
                            serverManager = undefined;
                        }

                        if (blocked) {
                            vscode.window.showErrorMessage(
                                'CalcpadCE: Windows blocked Calcpad.Server.exe. ' +
                                'Unblock the file (right-click → Properties → Unblock) ' +
                                'then click the CalcpadCE refresh button to retry.',
                                'Show Output'
                            ).then(choice => {
                                if (choice === 'Show Output') {
                                    serverDebugChannel.show();
                                }
                            });
                        } else if (serverMode === 'local') {
                            vscode.window.showErrorMessage(`CalcpadCE: Failed to start local server: ${message}`);
                        } else {
                            // Auto mode. Falling back to remote only makes
                            // sense if the user actually configured a remote
                            // URL — otherwise every API call will fail with
                            // "Server URL not configured" / a fetch against
                            // `/api/calcpad/*` with no host. Tell them.
                            const remoteUrl = settingsManager.getRemoteServerUrl();
                            if (!remoteUrl || remoteUrl.length === 0) {
                                vscode.window.showErrorMessage(
                                    `CalcpadCE: Bundled server failed to start and no remote URL is configured (${message}).`,
                                    'Show Output',
                                ).then(choice => {
                                    if (choice === 'Show Output') serverDebugChannel.show();
                                });
                            } else {
                                outputChannel.appendLine(`Falling back to remote API at ${remoteUrl}`);
                            }
                        }
                    });
                }).catch((err) => {
                    const message = err instanceof Error ? err.message : String(err);
                    outputChannel.appendLine(`Dotnet resolution failed: ${message}`);
                });
            } else if (serverMode === 'local') {
                vscode.window.showErrorMessage('CalcpadCE: Server mode is "local" but CalcpadServer.dll was not found in the extension.');
            } else {
                outputChannel.appendLine('No bundled DLL found, using remote API');
            }
        }

        outputChannel.appendLine('Initializing linter...');
        linter = new CalcpadServerLinter(apiClient, serverDebugChannel);

        outputChannel.appendLine('Initializing definitions service...');
        definitionsService = new CalcpadDefinitionsService(apiClient, serverDebugChannel);

        // Initialize semantic token provider
        outputChannel.appendLine('Initializing semantic token provider...');
        const semanticTokensProvider = new CalcpadSemanticTokensProvider(apiClient, serverDebugChannel);
        const semanticTokensDisposable = vscode.languages.registerDocumentSemanticTokensProvider(
            { language: 'calcpad' },
            semanticTokensProvider,
            semanticTokensLegend
        );

        // Initialize operator replacer
        outputChannel.appendLine('Initializing operator replacer...');
        const operatorReplacer = new OperatorReplacer(outputChannel);
        const operatorReplacerDisposable = operatorReplacer.registerDocumentChangeListener(context);

        // Initialize auto-indenter
        outputChannel.appendLine('Initializing auto-indenter...');
        const autoIndenter = new AutoIndenter(outputChannel);
        const autoIndenterDisposable = autoIndenter.registerDocumentChangeListener(context);

        // Initialize image inserter
        outputChannel.appendLine('Initializing image inserter...');
        const imageInserter = new ImageInserter(outputChannel);
        const imagePasteDisposable = imageInserter.registerPasteProvider();
        const imageInsertCommandDisposable = imageInserter.registerInsertCommand();

        // Initialize comment formatter
        outputChannel.appendLine('Initializing comment formatter...');
        const commentFormatter = new CommentFormatter(outputChannel);
        const commentFormatterDisposables = commentFormatter.registerCommands();

        // Initialize insert manager (snippet service)
        outputChannel.appendLine('Initializing insert manager...');
        const insertManager = new CalcpadInsertManager(apiClient, outputChannel);

        // Initialize quick typer (uses snippet data for quick type map)
        outputChannel.appendLine('Initializing quick typer...');
        const quickTyper = new QuickTyper(outputChannel, insertManager);
        const quickTyperDisposable = quickTyper.registerDocumentChangeListener(context);

        // Initialize autocomplete provider
        outputChannel.appendLine('Initializing autocomplete provider...');
        const completionProviderDisposable = CalcpadCompletionProvider.register(definitionsService, insertManager, outputChannel);

        // Initialize #include file completion provider
        outputChannel.appendLine('Initializing include file completion provider...');
        const includeCompletionDisposable = CalcpadIncludeCompletionProvider.register(definitionsService, outputChannel);

        // Initialize definition provider (Go to Definition)
        outputChannel.appendLine('Initializing definition provider...');
        const definitionProviderDisposable = CalcpadDefinitionProvider.register(apiClient, definitionsService, outputChannel);

        // Initialize #include link provider (always-underlined, clickable paths)
        outputChannel.appendLine('Initializing include link provider...');
        const includeLinkProviderDisposable = CalcpadIncludeLinkProvider.register(definitionsService, outputChannel);

        // Initialize reference provider (Find All References)
        outputChannel.appendLine('Initializing reference provider...');
        const referenceProviderDisposable = CalcpadReferenceProvider.register(apiClient, outputChannel);

        // Initialize rename provider (F2 Rename Symbol)
        outputChannel.appendLine('Initializing rename provider...');
        const renameProviderDisposable = CalcpadRenameProvider.register(apiClient, outputChannel);

        // Initialize hover provider (Hover Tooltips)
        outputChannel.appendLine('Initializing hover provider...');
        const hoverProviderDisposable = CalcpadHoverProvider.register(definitionsService, insertManager, outputChannel);

        // Initialize the compiled worksheet editor (.cpdz opens as an input form, with
        // the report available beside it). Both of its panels are standalone: there is no
        // text editor behind a .cpdz for a preview to be *of*.
        outputChannel.appendLine('Initializing compiled worksheet editor...');
        compiledEditor = CalcpadCompiledEditorProvider.register(
            apiClient,
            uiOverrides,
            (panel, text, uri, kind) => updatePreviewContent(
                panel, text, uri, false, undefined, kind === 'form', kind === 'report', true),
            outputChannel,
        );

    // Unified document processing function
    let isProcessingDocument = false;
    async function processDocument(document: vscode.TextDocument) {
        if (document.languageId !== 'calcpad' && document.languageId !== 'plaintext') {
            return;
        }
        if (isProcessingDocument) return;

        isProcessingDocument = true;
        try {
            await _doProcessDocument(document);
        } finally {
            isProcessingDocument = false;
        }
    }

    async function _doProcessDocument(document: vscode.TextDocument) {
        outputChannel.appendLine('[processDocument] Processing document: ' + document.uri.fsPath);

        // Run linting and definitions in parallel
        const [, definitions] = await Promise.all([
            linter.lintDocument(document),
            definitionsService.refreshDefinitions(document).catch((error: unknown) => {
                outputChannel.appendLine('Error fetching definitions: ' + error);
                return null;
            }),
        ]);

        if (definitions) {
            outputChannel.appendLine('[processDocument] Found ' + definitions.macros.length + ' macros, ' + definitions.variables.length + ' variables, ' + definitions.functions.length + ' functions, ' + definitions.customUnits.length + ' custom units');

            vueUiProvider?.updateVariables({
                macros: definitions.macros.map(m => ({
                    name: m.name,
                    params: m.parameters.length > 0 ? m.parameters.join('; ') : undefined,
                    definition: m.content.join('\n'),
                    source: m.source as 'local' | 'include',
                    sourceFile: m.sourceFile,
                    description: m.description,
                    paramTypes: m.paramTypes,
                    paramDescriptions: m.paramDescriptions,
                    defaults: m.defaults
                })),
                variables: definitions.variables.map(v => ({
                    name: v.name,
                    definition: v.expression,
                    expression: v.expression,
                    type: v.type,
                    source: v.source as 'local' | 'include',
                    sourceFile: v.sourceFile,
                    description: v.description
                })),
                functions: definitions.functions.map(f => ({
                    name: f.name,
                    params: f.parameters.join('; '),
                    definition: f.expression,
                    expression: f.expression,
                    returnType: f.returnType,
                    source: f.source as 'local' | 'include',
                    sourceFile: f.sourceFile,
                    description: f.description,
                    paramTypes: f.paramTypes,
                    paramDescriptions: f.paramDescriptions,
                    defaults: f.defaults
                })),
                customUnits: definitions.customUnits.map(u => ({
                    name: u.name,
                    definition: u.expression,
                    expression: u.expression,
                    source: u.source as 'local' | 'include',
                    sourceFile: u.sourceFile,
                    description: u.description
                }))
            });
        } else {
            outputChannel.appendLine('[processDocument] No definitions returned from server');
        }
    }

    // Centralized refresh function for when settings change
    async function refreshAllComponents() {
        outputChannel.appendLine('[Settings] Refreshing all components after settings change');

        // Use the effective server URL (local if running, remote otherwise)
        const effectiveUrl = settingsManager.getServerUrl();
        apiClient.setBaseUrl(effectiveUrl);
        outputChannel.appendLine(`[Settings] Using server URL: ${effectiveUrl}`);

        // Reload snippets from server
        try {
            await insertManager.reloadSnippets();
            outputChannel.appendLine('[Settings] Snippets reloaded');
        } catch (error) {
            outputChannel.appendLine('[Settings] Failed to reload snippets: ' + error);
        }

        // Refresh semantic tokens for all visible editors
        vscode.window.visibleTextEditors.forEach(editor => {
            if (editor.document.languageId === 'calcpad' || editor.document.languageId === 'plaintext') {
                semanticTokensProvider.refresh();
            }
        });

        // Reprocess active document (linting + definitions)
        const activeEditor = vscode.window.activeTextEditor;
        if (activeEditor) {
            await processDocument(activeEditor.document);
        }

        // Refresh preview(s) if open
        if (activeEditor && (wrappedPanel || unwrappedPanel || uiPanel || uiReportPanel)) {
            await refreshPreviewPanels(activeEditor.document);
            outputChannel.appendLine('[Settings] Preview refreshed');
        }

        outputChannel.appendLine('[Settings] All components refreshed');
    }

    vueUiProvider = new CalcpadVueUIProvider(context.extensionUri, context, settingsManager, insertManager);
    vueUiProvider.getSourceEditor = () => vscode.window.activeTextEditor ?? previewSourceEditor;
    vueUiProvider.getDefinitions = (uri: string) => definitionsService.getCachedDefinitions(uri);
    // Both fall back to previewSourceEditor: a setting is usually changed with the sidebar
    // focused while a *preview panel* holds the editor area, and a webview panel being active
    // means there is no activeTextEditor at all. Without the fallback the re-render these
    // hooks exist for silently does nothing.
    vueUiProvider.onPreviewThemeChanged = async () => {
        const editor = vscode.window.activeTextEditor ?? previewSourceEditor;
        if (!editor) return;
        await refreshPreviewPanels(editor.document);
    };
    vueUiProvider.onSettingsChanged = async () => {
        const editor = vscode.window.activeTextEditor ?? previewSourceEditor;
        if (!editor) return;
        await refreshPreviewPanels(editor.document);
    };
    // Rendered on demand rather than served from the cache: this is what the Properties
    // tab asks when it has no answer or wants a fresh one, and the cache is only as new
    // as the last time the input form itself was shown.
    vueUiProvider.resolveUiControls = async () => {
        const editor = vscode.window.activeTextEditor ?? previewSourceEditor;
        if (!editor) return null;
        try {
            const html = await renderForExport(editor.document.getText(), editor.document.uri, 'input');
            const controls = extractUiControls(html);
            uiControls.set(editor.document.uri.toString(), controls);
            return controls;
        } catch (e) {
            outputChannel.appendLine('[UI controls] Render failed: ' + e);
            return null;
        }
    };
    vueUiProvider.onUiOverridesEdited = (documentUri, overrides) => {
        uiOverrides.replace(documentUri, overrides);
        uiOverridesDirty.delete(documentUri);
    };
    const vueUiProviderDisposable = vscode.window.registerWebviewViewProvider(
        CalcpadVueUIProvider.viewType,
        vueUiProvider
    );
    // A restored session can come up with a compiled worksheet already in front.
    syncInputMode();

    const disposable = vscode.commands.registerCommand('vscode-calcpad.activate', () => {
        vscode.window.showInformationMessage('CalcpadCE activated!');
    });

    const previewCommand = vscode.commands.registerCommand('vscode-calcpad.previewHtml', () => {
        showPreview('regular');
    });

    const previewUnwrappedCommand = vscode.commands.registerCommand('vscode-calcpad.previewUnwrapped', () => {
        showPreview('unwrapped');
    });

    // Opens the input form, with the report preview to its right so the effect of
    // each entered value is visible. Toggling off closes the form and leaves the
    // report open.
    const toggleUiModeCommand = vscode.commands.registerCommand('vscode-calcpad.toggleUiMode', async () => {
        if (uiPanel) {
            // Prompted before the panel goes away, so the form is still on screen
            // while the message box asks about its values. Cancel leaves it open.
            if (!await discardUiValues(uiPanelDocKey, true)) return;
            uiPanel.dispose();
            outputChannel.appendLine('[UI] Input mode disabled');
            return;
        }
        outputChannel.appendLine('[UI] Input mode enabled');
        await showPreview('ui');
        // Opened after the form and while it holds focus, so Beside puts the
        // report in the column to its right rather than replacing it.
        await showPreview('uiReport');
        const form = previewPanelFor('ui');
        form?.reveal(form.viewColumn, false);
    });

    // Shows or hides the report preview. It accompanies the input form when that is open —
    // opened to its right, with focus handed back so the form stays in front — but it also
    // stands alone beside the editor, which is how you read the print layout without
    // filling in a form. Closing the panel directly does the same thing.
    const toggleUiReportCommand = vscode.commands.registerCommand('vscode-calcpad.toggleUiReport', async () => {
        // A compiled worksheet is its own form, so its report belongs to that editor
        // rather than to the shared input-form slot.
        const compiled = activeCompiledUri();
        if (compiled) {
            await compiledEditor?.toggleReport(compiled);
            return;
        }
        if (uiReportPanel) {
            uiReportPanel.dispose();
            return;
        }
        await showPreview('uiReport');
        const form = previewPanelFor('ui');
        form?.reveal(form.viewColumn, false);
    });

    // Writes the entered #UI values into the document as a uiOverrides metadata
    // comment, so they are restored the next time it opens.
    const saveUiValuesCommand = vscode.commands.registerCommand('vscode-calcpad.saveUiValues', async () => {
        const editor = vscode.window.activeTextEditor ?? previewSourceEditor;
        if (!editor) {
            vscode.window.showErrorMessage('No active editor found');
            return;
        }
        const count = await saveUiValuesFor(editor.document);
        if (count === null) {
            vscode.window.showInformationMessage('No #UI values have been entered.');
            return;
        }
        vscode.window.showInformationMessage(`Saved ${count} #UI value(s) to the document.`);
    });

    // `targetLine` lets a caller name the line (the sidebar's TOC does); invoked from the
    // palette or a keybinding it arrives undefined and the cursor's line is used. Only that
    // second form opens a preview when none is showing -- it is a request to see the line,
    // where a named line only syncs whatever results are already up.
    const focusPreviewToLineCommand = vscode.commands.registerCommand('vscode-calcpad.focusPreviewToLine', async (targetLine?: number) => {
        if (typeof targetLine === 'number') {
            postPreviewSourceLine(targetLine, true);
            return;
        }
        const editor = vscode.window.activeTextEditor;
        if (!editor) return;
        const line = editor.selection.active.line + 1;
        if (!wrappedPanel && !unwrappedPanel && !uiPanel && !uiReportPanel) {
            await showPreview('regular');
            // Wait for the webview to load its DOMContentLoaded listener before posting.
            setTimeout(() => postPreviewSourceLine(line), 600);
        } else {
            postPreviewSourceLine(line);
        }
    });

    const showInsertCommand = vscode.commands.registerCommand('vscode-calcpad.showInsert', () => {
        vscode.commands.executeCommand('workbench.view.extension.calcpad-ui');
    });

    const editMetadataPropertiesCommand = vscode.commands.registerCommand('vscode-calcpad.editMetadataProperties', async () => {
        const editor = vscode.window.activeTextEditor;
        if (!editor) return;
        const doc = editor.document;
        const curLine = editor.selection.active.line;
        const curText = doc.lineAt(curLine).text;

        const revealPanel = () => {
            vscode.commands.executeCommand('workbench.view.extension.calcpad-ui');
            // The webview may need a beat to resolve before it can switch tabs.
            setTimeout(() => {
                vueUiProvider?.focusTab('metadata');
                updateMetadataContext(vscode.window.activeTextEditor);
            }, 200);
        };

        const moveCursorTo = (line: number) => {
            const pos = new vscode.Position(line, doc.lineAt(line).text.length);
            editor.selection = new vscode.Selection(pos, pos);
        };

        // Already on a metadata comment — just open the editor for it.
        if (findMetadataCommentBlock([curText], 0)) {
            revealPanel();
            return;
        }

        // A metadata comment already sits directly above — move onto it.
        if (curLine > 0 && findMetadataCommentBlock([doc.lineAt(curLine - 1).text], 0)) {
            moveCursorTo(curLine - 1);
            revealPanel();
            return;
        }

        // On a definition, the panel shows a virtual block from real highlighter
        // results (correct params) and Apply creates the comment — no seeding, so
        // definition line numbers stay valid.
        const resolve = buildDefinitionResolver(
            definitionsService.getCachedDefinitions(doc.uri.toString())
            ?? { functions: [], macros: [], variables: [], customUnits: [] });
        if (resolve(curLine)) {
            revealPanel();
            return;
        }

        // Otherwise seed an empty comment so settings/lint markers can be added on
        // a non-definition line.
        const indent = (curText.match(/^[ \t]*/)?.[0]) ?? '';
        const newLineText = serializeMetadataComment({}, indent, '');
        await editor.edit(edit => {
            edit.insert(new vscode.Position(curLine, 0), newLineText + '\n');
        });
        // The inserted comment now occupies the original line index.
        moveCursorTo(curLine);
        revealPanel();
    });


    // All three take an optional variant, so the sidebar's Export tab can ask for the
    // preview / input-form / unwrapped rendering through the same commands. Invoked from
    // a menu or the palette there is no argument, and the report is what you get.
    const printToPdfCommand = vscode.commands.registerCommand('vscode-calcpad.printToPdf', (variant?: ExportVariant) => {
        runPdfExportCommand(variant ?? 'report');
    });

    const saveSourceHtmlCommand = vscode.commands.registerCommand('vscode-calcpad.saveSourceHtml', (variant?: ExportVariant) => {
        saveSourceHtml(variant ?? 'report');
    });

    const saveDocxCommand = vscode.commands.registerCommand('vscode-calcpad.saveDocx', (variant?: ExportVariant) => {
        saveDocx(variant ?? 'report');
    });

    const saveAsCompiledCommand = vscode.commands.registerCommand('vscode-calcpad.saveAsCompiled', () => {
        saveAsCompiled();
    });

    const exportPortableCommand = vscode.commands.registerCommand('vscode-calcpad.exportPortable', () => {
        exportPortable();
    });

    // The compiled-worksheet editor is a custom editor, so the workbench's own Save
    // drives it. This gives the Vue panel and the palette a way in as well.
    const saveCompiledUiValuesCommand = vscode.commands.registerCommand('vscode-calcpad.saveCompiledUiValues', () =>
        vscode.commands.executeCommand('workbench.action.files.save'));

    // Readonly virtual document provider for viewing webview source HTML
    let webviewSourceHtml = '';
    const webviewSourceProvider = new class implements vscode.TextDocumentContentProvider {
        onDidChangeEmitter = new vscode.EventEmitter<vscode.Uri>();
        onDidChange = this.onDidChangeEmitter.event;
        provideTextDocumentContent() { return webviewSourceHtml; }
    };
    const webviewSourceScheme = 'calcpad-webview-source';
    const webviewSourceRegistration = vscode.workspace.registerTextDocumentContentProvider(webviewSourceScheme, webviewSourceProvider);
    const webviewSourceUri = vscode.Uri.parse(`${webviewSourceScheme}:Webview Source.html`);

    const viewWebviewSourceCommand = vscode.commands.registerCommand('vscode-calcpad.viewWebviewSource', async () => {
        const inspectPanel = (unwrappedPanel && unwrappedPanel.active ? unwrappedPanel : wrappedPanel) ?? unwrappedPanel;
        if (!inspectPanel) {
            vscode.window.showWarningMessage('No active CalcpadCE preview to inspect.');
            return;
        }
        webviewSourceHtml = lastRenderedHtml(inspectPanel) ?? inspectPanel.webview.html;
        webviewSourceProvider.onDidChangeEmitter.fire(webviewSourceUri);
        const doc = await vscode.workspace.openTextDocument(webviewSourceUri);
        await vscode.window.showTextDocument(doc, vscode.ViewColumn.Beside, true);
    });

    const refreshVariablesCommand = vscode.commands.registerCommand('calcpad.refreshVariables', async () => {
        const activeEditor = vscode.window.activeTextEditor;
        if (activeEditor) {
            await processDocument(activeEditor.document);
        }
    });

    const installJuliaMonoDisposable = vscode.commands.registerCommand(
        'vscode-calcpad.installJuliaMono',
        () => installJuliaMonoCommand(context)
    );

    // Fire-and-forget: prompts the user once, skipped if already installed.
    void maybePromptInstall(context);

    const stopServerCommand = vscode.commands.registerCommand('calcpad.stopServer', async () => {
        outputChannel.appendLine('[Stop] Manual server stop triggered');
        if (!serverManager) {
            outputChannel.appendLine('[Stop] No serverManager available');
            vscode.window.showInformationMessage('CalcpadCE server is not configured.');
            return;
        }
        // Don't gate on `isRunning` — that flag only reflects whether *this*
        // VS Code window owns or has connected to the server. A peer window
        // may have spawned it (or this window may have been opened after the
        // server was already alive). serverManager.stop() handles the
        // lock-file fallback: it reads {basePath}/bin/.calcpad-server.lock
        // and kills the recorded PID even when there's no in-process child
        // reference. Without this, Linux users hit "server is not running"
        // and the lock-held server keeps going.
        const wasRunning = serverManager.isRunning;
        try {
            await serverManager.stop();
            outputChannel.appendLine(`[Stop] Server stopped successfully (wasRunning=${wasRunning})`);
            vscode.window.showInformationMessage(
                wasRunning
                    ? 'CalcpadCE server stopped. Use the refresh button to restart.'
                    : 'CalcpadCE server stopped via lock file. Use the refresh button to restart.',
            );
        } catch (err) {
            const msg = err instanceof Error ? err.message : String(err);
            outputChannel.appendLine(`[Stop] Server stop failed: ${msg}`);
            vscode.window.showErrorMessage(`CalcpadCE: Failed to stop server: ${msg}`);
        }
    });

    const refreshDocumentCommand = vscode.commands.registerCommand('calcpad.refreshDocument', async () => {
        outputChannel.appendLine('[Refresh] Manual document refresh triggered');

        // Check server health and restart if down
        if (serverManager && !serverManager.isRunning) {
            outputChannel.appendLine('[Refresh] Server is down, attempting restart...');
            try {
                await serverManager.restart();
                const serverUrl = serverManager.getBaseUrl();
                settingsManager.setLocalServerUrl(serverUrl);
                apiClient.setBaseUrl(serverUrl);
                apiClient.setAuthToken(serverManager.getAuthToken());
                outputChannel.appendLine(`[Refresh] Server restarted at ${serverUrl}`);
            } catch (err) {
                const msg = err instanceof Error ? err.message : String(err);
                outputChannel.appendLine(`[Refresh] Server restart failed: ${msg}`);
                const blocked = /Windows blocked the executable|EACCES|EPERM/i.test(msg);
                if (blocked) {
                    const exePath = serverManager.getExecutablePath();
                    vscode.window.showErrorMessage(
                        `CalcPad: Windows is still blocking Calcpad.Server.exe.\n${exePath}\n` +
                        'Right-click the file in Windows Explorer → Properties → check "Unblock", ' +
                        'then click refresh again.'
                    );
                } else {
                    vscode.window.showErrorMessage(`CalcpadCE: Server restart failed: ${msg}`);
                }
                return;
            }
        } else if (serverManager) {
            // Server thinks it's running — verify with health check
            const healthy = await apiClient.checkHealth();
            if (!healthy) {
                outputChannel.appendLine('[Refresh] Server health check failed, restarting...');
                try {
                    await serverManager.restart();
                    const serverUrl = serverManager.getBaseUrl();
                    settingsManager.setLocalServerUrl(serverUrl);
                    apiClient.setBaseUrl(serverUrl);
                    apiClient.setAuthToken(serverManager.getAuthToken());
                    outputChannel.appendLine(`[Refresh] Server restarted at ${serverUrl}`);
                } catch (err) {
                    const msg = err instanceof Error ? err.message : String(err);
                    outputChannel.appendLine(`[Refresh] Server restart failed: ${msg}`);
                    vscode.window.showErrorMessage(`CalcpadCE: Server restart failed: ${msg}`);
                    return;
                }
            }
        }

        const activeEditor = vscode.window.activeTextEditor;
        if (activeEditor) {
            // Re-lint and refresh definitions
            await processDocument(activeEditor.document);
            // Re-highlight (semantic tokens)
            semanticTokensProvider.refresh();
            // Re-render preview panels. This is the manual "run" path used
            // when auto-run is off.
            schedulePreviewUpdate();
            outputChannel.appendLine('[Refresh] Document re-linted, re-highlighted, and preview re-rendered');
        }
    });

    const prettifyDocumentCommand = vscode.commands.registerCommand('vscode-calcpad.prettifyDocument', async () => {
        const editor = vscode.window.activeTextEditor;
        if (!editor) {
            vscode.window.showInformationMessage('CalcpadCE: open a .cpd file to prettify.');
            return;
        }
        if (editor.document.languageId !== 'calcpad' && editor.document.languageId !== 'plaintext') {
            vscode.window.showInformationMessage('CalcpadCE: prettify is only available for CalcpadCE documents.');
            return;
        }

        const settingsManager = CalcpadSettingsManager.getInstance();
        const indentStyle = settingsManager.getExtra('prettifyIndentStyle', 'tab');
        const indentSize = settingsManager.getExtraNumber('prettifyIndentSize', 4);
        const trim = settingsManager.getExtraBool('prettifyTrimTrailingWhitespace', true);
        const indentUnit = indentStyle === 'space' ? ' '.repeat(Math.max(1, indentSize)) : '\t';

        try {
            const response = await apiClient.prettify(editor.document.getText(), indentUnit, trim);
            if (!response) {
                vscode.window.showErrorMessage('CalcpadCE: prettify request failed (no response from server).');
                return;
            }
            const fullRange = new vscode.Range(
                editor.document.positionAt(0),
                editor.document.positionAt(editor.document.getText().length)
            );
            const ok = await editor.edit(eb => eb.replace(fullRange, response.content));
            if (!ok) {
                vscode.window.showErrorMessage('CalcpadCE: prettify edit was rejected by the editor.');
            }
        } catch (err) {
            const msg = err instanceof Error ? err.message : String(err);
            outputChannel.appendLine('[Prettify] Error: ' + msg);
            vscode.window.showErrorMessage('CalcpadCE: prettify failed — ' + msg);
        }
    });

    const exportToPdfCommand = vscode.commands.registerCommand('vscode-calcpad.exportToPdf', (variant?: ExportVariant) => {
        runPdfExportCommand(variant ?? 'report');
    });


    // Process document on open
    const onDidOpenTextDocument = vscode.workspace.onDidOpenTextDocument(document => {
        processDocument(document).catch(e => outputChannel.appendLine('[processDocument] Error: ' + e));
    });

    // Process document on save
    const onDidSaveTextDocument = vscode.workspace.onDidSaveTextDocument(document => {
        processDocument(document).catch(e => outputChannel.appendLine('[processDocument] Error: ' + e));
    });

    // Lint on document change (with debouncing)
    let lintTimeout: NodeJS.Timeout | unknown = undefined;
    const onDidChangeTextDocument = vscode.workspace.onDidChangeTextDocument(event => {
        if (event.document.languageId === 'calcpad' || event.document.languageId === 'plaintext') {
            if (lintTimeout) {
                clearTimeout(lintTimeout as NodeJS.Timeout);
            }
            lintTimeout = setTimeout(() => {
                invalidateUiControls(event.document);
                processDocument(event.document).catch(e => outputChannel.appendLine('[processDocument] Error: ' + e));
            }, 500);
            // Only schedule preview update when auto-run is on. Otherwise the
            // preview updates only when the panel is (re)opened or the user
            // runs `calcpad.refreshDocument`.
            if (CalcpadSettingsManager.getInstance().getExtraBool('autoRun', true)) {
                schedulePreviewUpdate();
            }
        }
    });

    // A compiled worksheet is a custom editor, not a text one, so activating or closing
    // its tab never reaches onDidChangeActiveTextEditor — the tab events are what tell
    // the side panel a form has taken over.
    const onDidChangeTabs = vscode.window.tabGroups.onDidChangeTabs(() => syncInputMode());
    const onDidChangeTabGroups = vscode.window.tabGroups.onDidChangeTabGroups(() => syncInputMode());

    // Update preview and variables when active editor changes
    const onDidChangeActiveTextEditor = vscode.window.onDidChangeActiveTextEditor(async editor => {
        updateMetadataContext(editor);
        // A text editor taking over is what ends a compiled worksheet's claim on the
        // export commands; a webview taking focus is not, which is the point of both
        // this and previewSourceEditor.
        if (editor) compiledSourceUri = undefined;
        if (editor && (editor.document.languageId === 'calcpad' || editor.document.languageId === 'plaintext')) {
            // The input form follows the active editor, so switching documents takes
            // the form's values with it. Prompt as if the form were closing, since
            // for the outgoing document it is, and drop them so the incoming one is
            // seeded from its own uiOverrides comment. There is no veto for an editor
            // switch that has already happened, so Cancel switches back instead —
            // uiPanelDocKey still points at the outgoing document, which keeps the
            // resulting second pass through here from prompting again.
            if (uiPanel && uiPanelDocKey && uiPanelDocKey !== editor.document.uri.toString()) {
                const outgoing = uiPanelDocKey;
                if (!await discardUiValues(outgoing, true)) {
                    const document = vscode.workspace.textDocuments.find(d => d.uri.toString() === outgoing);
                    if (document)
                        await vscode.window.showTextDocument(document, { viewColumn: editor.viewColumn, preview: false });
                    return;
                }
                uiPanelDocKey = undefined;
            }
            // Update preview if any panel is open
            if (wrappedPanel || unwrappedPanel || uiPanel || uiReportPanel) {
                schedulePreviewUpdate();
            }
            // Update Variables tab
            processDocument(editor.document).catch(e => outputChannel.appendLine('[processDocument] Error: ' + e));
            // Last, and after the cancel path above returns: a switch the user backed out of
            // must not open anything.
            await maybeAutoEnterInputMode(editor.document);
        }
    });

    // Auto-sync the preview to the cursor's source line (gated on the setting).
    let cursorSyncTimeout: NodeJS.Timeout | undefined;
    let metadataContextTimeout: NodeJS.Timeout | undefined;
    const onDidChangeTextEditorSelection = vscode.window.onDidChangeTextEditorSelection(event => {
        const doc = event.textEditor.document;
        if (doc.languageId !== 'calcpad' && doc.languageId !== 'plaintext') return;

        if (metadataContextTimeout) clearTimeout(metadataContextTimeout);
        metadataContextTimeout = setTimeout(() => updateMetadataContext(event.textEditor), 150);

        if (!wrappedPanel && !unwrappedPanel && !uiPanel && !uiReportPanel) return;
        if (!CalcpadSettingsManager.getInstance().getExtraBool('previewCursorSync', false)) return;
        if (cursorSyncTimeout) clearTimeout(cursorSyncTimeout);
        cursorSyncTimeout = setTimeout(() => {
            postPreviewSourceLine(event.selections[0].active.line + 1);
        }, 150);
    });

    // Refresh all components when calcpad settings change
    const onDidChangeConfiguration = vscode.workspace.onDidChangeConfiguration(async event => {
        // Check if any calcpad settings changed
        if (event.affectsConfiguration('calcpad')) {
            outputChannel.appendLine('[Settings] Calcpad settings changed - triggering refresh');
            await refreshAllComponents();
        }
    });

    // Process all open calcpad documents on activation. Activation happens after the
    // document that triggered it is already active, so no active-editor event follows for it:
    // the one in front is judged for input mode here instead. The rest are marked as judged
    // without opening anything, so a restored session with eight worksheets does not come up
    // with eight forms.
    {
        const activeDocument = vscode.window.activeTextEditor?.document;
        for (const document of vscode.workspace.textDocuments) {
            if (document !== activeDocument) autoUiConsidered.add(document.uri.toString());
            processDocument(document);
        }
        if (activeDocument) void maybeAutoEnterInputMode(activeDocument);
    }

        outputChannel.appendLine('Registering subscriptions...');
        context.subscriptions.push(
            disposable,
            previewCommand,
            previewUnwrappedCommand,
            toggleUiModeCommand,
            toggleUiReportCommand,
            saveUiValuesCommand,
            focusPreviewToLineCommand,
            onDidChangeTextEditorSelection,
            showInsertCommand,
            editMetadataPropertiesCommand,
            printToPdfCommand,
            saveSourceHtmlCommand,
            saveDocxCommand,
            saveAsCompiledCommand,
            exportPortableCommand,
            saveCompiledUiValuesCommand,
            compiledEditor,
            refreshVariablesCommand,
            refreshDocumentCommand,
            stopServerCommand,
            exportToPdfCommand,
            prettifyDocumentCommand,
            vueUiProviderDisposable,
            vueUiProvider,
            linter,
            semanticTokensDisposable,
            outputChannel,
            serverDebugChannel,
            onDidChangeTextDocument,
            onDidOpenTextDocument,
            onDidSaveTextDocument,
            onDidChangeActiveTextEditor,
            onDidChangeTabs,
            onDidChangeTabGroups,
            onDidChangeConfiguration,
            operatorReplacerDisposable,
            quickTyperDisposable,
            autoIndenterDisposable,
            imagePasteDisposable,
            imageInsertCommandDisposable,
            ...commentFormatterDisposables,
            completionProviderDisposable,
            includeCompletionDisposable,
            definitionProviderDisposable,
            includeLinkProviderDisposable,
            referenceProviderDisposable,
            renameProviderDisposable,
            hoverProviderDisposable,
            insertManager,
            viewWebviewSourceCommand,
            webviewSourceRegistration,
            installJuliaMonoDisposable
        );
        
        outputChannel.appendLine('CalcPad extension activation completed successfully');
        
    } catch (error) {
        console.error('CalcPad extension activation failed:', error);
        if (outputChannel) {
            outputChannel.appendLine(`FATAL ERROR during activation: ${error}`);
        }
        // Still try to show the error to user
        vscode.window.showErrorMessage(`CalcpadCE extension failed to activate: ${error instanceof Error ? error.message : 'Unknown error'}`);
        throw error; // Re-throw to mark extension as failed
    }
}

export async function deactivate() {
    if (serverManager) {
        // Leave the server running for other VS Code instances (option C).
        // Use the `CalcPad: Stop Server` command to actually kill it.
        serverManager.disconnect();
    }
    if (linter) {
        linter.dispose();
    }
}