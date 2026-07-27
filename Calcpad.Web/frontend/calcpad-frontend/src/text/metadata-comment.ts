import { getIndentLength } from './comment-formatting';

/**
 * Parsed shape of a Calcpad definition-metadata comment
 * (`'<!--{...}-->`). Unknown keys are preserved via the index signature so a
 * round-trip through the editor UI doesn't drop properties it doesn't render.
 */
export interface MetadataCommentData {
    desc?: string;
    paramTypes?: string[];
    paramDesc?: string[];
    returnType?: string;
    LintIgnore?: string[];
    EndLintIgnore?: string[];
    NoPrintStart?: boolean;
    NoPrintEnd?: boolean;
    [key: string]: unknown;
}

/** Recognized value shape of the `#settings` directive JSON payload. */
export type SettingsValues = Record<string, string | number | boolean>;

/**
 * Location and parsed values of a `#settings` directive. `line`/`endLine` are its
 * 0-based opening/closing lines (equal when single-line), or null when the queried
 * position holds no directive. `layout` is set only for a multi-line directive.
 */
export interface SettingsDirective {
    line: number | null;
    endLine?: number;
    settings: SettingsValues;
    layout?: MetadataLayout;
}

/** One physical line of a metadata comment and the JSON keys it carries. */
export interface MetadataLayoutSegment {
    /** Leading whitespace of the physical line */
    indent: string;
    /** Top-level JSON keys living on this line, in document order */
    keys: string[];
}

/**
 * Physical line layout of a multi-line metadata comment, used to round-trip
 * edits without reflowing: existing keys stay on their line, new keys append to
 * the last line, and a line whose keys are all removed collapses away.
 */
export interface MetadataLayout {
    segments: MetadataLayoutSegment[];
}

/** A metadata comment occupying one or more document lines. */
export interface MetadataCommentBlock {
    /** 0-based line index where the comment opens */
    line: number;
    /** 0-based line index where the comment closes (equals {@link line} when single-line) */
    endLine: number;
    /** Leading whitespace of the opening line */
    indent: string;
    /** Closing comment quote (`'`) if the closing line ends with one, else '' */
    trailingQuote: string;
    /** Raw JSON between `<!--` and `-->`, continuation lines joined, trimmed */
    rawJson: string;
    /** Parsed object, or null when the JSON is malformed */
    data: MetadataCommentData | null;
    valid: boolean;
    /** Physical line layout, set only for multi-line comments. */
    layout?: MetadataLayout;
    /** Which properties actually apply to this line; set by the host. */
    context?: MetadataLineContext;
    /**
     * Parsed values of the `#settings` directive under the cursor, and its
     * 0-based line (null when the cursor isn't on a directive). Empty off a
     * directive line, so Apply creates a new one at the cursor instead of
     * editing a distant directive.
     */
    settings?: SettingsValues;
    settingsLine?: number | null;
    /** Closing line of a multi-line `#settings` directive (equals settingsLine when single-line). */
    settingsEndLine?: number | null;
    /** Physical line layout of a multi-line `#settings` directive. */
    settingsLayout?: MetadataLayout;
    /**
     * True when no comment exists yet and this block is a synthetic template for
     * the definition under the cursor. Applying it inserts a new line rather than
     * replacing {@link line}.
     */
    isNew?: boolean;
}

/** Kind of definition a metadata comment documents, or null when none follows. */
export type MetadataDefKind = 'variable' | 'function' | 'macro' | null;

/**
 * Describes which metadata properties are meaningful for a comment line, based
 * on the surrounding document. Used to hide fields that can't apply here.
 */
export interface MetadataLineContext {
    /**
     * Parameter count of the definition this comment documents: > 0 for a
     * function/macro, 0 for a plain variable/custom-unit definition, null when
     * no definition follows the comment.
     */
    paramCount: number | null;
    /**
     * Kind of definition the comment documents. Drives which fields the panel
     * offers: variables get no parameter fields, functions and macros each get
     * their own parameter-type vocabulary, and null lines only get the generic
     * (settings, lint) fields.
     */
    defKind: MetadataDefKind;
    /** True when a definition (any kind) follows the comment. */
    hasDefinition: boolean;
    /** True when an unclosed LintIgnore region is open at this line. */
    insideOpenLintRegion: boolean;
    /** True when an unclosed NoPrint region is open at this line. */
    insideOpenNoPrintRegion: boolean;
}

