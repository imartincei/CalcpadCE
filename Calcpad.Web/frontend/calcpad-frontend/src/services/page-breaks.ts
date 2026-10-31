/** Estimated PDF page breaks drawn over the HTML preview, mimicking Chromium's block pagination. */

import type { PdfSettings } from '../types/pdf-settings';

// PuppeteerSharp's PaperFormat sizes, in inches.
const PAPER_INCHES: Record<string, [number, number]> = {
    Letter: [8.5, 11],
    Legal: [8.5, 14],
    Tabloid: [11, 17],
    Ledger: [17, 11],
    A0: [33.1, 46.8],
    A1: [23.4, 33.1],
    A2: [16.54, 23.4],
    A3: [11.7, 16.54],
    A4: [8.27, 11.7],
    A5: [5.83, 8.27],
    A6: [4.13, 5.83],
};

const PX_PER_UNIT: Record<string, number> = {
    in: 96, cm: 96 / 2.54, mm: 96 / 25.4, pt: 96 / 72, pc: 16, px: 1,
};

function lengthToPx(length: string, fallback: number): number {
    const m = /^(\d*\.?\d+)(cm|mm|in|pt|pc|px)$/i.exec(length.trim());
    return m ? Number(m[1]) * PX_PER_UNIT[m[2].toLowerCase()] : fallback;
}

/** The PDF's content area in CSS pixels. */
export function printableArea(settings: PdfSettings): { width: number; height: number } {
    const [w, h] = PAPER_INCHES[settings.format] ?? PAPER_INCHES.Letter;
    const landscape = settings.orientation === 'landscape';
    const pageW = (landscape ? h : w) * 96;
    const pageH = (landscape ? w : h) * 96;
    return {
        width: pageW - lengthToPx(settings.marginLeft, 48) - lengthToPx(settings.marginRight, 48),
        height: pageH - lengthToPx(settings.marginTop, 72) - lengthToPx(settings.marginBottom, 72),
    };
}

/** Script to inject before `</body>` of a preview render. */
export function pageBreaksScript(settings: PdfSettings): string {
    const { width, height } = printableArea(settings);
    return `<script>
(function () {
    if (window.__cpdPageBreaks) return;
    window.__cpdPageBreaks = true;
    var PAGE_W = ${Math.max(100, width).toFixed(2)};
    var PAGE_H = ${Math.max(100, height).toFixed(2)};
    var ATOMIC = /^(P|H[1-6]|TR|LI|IMG|SVG|CANVAS|PRE|FIGURE|IFRAME|VIDEO|HR|INPUT|SELECT|TEXTAREA)$/i;
    var ATOMIC_DISPLAY = /^(inline|inline-block|inline-flex|inline-grid|flex|grid|table-row)$/;

    var style = document.createElement('style');
    style.textContent =
        'body{width:' + PAGE_W + 'px;max-width:none!important;}' +
        '.cpd-page-breaks{position:absolute;top:0;left:0;width:0;height:0;pointer-events:none;z-index:5;}' +
        '.cpd-page-break{position:absolute;height:0;border-top:2px dotted rgba(128,128,128,.75);}' +
        '.cpd-page-break::after{content:attr(data-page);position:absolute;right:0;top:-1.5em;' +
        'font:10px/1.4 system-ui,sans-serif;color:rgba(128,128,128,.9);}';
    document.head.appendChild(style);

    function blockChildren(el) {
        var kids = el.children;
        for (var i = 0; i < kids.length; i++) {
            var d = getComputedStyle(kids[i]).display;
            if (d !== 'none' && d !== 'contents' && !ATOMIC_DISPLAY.test(d)) return true;
        }
        return false;
    }

    function collect(el, out, noPrint) {
        var kids = el.children;
        for (var i = 0; i < kids.length; i++) {
            var c = kids[i];
            if (/^(SCRIPT|STYLE|LINK|TEMPLATE)$/.test(c.tagName)) continue;
            var cs = getComputedStyle(c);
            if (cs.display === 'none' || cs.position === 'fixed' || cs.position === 'absolute') continue;
            var hidden = noPrint || c.classList.contains('no-print');
            var forced = /page|always/.test(cs.breakBefore + ' ' + cs.pageBreakBefore);
            // Clipped boxes (a folded section) are measured whole, not by their hidden children.
            if (!ATOMIC.test(c.tagName) && !ATOMIC_DISPLAY.test(cs.display)
                && cs.overflowY === 'visible' && blockChildren(c)) {
                if (forced) out.push({ el: c, forced: true, marker: true });
                collect(c, out, hidden);
            } else {
                out.push({ el: c, noPrint: hidden, forced: forced });
            }
        }
    }

    function layout() {
        var units = [];
        collect(document.body, units, false);
        var sy = window.scrollY;
        var breaks = [];
        var pageStart = 0, removed = 0, prevBottom = 0;
        for (var i = 0; i < units.length; i++) {
            var u = units[i];
            var r = u.el.getBoundingClientRect();
            if (u.noPrint) { removed += r.height; continue; }
            var top = r.top + sy - removed;
            if (u.forced && top > pageStart) {
                pageStart = top;
                breaks.push((prevBottom + top) / 2 + removed);
            }
            if (u.marker || r.height === 0) continue;
            var bottom = r.bottom + sy - removed;
            while (bottom > pageStart + PAGE_H + 0.5) {
                if (top > pageStart && bottom - top <= PAGE_H) {
                    pageStart = top;
                    breaks.push((Math.min(prevBottom, top) + top) / 2 + removed);
                } else {
                    pageStart += PAGE_H;
                    breaks.push(pageStart + removed);
                }
            }
            prevBottom = Math.max(prevBottom, bottom);
        }
        draw(breaks);
    }

    var layer = null;
    function draw(breaks) {
        if (!layer) {
            layer = document.createElement('div');
            layer.className = 'cpd-page-breaks';
            layer.setAttribute('aria-hidden', 'true');
            document.documentElement.appendChild(layer);
        }
        var b = document.body.getBoundingClientRect();
        var left = b.left + window.scrollX - 8, width = b.width + 16;
        var html = '';
        for (var i = 0; i < breaks.length; i++) {
            html += '<div class="cpd-page-break" data-page="Page ' + (i + 2) + '" style="top:' +
                breaks[i].toFixed(1) + 'px;left:' + left.toFixed(1) + 'px;width:' + width.toFixed(1) + 'px"></div>';
        }
        layer.innerHTML = html;
    }

    var pending = 0;
    function schedule() {
        clearTimeout(pending);
        pending = setTimeout(layout, 150);
    }
    function start() {
        layout();
        if (window.ResizeObserver) new ResizeObserver(schedule).observe(document.body);
        window.addEventListener('load', schedule);
    }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start);
    else start();
})();
</script>`;
}
