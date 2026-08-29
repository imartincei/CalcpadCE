/**
 * The containment boundary for rendered worksheets. A worksheet legitimately carries author HTML
 * and JavaScript, so the rendered document is never the webview's top-level document: it goes
 * into an `<iframe srcdoc>` sandboxed with `allow-scripts` and deliberately without
 * `allow-same-origin`, leaving it on an opaque origin whose only way out is `postMessage` to the
 * shell, and there are two such frames so a render is brought forward only once it has loaded.
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
 * What a panel's preview frame reported before the render that replaced it. VS Code restores
 * a webview's own scroll offset across an `html` assignment but only for its top-level
 * document, so without this the preview would snap to the top on every keystroke.
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
 * A panel's shell, which outlives the documents rendered into it. Assigning
 * `webview.html` per render would defeat the double buffering the shell exists to do —
 * there is no frame to hold a finished render behind if the whole webview is rebuilt —
 * so the shell is installed once and documents are pushed in by message.
 */
interface ShellSession {
    /** Until the shell script has run, a posted message has nowhere to land. */
    ready: boolean;
    html: string | null;
    background: string;
    loading: boolean;
}

const shellSessions = new WeakMap<vscode.WebviewPanel, ShellSession>();

/**
 * The document last pushed into this panel, which the shell has to be able to re-send
 * anyway (see `cpdShellReady`). Read by the "inspect webview source" command rather than
 * having the extension keep a second copy of every render.
 */
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
    // The shell posts cpdShellReady once its script runs; everything queued until then is
    // sent from there. Assigning html is what starts that.
    panel.webview.html = buildPreviewShell({ background });
    return fresh;
}

/**
 * Shows a document in a panel, installing the shell first if this is the panel's first
 * render. The document goes into whichever buffer is behind and is brought forward once
 * it reports itself loaded, so the visible frame is never mid-replacement.
 */
export function renderIntoShell(
    panel: vscode.WebviewPanel,
    documentHtml: string,
    options: ShellOptions,
): void {
    const session = shellFor(panel, options.background);
    session.html = documentHtml;
    // A render arriving is what ends the wait, not the request that asked for it
    // finishing: a superseded request finishes without one, and the newer render it was
    // superseded by is still coming.
    session.loading = false;
    postShellState(panel, session);
}

/**
 * Raises or drops the "Calculating…" overlay. An overlay rather than a page of its own:
 * the page would have to be the webview's document, which is the shell.
 */
export function setShellLoading(panel: vscode.WebviewPanel, background: string, on: boolean): void {
    const session = shellFor(panel, background);
    session.loading = on;
    if (session.ready) void panel.webview.postMessage({ type: 'cpdLoading', on });
}

/**
 * The messages a preview frame sends about itself rather than about the document, handled for
 * every panel that hosts one so each keeps its own position. Returns whether the message was
 * one of these.
 */
export function handleFrameStateMessage(panel: vscode.WebviewPanel, message: any): boolean {
    switch (message?.type) {
        // The shell script has started. Also fires when VS Code reloads a webview it had
        // torn down (a panel hidden without retainContextWhenHidden), which is why the
        // last render is held here rather than left to live only in the shell: the
        // document is no longer part of `webview.html` and would come back empty.
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
        // A link in the rendered document: the webview host intercepts navigation for its
        // own document but not for a sandboxed frame, so the frame agent hands the click
        // here. The scheme is re-checked on this side, since openExternal will launch
        // whatever it is given.
        case 'openExternal': {
            const url = String(message.url ?? '');
            if (/^(https?|mailto):/i.test(url)) void vscode.env.openExternal(vscode.Uri.parse(url));
            return true;
        }
        default:
            return false;
    }
}

/**
 * The policy for the shell, which a `srcdoc` document inherits — so it is also the policy the
 * worksheet runs under, and has to stay wide enough for author content to work.
 * `'unsafe-inline'` is required by inline `<script>` in `#HTML` blocks and by the server
 * template's own `<style>`/`<script>`, which is also why there is no `'strict-dynamic'`.
 *
 * Script sources are the bare `https:` scheme rather than a CDN allowlist, matching
 * calcpad-desktop: a CDN bundle resolves its own dependencies at runtime from hosts that
 * appear nowhere in the worksheet, and a refused fetch fails silently.
 *
 * The tradeoff is deliberate — any HTTPS host is both a script origin and an exfiltration
 * sink, and containment here is the sandbox rather than the source list. The policy still
 * buys `object-src 'none'`, `base-uri 'none'` and `form-action 'none'`; `frame-ancestors`
 * is omitted because it is ignored in a `<meta>` policy.
 */
