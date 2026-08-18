/**
 * Keeping the preview where the user left it, across a re-render that replaces the whole
 * document. Every edit re-converts the worksheet into a fresh `srcdoc`, so nothing in the
 * frame survives and the position has to be posted out and seeded back in.
 *
 * A stored offset cannot do it. The DXF module renders WebGL to a data URL seconds after
 * `window.load`, so at restore time the page is short and `scrollTo` clamps, and an edit
 * that changes the drawing's height moves everything below it anyway. So the position is
 * a DOM anchor - the element at the top of the viewport and how far above it the edge sat
 * - re-aligned until the page stops changing size.
 *
 * Which element gets anchored is the other half, and two things in the rendered worksheet
 * make the obvious choice the wrong one:
 *
 *  - A document with errors carries a summary bar the server fixes across the top of the
 *    viewport. Its `rect.top` is zero at every scroll offset, so an offset measured
 *    against it means nothing - and sitting at the top is what makes a probe find it.
 *  - A loop body is one `<div class="indent">` holding every iteration flat, all repeating
 *    the same `data-source-line`, and the first iteration's id is written into the author's
 *    own tag and re-emitted by every pass after it. So the container, the id and the text
 *    all lead back to the loop's first line; only the iteration count tells them apart.
 *
 * The arithmetic lives here so all three front ends behave alike; the transport does not,
 * because each host addresses its own frame differently.
 */

/** Where the viewport's top edge sat, relative to an element that should still exist. */
export interface ScrollAnchor {
    /** Child indices from `<body>` down, counting only stable elements. */
    path: number[];
    tag: string;
    /** Start of its text, used to pick between candidates when the path has shifted. */
    sig: string;
    id?: string;
    /** Its `data-source-line`, which a loop repeats once per iteration. */
    src?: string;
    /** Which of its siblings carrying `src` this is - the iteration it belongs to. */
    nth?: number;
    /** The anchor's `rect.top` when it was captured. */
    dy: number;
}

export interface PreviewScrollState {
    x: number;
    y: number;
    /** The capture sat at the document's end, which growth above must not push it off. */
    atEnd?: boolean;
    anchor?: ScrollAnchor | null;
}

const MAX_PATH = 64;
const MAX_INDEX = 4096;
const SIG_LEN = 48;
const TAG = /^[A-Z][A-Z0-9]{0,11}$/;
const SRC = /^\d{1,7}$/;

function finite(value: unknown): number | null {
    const n = Number(value);
    return Number.isFinite(n) ? n : null;
}

function parseAnchor(raw: any): ScrollAnchor | null {
    if (!raw || typeof raw !== 'object') return null;
    const dy = finite(raw.dy);
    if (dy === null || !Array.isArray(raw.path) || raw.path.length > MAX_PATH) return null;
    if (typeof raw.tag !== 'string' || !TAG.test(raw.tag)) return null;

    const path: number[] = [];
    for (const step of raw.path) {
        const i = finite(step);
        if (i === null || i < 0 || i > MAX_INDEX) return null;
        path.push(Math.floor(i));
    }
    const anchor: ScrollAnchor = {
        path,
        tag: raw.tag,
        sig: typeof raw.sig === 'string' ? raw.sig.slice(0, SIG_LEN) : '',
        dy,
    };
    if (typeof raw.id === 'string' && raw.id.length <= 128) anchor.id = raw.id;
    // Only as a pair: a line without its iteration resolves to the loop's first pass.
    const nth = finite(raw.nth);
    if (typeof raw.src === 'string' && SRC.test(raw.src) && nth !== null && nth >= 0 && nth <= MAX_INDEX) {
        anchor.src = raw.src;
        anchor.nth = Math.floor(nth);
    }
    return anchor;
}

/**
 * Validates a `cpdScrollState` message before the host stores it. The frame is untrusted
 * and what it sends is written back into the next document, so every field is bounded
 * here rather than where it is used.
 */
export function parseScrollState(message: unknown): PreviewScrollState | null {
    const raw = message as any;
    if (!raw || typeof raw !== 'object') return null;
    const x = finite(raw.x);
    const y = finite(raw.y);
    if (x === null || y === null) return null;
    return { x, y, atEnd: raw.atEnd === true, anchor: parseAnchor(raw.anchor) };
}

/**
 * Builds a `<script>` body (no tag) that reports this frame's position and, when handed a
 * `seed`, restores it. With no seed only the reporting half is emitted.
 *
 * `emit` is inlined verbatim and must be a JavaScript *expression* for a function
 * `(state) => void`, not a statement.
 *
 * Publishes `window.__calcpadScrollSettled(done)`, which the host's frame agent waits on
 * before declaring the document ready, so the first re-align happens in the back buffer.
 */