/** Valid `paramTypes` values for custom functions (f(x;y) = ...). */
export const FUNCTION_PARAM_TYPES = ['value', 'vector', 'matrix', 'any'] as const;

/** Valid `paramTypes` values for macros (#def) — TokenType enum names. */
export const MACRO_PARAM_TYPES = [
    'None', 'Const', 'Operator', 'Bracket', 'LineContinuation',
    'Variable', 'LocalVariable', 'Function', 'Macro', 'MacroParameter',
    'Units', 'Setting',
    'Keyword', 'ControlBlockKeyword', 'EndKeyword', 'Command',
    'Include', 'FilePath', 'DataExchangeKeyword',
    'Comment', 'HtmlComment', 'Tag', 'HtmlContent', 'JavaScript', 'Css', 'Svg',
    'Input', 'Format',
] as const;

/**
 * A selectable value for an `enum` setting: `value` is written verbatim into the
 * `#settings` JSON (must match what Calcpad.Core parses), `label` is the friendly
 * text shown in the dropdown.
 */
export interface SettingOption {
    value: string;
    label: string;
}

export interface MetadataSettingKey {
    key: string;
    /** Friendly name shown in the key dropdown; `key` is what's written to JSON. */
    label: string;
    detail: string;
    type: 'number' | 'boolean' | 'string' | 'enum';
    options?: SettingOption[];
    def: string | number | boolean;
    /** Inclusive lower bound for `number` values. Mirrors Calcpad.Core's SettingsDto. */
    min?: number;
    /** Inclusive upper bound for `number` values; omit for open-ended. */
    max?: number;
}

const COLOR_SCALE_OPTIONS: SettingOption[] = [
    { value: 'None', label: 'None' },
    { value: 'Gray', label: 'Grayscale' },
    { value: 'Rainbow', label: 'Rainbow' },
    { value: 'Terrain', label: 'Terrain' },
    { value: 'VioletToYellow', label: 'Violet → Yellow' },
    { value: 'GreenToYellow', label: 'Green → Yellow' },
    { value: 'Blues', label: 'Blues' },
    { value: 'BlueToYellow', label: 'Blue → Yellow' },
    { value: 'BlueToRed', label: 'Blue → Red' },
    { value: 'PurpleToYellow', label: 'Purple → Yellow' },
];

/**
 * Recognized keys for the `settings` overrides object (the `#settings` directive).
 * Types and ranges mirror `Calcpad.Core`'s `SettingsDto` so the panel rejects the
 * same values the engine would reject. Keep in sync with `Settings.cs`.
 */
export const METADATA_SETTINGS_KEYS: MetadataSettingKey[] = [
    { key: 'decimals', label: 'Decimals', detail: 'Decimal places in output (0 to 15)', type: 'number', def: 4, min: 0, max: 15 },
    {
        key: 'degrees', label: 'Angle units', detail: 'Angle unit: 0=radians, 1=degrees, 2=gradians', type: 'enum', def: 0,
        options: [{ value: '0', label: 'Radians' }, { value: '1', label: 'Degrees' }, { value: '2', label: 'Gradians' }],
    },
    { key: 'complex', label: 'Complex numbers', detail: 'Enable complex number mode', type: 'boolean', def: false },
    { key: 'substitute', label: 'Substitute variables', detail: 'Substitute variable values into expressions', type: 'boolean', def: true },
    { key: 'formatEquations', label: 'Format equations', detail: 'Format equations in output', type: 'boolean', def: true },
    { key: 'zeroSmallMatrixElements', label: 'Zero small matrix elements', detail: 'Zero out near-zero matrix elements', type: 'boolean', def: true },
    { key: 'maxOutputCount', label: 'Max output count', detail: 'Maximum output rows (5 to 100)', type: 'number', def: 20, min: 5, max: 100 },
    { key: 'units', label: 'Default length unit', detail: 'Unit system string', type: 'string', def: 'm' },
    { key: 'vectorGraphics', label: 'Vector graphics', detail: 'Render plots as SVG', type: 'boolean', def: false },
    { key: 'colorScale', label: 'Plot color scale', detail: 'Plot color scale', type: 'enum', def: 'Rainbow', options: COLOR_SCALE_OPTIONS },
    { key: 'smoothScale', label: 'Smooth color scale', detail: 'Smooth color scale transitions', type: 'boolean', def: false },
    { key: 'shadows', label: 'Plot shadows', detail: 'Enable 3-D plot shadows', type: 'boolean', def: true },
    { key: 'adaptivePlot', label: 'Adaptive plotting', detail: 'Use adaptive sampling for plots', type: 'boolean', def: true },
    { key: 'plotWidth', label: 'Plot width', detail: 'Width of the plot area in pixels (at least 1)', type: 'number', def: 500, min: 1 },
    { key: 'plotHeight', label: 'Plot height', detail: 'Height of the plot area in pixels (at least 1)', type: 'number', def: 300, min: 1 },
    { key: 'plotStep', label: 'Plot mesh step', detail: 'Mesh size for map plotting in pixels (at least 0; 0 = auto)', type: 'number', def: 0, min: 0 },
    { key: 'precision', label: 'Numerical precision', detail: 'Relative precision for numerical methods (1e-15 to 1e-2)', type: 'number', def: 1e-14, min: 1e-15, max: 1e-2 },
    { key: 'tol', label: 'Solver tolerance', detail: 'Target tolerance for the iterative PCG/eigensolver (1e-15 to 1e-2)', type: 'number', def: 1e-6, min: 1e-15, max: 1e-2 },
];

