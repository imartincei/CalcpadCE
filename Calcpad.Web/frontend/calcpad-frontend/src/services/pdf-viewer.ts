/**
 * A self-contained pdf.js viewer document for the preview frame. pdf.js runs on the main thread
 * (its worker sets `globalThis.pdfjsWorker`), since the sandboxed frame can't load a worker URL.
 */

import { bytesToBase64 } from './image-utils';

export interface PdfViewerOptions {
    pdf: ArrayBuffer | Uint8Array;
    /** Contents of pdfjs-dist's `legacy/build/pdf.min.mjs`. */
    libSource: string;
    /** Contents of pdfjs-dist's `legacy/build/pdf.worker.min.mjs`. */
    workerSource: string;
    theme: 'light' | 'dark';
}

function inlineScript(source: string): string {
    return source.replace(/<\/script/gi, '<\\/script');
}

export function pdfViewerHtml(o: PdfViewerOptions): string {
    const c = o.theme === 'light'
        ? { bg: '#e6e6e6', bar: '#f3f3f3', fg: '#333333', border: '#cccccc' }
        : { bg: '#2b2b2b', bar: '#1e1e1e', fg: '#cccccc', border: '#3c3c3c' };
    return `<!DOCTYPE html>
<html>
<head>
    <meta charset="UTF-8">
    <title>CalcpadCE PDF Preview</title>
    <style>
        html, body { margin: 0; background: ${c.bg}; color: ${c.fg}; font: 12px/1.4 system-ui, sans-serif; }
        html { overflow-y: scroll; }
        #cpd-pdf-bar { position: fixed; top: 0; left: 0; right: 0; z-index: 2; display: flex; align-items: center;
                       gap: 6px; padding: 4px 10px; background: ${c.bar}; border-bottom: 1px solid ${c.border}; }
        #cpd-pdf-bar button { min-width: 26px; padding: 1px 6px; color: inherit; background: transparent;
                              border: 1px solid ${c.border}; border-radius: 3px; cursor: pointer; font: inherit; }
        #cpd-pdf-bar button:hover { background: rgba(128, 128, 128, 0.2); }
        #cpd-pdf-zoom { min-width: 3.5em; text-align: center; }
        #cpd-pdf-page { margin-left: auto; }
        #cpd-pdf-pages { padding: 44px 0 16px; }
        .cpd-pdf-page { margin: 0 auto 16px; background: #fff; box-shadow: 0 1px 4px rgba(0, 0, 0, 0.4); }
        .cpd-pdf-page canvas { display: block; width: 100%; height: 100%; }
        #cpd-pdf-status { padding: 60px 24px; text-align: center; }
    </style>
</head>
<body>
    <div id="cpd-pdf-bar">
        <button id="cpd-pdf-out" title="Zoom out (Ctrl+Wheel)">−</button>
        <span id="cpd-pdf-zoom"></span>
        <button id="cpd-pdf-in" title="Zoom in (Ctrl+Wheel)">+</button>
        <button id="cpd-pdf-fit" title="Fit page width">Fit</button>
        <span id="cpd-pdf-page"></span>
    </div>
    <div id="cpd-pdf-status">Loading PDF…</div>
    <div id="cpd-pdf-pages"></div>
    <script type="text/plain" id="cpd-pdf-data">${bytesToBase64(o.pdf)}</script>
    <script type="module">${inlineScript(o.workerSource)}</script>
    <script type="module">${inlineScript(o.libSource)}</script>
    <script type="module">${VIEWER_SCRIPT}</script>
</body>
</html>`;
}