export function scrollAnchorScript(emit: string, seed?: PreviewScrollState | null): string {
    const start = seed ? JSON.stringify(seed).replace(/</g, '\\u003c') : 'null';
    return `
(function () {
    if (window.__calcpadScrollReady) return;
    window.__calcpadScrollReady = true;
    // Named apart from whatever the expression closes over: both hosts hand in a function
    // that calls their own 'send', which a local of that name would shadow into a call on
    // itself.
    var emit = ${emit};
    var seed = ${start};

    // How long the page must hold still before the restore is finished, and the ceiling on
    // waiting: a cold CDN fetch of a drawing library routinely runs past five seconds.
    var QUIET_MS = 1500;
    var MAX_MS = 20000;
    // A scroll this far from what we last commanded, this long after commanding it, came
    // from somewhere else - a scrollbar drag raises no gesture event on the document.
    var STRAY_PX = 2;
    var STRAY_MS = 150;
    // How long the host may be kept waiting for the first re-align before showing anyway.
    var SETTLE_MS = 200;

    // Elements the hosts add and remove around the worksheet's own content: a different
    // number of scripts per mode, and find highlighting that wraps text mid-line. Left out
    // of the indices so neither shifts a path.
    var SKIP = { SCRIPT: 1, STYLE: 1, LINK: 1, TEMPLATE: 1 };
    function countable(el) {
        if (el.nodeType !== 1 || SKIP[el.tagName]) return false;
        return !(el.tagName === 'MARK' && el.classList.contains('cpd-find'));
    }
    function kids(el) {
        var out = [];
        var children = el.children;
        for (var i = 0; i < children.length; i++)
            if (countable(children[i])) out.push(children[i]);
        return out;
    }

    var BLOCK = {
        P: 1, DIV: 1, TABLE: 1, TR: 1, UL: 1, OL: 1, LI: 1, PRE: 1, HR: 1,
        IMG: 1, SECTION: 1, FIGURE: 1, BLOCKQUOTE: 1,
        H1: 1, H2: 1, H3: 1, H4: 1, H5: 1, H6: 1
    };

    function signature(el) {
        return (el.textContent || '').replace(/\\s+/g, ' ').trim().slice(0, ${SIG_LEN}).toLowerCase();
    }

    function pinned(el) {
        for (var node = el; node && node !== document.body; node = node.parentElement) {
            var position = getComputedStyle(node).position;
            if (position === 'fixed' || position === 'sticky') return true;
        }
        return false;
    }

    // elementsFromPoint answers innermost first and sees through whatever overlaps it, so
    // the first block that is neither pinned nor host furniture is the line being read.
    // Innermost matters: stopping at the outermost block would anchor a loop's container.
    function lineAt(x, y) {
        var stack = document.elementsFromPoint(x, y);
        for (var i = 0; i < stack.length; i++) {
            var el = stack[i];
            if (el === document.body || el === document.documentElement) break;
            if (BLOCK[el.tagName] && countable(el) && !pinned(el)) return el;
        }
        return null;
    }

    // How far down the viewport its own content starts. The body is padded clear of the
    // error bar, so the first row of pixels holds the bar and nothing else. An overlay
    // paints over what it covers and so opens the stack; stopping at the first thing that
    // is not one keeps this off the scroll path for a document without one.
    function overlayBottom(x) {
        var stack = document.elementsFromPoint(x, 2);
        var bottom = 0;
        for (var i = 0; i < stack.length; i++) {
            var el = stack[i];
            if (el === document.body || el === document.documentElement || !pinned(el)) break;
            bottom = Math.max(bottom, el.getBoundingClientRect().bottom);
        }
        return bottom;
    }

    function descend(el, y) {
        for (;;) {
            var children = kids(el);
            var next = null;
            for (var i = 0; i < children.length && !next; i++)
                if (children[i].getBoundingClientRect().bottom > y) next = children[i];
            if (!next || !BLOCK[next.tagName]) return el;
            el = next;
        }
    }

    function topElement() {
        var w = document.documentElement.clientWidth;
        var xs = [w / 2, w / 4, (w * 3) / 4];
        var y = overlayBottom(xs[0]) + 2;
        for (var i = 0; i < xs.length; i++) {
            var hit = lineAt(xs[i], y);
            if (hit) return hit;
        }
        // Every probe landing on nothing means the top of the viewport is a margin or a gap
        // between blocks, so take the first element that reaches into it - then descend,
        // since a body child is a whole loop as often as it is a line.
        var children = kids(document.body);
        for (var j = 0; j < children.length; j++)
            if (children[j].getBoundingClientRect().bottom > y) return descend(children[j], y);
        return null;
    }

    function occurrence(el, src) {
        var n = 0;
        for (var s = el.previousElementSibling; s; s = s.previousElementSibling)
            if (s.getAttribute('data-source-line') === src) ++n;
        return n;
    }

    function captureAnchor() {
        if (window.scrollY <= 0) return null;
        var el = topElement();
        if (!el) return null;

        // A datagrid's insides are generated by jspreadsheet and replaced wholesale when it
        // hydrates, so the container is the deepest thing worth pointing at.
        var grid = el.closest ? el.closest('.calcpad-ui-datagrid') : null;
        if (grid) el = grid;

        var path = [];
        var node = el;
        while (node && node !== document.body) {
            var parent = node.parentElement;
            if (!parent) return null;
            var index = kids(parent).indexOf(node);
            if (index < 0 || path.length >= ${MAX_PATH}) return null;
            path.unshift(index);
            node = parent;
        }
        if (node !== document.body) return null;

        var anchor = {
            path: path,
            tag: el.tagName,
            sig: signature(el),
            dy: el.getBoundingClientRect().top
        };
        if (el.id) anchor.id = el.id;
        var src = el.getAttribute('data-source-line');
        if (src) {
            anchor.src = src;
            anchor.nth = occurrence(el, src);
        }
        return anchor;
    }

    function resolvePath(anchor) {
        var el = document.body;
        for (var i = 0; i < anchor.path.length; i++) {
            var children = kids(el);
            var at = i === anchor.path.length - 1
                ? leaf(children, anchor, anchor.path[i])
                : children[anchor.path[i]];
            if (!at) return null;
            el = at;
        }
        return el === document.body ? null : el;
    }

    // The id is only a fallback: it is not unique inside a loop, and neither the tag nor
    // the text can tell the iterations apart, so getElementById answers with the first one.
    // The path knows which iteration this is.
    function resolveAnchor(anchor) {
        var byPath = resolvePath(anchor);
        if (byPath || !anchor.id) return byPath;
        var byId = document.getElementById(anchor.id);
        return byId && byId.tagName === anchor.tag && signature(byId) === anchor.sig ? byId : null;
    }

    // The document is not quite the one the anchor was taken from: the worksheet's own
    // script edits it (the DXF module removes its render container once the drawing is an
    // image) and a re-render can add or drop a line above. So the last step searches
    // outwards from where it was told to look.
    function leaf(children, anchor, from) {
        // Among siblings repeating a source line the iteration outranks both text and
        // index: an edit to the loop's bound shifts every index after it, and the text is
        // identical by construction.
        if (anchor.src) {
            var last = null;
            var n = 0;
            for (var j = 0; j < children.length; j++) {
                if (children[j].getAttribute('data-source-line') !== anchor.src) continue;
                last = children[j];
                if (n++ === anchor.nth) return last;
            }
            if (last) return last;
        }
        // Text decides it when the text is still there, index decides it otherwise. An edit
        // rewrites the value on the anchored line as often as it shifts the line, and only
        // one of those can be told apart from the other by looking.
        var byIndex = null;
        for (var d = 0; d <= 3; d++) {
            var candidates = d === 0 ? [children[from]] : [children[from - d], children[from + d]];
            for (var i = 0; i < candidates.length; i++) {
                var el = candidates[i];
                if (!el || el.tagName !== anchor.tag) continue;
                if (signature(el) === anchor.sig) return el;
                if (!byIndex) byIndex = el;
            }
        }
        return byIndex;
    }

    function atEnd() {
        var doc = document.documentElement;
        return window.scrollY > 0 && window.scrollY + doc.clientHeight >= doc.scrollHeight - 2;
    }

    var commandedAt = 0;
    var commanded = null;
    function scrollTo(x, y) {
        commandedAt = Date.now();
        window.scrollTo(x, y);
        // What was asked for is routinely unreachable - the whole point is that the page is
        // still growing into it - so where it actually landed is what a later scroll is
        // measured against. Read back here rather than from the scroll event, which a fully
        // clamped scrollTo never fires.
        commanded = { x: window.scrollX, y: window.scrollY };
    }

    var restoring = false;
    var reportQueued = false;
    function report() {
        if (restoring || reportQueued) return;
        reportQueued = true;
        requestAnimationFrame(function () {
            reportQueued = false;
            emit({ x: window.scrollX, y: window.scrollY, atEnd: atEnd(), anchor: captureAnchor() });
        });
    }
    window.addEventListener('scroll', report);

    // The host holds the render in its back buffer until this says the position has been
    // applied. Capped, because a worksheet still fetching a drawing library must not keep
    // the buffer back.
    var waiting = [];
    var isSettled = !seed;
    function markSettled() {
        if (isSettled) return;
        isSettled = true;
        for (var i = 0; i < waiting.length; i++) waiting[i]();
        waiting = [];
    }
    window.__calcpadScrollSettled = function (done) {
        if (isSettled) done();
        else waiting.push(done);
    };

    if (!seed) return;

    setTimeout(markSettled, SETTLE_MS);
    restoring = true;
    var deadline = Date.now() + QUIET_MS;
    var giveUpAt = Date.now() + MAX_MS;
    var resizedAt = 0;
    var observer = null;
    // The browser's own scroll anchoring is solving the same problem with less to go on,
    // and every adjustment it makes raises a scroll event the stray check would read as the
    // user taking over. Ours is authoritative until it finishes.
    document.documentElement.style.overflowAnchor = 'none';

    function apply() {
        if (!restoring || !document.body) return;
        var el = seed.anchor && resolveAnchor(seed.anchor);
        var y;
        // Someone reading the end of the document means the end, not a line that happened
        // to be there: content arriving above would otherwise push them off it.
        if (seed.atEnd) y = document.documentElement.scrollHeight;
        else if (el) y = el.getBoundingClientRect().top + window.scrollY - seed.anchor.dy;
        else y = seed.y;
        y = Math.max(0, Math.round(y));
        if (window.scrollX !== seed.x || window.scrollY !== y) scrollTo(seed.x, y);
        if (!isSettled) requestAnimationFrame(markSettled);
    }

    // Handing the position back mid-restore would replace what the host holds - which is
    // still the truth - with wherever a clamp against a half-laid-out page landed. The frame
    // goes quiet until the page settles, then re-anchors once so a path that has drifted
    // past repair is replaced rather than retried on every future render.
    function finish(reanchor) {
        if (!restoring) return;
        restoring = false;
        markSettled();
        if (observer) observer.disconnect();
        document.documentElement.style.overflowAnchor = '';
        if (!reanchor) return;
        var anchor = captureAnchor();
        // A restore that never left the top has only its own failure to report, and every
        // later render would then start from there.
        if (!anchor && (seed.atEnd || seed.y > 0)) return;
        emit({ x: window.scrollX, y: window.scrollY, atEnd: atEnd(), anchor: anchor });
    }

    ['wheel', 'touchstart', 'keydown', 'pointerdown'].forEach(function (type) {
        window.addEventListener(type, function () { finish(false); }, { passive: true, once: true });
    });
    window.addEventListener('scroll', function () {
        if (!restoring || !commanded) return;
        if (Date.now() - commandedAt < STRAY_MS) return;
        // Content settling moves the offset with nobody touching it: a page that shrinks
        // clamps, and that raises the same event a scrollbar drag does.
        if (Date.now() - resizedAt < STRAY_MS) return;
        if (Math.abs(window.scrollY - commanded.y) > STRAY_PX) finish(false);
    });

    function tick() {
        if (!restoring) return;
        var now = Date.now();
        if (now > giveUpAt || (now > deadline && document.readyState === 'complete')) {
            finish(true);
            return;
        }
        setTimeout(tick, 250);
    }

    function settle() {
        resizedAt = Date.now();
        deadline = Date.now() + QUIET_MS;
        apply();
    }

    // Released by whatever else means to move the page - a jump to a source line, an error
    // chip, a find hit - so the restore does not pull the user back off it.
    window.__calcpadReleaseScroll = function () { finish(false); };

    apply();
    document.addEventListener('DOMContentLoaded', settle);
    window.addEventListener('load', settle);
    // Capture phase, because load does not bubble from the elements that raise it late: the
    // DXF module's <img> is assigned its data URL once the drawing is rendered, and that is
    // the frame the page changes height on.
    document.addEventListener('load', settle, true);
    if (document.fonts && document.fonts.ready) document.fonts.ready.then(settle).catch(function () { });
    if (window.ResizeObserver) {
        observer = new ResizeObserver(settle);
        observer.observe(document.documentElement);
        if (document.body) observer.observe(document.body);
        else document.addEventListener('DOMContentLoaded', function () { observer.observe(document.body); });
    }
    tick();
})();
`;
}
