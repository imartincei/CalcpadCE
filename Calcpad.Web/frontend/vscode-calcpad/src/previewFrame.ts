/**
 * The containment boundary for rendered worksheets. Author HTML and script go into an
 * `<iframe srcdoc>` sandboxed with `allow-scripts` and deliberately without `allow-same-origin`,
 * leaving an opaque origin whose only way out is `postMessage` to the shell. Two such frames, so
 * a render is brought forward only once it has loaded.
 */

import * as vscode from 'vscode';
import {
    parseScrollState,
    previewDiagnosticsScript,
    scrollAnchorScript,
    BACK_BUFFER_CLEAR_CHARS,
    type PreviewScrollState,
} from 'calcpad-frontend';

/**
 * What a frame reported before the render that replaced it. VS Code restores scroll only for a
 * webview's top-level document, so without this the preview snaps to the top on every keystroke.
 */
interface PreviewFrameState {
    /** Reset when the panel changes document: neither position means anything then. */
    docKey: string;
    scroll?: PreviewScrollState;
    /** The `#UI` script's focus/caret state, which it posts as `cpdUiState`. */
    uiPosition?: unknown;
}

const frameStates = new WeakMap<vscode.WebviewPanel, PreviewFrameState>();

export function frameStateFor(panel: vscode.WebviewPanel, docKey: string): PreviewFrameState {
    const held = frameStates.get(panel);
    if (held && held.docKey === docKey) return held;
    const fresh: PreviewFrameState = { docKey };
    frameStates.set(panel, fresh);
    return fresh;
}

/**
 * A panel's shell, which outlives the documents rendered into it. Assigning `webview.html` per
 * render would rebuild the frames and defeat the double buffering, so it is installed once and
 * documents are pushed in by message.
 */
interface ShellSession {
    /** Until the shell script has run, a posted message has nowhere to land. */
    ready: boolean;
    html: string | null;
    background: string;
    loading: boolean;
}

const shellSessions = new WeakMap<vscode.WebviewPanel, ShellSession>();

/** The document last pushed in, which `cpdShellReady` has to re-send anyway. */
export function lastRenderedHtml(panel: vscode.WebviewPanel): string | undefined {
    return shellSessions.get(panel)?.html ?? undefined;
}

function postShellState(panel: vscode.WebviewPanel, session: ShellSession): void {
    if (!session.ready) return;
    void panel.webview.postMessage({ type: 'cpdLoading', on: session.loading });
    if (session.html !== null) {
        void panel.webview.postMessage({
            type: 'cpdRender',
            html: session.html,
            background: session.background,
        });
    }
}

function shellFor(panel: vscode.WebviewPanel, background: string): ShellSession {
    const held = shellSessions.get(panel);
    if (held) {
        held.background = background;
        return held;
    }
    const fresh: ShellSession = { ready: false, html: null, background, loading: false };
    shellSessions.set(panel, fresh);
    // Assigning html starts the shell, which posts cpdShellReady and drains the queue.
    panel.webview.html = buildPreviewShell({ background });
    return fresh;
}

/**
 * Shows a document, installing the shell on first render. It goes into the buffer behind and comes
 * forward once loaded, so the visible frame is never mid-replacement.
 */
export function renderIntoShell(
    panel: vscode.WebviewPanel,
    documentHtml: string,
    options: ShellOptions,
): void {
    const session = shellFor(panel, options.background);
    session.html = documentHtml;
    // The render ending the wait, not the request: a superseded one finishes without a render.
    session.loading = false;
    postShellState(panel, session);
}

/** Raises or drops the "Calculating…" overlay — a page of its own would displace the shell. */
export function setShellLoading(panel: vscode.WebviewPanel, background: string, on: boolean): void {
    const session = shellFor(panel, background);
    session.loading = on;
    if (session.ready) void panel.webview.postMessage({ type: 'cpdLoading', on });
}

