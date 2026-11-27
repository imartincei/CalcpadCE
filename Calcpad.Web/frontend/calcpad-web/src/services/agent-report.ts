import type { CalcpadError, ConvertResult, LintDiagnostic, LintResponse } from 'calcpad-frontend/types/api';

/** Longest report text returned to an agent. */
export const MAX_REPORT_CHARS = 60_000;

const ENTITIES: Record<string, string> = { amp: '&', lt: '<', gt: '>', quot: '"', apos: "'", nbsp: ' ' };

function unescape(text: string): string {
    return text.replace(/&(#x[0-9a-f]+|#\d+|\w+);/gi, (match, entity: string) => {
        if (entity[0] === '#') {
            const code = entity[1].toLowerCase() === 'x' ? parseInt(entity.slice(2), 16) : parseInt(entity.slice(1), 10);
            return Number.isFinite(code) ? String.fromCodePoint(code) : match;
        }
        return ENTITIES[entity.toLowerCase()] ?? match;
    });
}

/** A rendered report as plain text: no scripts, styles or markup. */
export function reportText(html: string): string {
    const text = html
        .replace(/<(script|style)\b[\s\S]*?<\/\1\s*>/gi, '')
        .replace(/<!--[\s\S]*?-->/g, '')
        .replace(/<\/t[dh]\s*>/gi, ' | ')
        .replace(/<br\s*\/?>/gi, '\n')
        .replace(/<\/(p|div|tr|h[1-6]|li)\s*>/gi, '\n')
        .replace(/<[^>]+>/g, '');
    return unescape(text)
        .replace(/[ \t ]+/g, ' ')
        .replace(/ *\n */g, '\n')
        .replace(/\n\s*\n+/g, '\n')
        .trim();
}

function lintEntry(d: LintDiagnostic) {
    return {
        source: 'linter',
        severity: d.severity.toLowerCase(),
        line: d.line + 1,
        column: d.column + 1,
        code: d.code,
        message: d.message,
        ...(d.includeOrigins?.length
            ? { inIncludes: d.includeOrigins.map(o => ({ file: o.filePath, line: o.line + 1, message: o.message })) }
            : {}),
    };
}

/** `sourceLine` from a run with line anchors is already 1-based. */
function runEntry(e: CalcpadError) {
    return { source: 'runtime', severity: 'error', line: e.sourceLine, code: e.code, message: e.message };
}

export function checkResult(path: string, lint: LintResponse | null, run: ConvertResult | null) {
    if (!lint && !run) throw new Error('The Calcpad server did not respond. Try again, or restart it from the File menu.');
    const linted = [...lint?.diagnostics ?? [], ...lint?.includeOnly ?? []].map(lintEntry);
    const ran = (run?.errors ?? []).map(runEntry)
        .filter(r => !linted.some(l => l.line === r.line && l.code === r.code));
    const diagnostics = [...linted, ...ran].sort((a, b) => a.line - b.line);
    return {
        path,
        errors: diagnostics.filter(d => d.severity === 'error').length,
        warnings: diagnostics.filter(d => d.severity === 'warning').length,
        diagnostics,
        ...(run ? {} : { note: 'The worksheet could not be run, so calculation errors are missing.' }),
        ...(lint ? {} : { note: 'The linter did not respond, so only calculation errors are shown.' }),
    };
}

export function renderResult(path: string, run: ConvertResult | null, maxChars = MAX_REPORT_CHARS) {
    if (!run) throw new Error('The Calcpad server did not respond. Try again, or restart it from the File menu.');
    const text = reportText(run.html);
    const truncated = text.length > maxChars;
    return {
        path,
        errors: run.errors.map(runEntry),
        report: truncated ? text.slice(0, maxChars) : text,
        ...(truncated ? { note: `The report is ${text.length} characters; only the first ${maxChars} are shown.` } : {}),
    };
}