export function previewCsp(): string {
    return [
        "default-src 'none'",
        "script-src 'unsafe-inline' 'unsafe-eval' https:",
        "style-src 'unsafe-inline' https:",
        'img-src data: blob: https: http:',
        'font-src data: https:',
        'media-src data: blob: https:',
        // blob: is load-bearing, not defensive slack: a worksheet that builds a file in
        // memory hands the object URL to a library that fetches it back (the DXF module
        // does exactly this — Blob -> createObjectURL -> viewer.Load({url})), and a
        // fetch of a blob: URL is checked against connect-src like any other.
        'connect-src blob: data: https: http://127.0.0.1:* http://localhost:*',
        'worker-src blob: https:',
        // Governs frames the worksheet embeds, not the shell's own: a srcdoc document
        // inherits its parent's policy instead of being matched against it, which is
        // why calcpad-desktop's frames load under a CSP naming no frame-src at all.
        // Anything nested here inherits the sandbox regardless.
        'frame-src data: blob: https: http:',
        "object-src 'none'",
        "base-uri 'none'",
        "form-action 'none'",
    ].join('; ') + ';';
}

/** Message types the shell relays out of the frame. Anything else is dropped. */
const RELAYED = [
    'navigateToLine',
    'consoleMessage',
    'uiValueChange',
    'openExternal',
    'cpdScrollState',
    'cpdUiState',
];

/**
 * Types only the frame the user is looking at may send. The demoted buffer still holds a
 * live document whose scroll restore re-anchors once when it settles — up to MAX_MS after
 * the render that replaced it (see scroll-anchor.ts) — and that report would overwrite
 * the position the new front frame has already sent.
 */
const FRONT_ONLY = ['cpdScrollState'];

/** How long a document gets to report itself loaded before it is shown regardless. */
const FRAME_READY_TIMEOUT_MS = 30000;

export interface ShellOptions {
    /** Background behind the frames, so the shell does not flash grey on load. */
    background: string;
}

/**
 * The shell that is assigned to `panel.webview.html`, once per panel. It holds the only
 * `acquireVsCodeApi` handle, the find widget, and the relay; a rendered document only
 * ever exists inside one of its two frames, pushed in by `renderIntoShell`.
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
        /* Double-buffered preview, matching calcpad-web: both frames stay laid out and
           painted, one occluding the other, so bringing a finished render forward is a
           z-index flip with nothing left to rasterize. Keeping the frame behind at full
           size is load-bearing rather than incidental — the scroll restore running in it
           measures with elementFromPoint and getBoundingClientRect, which need a real
           viewport, so display:none would leave it landing at the top on the swap. */
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
        /* Veils the render already on screen rather than replacing it, matching
           calcpad-web: with a buffer holding the last good document there is something
           worth leaving visible underneath. */
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
    <iframe id="cpd-doc-0" class="cpd-frame" sandbox="allow-scripts allow-popups allow-popups-to-escape-sandbox allow-modals allow-downloads"></iframe>
    <iframe id="cpd-doc-1" class="cpd-frame cpd-back" inert sandbox="allow-scripts allow-popups allow-popups-to-escape-sandbox allow-modals allow-downloads"></iframe>
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
            // Whether the render now being brought forward was big enough that holding the
            // previous one behind it is not worth the memory.
            var releaseBack = false;

            function toFrame(msg) {
                var w = frames[front].contentWindow;
                if (w) w.postMessage(msg, '*');
            }

            function applyBuffers() {
                for (var i = 0; i < 2; i++) {
                    frames[i].classList.toggle('cpd-back', i !== front);
                    // The buffer being rendered into is left reachable: the #UI script
                    // restores focus and caret as it loads, which inert would swallow.
                    if (i === front || i === pending) frames[i].removeAttribute('inert');
                    else frames[i].setAttribute('inert', '');
                }
            }

            // Writes a document into whichever buffer is behind and brings it forward once
            // it reports itself loaded. The front index is set to the slot written rather
            // than toggled, so two renders racing cannot flip back to the stale one.
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
                // Two live documents is the cost of never showing a half-replaced frame. For a
                // large render that cost doubles what the panel holds, and the demoted buffer is
                // only ever overwritten by the next render, so it is emptied instead of kept.
                if (releaseBack && demoted !== slot) frames[demoted].srcdoc = '';
            }

            // Identity is the only usable test for which window sent something: an opaque
            // origin reports itself as "null", so checking the origin string would admit any
            // other sandboxed frame just the same. Anything that is neither buffer is taken
            // as the host, which buys a nested frame nothing — fromHost only paints a buffer,
            // while the relay out to VS Code is reached by window identity alone.
            window.addEventListener('message', function (e) {
                var d = e.data;
                if (!d || typeof d.type !== 'string') return;
                if (e.source === frames[0].contentWindow) fromFrame(0, d);
                else if (e.source === frames[1].contentWindow) fromFrame(1, d);
                else fromHost(d);
            });

            function fromFrame(slot, d) {
                if (d.type === 'cpdFrameReady') { swap(slot); return; }
                // A buffer the user has already been moved off may still report — its own
                // load is unfinished — but only the one in front speaks for the document.
                if (slot !== front && FRONT_ONLY.indexOf(d.type) !== -1) return;
                if (d.type === 'cpdFindResult') { renderCount(d.total, d.current); return; }
                if (d.type === 'previewFindOpen') { openFind(); return; }
                if (d.type === 'previewContextMenu') { raiseContextMenu(d.x, d.y); return; }
                if (RELAYED.indexOf(d.type) !== -1) vscode.postMessage(d);
            }

            // The extension posts editor->preview sync and the renders themselves here;
            // the document that has to act on a sync is a frame deeper.
            function fromHost(d) {
                if (d.type === 'cpdRender') render(String(d.html || ''), d.background);
                else if (d.type === 'cpdLoading') loading.hidden = !d.on;
                else if (d.type === 'scrollToSourceLine') toFrame(d);
            }

            // Re-raise the frame's right-click as one on this document, which is the
            // only one VS Code watches for its webview/context contributions.
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
            // Ctrl+F with the shell focused. The frame posts previewFindOpen for the
            // same keystroke landing on the document, which the shell cannot see.
            document.addEventListener('keydown', function (e) {
                if ((e.ctrlKey || e.metaKey) && (e.key === 'f' || e.key === 'F')) { e.preventDefault(); openFind(); }
                else if (e.key === 'Escape' && !find.hidden) { e.preventDefault(); closeFind(); }
            });

            // Nothing can be pushed in until this runs, and it runs again whenever VS Code
            // reloads a webview it had torn down — so the host answers by re-sending the
            // render rather than assuming the frames still hold one.
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
    /**
     * The per-render console relay cap, from the user's setting. Must match what the console
     * patch injected into the same document passes — the guard installs once.
     */
    maxConsoleMessages?: number;
}

