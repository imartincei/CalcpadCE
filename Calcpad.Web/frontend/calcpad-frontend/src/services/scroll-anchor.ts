/**
 * Keeping the preview where the user left it, across a re-render that replaces the
 * whole document.
 *
 * Every `#UI` edit has the host re-convert the worksheet and assign a fresh `srcdoc`,
 * which builds a new browsing context: nothing in the frame survives, so where the user
 * was has to be posted out and seeded back in. That much the previous implementation
 * did. What it could not do is survive content that lays out asynchronously.
 *
 * A worksheet's own script is the hard case. The DXF module emits an `<img>` that is
 * `display:none` with `height:auto`, then imports three.js and dxf-viewer from a CDN,
 * renders WebGL, reads the canvas back as a data URL and only then makes the image
 * visible - seconds after `window.load`, which is where restoring used to stop. Two
 * things go wrong with a stored offset:
 *
 *  - At restore time the image occupies no space, so the document is short and
 *    `scrollTo` clamps to `scrollHeight - clientHeight`. The page grows afterwards and
 *    the view is left wherever the clamp put it.
 *  - An edit that changes the drawing's aspect ratio changes the image's height, so
 *    everything below it moves. No offset restored at any moment is the right one.
 *
 * So the position is stored as a DOM anchor instead: the element that was at the top of
 * the viewport, and how far above it the viewport edge sat. Restoring re-aligns that
 * element, and keeps re-aligning it until the page stops changing size - which is what
 * makes late content a non-event rather than a jump.
 *
 * The detection and the arithmetic live here so all three front ends behave alike; the
 * transport does not, because each host addresses its own frame differently.
 */

/** Where the viewport's top edge sat, relative to an element that should still exist. */
export interface ScrollAnchor {
    /** Child indices from `<body>` down, counting only stable elements. */
    path: number[];
    /** The anchor's tag, which validates a resolved path. */
    tag: string;
    /** Start of its text, used to pick between candidates when the path has shifted. */
    sig: string;
    /** Preferred key when the view emits one (`id="line-N"` in the code view). */
    id?: string;
    /** The anchor's `rect.top` when it was captured. */
    dy: number;
}

export interface PreviewScrollState {
    x: number;
    y: number;
    anchor?: ScrollAnchor | null;
}

const MAX_PATH = 64;
const MAX_INDEX = 4096;
const SIG_LEN = 48;
const TAG = /^[A-Z][A-Z0-9]{0,11}$/;

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
    return anchor;
}

/**
 * Validates a `cpdScrollState` message before the host stores it. The frame is
 * untrusted and what it sends is written back into the next document, so every field is
 * bounded here rather than where it is used.
 */
export function parseScrollState(message: unknown): PreviewScrollState | null {
    const raw = message as any;
    if (!raw || typeof raw !== 'object') return null;
    const x = finite(raw.x);
    const y = finite(raw.y);
    if (x === null || y === null) return null;
    return { x, y, anchor: parseAnchor(raw.anchor) };
}

/**
 * Builds a `<script>` body (no tag) that reports this frame's position and, when handed
 * one, restores it.
 *
 * `emit` is a JavaScript *expression* for a function `(state) => void`, inlined verbatim
 * and so a statement will not do:
 *
 * ```ts
 * scrollAnchorScript("function (s) { send({ type: 'cpdScrollState', x: s.x, y: s.y, anchor: s.anchor }); }", seed)
 * ```
 *
 * With no `seed` - a document opened rather than re-rendered, or one the user had at the
 * top - only the reporting half is emitted.
 */