/** Looks up the definition for a `#settings` key. */
export function settingSpec(key: string): MetadataSettingKey | undefined {
    return METADATA_SETTINGS_KEYS.find(s => s.key === key);
}

function rangeMessage(spec: MetadataSettingKey): string {
    if (spec.min !== undefined && spec.max !== undefined) return `${spec.label} must be between ${spec.min} and ${spec.max}`;
    if (spec.min !== undefined) return `${spec.label} must be at least ${spec.min}`;
    if (spec.max !== undefined) return `${spec.label} must be at most ${spec.max}`;
    return '';
}

/**
 * Returns an error message when `value` is not valid for `key`, else `null`.
 * Mirrors the type/range checks in `Calcpad.Core`'s `SettingsDto.Validate`.
 */
export function validateSettingValue(key: string, value: string | number | boolean): string | null {
    const spec = settingSpec(key);
    if (!spec) return null;
    if (spec.type === 'enum')
        return spec.options?.some(o => o.value === String(value))
            ? null
            : `${spec.label} must be one of: ${spec.options?.map(o => o.label).join(', ')}`;
    if (spec.type === 'number') {
        const n = Number(value);
        if (String(value).trim() === '' || !Number.isFinite(n)) return `${spec.label} must be a number`;
        if (spec.min !== undefined && n < spec.min || spec.max !== undefined && n > spec.max) return rangeMessage(spec);
    }
    return null;
}

export interface LintCode {
    code: string;
    description: string;
}

/**
 * Linter diagnostic codes, mirroring Calcpad.Highlighter's ErrorCodes catalog.
 * Used to populate the lint-ignore multi-select. Keep in sync with
 * `Calcpad.Highlighter/Linter/Constants/ErrorCodes.cs`.
 */
