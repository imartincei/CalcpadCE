import type { DefinitionResolver, MetadataDefinition } from './metadata-comment';
import { findUiDirectiveBlock } from './ui-directive';

/**
 * A definition declared on a source line, located by scanning the raw editor text —
 * line continuations joined, but nothing included or macro-expanded.
 */
export interface SourceDefinition extends MetadataDefinition {
    /** 0-based last physical line of the statement (equals `line` when not continued). */
    endLine: number;
}

// Identifier character classes, mirroring Calcpad.Core's Validator.
const VAR_START = /[\p{L}€£₤¥¢₽₹₩₪°′″%‰‱℧∡]/u;
const VAR_CHAR = /[\p{L}\p{Nd}€£₤¥¢₽₹₩₪°′″%‰‱℧∡,_‾‴⁗́̄̇̈⁰¹²³⁴⁵⁶⁷⁸⁹⁺⁻⁼⁽⁾₀₁₂₃₄₅₆₇₈₉₊₋₌₍₎ϑϕøØ]/u;

const DEF_RE = /^#def\s+([A-Za-z_][A-Za-z0-9_]*\$)\s*(\()?/i;
const END_DEF_RE = /^#end\s+def\b/i;

/** Characters that, at the end of a line, continue the statement onto the next one. */
const LINE_EXTENSION_CHARS = ';|&@:({[';

/** One statement, with the physical lines it spans. */
interface LogicalLine {
    text: string;
    line: number;
    endLine: number;
}

/** True when the line ends with an unclosed `'` or `"` comment quote. */
function endsInsideComment(text: string): boolean {
    let quote = '';
    for (const c of text) {
        if (quote) { if (c === quote) quote = ''; }
        else if (c === "'" || c === '"') quote = c;
    }
    return quote !== '';
}

/** True when the next physical line continues this one, per ExpressionParser.Parse. */
function hasLineExtension(text: string): boolean {
    if (text.endsWith(' _')) return true;
    const last = text[text.length - 1];
    return last !== undefined && LINE_EXTENSION_CHARS.includes(last) && !endsInsideComment(text);
}

/** Fold physical lines into statements, joining `_`- and operator-continued lines. */
function toLogicalLines(lines: string[]): LogicalLine[] {
    const out: LogicalLine[] = [];
    for (let i = 0; i < lines.length; i++) {
        let text = lines[i].replace(/\s+$/, '');
        const start = i;
        while (hasLineExtension(text) && i + 1 < lines.length) {
            const next = lines[++i].replace(/\s+$/, '');
            text = text.endsWith(' _') ? text.slice(0, -2) + next : `${text} ${next}`;
        }
        out.push({ text, line: start, endLine: i });
    }
    return out;
}

/** Index of the first `=` outside parentheses, or -1. Used to tell an inline `#def` apart. */
function equalsOutsideParens(text: string, from: number): number {
    let depth = 0;
    for (let i = from; i < text.length; i++) {
        const c = text[i];
        if (c === '(') depth++;
        else if (c === ')') depth--;
        else if (c === '=' && depth === 0) return i;
    }
    return -1;
}

/** Split a parameter list on top-level `;`, returning the declared parameter count. */
function countParams(params: string): number {
    if (params.trim() === '') return 0;
    let depth = 0, count = 1;
    for (const c of params) {
        if (c === '(' || c === '[' || c === '{') depth++;
        else if (c === ')' || c === ']' || c === '}') depth--;
        else if (c === ';' && depth === 0) count++;
    }
    return count;
}

/**
 * First definition in an expression: an identifier followed by `=`, or by a parameter list
 * and then `=`. Paired `'`/`"` comments are skipped, so code after a closing quote still
 * scans. `$` is not an identifier char, so a macro call never matches.
 */