// What was selected when the context menu last opened, for the Copy command it runs.
let lastSelection = '';

/** Puts the selection the last right-click carried on the clipboard. */
export function copyPreviewSelection(): void {
    if (lastSelection) void vscode.env.clipboard.writeText(lastSelection);
}

/**
 * Messages a frame sends about itself rather than the document, so each panel keeps its own
 * position. Returns whether the message was one of these.
 */
export function handleFrameStateMessage(panel: vscode.WebviewPanel, message: any): boolean {
    switch (message?.type) {
        // Also fires when VS Code reloads a torn-down webview, whose frames come back empty —
        // which is why the last render is held here rather than only in the shell.
        case 'cpdShellReady': {
            const session = shellSessions.get(panel);
            if (session) {
                session.ready = true;
                postShellState(panel, session);
            }
            return true;
        }
        case 'cpdScrollState': {
            const held = frameStates.get(panel);
            const state = parseScrollState(message);
            if (held && state) held.scroll = state;
            return true;
        }
        case 'cpdUiState': {
            const state = frameStates.get(panel);
            if (state) state.uiPosition = message.state;
            return true;
        }
        // Handed here by the frame agent. The scheme is re-checked, since openExternal
        // launches whatever it is given.
        case 'openExternal': {
            const url = String(message.url ?? '');
            // VS Code's own trusted-domain prompt is gated to http(s), so a local path
            // gets one of ours before the OS picks an application for it.
            if (/^https?:/i.test(url)) void vscode.env.openExternal(vscode.Uri.parse(url));
            else if (/^file:/i.test(url)) void confirmOpenFile(url);
            return true;
        }
        case 'previewCopy': {
            void vscode.env.clipboard.writeText(String(message.text ?? ''));
            return true;
        }
        case 'previewSelection': {
            lastSelection = String(message.text ?? '');
            return true;
        }
        default:
            return false;
    }
}

/**
 * The shell's policy, which a `srcdoc` document inherits — so it is the worksheet's too, and has
 * to stay wide enough for author content. `'unsafe-inline'` is required by `#HTML` blocks;
 * `https:` rather than a CDN allowlist, because a CDN bundle resolves its own dependencies from
 * hosts that appear nowhere in the worksheet.
 *
 * The tradeoff is deliberate: any HTTPS host is both a script origin and an exfiltration sink, and
 * containment here is the sandbox, not the source list. `frame-ancestors` is omitted — ignored in
 * a `<meta>` policy.
 *
 * Kept in step with template.html, which covers the surfaces with no shell above them: the PDF
 * renderer and browser-hosted calcpad-web.
 */
export function previewCsp(): string {
    return [
        "default-src 'none'",
        "script-src 'unsafe-inline' 'unsafe-eval' https:",
        "style-src 'unsafe-inline' https:",
        'img-src data: blob: https: http:',
        'font-src data: https:',
        'media-src data: blob: https:',
        // blob: is load-bearing: the DXF module does Blob -> createObjectURL -> viewer.Load({url}).
        'connect-src blob: data: https: http://127.0.0.1:* http://localhost:*',
        'worker-src blob: https:',
        // Frames the worksheet embeds, not the shell's own — a srcdoc document inherits
        // rather than being matched. Anything nested inherits the sandbox regardless.
        'frame-src data: blob: https: http:',
        "object-src 'none'",
        "base-uri 'none'",
        "form-action 'none'",
    ].join('; ') + ';';
}

/** The path comes from the worksheet, so opening it with the OS default application is the user's decision. */
async function confirmOpenFile(url: string): Promise<void> {
    const uri = vscode.Uri.parse(url);
    const choice = await vscode.window.showWarningMessage(
        'Open this file from the worksheet?',
        { modal: true, detail: `${uri.fsPath}\n\nYour system will choose the application that opens it.` },
        'Open',
    );
    if (choice === 'Open') void vscode.env.openExternal(uri);
}

