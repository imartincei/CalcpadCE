import type { SettingOption } from '../types/catalog';

/** Parsed JSON attributes of a `#UI {...} name = value` directive. */
export interface UiDirectiveData {
    type?: string;
    mode?: string;
    style?: string;
    reportStyle?: string;
    rows?: number;
    columns?: number;
    columnHeaders?: string[];
    rowHeaders?: string[];
    keys?: string[];
    values?: string[];
}

/**
 * A `#UI` directive found at a cursor line. Always single-line — `#UI` has no
 * `_`-continuation support, unlike metadata comments and `#settings`.
 */
export interface UiDirectiveBlock {
    /** 0-based line the directive is on. */
    line: number;
    /** Leading whitespace of the line. */
    indent: string;
    /** Exact "#UI"/"#ui" text as typed, preserved verbatim on serialization. */
    keyword: string;
    /** Raw JSON between the braces, or '' when the line has no JSON block. */
    rawJson: string;
    /** Parsed object; null only when a JSON block is present but malformed/unclosed. */
    data: UiDirectiveData | null;
    valid: boolean;
    /**
     * Everything after the JSON block (or after the keyword, when there is
     * none) — the assignment(s)/labels, preserved byte-for-byte and never
     * parsed. Always includes the whitespace separating it from what precedes
     * it, so serialization can just concatenate the pieces back together.
     */
    tail: string;
}

/** Leading whitespace of a line. */
function leadingWhitespace(line: string): string {
    return line.match(/^[ \t]*/)?.[0] ?? '';
}

/**
 * Detect the `#UI` directive on {@link cursorLine}, mirroring
 * `ExpressionParser.ParseKeywordUi`'s brace handling: the JSON block, if
 * present, runs from the first `{` after the keyword to the first `}` after
 * that (non-nested search — none of the recognized keys' values can contain a
 * brace). Returns null when the line isn't a `#UI` directive.
 */
export function findUiDirectiveBlock(lines: string[], cursorLine: number): UiDirectiveBlock | null {
    if (cursorLine < 0 || cursorLine >= lines.length) return null;

    const line = lines[cursorLine];
    const indent = leadingWhitespace(line);
    const rest = line.slice(indent.length);
    if (rest.slice(0, 3).toLowerCase() !== '#ui') return null;

    const keyword = rest.slice(0, 3);
    let cursor = 3;
    while (rest[cursor] === ' ') cursor++;

    if (rest[cursor] !== '{') {
        return { line: cursorLine, indent, keyword, rawJson: '', data: {}, valid: true, tail: rest.slice(3) };
    }

    const braceEnd = rest.indexOf('}', cursor);
    if (braceEnd < 0) {
        return { line: cursorLine, indent, keyword, rawJson: rest.slice(cursor), data: null, valid: false, tail: '' };
    }

    const rawJson = rest.slice(cursor, braceEnd + 1);
    const tail = rest.slice(braceEnd + 1);
    let data: UiDirectiveData | null = null;
    let valid = false;
    try {
        const parsed = JSON.parse(rawJson);
        if (parsed && typeof parsed === 'object' && !Array.isArray(parsed)) {
            data = parsed as UiDirectiveData;
            valid = true;
        }
    } catch {
        // Malformed JSON — surfaced to the UI via valid === false
    }
    return { line: cursorLine, indent, keyword, rawJson, data, valid, tail };
}

/**
 * A `#UI` line: the keyword as the line's first token, followed by something for it to describe.
 * The anchoring matches {@link findUiDirectiveBlock} and `ExpressionParser.ParseKeywordUi`, so a
 * commented line never counts, `#UIx` counts exactly as the engine counts it, and a bare `#UI`
 * produces no control and does not count either.
 */
const UI_DIRECTIVE_LINE = /^[﻿ \t]*#ui[ \t]*\S/im;

/**
 * True when the source declares at least one `#UI` control, which is what makes a document
 * one to fill in rather than to read. Scans this source alone: following `#include` would
 * mean reading files on every open, so a document whose only `#UI` lines are in an included
 * file is not recognized as one.
 */
export function documentHasUiDirectives(source: string): boolean {
    return UI_DIRECTIVE_LINE.test(source);
}

/** Drop keys that shouldn't be serialized: undefined/null, empty strings, empty arrays. */
function cleanUiData(data: UiDirectiveData): Record<string, unknown> {
    const clean: Record<string, unknown> = {};
    for (const [key, value] of Object.entries(data)) {
        if (value === undefined || value === null) continue;
        if (typeof value === 'string' && value.length === 0) continue;
        if (Array.isArray(value) && value.length === 0) continue;
        clean[key] = value;
    }
    return clean;
}

/**
 * Rebuild a `#UI` directive line from {@link data}, reusing {@link block}'s
 * indent/keyword/tail so everything but the JSON block is preserved exactly.
 * With no keys left, the JSON block is dropped entirely (`tail` already
 * carries the whitespace that separated it from the assignment).
 */
export function serializeUiDirective(
    data: UiDirectiveData,
    block: Pick<UiDirectiveBlock, 'indent' | 'keyword' | 'tail'>,
): string {
    const clean = cleanUiData(data);
    if (Object.keys(clean).length === 0) return block.indent + block.keyword + block.tail;
    return `${block.indent}${block.keyword} ${JSON.stringify(clean)}${block.tail}`;
}

/** Recognized value shape of the `#UI` JSON block's scalar keys. */
export interface UiPropertyKey {
    key: string;
    label: string;
    detail: string;
    type: 'string' | 'number' | 'enum';
    options?: SettingOption[];
    min?: number;
}

/**
 * Scalar `#UI` JSON keys, shaped like `MetadataSettingKey` so the panel can drive them with the
 * same typed-input logic as the `#settings` editor; the array-valued keys get bespoke list
 * editors instead. Keep in sync with `UiValidator.KnownKeys`/`KnownTypes`.
 */
export const UI_PROPERTY_KEYS: UiPropertyKey[] = [
    {
        key: 'type', label: 'Control type', detail: 'Widget type; auto-detected from the expression when left unset.',
        type: 'enum',
        options: [
            { value: '', label: '(auto-detect)' },
            { value: 'entry', label: 'Entry' },
            { value: 'datagrid', label: 'Datagrid' },
            { value: 'dropdown', label: 'Dropdown' },
            { value: 'radio', label: 'Radio' },
            { value: 'checkbox', label: 'Checkbox' },
        ],
    },
    {
        key: 'mode', label: 'Mode', detail: 'Only "number" is supported; #UI does not support string variables.',
        type: 'enum',
        options: [
            { value: '', label: '(default)' },
            { value: 'number', label: 'number' },
        ],
    },
    { key: 'style', label: 'Style class', detail: 'Extra CSS class applied to the input widget.', type: 'string' },
    { key: 'reportStyle', label: 'Report style class', detail: 'Extra CSS class applied in #post/report mode.', type: 'string' },
    { key: 'rows', label: 'Rows', detail: 'Datagrid row count (auto-detected when unset).', type: 'number', min: 0 },
    { key: 'columns', label: 'Columns', detail: 'Datagrid column count (auto-detected when unset).', type: 'number', min: 0 },
];