function findDefinitionInExpression(text: string): { kind: 'variable' | 'function'; name: string; paramCount: number } | null {
    let quote = '';
    let depth = 0;
    for (let i = 0; i < text.length; i++) {
        const c = text[i];
        if (quote) { if (c === quote) quote = ''; continue; }
        if (c === "'" || c === '"') { quote = c; continue; }
        if (c === '(' || c === '[' || c === '{') { depth++; continue; }
        if (c === ')' || c === ']' || c === '}') { depth--; continue; }
        if (depth !== 0 || !VAR_START.test(c)) continue;

        let end = i + 1;
        while (end < text.length && VAR_CHAR.test(text[end])) end++;
        const name = text.slice(i, end);
        let j = end;
        while (text[j] === ' ' || text[j] === '\t') j++;

        if (text[j] === '(') {
            const close = matchingParen(text, j);
            if (close > 0) {
                let k = close + 1;
                while (text[k] === ' ' || text[k] === '\t') k++;
                if (text[k] === '=' && text[k + 1] !== '=')
                    return { kind: 'function', name, paramCount: countParams(text.slice(j + 1, close)) };
            }
        } else if (text[j] === '=' && text[j + 1] !== '=') {
            return { kind: 'variable', name, paramCount: 0 };
        }
        i = end - 1;
    }
    return null;
}

/** Index of the `)` matching the `(` at {@link open}, skipping comments; -1 when unbalanced. */
function matchingParen(text: string, open: number): number {
    let depth = 0, quote = '';
    for (let i = open; i < text.length; i++) {
        const c = text[i];
        if (quote) { if (c === quote) quote = ''; continue; }
        if (c === "'" || c === '"') { quote = c; continue; }
        if (c === '(') depth++;
        else if (c === ')' && --depth === 0) return i;
    }
    return -1;
}

/** Parse a `#def` line into its macro name and parameter count, or null when malformed. */
function parseMacroDef(text: string): { name: string; paramCount: number } | null {
    const m = DEF_RE.exec(text);
    if (!m) return null;
    if (!m[2]) return { name: m[1], paramCount: 0 };
    const open = m[0].length - 1;
    const close = matchingParen(text, open);
    return { name: m[1], paramCount: close < 0 ? 0 : countParams(text.slice(open + 1, close)) };
}

/**
 * Every definition in raw Calcpad source, keyed by each physical line its statement spans.
 * Continuations are joined but nothing is included or expanded, so a `#def` reports the macro
 * and a macro call reports nothing — unlike the highlighter's post-expansion view, which
 * attributes a macro body's assignments to the call site. Custom units count as variables.
 */
export function scanSourceDefinitions(lines: string[]): Map<number, SourceDefinition> {
    const byLine = new Map<number, SourceDefinition>();
    let inMacroBody = false;

    for (const { text, line, endLine } of toLogicalLines(lines)) {
        const trimmed = text.trim();
        if (inMacroBody) {
            if (END_DEF_RE.test(trimmed)) inMacroBody = false;
            continue;
        }
        if (trimmed === '') continue;

        let found: { kind: SourceDefinition['kind']; name: string; paramCount: number } | null = null;
        if (trimmed[0] === '#') {
            const macro = parseMacroDef(trimmed);
            if (macro) {
                // No `=` outside the parameter list means the body runs to `#end def`.
                inMacroBody = equalsOutsideParens(trimmed, 4) < 0;
                found = { kind: 'macro', ...macro };
            } else {
                const ui = findUiDirectiveBlock([text], 0);
                if (ui) found = findDefinitionInExpression(ui.tail);
            }
        } else {
            found = findDefinitionInExpression(text);
        }
        if (!found) continue;

        const definition: SourceDefinition = { ...found, line, endLine };
        for (let i = line; i <= endLine; i++)
            if (!byLine.has(i)) byLine.set(i, definition);
    }
    return byLine;
}

/** A {@link DefinitionResolver} over raw source text. */
export function buildSourceDefinitionResolver(lines: string[]): DefinitionResolver {
    const byLine = scanSourceDefinitions(lines);
    return (lineIndex: number) => byLine.get(lineIndex) ?? null;
}
