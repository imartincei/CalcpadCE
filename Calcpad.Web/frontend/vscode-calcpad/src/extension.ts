import * as vscode from 'vscode';
import * as path from 'path';
import { CalcpadApiClient, DEFAULT_PDF_SETTINGS, parseConvertErrorHeader, findMetadataCommentBlock, serializeMetadataComment, computeMetadataBlock, buildDefinitionResolver, extractBodyHtml, UiOverrideStore, writeUiOverrides, variantRender } from 'calcpad-frontend';
import type { PdfSettings as FrontendPdfSettings, ExportVariant } from 'calcpad-frontend';
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
import { CommentFormatter } from './commentFormatter';
import { CalcpadServerManager } from './calcpadServerManager';
import { DotnetRuntimeManager } from './dotnetRuntimeManager';
import { VSCodeLogger, VSCodeFileSystem } from './adapters';
import { installJuliaMonoCommand, maybePromptInstall } from './installFont';

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
// Document the input form is currently showing, so its values can be prompted
// about and dropped when the form closes.
let uiPanelDocKey: string | undefined = undefined;

type PreviewKind = 'regular' | 'unwrapped' | 'ui' | 'uiReport';

function previewPanelFor(kind: PreviewKind): vscode.WebviewPanel | undefined {
    switch (kind) {
        case 'regular': return wrappedPanel;
        case 'unwrapped': return unwrappedPanel;
        case 'ui': return uiPanel;
        default: return uiReportPanel;
    }
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
}

/**
 * A "Calculating…" page shown in the preview webview during long renders,
 * mirroring the spinner overlay in calcpad-web.
 */
function getPreviewLoadingHtml(): string {
    return `<!DOCTYPE html>
<html>
<head>
    <meta charset="UTF-8">
    <title>Calculating</title>
    <style>
        html, body { height: 100%; margin: 0; }
        body {
            display: flex;
            flex-direction: column;
            align-items: center;
            justify-content: center;
            gap: 14px;
            background: #fff;
            color: #333;
            font: 14px/1.4 var(--vscode-font-family, 'Segoe UI', sans-serif);
        }
        .preview-spinner {
            width: 36px;
            height: 36px;
            border: 3px solid rgba(0, 0, 0, 0.15);
            border-top-color: #0078d4;
            border-radius: 50%;
            animation: preview-spin 0.8s linear infinite;
        }
        @keyframes preview-spin { to { transform: rotate(360deg); } }
    </style>
</head>
<body>
    <div class="preview-spinner"></div>
    <span>Calculating…</span>
</body>
</html>`;
}

/**
 * Shows the loading page in the preview webview only if the render outlasts
 * PREVIEW_BUSY_DELAY_MS, so fast renders never flash it (mirrors Calcpad.Wpf's
 * delayed spinner). Returns a function that must be called when the render ends.
 */
function showPreviewLoading(panel: vscode.WebviewPanel): () => void {
    const timer = setTimeout(() => {
        panel.webview.html = getPreviewLoadingHtml();
    }, PREVIEW_BUSY_DELAY_MS);
    let ended = false;
    return () => {
        if (ended) return;
        ended = true;
        clearTimeout(timer);
    };
}

// Extends the shared PdfSettings with additional server-side fields
interface FullPdfSettings extends FrontendPdfSettings {
    enableHeader: boolean;
    documentSubtitle: string;
    headerCenter: string;
    author: string;
    enableFooter: boolean;
    footerCenter: string;
    company: string;
    project: string;
    showPageNumbers: boolean;
    orientation: string;
    printBackground: boolean;
    scale: number;
    headerTemplate: string;
    footerTemplate: string;
    backgroundSvgPath: string;
}