/** Message types the shell relays out of the frame. Anything else is dropped. */
const RELAYED = [
    'navigateToLine',
    'consoleMessage',
    'uiValueChange',
    'openExternal',
    'previewCopy',
    'cpdScrollState',
    'cpdUiState',
];

/**
 * Types only the front frame may send. A demoted buffer re-anchors once as it settles, which
 * would overwrite the position the new front frame has already reported.
 */
const FRONT_ONLY = ['cpdScrollState'];

/** How long a document gets to report itself loaded before it is shown regardless. */
const FRAME_READY_TIMEOUT_MS = 30000;

export interface ShellOptions {
    /** Background behind the frames, so the shell does not flash grey on load. */
    background: string;
}

/**
 * Assigned to `panel.webview.html` once per panel. Holds the only `acquireVsCodeApi` handle, the
 * find widget and the relay; a document only ever lives in one of its two frames.
 */
function buildPreviewShell(options: ShellOptions): string {
    const { background } = options;

    return `<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta http-equiv="Content-Security-Policy" content="${previewCsp().replace(/"/g, '&quot;')}">
    <title>CalcpadCE Preview</title>
    <style>
        :root { --cpd-bg: ${background}; }
        html, body { height: 100%; margin: 0; padding: 0; background: var(--cpd-bg); }
        /* Both buffers stay painted so the swap is a z-index flip. The frame behind keeps
           full size because its scroll restore measures against a real viewport. */
        .cpd-frame {
            position: absolute;
            inset: 0;
            width: 100%;
            height: 100%;
            border: 0;
            background: var(--cpd-bg);
            z-index: 1;
        }
        .cpd-frame.cpd-back { z-index: 0; pointer-events: none; }
        /* Veils the last good render rather than replacing it. */
        .cpd-loading {
            position: fixed;
            inset: 0;
            z-index: 5;
            display: flex;
            flex-direction: column;
            align-items: center;
            justify-content: center;
            gap: 14px;
            background: color-mix(in srgb, var(--cpd-bg) 70%, transparent);
            color: var(--vscode-foreground, #333);
            font: 14px/1.4 var(--vscode-font-family, 'Segoe UI', sans-serif);
            pointer-events: none;
        }
        .cpd-loading[hidden] { display: none; }
        .cpd-spinner {
            width: 36px;
            height: 36px;
            border: 3px solid rgba(128, 128, 128, 0.25);
            border-top-color: #0078d4;
            border-radius: 50%;
            animation: cpd-spin 0.8s linear infinite;
        }
        @keyframes cpd-spin { to { transform: rotate(360deg); } }
        .cpd-find-bar {
            position: fixed;
            top: 0;
            right: 18px;
            z-index: 10;
            display: flex;
            align-items: center;
            gap: 4px;
            padding: 4px 6px;
            border: 1px solid var(--vscode-widget-border, rgba(128,128,128,0.35));
            border-top: none;
            border-radius: 0 0 4px 4px;
            background: var(--vscode-editorWidget-background, #252526);
            color: var(--vscode-editorWidget-foreground, #ccc);
            font: 12px var(--vscode-font-family, 'Segoe UI', sans-serif);
            box-shadow: 0 2px 8px rgba(0,0,0,0.3);
        }
        .cpd-find-bar[hidden] { display: none; }
        #cpd-find-input {
            width: 16em;
            padding: 3px 5px;
            border: 1px solid var(--vscode-input-border, transparent);
            background: var(--vscode-input-background, #3c3c3c);
            color: var(--vscode-input-foreground, #ccc);
            font: inherit;
        }
        #cpd-find-input:focus { outline: 1px solid var(--vscode-focusBorder, #007fd4); }
        #cpd-find-count { min-width: 4.5em; text-align: center; opacity: 0.8; }
        .cpd-find-bar button {
            border: none;
            border-radius: 3px;
            padding: 2px 6px;
            background: transparent;
            color: inherit;
            cursor: pointer;
            font: inherit;
        }
        .cpd-find-bar button:hover { background: var(--vscode-toolbar-hoverBackground, rgba(90,93,94,0.31)); }
        .cpd-find-bar button:disabled { opacity: 0.4; cursor: default; background: transparent; }
    </style>
</head>
<body>
    <div id="cpd-find" class="cpd-find-bar" hidden>
        <input id="cpd-find-input" type="text" placeholder="Find in preview" spellcheck="false">
        <span id="cpd-find-count"></span>
        <button id="cpd-find-prev" title="Previous match (Shift+Enter)">&#8593;</button>
        <button id="cpd-find-next" title="Next match (Enter)">&#8595;</button>
        <button id="cpd-find-close" title="Close (Esc)">&#10005;</button>
    </div>
    <div id="cpd-loading" class="cpd-loading" hidden>
        <div class="cpd-spinner"></div>
        <span>Calculating…</span>
    </div>
    <iframe id="cpd-doc-0" class="cpd-frame" sandbox="allow-scripts"></iframe>
    <iframe id="cpd-doc-1" class="cpd-frame cpd-back" inert sandbox="allow-scripts"></iframe>
    <script>
        (function () {
            var vscode = acquireVsCodeApi();
            var RELAYED = ${JSON.stringify(RELAYED)};
            var FRONT_ONLY = ${JSON.stringify(FRONT_ONLY)};
            var READY_TIMEOUT_MS = ${FRAME_READY_TIMEOUT_MS};
            var BACK_BUFFER_CLEAR_CHARS = ${BACK_BUFFER_CLEAR_CHARS};
            var frames = [document.getElementById('cpd-doc-0'), document.getElementById('cpd-doc-1')];
            var loading = document.getElementById('cpd-loading');
            var find = document.getElementById('cpd-find');
            var input = document.getElementById('cpd-find-input');
            var count = document.getElementById('cpd-find-count');

            var front = 0;
            var pending = -1;
            var readyTimer = 0;
            // Render big enough that holding the previous one behind it costs more than it saves.
            var releaseBack = false;

            function toFrame(msg) {
                var w = frames[front].contentWindow;
                if (w) w.postMessage(msg, '*');
            }

            function applyBuffers() {
                for (var i = 0; i < 2; i++) {
                    frames[i].classList.toggle('cpd-back', i !== front);
                    // Left reachable while rendering: inert would swallow the #UI focus restore.
                    if (i === front || i === pending) frames[i].removeAttribute('inert');
                    else frames[i].setAttribute('inert', '');
                }
            }

            // front is set to the slot written, not toggled, so racing renders cannot flip back.
            function render(html, background) {
                if (background) document.documentElement.style.setProperty('--cpd-bg', background);
                var slot = front === 0 ? 1 : 0;
                pending = slot;
                releaseBack = html.length > BACK_BUFFER_CLEAR_CHARS;
                applyBuffers();
                clearTimeout(readyTimer);
                readyTimer = setTimeout(function () { swap(slot); }, READY_TIMEOUT_MS);
                frames[slot].srcdoc = html;
            }

            function swap(slot) {
                if (slot !== pending) return;
                clearTimeout(readyTimer);
                pending = -1;
                var demoted = front;
                front = slot;
                applyBuffers();
                // The demoted buffer is only ever overwritten, so a large one is emptied not kept.
                if (releaseBack && demoted !== slot) frames[demoted].srcdoc = '';
            }

            // Window identity, not origin: an opaque origin reports "null", which would admit
            // any other sandboxed frame. Neither buffer means the host, which only paints.
            window.addEventListener('message', function (e) {
                var d = e.data;
                if (!d || typeof d.type !== 'string') return;
                if (e.source === frames[0].contentWindow) fromFrame(0, d);
                else if (e.source === frames[1].contentWindow) fromFrame(1, d);
                else fromHost(d);
            });

            function fromFrame(slot, d) {
                if (d.type === 'cpdFrameReady') { swap(slot); return; }
                // A demoted buffer still finishes loading, but only the front one speaks.
                if (slot !== front && FRONT_ONLY.indexOf(d.type) !== -1) return;
                if (d.type === 'cpdFindResult') { renderCount(d.total, d.current); return; }
                if (d.type === 'previewFindOpen') { openFind(); return; }
                if (d.type === 'previewContextMenu') {
                    // Stashed before the menu opens: the command it runs has no way back in.
                    vscode.postMessage({ type: 'previewSelection', text: d.selection || '' });
                    raiseContextMenu(d.x, d.y);
                    return;
                }
                if (RELAYED.indexOf(d.type) !== -1) vscode.postMessage(d);
            }

            // Renders and editor->preview sync; a sync has to reach a frame deeper.
            function fromHost(d) {
                if (d.type === 'cpdRender') render(String(d.html || ''), d.background);
                else if (d.type === 'cpdLoading') loading.hidden = !d.on;
                else if (d.type === 'scrollToSourceLine') toFrame(d);
            }

            // VS Code only watches this document for its context contributions.
            function raiseContextMenu(x, y) {
                frames[front].dispatchEvent(new MouseEvent('contextmenu', {
                    bubbles: true,
                    cancelable: true,
                    button: 2,
                    clientX: Number(x) || 0,
                    clientY: Number(y) || 0,
                }));
            }

            var prev = document.getElementById('cpd-find-prev');
            var next = document.getElementById('cpd-find-next');

            function renderCount(total, current) {
                count.textContent = total > 0 ? (current + 1) + '/' + total : (input.value ? '0/0' : '');
                prev.disabled = next.disabled = total === 0;
            }

            function openFind() {
                find.hidden = false;
                input.focus();
                input.select();
            }

            function closeFind() {
                find.hidden = true;
                toFrame({ type: 'cpdFindClear' });
                renderCount(0, 0);
                frames[front].focus();
            }

            input.addEventListener('input', function () {
                toFrame({ type: 'cpdFindApply', query: input.value });
            });
            input.addEventListener('keydown', function (e) {
                if (e.key === 'Enter') { e.preventDefault(); toFrame({ type: 'cpdFindStep', dir: e.shiftKey ? -1 : 1 }); }
                else if (e.key === 'Escape') { e.preventDefault(); closeFind(); }
            });
            prev.addEventListener('click', function () { toFrame({ type: 'cpdFindStep', dir: -1 }); });
            next.addEventListener('click', function () { toFrame({ type: 'cpdFindStep', dir: 1 }); });
            document.getElementById('cpd-find-close').addEventListener('click', closeFind);
            // Ctrl+F on the shell; the frame posts previewFindOpen for its own copy.
            document.addEventListener('keydown', function (e) {
                if ((e.ctrlKey || e.metaKey) && (e.key === 'f' || e.key === 'F')) { e.preventDefault(); openFind(); }
                else if (e.key === 'Escape' && !find.hidden) { e.preventDefault(); closeFind(); }
            });

            // Runs again after VS Code tears a webview down, so the host re-sends the render.
            vscode.postMessage({ type: 'cpdShellReady' });
        })();
    </script>
</body>
</html>`;
}