/** Shown in place of the PDF view when the PDF could not be generated. */
export function pdfPreviewErrorHtml(message: string, theme: 'light' | 'dark'): string {
    const c = theme === 'light' ? { fg: '#3c3c3c', bg: '#ffffff' } : { fg: '#cccccc', bg: '#1e1e1e' };
    const text = message.replace(/[&<>"]/g, ch => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' })[ch]!);
    return `<!DOCTYPE html>
<html>
<head>
    <meta charset="UTF-8">
    <title>CalcpadCE PDF Preview</title>
    <style>
        body { color: ${c.fg}; background: ${c.bg}; margin: 0; padding: 40px 24px; font: 13px/1.5 system-ui, sans-serif; }
        main { max-width: 34em; margin: 0 auto; }
        .error { padding: 10px 14px; border-left: 3px solid #d32f2f; }
    </style>
</head>
<body>
    <main>
        <h3>PDF preview failed</h3>
        <p class="error">${text}</p>
        <p>Turn off <strong>Render Preview as PDF</strong> in the Settings tab to return to the HTML preview.</p>
    </main>
</body>
</html>`;
}

const VIEWER_SCRIPT = `
const lib = globalThis.pdfjsLib;
const statusEl = document.getElementById('cpd-pdf-status');
const pagesEl = document.getElementById('cpd-pdf-pages');
const zoomEl = document.getElementById('cpd-pdf-zoom');
const pageEl = document.getElementById('cpd-pdf-page');
const STEPS = [0.25, 0.33, 0.5, 0.67, 0.75, 0.9, 1, 1.1, 1.25, 1.5, 1.75, 2, 2.5, 3, 4];
let pdf = null;
let sizes = [];
let divs = [];
let zoom = 'fit';
let scale = 1;

function fitScale() {
    const widest = sizes.reduce((m, s) => Math.max(m, s.w), 1);
    return Math.max(0.25, (document.documentElement.clientWidth - 32) / widest);
}

function applyZoom() {
    const root = document.documentElement;
    const ratio = root.scrollHeight > 0 ? root.scrollTop / root.scrollHeight : 0;
    scale = zoom === 'fit' ? fitScale() : zoom;
    zoomEl.textContent = Math.round(scale * 100) + '%';
    divs.forEach((div, i) => {
        div.style.width = sizes[i].w * scale + 'px';
        div.style.height = sizes[i].h * scale + 'px';
    });
    root.scrollTop = ratio * root.scrollHeight;
    renderVisible();
}

function step(dir) {
    const next = dir > 0 ? STEPS.find(s => s > scale + 0.001) : [...STEPS].reverse().find(s => s < scale - 0.001);
    if (next === undefined) return;
    zoom = next;
    applyZoom();
}

async function renderPage(i) {
    const div = divs[i];
    if (div.dataset.scale === String(scale)) return;
    div.dataset.scale = String(scale);
    const page = await pdf.getPage(i + 1);
    const viewport = page.getViewport({ scale: scale * (window.devicePixelRatio || 1) });
    const canvas = document.createElement('canvas');
    canvas.width = Math.floor(viewport.width);
    canvas.height = Math.floor(viewport.height);
    div.cpdTask?.cancel();
    const task = page.render({ canvasContext: canvas.getContext('2d'), canvas, viewport });
    div.cpdTask = task;
    try {
        await task.promise;
    } catch (e) {
        if (e && e.name === 'RenderingCancelledException') return;
        throw e;
    }
    if (div.dataset.scale === String(scale)) div.replaceChildren(canvas);
}

function renderVisible() {
    const h = window.innerHeight;
    let current = 0;
    divs.forEach((div, i) => {
        const r = div.getBoundingClientRect();
        if (r.bottom > -h && r.top < 2 * h) void renderPage(i);
        if (r.top < h / 2) current = i;
    });
    pageEl.textContent = divs.length ? 'Page ' + (current + 1) + ' of ' + divs.length : '';
}

let scrollPending = false;
window.addEventListener('scroll', () => {
    if (scrollPending) return;
    scrollPending = true;
    requestAnimationFrame(() => { scrollPending = false; renderVisible(); });
}, { passive: true });
window.addEventListener('resize', () => { if (zoom === 'fit') applyZoom(); else renderVisible(); });
window.addEventListener('wheel', e => {
    if (!e.ctrlKey) return;
    e.preventDefault();
    step(e.deltaY < 0 ? 1 : -1);
}, { passive: false });
document.getElementById('cpd-pdf-in').addEventListener('click', () => step(1));
document.getElementById('cpd-pdf-out').addEventListener('click', () => step(-1));
document.getElementById('cpd-pdf-fit').addEventListener('click', () => { zoom = 'fit'; applyZoom(); });

try {
    const bin = atob(document.getElementById('cpd-pdf-data').textContent.trim());
    const data = new Uint8Array(bin.length);
    for (let i = 0; i < bin.length; i++) data[i] = bin.charCodeAt(i);
    pdf = await lib.getDocument({ data }).promise;
    for (let n = 1; n <= pdf.numPages; n++) {
        const vp = (await pdf.getPage(n)).getViewport({ scale: 1 });
        sizes.push({ w: vp.width, h: vp.height });
        const div = document.createElement('div');
        div.className = 'cpd-pdf-page';
        pagesEl.appendChild(div);
        divs.push(div);
    }
    statusEl.remove();
    applyZoom();
} catch (e) {
    statusEl.textContent = 'Could not display the PDF: ' + (e && e.message ? e.message : e);
}
`;
