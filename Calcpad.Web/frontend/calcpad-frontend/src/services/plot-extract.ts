/**
 * Rendered plot extracted from Calcpad HTML: `bytes` is the raw image payload (PNG bytes or
 * UTF-8 SVG text) and `dataUri` is what preview thumbnails reference. Core marks plots with
 * `data-plot="png"` on the `<img>` or `data-plot="svg"` on the root `<svg>`, so file-mode plots
 * are untagged and not extracted.
 */
export interface ExtractedPlot {
    index: number;
    ext: 'png' | 'svg';
    mime: 'image/png' | 'image/svg+xml';
    bytes: Uint8Array;
    dataUri: string;
}

const PNG_RE = /<img\b[^>]*\bdata-plot="png"[^>]*\bsrc="data:image\/png;base64,([^"]+)"[^>]*>/g;
const SVG_RE = /<svg\b[^>]*\bdata-plot="svg"[\s\S]*?<\/svg>/g;

export function extractPlotsFromHtml(html: string): ExtractedPlot[] {
    type Hit = { pos: number; plot: Omit<ExtractedPlot, 'index'> };
    const hits: Hit[] = [];

    for (const m of html.matchAll(PNG_RE)) {
        const b64 = m[1];
        hits.push({
            pos: m.index ?? 0,
            plot: {
                ext: 'png',
                mime: 'image/png',
                bytes: base64ToBytes(b64),
                dataUri: 'data:image/png;base64,' + b64,
            },
        });
    }

    for (const m of html.matchAll(SVG_RE)) {
        const svg = normalizeSvgForStandalone(m[0]);
        const bytes = new TextEncoder().encode(svg);
        hits.push({
            pos: m.index ?? 0,
            plot: {
                ext: 'svg',
                mime: 'image/svg+xml',
                bytes,
                dataUri: 'data:image/svg+xml;base64,' + bytesToBase64(bytes),
            },
        });
    }

    hits.sort((a, b) => a.pos - b.pos);
    return hits.map((h, index) => ({ index, ...h.plot }));
}

/**
 * Debug-mode wrapping in Calcpad.Core injects `id="line-N" data-source-line=… class="line"` into
 * the outermost tag of every rendered line, including the plot's `<svg>`, leaving two `class`
 * attributes that the strict XML parser behind `data:image/svg+xml` rejects. Merge duplicate
 * class attrs on the opening tag so the standalone SVG round-trips through any conforming parser.
 */
function normalizeSvgForStandalone(svg: string): string {
    const openMatch = svg.match(/^<svg\b[^>]*>/);
    if (!openMatch) return svg;
    const opening = openMatch[0];
    const classes = [...opening.matchAll(/\bclass="([^"]*)"/g)].map(m => m[1]);
    if (classes.length < 2) return svg;
    const merged = classes.join(' ');
    const stripped = opening.replace(/\s*\bclass="[^"]*"/g, '');
    const rebuilt = stripped.replace(/\s*>$/, ` class="${merged}">`);
    return rebuilt + svg.slice(opening.length);
}

function base64ToBytes(b64: string): Uint8Array {
    const bin = atob(b64);
    const out = new Uint8Array(bin.length);
    for (let i = 0; i < bin.length; i++) out[i] = bin.charCodeAt(i);
    return out;
}

function bytesToBase64(bytes: Uint8Array): string {
    let bin = '';
    for (let i = 0; i < bytes.length; i++) bin += String.fromCharCode(bytes[i]);
    return btoa(bin);
}