export const LINT_CODES: LintCode[] = [
    { code: 'CPD-1101', description: 'Malformed #include statement' },
    { code: 'CPD-1102', description: 'Missing #include filename' },
    { code: 'CPD-2201', description: 'Duplicate macro definition' },
    { code: 'CPD-2202', description: "Macro name must end with '$'" },
    { code: 'CPD-2203', description: "Macro parameter must end with '$'" },
    { code: 'CPD-2204', description: 'Invalid macro name (must start with a letter)' },
    { code: 'CPD-2205', description: 'Malformed #def syntax' },
    { code: 'CPD-2206', description: 'Unmatched #def or #end def' },
    { code: 'CPD-2207', description: 'Nested macro definition not allowed' },
    { code: 'CPD-2208', description: 'Macro parameter must start with a letter' },
    { code: 'CPD-2209', description: 'Macro definition inside a control block has no effect' },
    { code: 'CPD-2210', description: 'Invalid character in macro name' },
    { code: 'CPD-2211', description: 'Invalid character in macro parameter' },
    { code: 'CPD-2212', description: 'Duplicate macro parameter' },
    { code: 'CPD-3101', description: 'Unmatched opening parenthesis' },
    { code: 'CPD-3102', description: 'Unmatched closing parenthesis' },
    { code: 'CPD-3103', description: 'Unmatched opening square bracket' },
    { code: 'CPD-3104', description: 'Unmatched closing square bracket' },
    { code: 'CPD-3105', description: 'Unmatched opening curly brace or control block' },
    { code: 'CPD-3106', description: 'Unmatched closing curly brace' },
    { code: 'CPD-3201', description: 'Invalid variable name (must start with a letter)' },
    { code: 'CPD-3202', description: 'Invalid function name' },
    { code: 'CPD-3203', description: 'Function name conflicts with a built-in function' },
    { code: 'CPD-3204', description: 'Variable name conflicts with a keyword' },
    { code: 'CPD-3205', description: 'Variable name conflicts with a built-in constant' },
    { code: 'CPD-3206', description: 'Function must have at least one parameter' },
    { code: 'CPD-3301', description: 'Undefined variable' },
    { code: 'CPD-3302', description: 'Function called with the wrong number of parameters' },
    { code: 'CPD-3303', description: 'Undefined macro' },
    { code: 'CPD-3304', description: 'Macro called with the wrong number of parameters' },
    { code: 'CPD-3305', description: 'Undefined function' },
    { code: 'CPD-3306', description: 'Invalid element access' },
    { code: 'CPD-3307', description: 'Too few parameters' },
    { code: 'CPD-3308', description: 'Too many parameters' },
    { code: 'CPD-3309', description: 'Parameter type mismatch' },
    { code: 'CPD-3310', description: 'Undefined unit' },
    { code: 'CPD-3311', description: 'Empty parameter in a function call' },
    { code: 'CPD-3312', description: 'Unused variable' },
    { code: 'CPD-3313', description: 'Redefinition of existing function' },
    { code: 'CPD-3401', description: 'Invalid operator usage' },
    { code: 'CPD-3402', description: 'Unknown command name' },
    { code: 'CPD-3403', description: 'Unknown directive' },
    { code: 'CPD-3404', description: 'Invalid assignment' },
    { code: 'CPD-3405', description: '# directive not allowed inside a command block' },
    { code: 'CPD-3406', description: 'Invalid command syntax' },
    { code: 'CPD-3407', description: 'Incomplete expression' },
    { code: 'CPD-3408', description: 'Command variable mismatch' },
    { code: 'CPD-3409', description: 'Reassignment of a constant' },
    { code: 'CPD-3410', description: 'Outer-scope assignment (←) to an undefined variable' },
    { code: 'CPD-3411', description: 'Invalid paramType value in a metadata comment' },
    { code: 'CPD-3412', description: 'Invalid metadata-comment JSON' },
    { code: 'CPD-3601', description: 'Invalid format specifier' },
];

const LEADING_WS = /^[ \t]*/;

/** Leading whitespace of a line. */
function leadingWhitespace(line: string): string {
    return line.match(LEADING_WS)?.[0] ?? '';
}

/** Drop a trailing `_` line-continuation marker (and its whitespace) from a fragment. */
function stripContinuation(fragment: string): string {
    const trimmed = fragment.replace(/\s+$/, '');
    return trimmed.endsWith('_') ? trimmed.slice(0, -1) : trimmed;
}

/** True when the line ends with a `_` line-continuation marker. */
function endsWithContinuation(line: string): boolean {
    return line.replace(/\s+$/, '').endsWith('_');
}

/** True when the line opens a metadata comment: a comment quote followed by `<!--`. */
function isMetadataOpener(line: string): boolean {
    const rest = line.slice(getIndentLength(line));
    const quote = rest[0];
    return (quote === "'" || quote === '"') && rest.includes('<!--');
}

/**
 * Locate the opening line of the `_`-continued block containing {@link cursorLine},
 * walking back over continuation lines until {@link isOpener} matches. Returns -1
 * when the cursor isn't on such a block (its opener or a continuation line).
 */
function findContinuationStart(lines: string[], cursorLine: number, isOpener: (line: string) => boolean): number {
    if (isOpener(lines[cursorLine])) return cursorLine;
    let i = cursorLine;
    while (i > 0 && endsWithContinuation(lines[i - 1])) i--;
    return i !== cursorLine && isOpener(lines[i]) ? i : -1;
}

/**
 * Scan a JSON object string and return each top-level key with the character
 * offset of its opening quote. Used to map keys onto the physical lines they sit
 * on for structure-preserving edits.
 */