function getPdfSettings(): FullPdfSettings {
    const settingsManager = CalcpadSettingsManager.getInstance();
    const stored = settingsManager.getExtraObject('pdfSettings', {} as Partial<FullPdfSettings>);
    const activeEditor = vscode.window.activeTextEditor;

    const fileName = activeEditor
        ? path.basename(activeEditor.document.fileName, path.extname(activeEditor.document.fileName))
        : 'CalcpadCE Document';

    return {
        // User-configurable settings (defaults from shared module)
        format: stored.format ?? DEFAULT_PDF_SETTINGS.format,
        marginTop: stored.marginTop ?? DEFAULT_PDF_SETTINGS.marginTop,
        marginBottom: stored.marginBottom ?? DEFAULT_PDF_SETTINGS.marginBottom,
        marginLeft: stored.marginLeft ?? DEFAULT_PDF_SETTINGS.marginLeft,
        marginRight: stored.marginRight ?? DEFAULT_PDF_SETTINGS.marginRight,
        documentTitle: stored.documentTitle || fileName,
        dateTimeFormat: stored.dateTimeFormat ?? DEFAULT_PDF_SETTINGS.dateTimeFormat,

        // Hardcoded defaults (to be re-exposed in UI later)
        enableHeader: true,
        documentSubtitle: '',
        headerCenter: '',
        author: '',
        enableFooter: true,
        footerCenter: '',
        company: '',
        project: '',
        showPageNumbers: true,
        orientation: 'portrait',
        printBackground: true,
        scale: 1.0,
        headerTemplate: 'default',
        footerTemplate: 'default',
        backgroundSvgPath: ''
    };
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
 * Scan HTML for <img src="..."> tags with local file paths, read the files from disk,
 * and return a cache mapping original src values to base64 data URIs.
 */
async function buildImageCache(html: string, documentDir: string): Promise<Record<string, string>> {
    const cache: Record<string, string> = {};
    const imgSrcRegex = /<img\s[^>]*?src\s*=\s*["']([^"']+)["'][^>]*>/gi;
    let match;

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

        try {
            const absolutePath = path.resolve(documentDir, src);
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

            outputChannel.appendLine(`[IMAGE CACHE] Cached: ${src} (${imageData.length} bytes)`);
        } catch (error) {
            outputChannel.appendLine(`[IMAGE CACHE] Could not read image: ${src} (${error instanceof Error ? error.message : 'Unknown error'})`);
        }
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
 * Generate a <script> block that replaces local image src attributes
 * with cached base64 data URIs on DOMContentLoaded.
 */
function getImageCacheScript(imageCache: Record<string, string>): string {
    if (Object.keys(imageCache).length === 0) {
        return '';
    }

    const cacheJson = JSON.stringify(imageCache);
    return `
        <script>
            (function() {
                const imageCache = ${cacheJson};
                document.addEventListener('DOMContentLoaded', function() {
                    const images = document.querySelectorAll('img');
                    images.forEach(function(img) {
                        const src = img.getAttribute('src');
                        if (src && imageCache[src]) {
                            img.src = imageCache[src];
                        }
                    });
                });
            })();
        </script>
    `;
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
function sanitizeServerHtml(html: string): string {
    const bodyOpen = html.indexOf('<body');
    const bodyClose = html.lastIndexOf('</body>');
    if (bodyOpen === -1 || bodyClose === -1) return html;

    const bodyStart = html.indexOf('>', bodyOpen) + 1;
    const body = html.substring(bodyStart, bodyClose);
    const sanitized = body.replace(/(<!--[\s\S]*?-->)|(<script[\s\S]*?<\/script>)|(<style[\s\S]*?<\/style>)|(<\/?[a-zA-Z][^<>]*>)|(<)/g,
        (_match, comment, script, style, tag) => comment ?? script ?? style ?? tag ?? '&lt;');

    return html.substring(0, bodyStart) + sanitized + html.substring(bodyClose);
}

function getVsCodeApiInitScript(): string {
    // acquireVsCodeApi may only be called once per webview, so publish the handle
    // for the backend's #UI event script rather than letting it acquire its own.
    return `<script>const vscode = acquireVsCodeApi(); window.__calcpadVsCode = vscode;</script>`;
}

function getErrorNavigationScript(): string {
    return `
        <script>

            // Intercept console methods and send to VS Code
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

                    vscode.postMessage({
                        type: 'consoleMessage',
                        level: level,
                        message: message
                    });
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
                vscode.postMessage({ type: 'consoleMessage', level: 'error', message: '[Uncaught] ' + detail + ' (' + lineno + ':' + colno + ')' });
            };
            window.onunhandledrejection = function(event) {
                const reason = event.reason;
                const detail = reason ? (reason.message || String(reason)) : String(reason);
                vscode.postMessage({ type: 'consoleMessage', level: 'error', message: '[Unhandled Rejection] ' + detail });
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
                            vscode.postMessage({
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
 */
function getLineLinkScript(scrollToLine?: number): string {
    const scrollTarget = typeof scrollToLine === 'number' ? String(scrollToLine) : 'null';
    return `
        <script>
            document.addEventListener('DOMContentLoaded', function() {
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
                        vscode.postMessage({ type: 'navigateToLine', line: parseInt(src, 10), lineType: 'source' });
                    });
                    el.appendChild(link);
                    el.addEventListener('mouseenter', function() {
                        hideAllLineLinks();
                        link.style.display = 'inline-block';
                    });
                });
                window.addEventListener('scroll', hideAllLineLinks);

                // Error-summary chips: scroll the preview to the referenced output line.
                document.querySelectorAll('.roundBox').forEach(function(box) {
                    box.addEventListener('click', function() {
                        var line = box.getAttribute('data-line');
                        var target = line && document.getElementById('line-' + line);
                        if (target) target.scrollIntoView({ block: 'start' });
                    });
                });

                var scrollToLine = ${scrollTarget};
                if (scrollToLine !== null) {
                    var target = document.getElementById('line-' + scrollToLine);
                    if (target) target.scrollIntoView({ block: 'center' });
                }

                // Editor -> preview sync. The extension posts
                // { type: 'scrollToSourceLine', line } (a source line) on cursor
                // move (when auto-sync is on) or via the 'Focus Preview to Line'
                // command. Match data-source-line first (wrapped view), then the
                // code view's line-num anchors, falling back to the nearest
                // preceding source line so blank/continuation lines still resolve.
                var focusTimer = null;
                function focusPreviewLine(line) {
                    if (typeof line !== 'number' || isNaN(line)) return;
                    var target = document.querySelector('[data-source-line="' + line + '"]');
                    if (!target) {
                        var anchor = document.querySelector('a.line-num[data-text="' + line + '"]');
                        if (anchor) target = anchor.closest('.line-text') || anchor;
                    }
                    if (!target) {
                        var best = null, bestSrc = -1;
                        document.querySelectorAll('[data-source-line]').forEach(function(el) {
                            var s = parseInt(el.getAttribute('data-source-line'), 10);
                            if (!isNaN(s) && s <= line && s > bestSrc) { bestSrc = s; best = el; }
                        });
                        target = best;
                    }
                    if (!target) return;
                    target.scrollIntoView({ block: 'center' });
                    document.querySelectorAll('.cpd-line-focus').forEach(function(el) { el.classList.remove('cpd-line-focus'); });
                    target.classList.add('cpd-line-focus');
                    if (focusTimer) clearTimeout(focusTimer);
                    focusTimer = setTimeout(function() { target.classList.remove('cpd-line-focus'); }, 1200);
                }
                window.addEventListener('message', function(e) {
                    var d = e.data;
                    if (d && d.type === 'scrollToSourceLine') focusPreviewLine(d.line);
                });
            });
        </script>
    `;
}

async function updatePreviewContent(panel: vscode.WebviewPanel, content: string, sourceFileUri: vscode.Uri, unwrapped: boolean = false, scrollToLine?: number, enableUi: boolean = false, forPrint: boolean = false) {
    const docKey = sourceFileUri.toString();
    if ((enableUi || forPrint) && !uiOverrides.has(docKey) && !uiOverridesDirty.has(docKey))
        uiOverrides.readFromSource(docKey, content);
    if (enableUi) uiPanelDocKey = docKey;

    const mode = enableUi ? 'ui' : forPrint ? 'report' : unwrapped ? 'unwrapped' : 'wrapped';
    outputChannel.appendLine(`Starting updatePreviewContent (${mode})...`);
    outputChannel.appendLine(`Content length: ${content.length} characters`);

    // Update panel title with current file name
    const activeEditor = vscode.window.activeTextEditor ?? previewSourceEditor;
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
        const response = await fetch(`${apiBaseUrl}${endpoint}`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                content: content,
                settings: settings,
                theme: theme,
                forceUnwrappedCode: unwrapped,
                sourceFilePath: sourceFileUri.fsPath,
                enableUi: enableUi,
                forPrint: forPrint,
                // The plain preview shows the document as written; entered values
                // belong to the input form and the report that accompanies it.
                uiOverrides: enableUi || forPrint ? uiOverrides.toRecord(docKey) : undefined,
                // The report is a print layout, but on screen beside the editor, so it
                // keeps the line links that forPrint would otherwise suppress.
                includeLineAnchors: forPrint ? true : undefined
            }),
            signal: AbortSignal.timeout(10000)
        });
        if (!response.ok) {
            throw new Error(`Server returned ${response.status}`);
        }
        outputChannel.appendLine('API call successful');

        vueUiProvider?.updateConvertErrors(parseConvertErrorHeader(response));

        // Use the entire API response as the webview HTML
        const apiResponse = await response.text();

        // Share the freshly-converted HTML with the Vue side panel so the
        // Export tab can extract plot bytes without another server round-trip.
        vueUiProvider?.setCachedHtml(apiResponse);

        // Log to dedicated HTML output channel (without stealing focus)
        calcpadOutputHtmlChannel.clear();
        calcpadOutputHtmlChannel.appendLine(extractBodyHtml(apiResponse));

        outputChannel.appendLine(`HTML Length: ${apiResponse.length} characters`);

        // Build image cache: read local image files and convert to base64 data URIs
        let imageCacheScript = '';
        if (activeEditor && !activeEditor.document.isUntitled) {
            const documentDir = path.dirname(activeEditor.document.uri.fsPath);
            const imageCache = await buildImageCache(apiResponse, documentDir);
            imageCacheScript = getImageCacheScript(imageCache);
        }

        // Inject JavaScript for error link navigation and console interception
        const errorNavigationScript = getErrorNavigationScript();

        // Visible, styled vertical scrollbar for the preview
        const scrollbarStyleScript = getScrollbarStyleScript();

        // Hover line links + roundBox scroll + optional scroll-to-line target
        const lineLinkScript = getLineLinkScript(scrollToLine);

        // Override VS Code's injected theme to match the selected preview theme
        const themeOverrideScript = getThemeOverrideScript(theme);

        // Sanitize server HTML to escape stray '<' that aren't part of valid tags
        const sanitizedResponse = sanitizeServerHtml(apiResponse);

        // Inject console interception + error nav + scrollbar style at end of <head> so it runs before any user scripts in body
        const vsCodeApiInit = getVsCodeApiInitScript();
        let htmlWithScript = sanitizedResponse.replace('</head>', vsCodeApiInit + errorNavigationScript + scrollbarStyleScript + '</head>');
        // Inject image cache + theme override + line links before closing body tag
        htmlWithScript = htmlWithScript.replace('</body>', imageCacheScript + themeOverrideScript + lineLinkScript + '</body>');

        panel.webview.html = htmlWithScript;

        outputChannel.appendLine('Webview HTML set directly');

    } catch (error) {
        outputChannel.appendLine(`ERROR in updatePreviewContent: ${error instanceof Error ? error.message : 'Unknown error'}`);
        const settingsManager = CalcpadSettingsManager.getInstance(extensionContext);
        const errorApiBaseUrl = settingsManager.getServerUrl();
        const endpoint = unwrapped ? 'convert?unwrap=true' : 'convert';
        const errorHtml = `
            <!DOCTYPE html>
            <html>
            <head>
                <meta charset="UTF-8">
                <title>CalcpadCE Preview Error</title>
            </head>
            <body>
                <div style="color: #d32f2f; background: #ffebee; padding: 15px; border-radius: 4px; margin: 20px;">
                    <h3>Preview Error${unwrapped ? ' (Unwrapped)' : ''}</h3>
                    <p>${error instanceof Error ? error.message : 'Unknown error'}</p>
                    <p>Server URL: ${errorApiBaseUrl}/api/calcpad/${endpoint}</p>
                </div>
            </body>
            </html>
        `;

        panel.webview.html = errorHtml;
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

    const response = await fetch(`${apiBaseUrl}${endpoint}`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
            content: documentContent,
            settings: settings,
            sourceFilePath: sourceFileUri.fsPath,
            forPrint: render.forPrint,
            enableUi: render.enableUi,
            uiOverrides: render.useOverrides ? uiOverrides.toRecord(sourceFileUri.toString()) : undefined,
            includeLineAnchors: false,
        }),
        signal: AbortSignal.timeout(30000)
    });
    if (!response.ok) {
        throw new Error(`Server returned ${response.status}`);
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
    const imageCache = await buildImageCache(html, sourceDir);
    html = applyImageCache(html, imageCache);

    progress?.report({ increment: 50, message: 'Generating PDF...' });

    // Step 2: Generate PDF from HTML
    const pdfResponse = await fetch(`${apiBaseUrl}/api/calcpad/pdf`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
            html: html,
            options: getPdfSettings()
        }),
        signal: AbortSignal.timeout(60000)
    });
    if (!pdfResponse.ok) {
        throw new Error(`PDF generation failed: ${pdfResponse.status}`);
    }

    progress?.report({ increment: 80, message: 'Saving PDF file...' });

    const pdfBuffer = await pdfResponse.arrayBuffer();
    await vscode.workspace.fs.writeFile(saveUri, new Uint8Array(pdfBuffer));

    progress?.report({ increment: 100, message: 'PDF generation complete!' });
}

/**
 * Editor → save dialog → generate → "Open PDF" prompt. Shared entry point for
 * <c>vscode-calcpad.exportToPdf</c> and <c>vscode-calcpad.printToPdf</c>, which
 * both produce the report; the Export tab passes other variants through.
 */
async function runPdfExportCommand(variant: ExportVariant = 'report'): Promise<void> {
    // The Print Report button lives on the report and input panels' title bars, so the
    // webview holds focus and there is no active *text* editor — fall back to the editor
    // the previews were opened from.
    const activeEditor = vscode.window.activeTextEditor ?? previewSourceEditor;
    if (!activeEditor) {
        vscode.window.showErrorMessage('No active CalcpadCE document found');
        return;
    }

    // Default save location: same dir as the .cpd, with .pdf extension.
    const currentDir = path.dirname(activeEditor.document.fileName);
    const baseFilename = path.basename(activeEditor.document.fileName, path.extname(activeEditor.document.fileName));
    const defaultPath = path.join(currentDir, baseFilename + '.pdf');

    const saveUri = await vscode.window.showSaveDialog({
        defaultUri: vscode.Uri.file(defaultPath),
        filters: { 'PDF Files': ['pdf'] }
    });
    if (!saveUri) return;

    try {
        await vscode.window.withProgress({
            location: vscode.ProgressLocation.Notification,
            title: 'Generating PDF...',
            cancellable: false
        }, async progress => {
            progress.report({ increment: 0, message: 'Starting PDF generation...' });
            await generatePdfToFile(
                activeEditor.document.getText(),
                activeEditor.document.uri,
                saveUri,
                variant,
                progress
            );
        });

        const openChoice = await vscode.window.showInformationMessage(
            `PDF saved to ${saveUri.fsPath}`,
            'Open PDF'
        );
        if (openChoice === 'Open PDF') {
            vscode.env.openExternal(saveUri);
        }
    } catch (error) {
        const msg = error instanceof Error ? error.message : String(error);
        outputChannel.appendLine(`[PDF] ${msg}`);
        vscode.window.showErrorMessage(`Failed to generate PDF: ${msg}`);
    }
}

/**
 * Convert the active CalcPad document to HTML on the server, then save
 * the result via a native Save dialog. Used by the Export tab's
 * "Save HTML…" buttons, which pick the variant.
 */
async function saveSourceHtml(variant: ExportVariant = 'report') {
    const activeEditor = vscode.window.activeTextEditor ?? previewSourceEditor;
    if (!activeEditor) {
        vscode.window.showErrorMessage('No active CalcpadCE document found');
        return;
    }
    try {
        const currentDir = path.dirname(activeEditor.document.fileName);
        const baseFilename = path.basename(activeEditor.document.fileName, path.extname(activeEditor.document.fileName));
        const defaultPath = path.join(currentDir, baseFilename + '.html');
        const saveUri = await vscode.window.showSaveDialog({
            defaultUri: vscode.Uri.file(defaultPath),
            filters: { 'HTML Files': ['html', 'htm'] },
        });
        if (!saveUri) return;

        await vscode.window.withProgress({
            location: vscode.ProgressLocation.Notification,
            title: 'Generating HTML…',
            cancellable: false,
        }, async () => {
            const html = await renderForExport(
                activeEditor.document.getText(), activeEditor.document.uri, variant);
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
    const activeEditor = vscode.window.activeTextEditor ?? previewSourceEditor;
    if (!activeEditor) {
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

        const currentDir = path.dirname(activeEditor.document.fileName);
        const baseFilename = path.basename(activeEditor.document.fileName, path.extname(activeEditor.document.fileName));
        const defaultPath = path.join(currentDir, baseFilename + '.docx');
        const saveUri = await vscode.window.showSaveDialog({
            defaultUri: vscode.Uri.file(defaultPath),
            filters: { 'Word Documents': ['docx'] },
        });
        if (!saveUri) return;

        await vscode.window.withProgress({
            location: vscode.ProgressLocation.Notification,
            title: 'Generating Word document…',
            cancellable: false,
        }, async () => {
            const settings = await settingsManager.getApiSettings();
            const documentContent = activeEditor.document.getText();

            const response = await fetch(`${apiBaseUrl}/api/calcpad/docx`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    content: documentContent,
                    settings,
                    sourceFilePath: activeEditor.document.uri.fsPath,
                    forPrint: render.forPrint,
                    uiOverrides: render.useOverrides
                        ? uiOverrides.toRecord(activeEditor.document.uri.toString())
                        : undefined,
                }),
                signal: AbortSignal.timeout(60000),
            });
            if (!response.ok) {
                throw new Error(`Server returned ${response.status}`);
            }
            const buf = await response.arrayBuffer();
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
// same source line works for the preview, unwrapped and report panels.
function postPreviewSourceLine(line: number) {
    const msg = { type: 'scrollToSourceLine', line };
    wrappedPanel?.webview.postMessage(msg);
    unwrappedPanel?.webview.postMessage(msg);
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
async function showPreview(kind: PreviewKind, scrollToLine?: number) {
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
        unwrapped && wrappedPanel ? vscode.ViewColumn.Active : vscode.ViewColumn.Beside,
        {
            enableScripts: true,
            enableFindWidget: true
        }
    );

    if (kind === 'regular') {
        wrappedPanel = panel;
    } else if (kind === 'ui') {
        uiPanel = panel;
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

    panel.webview.onDidReceiveMessage(message => handlePreviewMessage(message, kind));

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
        const includeCompletionDisposable = CalcpadIncludeCompletionProvider.register(outputChannel);

        // Initialize definition provider (Go to Definition)
        outputChannel.appendLine('Initializing definition provider...');
        const definitionProviderDisposable = CalcpadDefinitionProvider.register(apiClient, outputChannel);

        // Initialize #include link provider (always-underlined, clickable paths)
        outputChannel.appendLine('Initializing include link provider...');
        const includeLinkProviderDisposable = CalcpadIncludeLinkProvider.register(outputChannel);

        // Initialize reference provider (Find All References)
        outputChannel.appendLine('Initializing reference provider...');
        const referenceProviderDisposable = CalcpadReferenceProvider.register(apiClient, outputChannel);

        // Initialize rename provider (F2 Rename Symbol)
        outputChannel.appendLine('Initializing rename provider...');
        const renameProviderDisposable = CalcpadRenameProvider.register(apiClient, outputChannel);

        // Initialize hover provider (Hover Tooltips)
        outputChannel.appendLine('Initializing hover provider...');
        const hoverProviderDisposable = CalcpadHoverProvider.register(definitionsService, insertManager, outputChannel);

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
    vueUiProvider.onPreviewThemeChanged = async () => {
        const activeEditor = vscode.window.activeTextEditor;
        if (!activeEditor) return;
        await refreshPreviewPanels(activeEditor.document);
    };
    vueUiProvider.onSettingsChanged = async () => {
        const activeEditor = vscode.window.activeTextEditor;
        if (!activeEditor) return;
        await refreshPreviewPanels(activeEditor.document);
    };
    const vueUiProviderDisposable = vscode.window.registerWebviewViewProvider(
        CalcpadVueUIProvider.viewType,
        vueUiProvider
    );

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

    const focusPreviewToLineCommand = vscode.commands.registerCommand('vscode-calcpad.focusPreviewToLine', async () => {
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
        webviewSourceHtml = inspectPanel.webview.html;
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

    // Update preview and variables when active editor changes
    const onDidChangeActiveTextEditor = vscode.window.onDidChangeActiveTextEditor(async editor => {
        updateMetadataContext(editor);
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

    // Process all open calcpad documents on activation
    vscode.workspace.textDocuments.forEach(document => processDocument(document));

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