/**
 * The frame's own half of the boundary: the pieces that stopped working once the document left
 * the top level. Scroll is the notable one — VS Code restores a webview's scroll offset only for
 * its top-level document, so the position is reported to the host and seeded into the replacement
 * as a DOM anchor, and external links are intercepted here too, since the webview's navigation
 * handling does not reach inside a sandboxed frame.
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

                // Tells the shell to bring this document's buffer forward, sent from inside
                // the document because the shell cannot tell an iframe's load event for the
                // render it just wrote from the one fired for its initial about:blank. Held
                // (with a cap) until the scroll agent has applied the position it was seeded
                // with, so the buffer comes forward already where the user was.
                window.addEventListener('load', function () {
                    var ready = function () { send({ type: 'cpdFrameReady' }); };
                    if (window.__calcpadScrollSettled) window.__calcpadScrollSettled(ready);
                    else ready();
                });

                // CSP violations and resource load failures, which no console relay can
                // see. Shared with calcpad-web so all three front ends report alike.
                ${previewDiagnosticsScript(
        "function (level, message) { send({ type: 'consoleMessage', level: level, message: message }); }",
        maxConsoleMessages)}

                // VS Code raises its webview/context menu from a contextmenu event on the
                // shell's document, and the real one lands here instead, so the coordinates
                // are handed out for the shell to raise it — the frame is full-bleed at the
                // origin, so they need no translation. Datagrids bring their own menu, so a
                // right-click inside one is left be.
                document.addEventListener('contextmenu', function (e) {
                    var t = e.target;
                    if (t && t.closest && t.closest('.jss_container, .calcpad-ui-datagrid')) return;
                    e.preventDefault();
                    send({ type: 'previewContextMenu', x: e.clientX, y: e.clientY });
                });

                // An anchor to an external target: the webview host intercepts navigation
                // for its own document, not for a sandboxed frame, so the click is handed
                // out to the extension instead of being left to navigate the frame.
                document.addEventListener('click', function (e) {
                    var a = e.target && e.target.closest ? e.target.closest('a[href]') : null;
                    if (!a) return;
                    var href = a.getAttribute('href') || '';
                    if (!/^(https?|mailto):/i.test(href)) return;
                    e.preventDefault();
                    send({ type: 'openExternal', url: href });
                });

                // An offset alone cannot survive content the worksheet lays out
                // asynchronously, so what is carried across is a DOM anchor. Shared with
                // calcpad-web; see scroll-anchor.ts.
                ${scrollAnchorScript(
        "function (s) { send({ type: 'cpdScrollState', x: s.x, y: s.y, atEnd: s.atEnd, anchor: s.anchor }); }", scroll)}

                // Find runs in here rather than in the shell: the marking walk needs the
                // document, and an opaque origin denies the shell any reach into it.
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

                // Only the shell embeds this document, so window.parent is the one sender
                // that can reach here; anything else is ignored.
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
