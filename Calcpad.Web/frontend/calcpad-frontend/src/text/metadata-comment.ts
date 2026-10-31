import { getIndentLength } from './comment-formatting';
import { findUiDirectiveBlock } from './ui-directive';
import type { UiDirectiveBlock } from './ui-directive';
import type { PdfSettings } from '../types/pdf-settings';

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
    pdf?: PdfCommentValues;
    [key: string]: unknown;
}

/**
 * PDF export settings a document pins for itself, via the `pdf` key of a
 * metadata comment. Every field is optional: whatever the document doesn't state
 * falls back to the host's stored defaults, key by key.
 */
export type PdfCommentValues = Partial<PdfSettings>;

/** Recognized value shape of the `#settings` directive JSON payload. */
export type SettingsValues = Record<string, string | number | boolean>;

/**
 * Location and parsed values of a `#settings` directive. `line`/`endLine` are its 0-based
 * opening/closing lines (equal when single-line, null when the position holds no directive),
 * and `layout` is set only for a multi-line directive.
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
    /**
     * The `#UI` directive at the cursor's line, if any. Independent of
     * {@link line}/{@link endLine} (which belong to the metadata comment) —
     * a `#UI` line and a metadata comment/`#settings` directive never share a
     * physical line, so the three bindings never conflict.
     */
    uiDirective?: UiDirectiveBlock | null;
    /** Key of the document this block was computed from; set by the host. */
    docKey?: string;
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
    /** Name of that definition, null when none follows. Identifies the panel's target. */
    defName: string | null;
    /** True when a definition (any kind) follows the comment. */
    hasDefinition: boolean;
    /** True when an unclosed LintIgnore region is open at this line. */
    insideOpenLintRegion: boolean;
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

export interface LintCode {
    code: string;
    description: string;
}

/**
 * Linter diagnostic codes, mirroring Calcpad.Highlighter's ErrorCodes catalog, used to
 * populate the lint-ignore multi-select. Keep in sync with
 * `Calcpad.Highlighter/Linter/Constants/ErrorCodes.cs`.
 */
