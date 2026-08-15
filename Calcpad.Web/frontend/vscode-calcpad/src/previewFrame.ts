/**
 * The containment boundary for rendered worksheets.
 *
 * A worksheet legitimately carries author HTML and JavaScript — `#HTML` blocks, CDN
 * `<script src>` tags, inline animation code — so the preview cannot filter scripts
 * without breaking the product. What it can do is deny that script anything worth
 * reaching. The rendered document is therefore never the webview's top-level
 * document: it goes into a `<iframe srcdoc>` sandboxed with `allow-scripts` and
 * deliberately without `allow-same-origin`, which leaves it on an opaque origin with
 * no `acquireVsCodeApi`, no storage, and no reachable parent DOM. `postMessage` to
 * the shell is the only way out, and the shell forwards a fixed set of message types
 * and nothing else.
 *
 * This mirrors what calcpad-web already does with its preview frames (App.vue), so
 * all three front ends draw the untrusted-content boundary in the same place.
 */

import * as vscode from 'vscode';
import {
    parseScrollState,
    previewDiagnosticsScript,
    scrollAnchorScript,
    type PreviewScrollState,
} from 'calcpad-frontend';

/**
 * What a panel's preview frame reported before the render that replaced it. Every
 * render builds a fresh document, so anything the old one knew about where the user
 * was has to be carried across by the host. VS Code restores a webview's own scroll
 * offset across an `html` assignment, but only for its top-level document — without
 * this the preview would snap to the top on every keystroke.
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
 * The messages a preview frame sends about itself rather than about the document.
 * Handled for every panel that hosts one — the previews and the compiled-worksheet
 * editor alike — so each keeps its own position. Returns whether the message was one
 * of these.
 */