function scanTopLevelKeys(json: string): { key: string; offset: number }[] {
    const keys: { key: string; offset: number }[] = [];
    let depth = 0, inString = false, escaped = false, expectKey = false, quoteStart = -1;
    for (let i = 0; i < json.length; i++) {
        const c = json[i];
        if (inString) {
            if (escaped) { escaped = false; continue; }
            if (c === '\\') { escaped = true; continue; }
            if (c === '"') {
                inString = false;
                if (depth === 1 && expectKey) {
                    let j = i + 1;
                    while (j < json.length && /\s/.test(json[j])) j++;
                    if (json[j] === ':') {
                        keys.push({ key: JSON.parse(json.slice(quoteStart, i + 1)), offset: quoteStart });
                        expectKey = false;
                    }
                }
            }
            continue;
        }
        if (c === '"') { inString = true; quoteStart = i; continue; }
        if (c === '{' || c === '[') { depth++; if (c === '{' && depth === 1) expectKey = true; continue; }
        if (c === '}' || c === ']') { depth--; continue; }
        if (depth === 1) {
            if (c === ',') expectKey = true;
            else if (c === ':') expectKey = false;
        }
    }
    return keys;
}

/** Parse a JSON object string into metadata data, or null when malformed. */
function parseMetadataJson(rawJson: string): { data: MetadataCommentData | null; valid: boolean } {
    try {
        const parsed = JSON.parse(rawJson);
        if (parsed && typeof parsed === 'object' && !Array.isArray(parsed)) {
            return { data: parsed as MetadataCommentData, valid: true };
        }
    } catch {
        // Malformed JSON — surfaced to the UI via valid === false
    }
    return { data: null, valid: false };
}

/**
 * Detect the metadata comment (`'<!--{...}-->`) that contains the cursor's line.
 * Mirrors the tokenizer/linter rules: the block opens with a comment quote
 * (`'` or `"`) followed by `<!--{`, and either closes with `-->` on the same
 * line or continues across `_`-terminated lines until `-->`. The cursor may sit
 * on any physical line of a multi-line block. Returns null when the cursor line
 * holds no such comment.
 */
export function findMetadataCommentBlock(lines: string[], cursorLine: number): MetadataCommentBlock | null {
    if (cursorLine < 0 || cursorLine >= lines.length) return null;

    const startLine = findContinuationStart(lines, cursorLine, isMetadataOpener);
    if (startLine < 0) return null;

    const openerText = lines[startLine];
    const indent = leadingWhitespace(openerText);
    const rest = openerText.slice(indent.length);
    const quote = rest[0];
    const afterOpen = rest.slice(rest.indexOf('<!--') + 4);

    const sameLineClose = afterOpen.indexOf('-->');
    if (sameLineClose >= 0) {
        const rawJson = afterOpen.slice(0, sameLineClose).trim();
        if (!rawJson.startsWith('{')) return null;
        const afterClose = afterOpen.slice(sameLineClose + 3).trim();
        const trailingQuote = afterClose === quote ? quote : '';
        const { data, valid } = parseMetadataJson(rawJson);
        return { line: startLine, endLine: startLine, indent, trailingQuote, rawJson, data, valid };
    }

    // Multi-line: collect one JSON fragment (and its indent) per physical line.
    const fragments = [stripContinuation(afterOpen)];
    const indents = [indent];
    let endLine = -1;
    let trailingQuote = '';
    for (let i = startLine + 1; i < lines.length; i++) {
        const line = lines[i];
        indents.push(leadingWhitespace(line));
        const closeIdx = line.indexOf('-->');
        if (closeIdx >= 0) {
            fragments.push(line.slice(0, closeIdx));
            const afterClose = line.slice(closeIdx + 3).trim();
            trailingQuote = afterClose === quote ? quote : '';
            endLine = i;
            break;
        }
        fragments.push(stripContinuation(line));
    }
    if (endLine < 0) return null;
    if (cursorLine > endLine) return null;

    const rawJson = fragments.join('\n').trim();
    if (!rawJson.startsWith('{')) return null;
    const { data, valid } = parseMetadataJson(fragments.join('\n'));

    return {
        line: startLine,
        endLine,
        indent,
        trailingQuote,
        rawJson,
        data,
        valid,
        layout: buildLayout(fragments, indents),
    };
}