export const LINT_CODES: LintCode[] = [
    { code: 'CPD-1101', description: 'Malformed #include statement' },
    { code: 'CPD-1102', description: 'Missing #include filename' },
    { code: 'CPD-1103', description: 'Deprecated #include input values' },
    { code: 'CPD-1104', description: 'Include file not found' },
    { code: 'CPD-1105', description: 'Circular #include' },
    { code: 'CPD-1106', description: 'Invalid #ProjectPath or #LibraryPath' },
    { code: 'CPD-2200', description: 'Macro or include error' },
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
    { code: 'CPD-2213', description: 'Required parameter after optional parameter' },
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
    { code: 'CPD-3207', description: 'Required parameter after optional parameter in function definition' },
    { code: 'CPD-3301', description: 'Undefined variable' },
    { code: 'CPD-3302', description: 'Function called with the wrong number of parameters' },
    { code: 'CPD-3303', description: 'Undefined macro' },
    { code: 'CPD-3304', description: 'Macro called with the wrong number of parameters' },
    { code: 'CPD-3305', description: 'Undefined function' },
    { code: 'CPD-3307', description: 'Too few parameters' },
    { code: 'CPD-3308', description: 'Too many parameters' },
    { code: 'CPD-3310', description: 'Undefined unit' },
    { code: 'CPD-3311', description: 'Empty parameter in a function call' },
    { code: 'CPD-3312', description: 'Unused variable' },
    { code: 'CPD-3313', description: 'Redefinition of existing function' },
    { code: 'CPD-3314', description: 'Unknown keyword argument' },
    { code: 'CPD-3315', description: 'Unknown keyword argument in function call' },
    { code: 'CPD-3316', description: 'Duplicate keyword argument in a macro call' },
    { code: 'CPD-3317', description: 'Circular macro reference' },
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
    { code: 'CPD-3413', description: 'Invalid #settings JSON' },
    { code: 'CPD-3414', description: 'Invalid PDF settings in a metadata comment' },
    { code: 'CPD-3415', description: 'Invalid #UI format' },
    { code: 'CPD-3416', description: "'uiOverrides' metadata comment not at the end of the file" },
    { code: 'CPD-3417', description: "Duplicate 'uiOverrides' metadata comment" },
    { code: 'CPD-3418', description: "'uiOverrides' sharing a comment with another key" },
    { code: 'CPD-3419', description: 'Deprecated stored input value' },
    { code: 'CPD-3420', description: 'Deprecated #input keyword' },
    { code: 'CPD-3421', description: "'fileInfo' metadata comment not on the last line" },
    { code: 'CPD-3601', description: 'Invalid format specifier' },
    { code: 'CPD-5101', description: 'Cannot evaluate literal' },
    { code: 'CPD-5102', description: 'Expression is empty' },
    { code: 'CPD-5103', description: 'Missing operand' },
    { code: 'CPD-5104', description: 'Cannot evaluate as type' },
    { code: 'CPD-5105', description: 'Invalid expression (stack leak)' },
    { code: 'CPD-5106', description: 'Invalid expression (empty stack)' },
    { code: 'CPD-5107', description: 'Undefined input field' },
    { code: 'CPD-5108', description: 'Cannot parse number' },
    { code: 'CPD-5109', description: 'More than one assignment' },
    { code: 'CPD-5110', description: 'Unexpected delimiter' },
    { code: 'CPD-5111', description: 'Missing delimiter in function definition' },
    { code: 'CPD-5112', description: 'Duplicate keyword argument in a function call' },
    { code: 'CPD-5113', description: 'Missing item in a $Plot or $Map command' },
    { code: 'CPD-5120', description: 'More than one delimiter in a plot command' },
    { code: 'CPD-5114', description: 'Bracket mismatch' },
    { code: 'CPD-5115', description: 'Function definition inside a solver block' },
    { code: 'CPD-5116', description: 'Missing file name' },
    { code: 'CPD-5117', description: 'Invalid data type' },
    { code: 'CPD-5118', description: 'Missing matrix index' },
    { code: 'CPD-5119', description: 'Invalid number of indexes' },
    { code: 'CPD-5121', description: 'Invalid #for header' },
    { code: 'CPD-5201', description: 'Condition cannot be empty' },
    { code: 'CPD-5202', description: 'Duplicate #else' },
    { code: 'CPD-5203', description: '#else if after #else' },
    { code: 'CPD-5204', description: 'Blocks nested too deeply' },
    { code: 'CPD-5301', description: 'Cannot evaluate as a function' },
    { code: 'CPD-5302', description: 'Cannot evaluate as a function or operator' },
    { code: 'CPD-5303', description: 'Cannot redefine existing units' },
    { code: 'CPD-5304', description: 'Right side must be constant' },
    { code: 'CPD-5305', description: 'Recursion not allowed in function definition' },
    { code: 'CPD-5306', description: 'Circular function reference' },
    { code: 'CPD-5401', description: 'Power must be unitless' },
    { code: 'CPD-5402', description: 'Right side does not evaluate to units' },
    { code: 'CPD-5403', description: 'Inconsistent units' },
    { code: 'CPD-5404', description: 'Invalid units for function' },
    { code: 'CPD-5405', description: 'Root index must be unitless' },
    { code: 'CPD-5406', description: 'Root index cannot be complex' },
    { code: 'CPD-5407', description: 'Root index must be an integer greater than 1' },
    { code: 'CPD-5408', description: 'Factorial argument out of range' },
    { code: 'CPD-5409', description: 'Factorial argument must be unitless' },
    { code: 'CPD-5410', description: 'Factorial argument must be a positive integer' },
    { code: 'CPD-5411', description: 'Factorial argument cannot be complex' },
    { code: 'CPD-5412', description: 'Remainder denominator must be unitless' },
    { code: 'CPD-5413', description: 'Both values must be integers' },
    { code: 'CPD-5414', description: 'Calculated units inconsistent with target units' },
    { code: 'CPD-5415', description: 'Inconsistent units for assigned value' },
    { code: 'CPD-5416', description: 'Inconsistent units for assigned value' },
    { code: 'CPD-5417', description: 'Result is not a real number' },
    { code: 'CPD-5418', description: 'Argument out of range' },
    { code: 'CPD-5419', description: 'Condition cannot be complex' },
    { code: 'CPD-5420', description: 'Condition result is invalid' },
    { code: 'CPD-5421', description: 'Plot limits are identical' },
    { code: 'CPD-5422', description: 'Inconsistent units in operation' },
    { code: 'CPD-5423', description: 'Units raised to a complex power' },
    { code: 'CPD-5424', description: 'Cannot evaluate remainder' },
    { code: 'CPD-5501', description: 'Index out of range' },
    { code: 'CPD-5502', description: 'Cannot assign vector to scalar' },
    { code: 'CPD-5503', description: 'Value must be scalar' },
    { code: 'CPD-5504', description: 'Value must be real' },
    { code: 'CPD-5505', description: 'Value must be a positive integer' },
    { code: 'CPD-5506', description: 'Value must be a matrix' },
    { code: 'CPD-5507', description: 'Value must be a vector' },
    { code: 'CPD-5508', description: 'Step cannot be zero' },
    { code: 'CPD-5509', description: 'Vector size limit exceeded' },
    { code: 'CPD-5510', description: 'Cross product needs 2 or 3 element vectors' },
    { code: 'CPD-5511', description: 'Function not defined in complex mode' },
    { code: 'CPD-5512', description: 'Matrix dimensions do not match' },
    { code: 'CPD-5513', description: 'Matrix must be square' },
    { code: 'CPD-5514', description: 'Lp norm needs p >= 1' },
    { code: 'CPD-5515', description: 'Matrix must be symmetric' },
    { code: 'CPD-5516', description: 'Matrix is not positive definite' },
    { code: 'CPD-5517', description: 'Matrix is singular' },
    { code: 'CPD-5518', description: 'Matrix is singular or close to singular' },
    { code: 'CPD-5519', description: 'Vectors and matrices not supported in complex mode' },
    { code: 'CPD-5520', description: 'Cannot interpolate with a non-scalar value' },
    { code: 'CPD-5521', description: 'Jacobi iteration failed' },
    { code: 'CPD-5522', description: 'Invalid operand' },
    { code: 'CPD-5523', description: 'Invalid argument' },
    { code: 'CPD-5524', description: 'Argument is not an eigenvalue' },
    { code: 'CPD-5525', description: 'QL algorithm failed to converge' },
    { code: 'CPD-5526', description: 'Matrix size limit exceeded' },
    { code: 'CPD-5527', description: 'Embedded data size limit exceeded' },
    { code: 'CPD-5528', description: 'Datagrid size limit exceeded' },
    { code: 'CPD-5529', description: 'Matrix needs at least as many rows as columns' },
    { code: 'CPD-5530', description: 'Variable is not a matrix' },
    { code: 'CPD-5531', description: 'Value must be a high-performance matrix' },
    { code: 'CPD-5532', description: 'Value must be a high-performance vector' },
    { code: 'CPD-5533', description: 'FFT input needs one or two rows' },
    { code: 'CPD-5534', description: 'Matrix is ill-conditioned' },
    { code: 'CPD-5535', description: 'PCG solver failed to converge' },
    { code: 'CPD-5536', description: 'SVD failed to converge' },
    { code: 'CPD-5537', description: 'Complex value outside of complex mode' },
    { code: 'CPD-5601', description: 'Cannot evaluate the function for the solver variable' },
    { code: 'CPD-5602', description: 'Function not defined for the solver variable' },
    { code: 'CPD-5603', description: 'Solver limits out of range' },
    { code: 'CPD-5604', description: 'No solution' },
    { code: 'CPD-5701', description: 'Cannot write PNG file' },
    { code: 'CPD-5702', description: 'Cannot write SVG file' },
    { code: 'CPD-5703', description: 'Cannot convert PNG to Base64' },
    { code: 'CPD-5704', description: 'File not found' },
    { code: 'CPD-5705', description: 'Path not found' },
    { code: 'CPD-5706', description: 'File format not supported' },
    { code: 'CPD-5707', description: 'Cannot read or write data file' },
    { code: 'CPD-5900', description: 'Unexpected error' },
    { code: 'CPD-5901', description: 'Interrupted by user' },
    { code: 'CPD-5902', description: 'Calculations not active' },
    { code: 'CPD-5903', description: 'Possibly infinite loop' },
    { code: 'CPD-5904', description: 'Too many errors' },
    { code: 'CPD-5905', description: 'Iteration limit exceeded' },
    { code: 'CPD-5906', description: 'Output too long' },
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
 * Detect the metadata comment (`'<!--{...}-->`) that contains the cursor's line, mirroring the
 * tokenizer/linter rules: it opens with a comment quote followed by `<!--{` and either closes
 * with `-->` on the same line or continues across `_`-terminated lines. The cursor may sit on
 * any physical line of a multi-line block; returns null when the line holds no such comment.
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
 * True when {@link data} still serializes to something. Keys that clean away — an
 * emptied object among them — leave a comment with nothing in it, which callers
 * remove rather than write out as `'<!--{}-->'`.
 */
export function hasMetadataContent(data: MetadataCommentData): boolean {
    return Object.keys(cleanMetadata(data)).length > 0;
}

/**
 * Serialize a cleaned key/value object across the physical lines described by {@link layout},
 * wrapped by {@link opener}`{` … `}`{@link closer}: existing keys stay on their line, new ones
 * append to the last, and a line whose keys are all removed collapses away. Returns null when no
 * keys remain, so callers can fall back to a single line.
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
 * Build a metadata-comment from a data object, preserving the original indentation and
 * trailing comment quote. With {@link layout} an existing multi-line comment round-trips in
 * place (see {@link serializeWithLayout}); without one, everything serializes onto a single
 * line.
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
 * Parse the `#settings` directive containing the 0-based {@link line}, or null when that line
 * isn't part of one; like a metadata comment it may span several physical lines via `_`
 * continuation. Directives are cursor-local, so a document may hold several (each applying to
 * the lines below it), and a present-but-malformed one returns its span with empty settings so
 * Apply overwrites it in place.
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

/**
 * Collect the PDF settings the document pins for itself: the `pdf` object of every metadata
 * comment, shallow-merged in document order so a later comment overrides an earlier one key by
 * key. Returns an empty object when the document pins nothing, letting the caller fall back to
 * the host's stored defaults.
 */
export function pdfSettingsFromDocument(lines: string[]): PdfCommentValues {
    const merged: Record<string, unknown> = {};
    for (let i = 0; i < lines.length; i++) {
        const block = findMetadataCommentBlock(lines, i);
        // Only the opening line contributes, so a multi-line comment isn't merged once per line.
        if (!block?.valid || block.line !== i) continue;
        const pdf = block.data?.pdf;
        if (pdf && typeof pdf === 'object' && !Array.isArray(pdf)) Object.assign(merged, pdf);
    }
    return merged as PdfCommentValues;
}

/** A recognized definition line and how many parameters it declares. */
export interface MetadataDefinition {
    kind: Exclude<MetadataDefKind, null>;
    name: string;
    paramCount: number;
    /** 0-based first physical line of the statement, where its comment belongs. */
    line: number;
}

/**
 * Resolves the definition declared on a 0-based document line, or null when the
 * line isn't a definition. Backed by a scan of the raw source (see
 * `buildSourceDefinitionResolver`), so a `#def` reports the macro itself and a
 * macro call reports nothing.
 */
export type DefinitionResolver = (lineIndex: number) => MetadataDefinition | null;

/**
 * Analyze which metadata properties apply to a comment on the given line by
 * inspecting the document around it: the definition it documents (the next
 * non-blank, non-comment line) and whether a LintIgnore region opened earlier
 * is still open here.
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
    for (let i = 0; i < commentLine; i++) {
        const block = findMetadataCommentBlock(lines, i);
        if (!block?.valid || !block.data) continue;
        if (block.data.EndLintIgnore !== undefined) insideOpenLintRegion = false;
        if (block.data.LintIgnore !== undefined) insideOpenLintRegion = true;
    }

    return {
        paramCount: definition?.paramCount ?? null,
        defKind: definition?.kind ?? null,
        defName: definition?.name ?? null,
        hasDefinition: definition !== null,
        insideOpenLintRegion,
    };
}

/**
 * Resolve the metadata comment the panel should edit for the cursor line: the existing comment
 * when the cursor sits on it or on a definition it documents, a synthetic (isNew)
 * {@link MetadataCommentBlock} when the cursor is on a definition with no comment yet, and
 * null when it is on neither.
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

        // Bind the #UI section to the directive under the cursor (if any). No
        // synthetic/isNew case here — a #UI line requires a pre-existing
        // variable assignment the panel can't fabricate.
        block.uiDirective = findUiDirectiveBlock(lines, cursorLine);
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

    const definition = resolveDefinition(cursorLine);
    if (definition) {
        // The comment belongs above the statement's first line, which the cursor may
        // not be on when the definition spans continuation lines.
        const defLine = definition.line;
        if (defLine > 0) {
            const above = findMetadataCommentBlock(lines, defLine - 1);
            if (above) {
                above.context = analyzeMetadataLine(lines, defLine - 1, resolveDefinition);
                return above;
            }
        }
        return {
            line: defLine,
            endLine: defLine,
            indent: leadingWhitespace(lines[defLine]),
            trailingQuote: '',
            rawJson: '',
            data: {},
            valid: true,
            isNew: true,
            context: analyzeMetadataLine(lines, defLine - 1, resolveDefinition),
        };
    }

    // Null case: the cursor is on neither a definition nor a comment, so offer the region
    // markers that apply to a bare line and insert a new comment above the cursor on Apply.
    // defKind is forced null so the definition-oriented fields stay hidden.
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
        context: { ...region, paramCount: null, defKind: null, defName: null, hasDefinition: false },
    };
}