export interface AgentOptions {
    /** Seeded back into the fresh document so a re-render lands where the user was. */
    scroll?: PreviewScrollState;
    /** The `#UI` script's focus/caret state, which only survives via the host. */
    uiPosition?: unknown;
    /** Console relay cap. Must match what the console patch in the same document passes. */
    maxConsoleMessages?: number;
}

/**
 * The frame's half of the boundary: what stopped working once the document left the top level.
 * VS Code restores scroll only for its top-level document, so the position goes out to the host
 * and comes back as a DOM anchor; links are intercepted here for the same reason.
 */
export function getFrameAgentScript(options: AgentOptions = {}): string {
    const { scroll, uiPosition, maxConsoleMessages } = options;
    // Carries a control key taken from the document, so close any tag it could open.
    const seedUi = uiPosition !== undefined
        ? `window.__calcpadUiPosition = ${JSON.stringify(uiPosition).replace(/</g, '\\u003c')};`
        : '';

    return `
        <style>
            mark.cpd-find { background: rgba(234,179,8,0.45); color: inherit; border-radius: 2px; }
            mark.cpd-find.cpd-find-current { background: rgba(249,115,22,0.95); color: #000; }
        </style>
        <script>
            ${seedUi}
            (function () {
                if (window.__calcpadAgentReady) return;
                window.__calcpadAgentReady = true;
                var send = function (msg) {
                    try { window.parent.postMessage(msg, '*'); } catch (e) {}
                };
                window.__calcpadSend = send;

                // Sent from in here: the shell cannot tell this load event from about:blank's.
                // Held until the scroll position is applied, so the buffer arrives where the user was.
                window.addEventListener('load', function () {
                    var ready = function () { send({ type: 'cpdFrameReady' }); };
                    if (window.__calcpadScrollSettled) window.__calcpadScrollSettled(ready);
                    else ready();
                });

                // CSP violations and load failures, which no console relay sees.
                ${previewDiagnosticsScript(
        "function (level, message) { send({ type: 'consoleMessage', level: level, message: message }); }",
        maxConsoleMessages)}

                // Handed to the shell to re-raise; the frame is full-bleed, so no translation.
                // Datagrids bring their own menu.
                document.addEventListener('contextmenu', function (e) {
                    var t = e.target;
                    if (t && t.closest && t.closest('.jss_container, .calcpad-ui-datagrid')) return;
                    e.preventDefault();
                    send({ type: 'previewContextMenu', x: e.clientX, y: e.clientY, selection: selectedText() });
                });

                // VS Code's Copy acts on the shell, which cannot reach into this document.
                function selectedText() {
                    var s = window.getSelection();
                    return s ? String(s) : '';
                }

                document.addEventListener('keydown', function (e) {
                    if (!(e.ctrlKey || e.metaKey) || (e.key !== 'c' && e.key !== 'C' && e.key !== 'Insert')) return;
                    var text = selectedText();
                    if (!text) return;
                    e.preventDefault();
                    send({ type: 'previewCopy', text: text });
                });

                // The webview's navigation handling does not reach a sandboxed frame, so the
                // activation goes to the extension and everything else is blocked. auxclick
                // is bound too, since middle-click fires no click.
                function onLinkActivate(e) {
                    if (e.type === 'auxclick' && e.button !== 1) return;
                    var a = e.target && e.target.closest ? e.target.closest('a[href]') : null;
                    if (!a || a.hasAttribute('data-text')) return;
                    var href = a.getAttribute('href') || '';
                    if (!href) return;
                    e.preventDefault();
                    // srcdoc has no usable document URL, so the default action navigates to
                    // about:srcdoc#id and blanks the frame instead of scrolling.
                    if (href.charAt(0) === '#') { scrollToFragment(href.slice(1)); return; }
                    if (!/^(https?|file):/i.test(href)) return;
                    send({ type: 'openExternal', url: href });
                }
                function scrollToFragment(raw) {
                    var id = raw;
                    try { id = decodeURIComponent(raw); } catch (_e) {}
                    var t = id ? (document.getElementById(id) || document.getElementsByName(id)[0]) : document.body;
                    if (!t) return;
                    if (window.__calcpadReleaseScroll) window.__calcpadReleaseScroll();
                    t.scrollIntoView({ block: 'start' });
                }
                document.addEventListener('click', onLinkActivate);
                document.addEventListener('auxclick', onLinkActivate);

                // A DOM anchor, not an offset: the worksheet lays out asynchronously.
                ${scrollAnchorScript(
        "function (s) { send({ type: 'cpdScrollState', x: s.x, y: s.y, atEnd: s.atEnd, anchor: s.anchor }); }", scroll)}

                // Runs here: the marking walk needs the document the shell cannot reach.
                var matches = [];
                var current = 0;
                function clearMarks() {
                    var marks = document.querySelectorAll('mark.cpd-find');
                    for (var i = 0; i < marks.length; i++) {
                        var m = marks[i];
                        var parent = m.parentNode;
                        if (!parent) continue;
                        parent.replaceChild(document.createTextNode(m.textContent || ''), m);
                        parent.normalize();
                    }
                    matches = [];
                    current = 0;
                }
                function highlight() {
                    for (var i = 0; i < matches.length; i++) matches[i].classList.remove('cpd-find-current');
                    var target = matches[current];
                    if (!target) return;
                    target.classList.add('cpd-find-current');
                    if (window.__calcpadReleaseScroll) window.__calcpadReleaseScroll();
                    target.scrollIntoView({ block: 'center' });
                }
                function applyFind(query) {
                    clearMarks();
                    if (!query || !document.body) { send({ type: 'cpdFindResult', total: 0, current: 0 }); return; }
                    var needle = query.toLowerCase();
                    var walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT, {
                        acceptNode: function (node) {
                            var p = node.parentElement;
                            if (!node.nodeValue || !p) return NodeFilter.FILTER_REJECT;
                            if (p.tagName === 'SCRIPT' || p.tagName === 'STYLE') return NodeFilter.FILTER_REJECT;
                            return node.nodeValue.toLowerCase().indexOf(needle) !== -1
                                ? NodeFilter.FILTER_ACCEPT
                                : NodeFilter.FILTER_REJECT;
                        }
                    });
                    var targets = [];
                    var n = walker.nextNode();
                    while (n) { targets.push(n); n = walker.nextNode(); }
                    for (var i = 0; i < targets.length; i++) {
                        var node = targets[i];
                        var text = node.nodeValue || '';
                        var hay = text.toLowerCase();
                        var frag = document.createDocumentFragment();
                        var last = 0;
                        var idx = hay.indexOf(needle);
                        while (idx !== -1) {
                            if (idx > last) frag.appendChild(document.createTextNode(text.slice(last, idx)));
                            var mark = document.createElement('mark');
                            mark.className = 'cpd-find';
                            mark.textContent = text.slice(idx, idx + query.length);
                            frag.appendChild(mark);
                            matches.push(mark);
                            last = idx + query.length;
                            idx = hay.indexOf(needle, last);
                        }
                        if (last < text.length) frag.appendChild(document.createTextNode(text.slice(last)));
                        if (node.parentNode) node.parentNode.replaceChild(frag, node);
                    }
                    current = 0;
                    highlight();
                    send({ type: 'cpdFindResult', total: matches.length, current: current });
                }
                function stepFind(dir) {
                    if (!matches.length) return;
                    current = (current + dir + matches.length) % matches.length;
                    highlight();
                    send({ type: 'cpdFindResult', total: matches.length, current: current });
                }
                document.addEventListener('keydown', function (e) {
                    if ((e.ctrlKey || e.metaKey) && (e.key === 'f' || e.key === 'F')) {
                        e.preventDefault();
                        send({ type: 'previewFindOpen' });
                    }
                });

                // Only the shell embeds this document, so window.parent is the only sender.
                window.addEventListener('message', function (e) {
                    if (e.source !== window.parent) return;
                    var d = e.data;
                    if (!d || typeof d.type !== 'string') return;
                    if (d.type === 'cpdFindApply') applyFind(String(d.query || ''));
                    else if (d.type === 'cpdFindStep') stepFind(Number(d.dir) || 0);
                    else if (d.type === 'cpdFindClear') clearMarks();
                });
            })();
        </script>
    `;
}