/** Distribute a multi-line comment's top-level keys onto the physical lines they occupy. */
function buildLayout(fragments: string[], indents: string[]): MetadataLayout {
    const joined = fragments.join('\n');
    const segments: MetadataLayoutSegment[] = fragments.map((_, i) => ({ indent: indents[i], keys: [] }));

    const starts: number[] = [];
    let acc = 0;
    for (const fragment of fragments) {
        starts.push(acc);
        acc += fragment.length + 1; // +1 for the joining '\n'
    }
    const segmentOf = (offset: number): number => {
        let idx = 0;
        for (let i = 0; i < starts.length; i++) {
            if (offset >= starts[i]) idx = i;
            else break;
        }
        return idx;
    };

    for (const { key, offset } of scanTopLevelKeys(joined)) {
        segments[segmentOf(offset)].keys.push(key);
    }
    return { segments };
}

/**
 * Drop keys that shouldn't be serialized: undefined/null, empty strings, and
 * empty objects (e.g. an empty `settings`). Empty arrays are kept — they carry
 * meaning for the LintIgnore/EndLintIgnore region markers, and the panel never
 * emits empty paramTypes/paramDesc arrays in the first place.
 */
function cleanMetadata(data: MetadataCommentData): Record<string, unknown> {
    const clean: Record<string, unknown> = {};
    for (const [key, value] of Object.entries(data)) {
        if (value === undefined || value === null) continue;
        if (typeof value === 'string' && value.length === 0) continue;
        if (typeof value === 'object' && !Array.isArray(value) && Object.keys(value as object).length === 0) continue;
        clean[key] = value;
    }
    return clean;
}

/**
 * Serialize a cleaned key/value object across the physical lines described by
 * {@link layout}, wrapped by {@link opener}`{` … `}`{@link closer}. Existing keys
 * stay on their line, keys not on any line append to the last line, and a line
 * whose keys are all removed collapses away (its `_` continuation dropped).
 * Returns null when no keys remain, so callers can fall back to a single line.
 */
function serializeWithLayout(
    clean: Record<string, unknown>,
    layout: MetadataLayout,
    opener: string,
    closer: string,
): string | null {
    const present = new Set<string>();
    const segments = layout.segments.map(seg => ({
        indent: seg.indent,
        keys: seg.keys.filter(k => {
            if (k in clean) { present.add(k); return true; }
            return false;
        }),
    }));
    const added = Object.keys(clean).filter(k => !present.has(k));
    if (added.length) segments[segments.length - 1].keys.push(...added);

    const kept = segments.filter(seg => seg.keys.length > 0);
    if (kept.length === 0) return null;

    const renderKeys = (keys: string[]) =>
        keys.map(k => `${JSON.stringify(k)}:${JSON.stringify(clean[k])}`).join(',');

    let out = `${opener}{` + renderKeys(kept[0].keys);
    for (let i = 1; i < kept.length; i++) {
        out += `, _\n${kept[i].indent}` + renderKeys(kept[i].keys);
    }
    return out + `}${closer}`;
}

/**
 * Build a metadata-comment from a data object, preserving the original
 * indentation and trailing comment quote. When {@link layout} describes an
 * existing multi-line comment, edits round-trip in place: existing keys keep
 * their line, new keys append to the last line, and a line whose keys are all
 * removed collapses away (its `_` continuation dropped). Without a layout — a
 * new or single-line comment — everything serializes onto one line.
 */
export function serializeMetadataComment(
    data: MetadataCommentData,
    indent = '',
    trailingQuote = '',
    layout?: MetadataLayout,
): string {
    const clean = cleanMetadata(data);
    const singleLine = `${indent}'<!--${JSON.stringify(clean)}-->${trailingQuote}`;
    if (!layout || layout.segments.length === 0) return singleLine;
    return serializeWithLayout(clean, layout, `${indent}'<!--`, `-->${trailingQuote}`) ?? singleLine;
}

const SETTINGS_OPEN_RE = /^\s*#settings\b\s*/i;

/**
 * Parse the `#settings` directive containing the 0-based {@link line}, or null
 * when that line isn't part of one. Like a metadata comment, the directive's JSON
 * may span multiple physical lines via `_` continuation, and the cursor can sit
 * on any of them. `#settings` directives are cursor-local: the panel edits the
 * one the cursor sits on and can create new ones elsewhere, so a document may
 * hold several (each applies to the lines below it). A present-but-malformed
 * directive returns its span with empty settings so Apply overwrites it in place.
 */