export function scrollAnchorScript(emit: string, seed?: PreviewScrollState | null): string {
    const start = seed ? JSON.stringify(seed).replace(/</g, '\\u003c') : 'null';
    return `
(function () {
    if (window.__calcpadScrollReady) return;
    window.__calcpadScrollReady = true;
    // Named apart from whatever the expression closes over: both hosts hand in a
    // function that calls their own 'send', which a local of that name would shadow
    // into a call on itself.
    var emit = ${emit};
    var seed = ${start};

    // How long the page must hold still before the restore is considered finished, and
    // the ceiling on waiting for it. A cold CDN fetch of a drawing library routinely
    // runs past five seconds; the cap is generous because any user gesture ends the
    // restore immediately anyway.
    var QUIET_MS = 1500;
    var MAX_MS = 20000;
    // A scroll this far from what we last commanded, this long after commanding it, came
    // from somewhere else - a scrollbar drag raises no gesture event on the document.
    var STRAY_PX = 2;
    var STRAY_MS = 150;

    // Elements the hosts add and remove around the worksheet's own content: a different
    // number of scripts per mode, and find highlighting that wraps text mid-line. Left
    // out of the indices so neither shifts a path.
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

    // Anchoring to an inline fragment would tie the offset to text that an edit rewrites,
    // so the capture climbs to the block that contains it. Cheaper than asking for
    // computed styles on a path that runs on every scroll frame.
    var BLOCK = {
        P: 1, DIV: 1, TABLE: 1, TR: 1, UL: 1, OL: 1, LI: 1, PRE: 1, HR: 1,
        IMG: 1, SECTION: 1, FIGURE: 1, BLOCKQUOTE: 1,
        H1: 1, H2: 1, H3: 1, H4: 1, H5: 1, H6: 1
    };

    function signature(el) {
        return (el.textContent || '').replace(/\\s+/g, ' ').trim().slice(0, ${SIG_LEN}).toLowerCase();
    }

    function topElement() {
        var w = document.documentElement.clientWidth;
        var xs = [w / 2, w / 4, (w * 3) / 4];
        for (var i = 0; i < xs.length; i++) {
            var hit = document.elementFromPoint(xs[i], 2);
            if (hit && hit !== document.body && hit !== document.documentElement) return hit;
        }
        // Every probe landing on nothing means the top of the viewport is a margin or a
        // gap between blocks, so take the first element that reaches into it.
        var children = kids(document.body);
        for (var j = 0; j < children.length; j++)
            if (children[j].getBoundingClientRect().bottom > 0) return children[j];
        return null;
    }

    function captureAnchor() {
        if (window.scrollY <= 0) return null;
        var el = topElement();
        if (!el) return null;

        // A datagrid's insides are generated by jspreadsheet and replaced wholesale when
        // it hydrates, so the container is the deepest thing worth pointing at.
        var grid = el.closest ? el.closest('.calcpad-ui-datagrid') : null;
        if (grid) el = grid;
        else while (!BLOCK[el.tagName] && el.parentElement && el.parentElement !== document.body)
            el = el.parentElement;

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
        return anchor;
    }

    function resolveAnchor(anchor) {
        if (anchor.id) {
            var byId = document.getElementById(anchor.id);
            if (byId && byId.tagName === anchor.tag) return byId;
        }
        var el = document.body;
        for (var i = 0; i < anchor.path.length; i++) {
            var children = kids(el);
            var at = children[anchor.path[i]];
            if (!at) return null;
            if (i === anchor.path.length - 1) at = leaf(children, anchor, anchor.path[i]);
            if (!at) return null;
            el = at;
        }
        return el === document.body ? null : el;
    }

    // The document is not quite the one the anchor was taken from: the worksheet's own
    // script edits it (the DXF module removes its render container once the drawing is
    // an image) and a re-render can add or drop a line above. So the last step searches
    // outwards from where it was told to look.
    //
    // Text decides it when the text is still there, index decides it otherwise. An edit
    // rewrites the value on the anchored line as often as it shifts the line, and only
    // one of those can be told apart from the other by looking.
    function leaf(children, anchor, from) {
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

    var commandedAt = 0;
    var commanded = null;
    function scrollTo(x, y) {
        commandedAt = Date.now();
        window.scrollTo(x, y);
        // What was asked for is routinely unreachable - the whole point is that the page
        // is still growing into it - so where it actually landed is what a later scroll
        // is measured against. Read back here rather than from the scroll event, which a
        // fully clamped scrollTo never fires.
        commanded = { x: window.scrollX, y: window.scrollY };
    }

    var restoring = false;
    var reportQueued = false;
    function report() {
        if (restoring || reportQueued) return;
        reportQueued = true;
        requestAnimationFrame(function () {
            reportQueued = false;
            emit({ x: window.scrollX, y: window.scrollY, anchor: captureAnchor() });
        });
    }
    window.addEventListener('scroll', report);

    if (!seed) return;

    restoring = true;
    var deadline = Date.now() + QUIET_MS;
    var giveUpAt = Date.now() + MAX_MS;
    var observer = null;
    // The browser's own scroll anchoring shifts the offset when content above the
    // viewport grows. It is solving the same problem with less to go on, and every
    // adjustment it makes raises a scroll event that the stray check below would read as
    // the user taking over. Ours is authoritative until it finishes.
    document.documentElement.style.overflowAnchor = 'none';

    function apply() {
        if (!restoring || !document.body) return;
        var el = seed.anchor && resolveAnchor(seed.anchor);
        var y = el ? el.getBoundingClientRect().top + window.scrollY - seed.anchor.dy : seed.y;
        y = Math.max(0, Math.round(y));
        if (window.scrollX !== seed.x || window.scrollY !== y) scrollTo(seed.x, y);
    }

    // Handing the position back mid-restore would replace what the host holds - which is
    // still the truth - with wherever a clamp against a half-laid-out page landed. The
    // frame goes quiet until the page has settled, and re-anchors once, so a path that
    // has drifted past repair is replaced rather than retried on every future render.
    function finish(reanchor) {
        if (!restoring) return;
        restoring = false;
        if (observer) observer.disconnect();
        document.documentElement.style.overflowAnchor = '';
        if (reanchor) emit({ x: window.scrollX, y: window.scrollY, anchor: captureAnchor() });
    }

    ['wheel', 'touchstart', 'keydown', 'pointerdown'].forEach(function (type) {
        window.addEventListener(type, function () { finish(false); }, { passive: true, once: true });
    });
    window.addEventListener('scroll', function () {
        if (!restoring || !commanded) return;
        if (Date.now() - commandedAt < STRAY_MS) return;
        if (Math.abs(window.scrollY - commanded.y) > STRAY_PX) finish(false);
    });

    // The point of the loop: a drawing that only becomes visible once its library has
    // loaded and rendered changes the page height long after every load event has fired,
    // and the anchor has to be re-aligned each time it does.
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
        deadline = Date.now() + QUIET_MS;
        apply();
    }

    // Released by whatever else means to move the page - a jump to a source line, an
    // error chip, a find hit - so the restore does not pull the user back off it.
    window.__calcpadReleaseScroll = function () { finish(false); };

    apply();
    document.addEventListener('DOMContentLoaded', settle);
    window.addEventListener('load', settle);
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