export function handleFrameStateMessage(panel: vscode.WebviewPanel, message: any): boolean {
    switch (message?.type) {
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
        // A link in the rendered document. The webview host intercepts navigation for
        // its own document but not for a sandboxed frame, so the frame agent hands the
        // click here instead. The scheme is re-checked on this side: the frame is
        // untrusted, and openExternal will launch whatever it is given.
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
 * The policy for the shell, which a `srcdoc` document inherits — so this is also the
 * policy the worksheet runs under, and it has to stay wide enough for author content
 * to work. `'unsafe-inline'` is required by inline `<script>` in `#HTML` blocks and
 * by the server template's own `<style>`/`<script>`; a nonce would silently revoke it.
 * For the same reason there is no `'strict-dynamic'`: it makes the browser ignore
 * every host- and scheme-source, which would leave all CDN imports dead.
 *
 * Script sources are the bare `https:` scheme rather than a list of known CDNs,
 * matching calcpad-desktop's CSP. A named allowlist cannot work here: a CDN bundle
 * resolves its own dependencies at runtime from hosts that appear nowhere in the
 * worksheet, so the list is unknowable by inspection. The DXF module is the worked
 * example — it imports `dxf-viewer` from jsdelivr, which then pulls `opentype.js`
 * from `cdn.skypack.dev`. An allowlist fails these one dependency at a time, and a
 * refused fetch does not throw, so each failure is silent (see the
 * `securitypolicyviolation` relay in the agent below).
 *
 * The tradeoff is real and is accepted deliberately: any HTTPS host is both a script
 * origin and an exfiltration sink. Containment here is the sandbox — the opaque
 * origin denying the frame `acquireVsCodeApi`, storage and any reach into the shell —
 * not the source list. What the policy still buys: `object-src 'none'` (no plugin
 * content), `base-uri 'none'` (no rewriting where relative URLs resolve) and
 * `form-action 'none'` (a phishing form in a worksheet has nowhere to post).
 * `frame-ancestors` is omitted deliberately — it is ignored in a `<meta>` policy.
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

/**
 * Escape a document for use as a `srcdoc` attribute value. Only `&` and the `"`
 * delimiter can end the value or start an entity, so escaping both is complete —
 * no sequence in the document can break back out into the shell's markup.
 */
function escapeSrcdoc(html: string): string {
    return html.replace(/&/g, '&amp;').replace(/"/g, '&quot;');
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

export interface ShellOptions {
    /** Background behind the frame, so the shell does not flash grey on load. */
    background: string;
}

/**
 * Wraps a rendered document in the shell that is actually assigned to
 * `panel.webview.html`. The shell holds the only `acquireVsCodeApi` handle, the find
 * widget, and the relay; the document itself only ever exists inside the frame.
 */
export function buildPreviewShell(documentHtml: string, options: ShellOptions): string {
    const { background } = options;

    return `<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta http-equiv="Content-Security-Policy" content="${previewCsp().replace(/"/g, '&quot;')}">
    <title>CalcpadCE Preview</title>
    <style>
        html, body { height: 100%; margin: 0; padding: 0; background: ${background}; }
        #cpd-doc { display: block; border: 0; width: 100%; height: 100%; background: ${background}; }
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
    <iframe id="cpd-doc" sandbox="allow-scripts allow-popups allow-popups-to-escape-sandbox allow-modals allow-downloads" srcdoc="${escapeSrcdoc(documentHtml)}"></iframe>
    <script>
        (function () {
            var vscode = acquireVsCodeApi();
            var frame = document.getElementById('cpd-doc');
            var RELAYED = ${JSON.stringify(RELAYED)};
            var find = document.getElementById('cpd-find');
            var input = document.getElementById('cpd-find-input');
            var count = document.getElementById('cpd-find-count');

            function toFrame(msg) {
                if (frame.contentWindow) frame.contentWindow.postMessage(msg, '*');
            }

            // The frame is the one window that can reach here, and identity is the only
            // usable test: an opaque origin reports itself as "null", so checking the
            // origin string would admit any other sandboxed frame just the same.
            window.addEventListener('message', function (e) {
                if (e.source !== frame.contentWindow) return;
                var d = e.data;
                if (!d || typeof d.type !== 'string') return;
                if (d.type === 'cpdFindResult') { renderCount(d.total, d.current); return; }
                if (d.type === 'previewFindOpen') { openFind(); return; }
                if (d.type === 'previewContextMenu') { raiseContextMenu(d.x, d.y); return; }
                if (RELAYED.indexOf(d.type) !== -1) vscode.postMessage(d);
            });

            // Re-raise the frame's right-click as one on this document, which is the
            // only one VS Code watches for its webview/context contributions.
            function raiseContextMenu(x, y) {
                frame.dispatchEvent(new MouseEvent('contextmenu', {
                    bubbles: true,
                    cancelable: true,
                    button: 2,
                    clientX: Number(x) || 0,
                    clientY: Number(y) || 0,
                }));
            }

            // Host -> frame. The extension posts editor->preview sync here; the document
            // that has to act on it is a frame deeper.
            window.addEventListener('message', function (e) {
                if (e.source === frame.contentWindow) return;
                var d = e.data;
                if (d && d.type === 'scrollToSourceLine') toFrame(d);
            });

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
                frame.focus();
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
}

/**
 * The frame's own half of the boundary: readiness for the things the shell used to do
 * by reaching into the document, plus the pieces that stopped working once the
 * document left the top level.
 *
 * Scroll is the notable one. VS Code restores a webview's scroll offset across an
 * `html` assignment, but only for the top-level document — with the report a frame
 * deeper, every keystroke's re-render would otherwise snap the preview back to the
 * top. The position is reported to the host and seeded into the replacement instead,
 * as a DOM anchor rather than an offset so late-rendering content cannot shift it.
 *
 * External links are intercepted rather than left to the webview's navigation
 * handling, which does not reach inside a sandboxed frame.
 */
export function getFrameAgentScript(options: AgentOptions = {}): string {
    const { scroll, uiPosition } = options;
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

                // CSP violations and resource load failures, which no console relay can
                // see. Shared with calcpad-web so all three front ends report alike.
                ${previewDiagnosticsScript(
        "function (level, message) { send({ type: 'consoleMessage', level: level, message: message }); }")}

                // VS Code raises its webview/context menu from a contextmenu event on the
                // shell's document, and the real one lands here instead — a document it
                // cannot see. The coordinates are handed out so the shell can raise it;
                // the frame is full-bleed at the origin, so they need no translation.
                // Datagrids bring their own menu, so a right-click inside one is left be.
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
        "function (s) { send({ type: 'cpdScrollState', x: s.x, y: s.y, anchor: s.anchor }); }", scroll)}

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