export function settingsDirectiveOnLine(lines: string[], line: number): SettingsDirective | null {
    if (line < 0 || line >= lines.length) return null;

    const start = findContinuationStart(lines, line, l => SETTINGS_OPEN_RE.test(l));
    if (start < 0) return null;

    const openerLen = lines[start].match(SETTINGS_OPEN_RE)![0].length;
    const fragments = [stripContinuation(lines[start].slice(openerLen))];
    const indents = [leadingWhitespace(lines[start])];
    let endLine = start;
    if (endsWithContinuation(lines[start])) {
        for (let i = start + 1; i < lines.length; i++) {
            indents.push(leadingWhitespace(lines[i]));
            fragments.push(stripContinuation(lines[i]));
            endLine = i;
            if (!endsWithContinuation(lines[i])) break;
        }
    }
    if (line > endLine) return null;

    const joined = fragments.join('\n');
    try {
        const parsed = JSON.parse(joined);
        if (parsed && typeof parsed === 'object' && !Array.isArray(parsed))
            return { line: start, endLine, settings: parsed as SettingsValues, layout: buildLayout(fragments, indents) };
    } catch {
        // Malformed JSON — surface the span so Apply replaces it.
    }
    return { line: start, endLine, settings: {} };
}

/**
 * Build a `#settings` directive from a settings object. With {@link layout} the
 * directive round-trips a multi-line block in place (see {@link serializeWithLayout});
 * otherwise it serializes onto one line.
 */
export function serializeSettingsDirective(settings: SettingsValues, layout?: MetadataLayout): string {
    const singleLine = `#settings ${JSON.stringify(settings)}`;
    if (!layout || layout.segments.length === 0) return singleLine;
    return serializeWithLayout(settings, layout, '#settings ', '') ?? singleLine;
}

/** A recognized definition line and how many parameters it declares. */
export interface MetadataDefinition {
    kind: Exclude<MetadataDefKind, null>;
    paramCount: number;
}

/**
 * Resolves the definition declared on a 0-based document line, or null when the
 * line isn't a definition. Backed by real highlighter results (see
 * {@link buildDefinitionResolver}) so identifier rules — Unicode names, custom
 * units, command-block functions — match the engine exactly.
 */
export type DefinitionResolver = (lineIndex: number) => MetadataDefinition | null;

/**
 * Build a {@link DefinitionResolver} from the highlighter's definitions response.
 * Only local definitions are indexed (included files live on other lines). Custom
 * units are reported as variables, matching how the metadata panel treats them.
 */
export function buildDefinitionResolver(definitions: {
    functions: { lineNumber: number; parameters?: string[]; source?: string }[];
    macros: { lineNumber: number; parameters?: string[]; source?: string }[];
    variables: { lineNumber: number; source?: string }[];
    customUnits: { lineNumber: number; source?: string }[];
}): DefinitionResolver {
    const byLine = new Map<number, MetadataDefinition>();
    const isLocal = (source?: string) => source === undefined || source === 'local';
    for (const v of definitions.variables)
        if (isLocal(v.source)) byLine.set(v.lineNumber, { kind: 'variable', paramCount: 0 });
    for (const u of definitions.customUnits)
        if (isLocal(u.source)) byLine.set(u.lineNumber, { kind: 'variable', paramCount: 0 });
    for (const f of definitions.functions)
        if (isLocal(f.source)) byLine.set(f.lineNumber, { kind: 'function', paramCount: f.parameters?.length ?? 0 });
    for (const m of definitions.macros)
        if (isLocal(m.source)) byLine.set(m.lineNumber, { kind: 'macro', paramCount: m.parameters?.length ?? 0 });
    return (lineIndex: number) => byLine.get(lineIndex) ?? null;
}

/**
 * Analyze which metadata properties apply to a comment on the given line by
 * inspecting the document around it: the definition it documents (the next
 * non-blank, non-comment line) and whether a LintIgnore region opened earlier
 * is still open here. The definition kind/param-count come from real highlighter
 * results via {@link resolveDefinition}, not from parsing the line text.
 */
