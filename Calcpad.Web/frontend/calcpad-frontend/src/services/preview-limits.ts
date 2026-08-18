/**
 * The size ceilings the preview pipeline enforces, shared so all three front ends
 * hold the same line.
 *
 * A rendered worksheet is one string, and showing it duplicates that string many
 * times over: the host keeps it to answer "open full HTML", the debug channel
 * mirrors its body, the injection passes each build a copy, the message that
 * carries it into the webview is cloned, and both buffers of the double-buffered
 * frame hold a live document built from it. A document large enough to matter is
 * therefore large enough several times over, and the DOM built from it costs more
 * again — so the check that matters is the one in front of the whole pipeline,
 * before any of those copies exist. Everything downstream is bounded by it.
 *
 * The three inputs that can grow without a document being large to begin with get
 * their own caps here: inlined image bytes, the debug mirror, and the console relay.
 */

/** Default for the `maxPreviewSizeMB` setting. The DOM built from it costs several times more. */
export const DEFAULT_PREVIEW_SIZE_MB = 24;
export const MIN_PREVIEW_SIZE_MB = 1;
export const MAX_PREVIEW_SIZE_MB = 256;

/** Total base64 image bytes a single render will inline before it stops. */
export const MAX_INLINE_IMAGE_TOTAL_BYTES = 24 * 1024 * 1024;

/** How much of a render's body the `html` debug channel keeps. It is for reading, not archiving. */
export const MAX_HTML_MIRROR_CHARS = 256 * 1024;

/** Per-message and per-document ceilings on what a frame may relay to the host's log. */
export const MAX_CONSOLE_MESSAGE_CHARS = 4000;
export const MAX_CONSOLE_MESSAGES_PER_DOCUMENT = 500;

/**
 * Above this, a buffer demoted to the back is emptied rather than left holding its
 * document. Below it, keeping the last good render costs little and saves a repaint.
 */
export const BACK_BUFFER_CLEAR_CHARS = 4 * 1024 * 1024;

const BYTES_PER_MB = 1024 * 1024;

/**
 * The character count a render must stay under, from the setting's value in MB.
 * Sizes are compared as `string.length` throughout: this content is overwhelmingly
 * ASCII, so a character is a byte, and counting them costs nothing.
 */
export function previewSizeLimitChars(mb: number): number {
    const clamped = Number.isFinite(mb)
        ? Math.min(MAX_PREVIEW_SIZE_MB, Math.max(MIN_PREVIEW_SIZE_MB, Math.floor(mb)))
        : DEFAULT_PREVIEW_SIZE_MB;
    return clamped * BYTES_PER_MB;
}

export function formatSize(chars: number): string {
    const mb = chars / BYTES_PER_MB;
    return mb >= 10 ? `${Math.round(mb)} MB` : `${mb.toFixed(1)} MB`;
}

/** Clips text for a log line, saying what was left out rather than trailing off. */
export function truncateForOutput(text: string, max: number = MAX_HTML_MIRROR_CHARS): string {
    if (text.length <= max) return text;
    return `${text.slice(0, max)}\n… truncated (showing ${formatSize(max)} of ${formatSize(text.length)})`;
}

export interface PreviewLimitNotice {
    /** Size of the render that was refused. */
    chars: number;
    limitChars: number;
    theme: 'light' | 'dark';
}

/**
 * The page shown in place of a render that exceeded the limit. A document of its own
 * rather than an overlay, so it goes through the same buffer as any other render and
 * leaves the last good one untouched until it is swapped forward.
 *
 * Static: the way past the limit is the setting, not a button here. A per-render override
 * would have to be honoured by a host the notice cannot see from inside its sandboxed
 * frame, and the setting already says the same thing in one place for every front end.
 */
export function previewLimitNoticeHtml(o: PreviewLimitNotice): string {
    const c = o.theme === 'light'
        ? { fg: '#3c3c3c', dim: '#6e6e6e', bg: '#ffffff', accent: '#0066cc', border: '#d0d0d0' }
        : { fg: '#cccccc', dim: '#858585', bg: '#1e1e1e', accent: '#4FC1FF', border: '#3c3c3c' };
    return `<!DOCTYPE html>
<html>
<head>
    <meta charset="UTF-8">
    <title>CalcpadCE Preview</title>
    <style>
        body { color: ${c.fg}; background: ${c.bg}; margin: 0; padding: 40px 24px;
               font: 14px/1.5 var(--vscode-font-family, system-ui, sans-serif); }
        main { max-width: 34em; margin: 0 auto; }
        h3 { margin: 0 0 0.4em; font-size: 1.15em; }
        .size { padding: 10px 14px; margin: 1em 0; border: 1px solid ${c.border}; border-radius: 4px;
                font-family: var(--vscode-editor-font-family, monospace); }
        .dim { color: ${c.dim}; }
        ul { padding-left: 1.2em; }
        li { margin: 0.35em 0; }
        .setting { margin-top: 1.4em; padding: 10px 14px; border-left: 3px solid ${c.accent};
                   background: color-mix(in srgb, ${c.accent} 8%, transparent); }
        code { font-family: var(--vscode-editor-font-family, monospace); }
    </style>
</head>
<body>
    <main>
        <h3>Preview blocked</h3>
        <p class="dim">The document rendered to more HTML than the preview will hold, and showing it
        risks running the app out of memory.</p>
        <div class="size">
            This render: <strong>${formatSize(o.chars)}</strong><br>
            Preview limit: <strong>${formatSize(o.limitChars)}</strong>
        </div>
        <p>What usually accounts for it:</p>
        <ul>
            <li>Base64 images embedded in the document — save them to an <code>./images/</code>
                folder and reference them by path instead.</li>
            <li>A large <code>#read</code> data set or <code>#UI</code> datagrid rendered in full.</li>
            <li>Many or very large plots.</li>
        </ul>
        <p class="dim">Exporting to PDF, HTML or DOCX still works — those do not go through the
        preview.</p>
        <div class="setting">
            To preview it anyway, raise <strong>Max Preview Size (MB)</strong> in the Settings tab
            (currently ${Math.round(o.limitChars / (1024 * 1024))}). The preview re-renders as soon
            as you change it.
        </div>
    </main>
</body>
</html>`;
}

/**
 * A `<script>` body (no tag) declaring the relay guard the console patches share:
 * `__calcpadRelayLine(text)` clips a message to the per-message ceiling, counts what has
 * been sent for this document, and returns null once the per-document ceiling is reached —
 * after handing back one final line saying so. Clipping happens here, inside the frame,
 * so an oversized string is never posted across the boundary in the first place.
 *
 * The counter needs no reset: every render is a fresh document.
 */
export function consoleRelayGuardScript(): string {
    return [
        '(function () {',
        '  if (window.__calcpadRelayLine) return;',
        '  var sent = 0;',
        '  window.__calcpadRelayLine = function (text) {',
        '    var s = String(text == null ? "" : text);',
        `    if (sent > ${MAX_CONSOLE_MESSAGES_PER_DOCUMENT}) return null;`,
        '    sent++;',
        `    if (sent > ${MAX_CONSOLE_MESSAGES_PER_DOCUMENT})`,
        `      return '[Calcpad] further console output from this render suppressed (over ${MAX_CONSOLE_MESSAGES_PER_DOCUMENT} messages).';`,
        `    if (s.length > ${MAX_CONSOLE_MESSAGE_CHARS})`,
        `      s = s.slice(0, ${MAX_CONSOLE_MESSAGE_CHARS}) + ' … [' + s.length + ' chars, truncated]';`,
        '    return s;',
        '  };',
        '})();',
    ].join('\n');
}