export function analyzeMetadataLine(
    lines: string[],
    commentLine: number,
    resolveDefinition: DefinitionResolver,
): MetadataLineContext {
    let definition: MetadataDefinition | null = null;
    for (let i = commentLine + 1; i < lines.length; i++) {
        const t = lines[i].trim();
        if (t === '') continue;
        // Skip stacked comment/metadata lines above the definition.
        if (t.startsWith("'") || t.startsWith('"')) continue;
        definition = resolveDefinition(i);
        break;
    }

    let insideOpenLintRegion = false;
    let insideOpenNoPrintRegion = false;
    for (let i = 0; i < commentLine; i++) {
        const block = findMetadataCommentBlock(lines, i);
        if (!block?.valid || !block.data) continue;
        if (block.data.EndLintIgnore !== undefined) insideOpenLintRegion = false;
        if (block.data.LintIgnore !== undefined) insideOpenLintRegion = true;
        if (block.data.NoPrintEnd !== undefined) insideOpenNoPrintRegion = false;
        if (block.data.NoPrintStart !== undefined) insideOpenNoPrintRegion = true;
    }

    return {
        paramCount: definition?.paramCount ?? null,
        defKind: definition?.kind ?? null,
        hasDefinition: definition !== null,
        insideOpenLintRegion,
        insideOpenNoPrintRegion,
    };
}

/**
 * Resolve the metadata comment the panel should edit for the cursor line.
 * Returns the existing comment when the cursor sits on it or on a definition it
 * documents. When the cursor is on a definition with no comment yet, returns a
 * synthetic {@link MetadataCommentBlock} (isNew) describing the comment that
 * Apply would create above that definition, so the panel can surface the
 * relevant fields immediately. Returns null when the cursor is on neither a
 * metadata comment nor a definition.
 */
export function computeMetadataBlock(
    lines: string[],
    cursorLine: number,
    resolveDefinition: DefinitionResolver,
): MetadataCommentBlock | null {
    const block = computeCommentBlock(lines, cursorLine, resolveDefinition);
    if (block) {
        // Bind the settings section to the directive under the cursor (if any).
        // Off a `#settings` line it's empty, so the panel returns to its basic
        // state and Apply creates a new directive rather than editing a distant one.
        const directive = settingsDirectiveOnLine(lines, cursorLine);
        block.settings = directive?.settings ?? {};
        block.settingsLine = directive?.line ?? null;
        block.settingsEndLine = directive?.endLine ?? directive?.line ?? null;
        block.settingsLayout = directive?.layout;
    }
    return block;
}

function computeCommentBlock(
    lines: string[],
    cursorLine: number,
    resolveDefinition: DefinitionResolver,
): MetadataCommentBlock | null {
    const existing = findMetadataCommentBlock(lines, cursorLine);
    if (existing) {
        existing.context = analyzeMetadataLine(lines, cursorLine, resolveDefinition);
        return existing;
    }

    if (cursorLine < 0 || cursorLine >= lines.length) return null;

    const indent = lines[cursorLine].match(/^[ \t]*/)?.[0] ?? '';

    if (resolveDefinition(cursorLine)) {
        // A metadata comment directly above the definition takes precedence.
        if (cursorLine > 0) {
            const above = findMetadataCommentBlock(lines, cursorLine - 1);
            if (above) {
                above.context = analyzeMetadataLine(lines, cursorLine - 1, resolveDefinition);
                return above;
            }
        }
        return {
            line: cursorLine,
            endLine: cursorLine,
            indent,
            trailingQuote: '',
            rawJson: '',
            data: {},
            valid: true,
            isNew: true,
            context: analyzeMetadataLine(lines, cursorLine - 1, resolveDefinition),
        };
    }

    // Null case: the cursor is on neither a definition nor a comment. Offer the
    // region markers (settings, lint, no-print) that apply to a bare line, and on
    // Apply insert a new comment on the line above the cursor. defKind is forced
    // null so the definition-oriented fields stay hidden; only the region state
    // (open lint/no-print regions) from the surrounding document is kept.
    const region = analyzeMetadataLine(lines, cursorLine, resolveDefinition);
    return {
        line: cursorLine,
        endLine: cursorLine,
        indent,
        trailingQuote: '',
        rawJson: '',
        data: {},
        valid: true,
        isNew: true,
        context: { ...region, paramCount: null, defKind: null, hasDefinition: false },
    };
}
