// Calcpad Server API Types
// Based on API_SCHEMA.md

// ============================================
// Lint API Types
// ============================================

export interface LintRequest {
    content: string;
    sourceFilePath?: string;
}

export interface LintResponse {
    errorCount: number;
    warningCount: number;
    diagnostics: LintDiagnostic[];
}

export interface LintDiagnostic {
    line: number;        // Zero-based line number
    column: number;      // Zero-based column (start position)
    endColumn: number;   // Zero-based end column position
    code: string;        // Error code (e.g., "CPD-3301")
    message: string;     // Human-readable error/warning message
    severity: string;    // Severity name: "error" or "warning"
    severityId: number;  // Severity ID: 0=Error, 1=Warning
    source: string;      // Source of the diagnostic (default: "Calcpad Linter")
}

// ============================================
// Prettify API Types
// ============================================

export interface PrettifyRequest {
    content: string;
    /** String emitted per indent level. Defaults to a tab on the server when omitted. */
    indentUnit?: string;
    /** Whether to strip trailing whitespace on each line (default: true). */
    trimTrailingWhitespace?: boolean;
}

export interface PrettifyResponse {
    content: string;
}

// ============================================
// Highlight API Types
// ============================================

export interface HighlightRequest {
    content: string;
    includeText?: boolean;
    sourceFilePath?: string;
}

export interface HighlightResponse {
    tokens: HighlightToken[];
}

export interface HighlightToken {
    line: number;      // Zero-based line number
    column: number;    // Zero-based column (character offset from start of line)
    length: number;    // Length of the token in characters
    type: string;      // Token type name for display/debugging
    typeId: number;    // Token type ID for efficient processing
    text?: string;     // Actual token text (only if includeText is true)
}

// ============================================
// Token Type Enum
// ============================================

export enum CalcpadTokenType {
    // ===== Core Syntax =====

    /** Whitespace or unknown content */
    None = 0,

    /** Numeric constants (e.g., 123, 3.14, 1e-5) */
    Const = 1,

    /** Operators (e.g., +, -, *, /, =, ≤, ≥) */
    Operator = 2,

    /** Brackets: (), [], {} */
    Bracket = 3,

    /** Line continuation marker (underscore _ at end of line) */
    LineContinuation = 4,

    // ===== Identifiers =====

    /** Variable identifiers */
    Variable = 5,

    /** Local variables scoped to a single expression or command block (function params, #for vars, command scope vars) */
    LocalVariable = 6,

    /** Function names (built-in or user-defined) */
    Function = 7,

    /** Macro names (ending with $) */
    Macro = 8,

    /** Macro parameters in #def statements (e.g., param1$, param2$ in #def macro$(param1$; param2$)) */
    MacroParameter = 9,

    /** Unit identifiers (e.g., m, kg, N/m²) */
    Units = 10,

    /** Special setting variables (PlotHeight, PlotWidth, PlotSVG, Precision, Tol, etc.) */
    Setting = 11,

    // ===== Keywords and Commands =====

    /** Keywords starting with # (e.g., #if, #else, #def) */
    Keyword = 12,

    /** Control block keywords (#if, #repeat, #for, #while, #def, #else, #else if, #break, #continue) */
    ControlBlockKeyword = 13,

    /** End keywords that close control blocks (#end if, #end def, #loop) */
    EndKeyword = 14,

    /** Commands starting with $ (e.g., $plot, $find, $sum) */
    Command = 15,

    // ===== File and Data Exchange =====

    /** Include file paths */
    Include = 16,

    /** File paths in data exchange keywords (#read, #write, #append) */
    FilePath = 17,

    /** Sub-keywords in data exchange statements (from, to, sep, type) */
    DataExchangeKeyword = 18,

    // ===== Comments and Documentation =====

    /** Plain text comments enclosed in ' or " without HTML content */
    Comment = 19,

    /** HTML comments (<!-- ... -->) */
    HtmlComment = 20,

    /** HTML tags within comments */
    Tag = 21,

    /** HTML content (text between HTML tags) */
    HtmlContent = 22,

    /** JavaScript code within script tags in comments */
    JavaScript = 23,

    /** CSS code within style tags in comments */
    Css = 24,

    /** SVG markup within svg tags in comments */
    Svg = 25,

    // ===== Special =====

    /** Input markers (? or #{...}) */
    Input = 26,

    /** Format specifiers (e.g., :f2, :e3) */
    Format = 27,

    // [Future Reserved] 28-30 used to hold StringVariable, StringFunction, StringTable
    FutureReserved28 = 28,
    FutureReserved29 = 29,
    FutureReserved30 = 30
}

// ============================================
// Definitions API Types
// ============================================

export interface DefinitionsRequest {
    content: string;
    sourceFilePath?: string;
}

export interface DefinitionsResponse {
    macros: MacroDefinition[];
    functions: FunctionDefinition[];
    variables: VariableDefinition[];
    customUnits: CustomUnitDefinition[];
    /** Resolved absolute `#ProjectPath`, or null when undeclared/unresolvable. */
    projectPath: string | null;
    /** Resolved absolute `#LibraryPath`, or null when undeclared/unresolvable. */
    libraryPath: string | null;
}

export interface MacroDefinition {
    name: string;
    parameters: string[];
    isMultiline: boolean;
    content: string[];
    lineNumber: number;  // Zero-based line number
    source: string;
    sourceFile?: string;
    description?: string;
    paramTypes?: string[];
    paramDescriptions?: string[];
    defaults?: (string | null)[];
}

export interface FunctionDefinition {
    name: string;
    parameters: string[];
    expression?: string;
    returnType: string;
    returnTypeId: number;
    hasCommandBlock: boolean;
    commandBlockType?: string;
    commandBlockStatements?: string[];
    lineNumber: number;  // Zero-based line number
    source: string;
    sourceFile?: string;
    description?: string;
    paramTypes?: string[];
    paramDescriptions?: string[];
    defaults?: (string | null)[];
}

export interface VariableDefinition {
    name: string;
    expression?: string;
    type: string;
    typeId: number;
    lineNumber: number;  // Zero-based line number
    source: string;
    sourceFile?: string;
    description?: string;
}

export interface CustomUnitDefinition {
    name: string;
    expression?: string;
    lineNumber: number;  // Zero-based line number
    source: string;
    sourceFile?: string;
    description?: string;
}

export interface SymbolLocation {
    line: number;        // Zero-based line number (mapped to original source)
    column: number;      // Zero-based column
    length: number;      // Token length in characters
    source: string;      // "local" or "include"
    sourceFile?: string; // File path if from #include
    isAssignment: boolean; // true for definitions/reassignments
}

// ============================================
// Symbol-at-position API Types
// ============================================

export type SymbolKind = 'variable' | 'function' | 'macro';

export interface SymbolAtPositionRequest {
    content: string;
    line: number;       // 0-based, in original source
    column: number;     // 0-based
    sourceFilePath?: string;
}

export interface SymbolAtPositionResponse {
    symbolName: string;
    kind: SymbolKind;
    locations: SymbolLocation[];
}

// Type IDs for variables and function return types
export enum CalcpadTypeId {
    Unknown = 0,
    Value = 1,
    Vector = 2,
    Matrix = 3,
    FutureReserved4 = 4,
    Various = 5,
    Function = 6,
    InlineMacro = 7,
    MultilineMacro = 8,
    CustomUnit = 9,
    FutureReserved10 = 10
}

// ============================================
// Convert Errors (returned via X-Calcpad-Errors response header)
// ============================================

export type CalcpadErrorSource = 'Macro' | 'Expression';

export interface CalcpadError {
    sourceLine: number;
    outputLine: number;
    message: string;
    source: CalcpadErrorSource;
}

export interface ConvertResult {
    html: string;
    errors: CalcpadError[];
}

export interface CpdzDecodeResponse {
    /** The decoded Calcpad source. */
    content: string;
    /**
     * True when the file is a composite archive bundling images. Its original bytes
     * must be passed back on encode, or those images are lost.
     */
    composite: boolean;
}

export interface CpdzEncodeResponse {
    /** Base64 of the encoded file's bytes. */
    data: string;
}

/**
 * The outcome of bundling a worksheet for compiling. `content` is the self-contained
 * source; when it is missing, `errors` says what the worksheet still depends on.
 */
export interface PortableBundleResult {
    content?: string;
    errors: string[];
}

/**
 * The outcome of packing a worksheet into a portable archive. `zip` is the file to save and
 * `bundled` names what travelled with the document; when they are missing, `errors` says what
 * stopped the package — a reference that could not be read, or two that share a name.
 */
export interface PortablePackageResult {
    zip?: Uint8Array;
    /** Suggested file name, taken from the document's own. */
    name?: string;
    /** The folder inside the archive holding every referenced file. */
    refsFolder?: string;
    bundled: string[];
    errors: string[];
}

/**
 * Which rendering an export captures.
 *
 * - `report` — hides `#pre` and applies the entered `#UI` values. The default everywhere.
 * - `preview` — the on-screen rendering: `#pre` and `#post` both shown, no overrides.
 * - `input` — the `#UI` form itself, with the entered values in its controls.
 * - `unwrapped` — the macro-expanded source listing.
 *
 * `input` and `unwrapped` have no Word form.
 */
export type ExportVariant = 'report' | 'preview' | 'input' | 'unwrapped';

/** Interactive `#UI` options for a convert request. */
export interface UiConvertOptions {
    /** Render `#UI` lines as controls and hide `#post` content. */
    enableUi?: boolean;
    /**
     * Values to substitute, keyed by the control identity the preview reports in
     * `data-ui-var` (`"L:1"`, or `"L:1:2"` for the second pass of a loop). A bare
     * `"L"` covers every declaration of that name.
     */
    uiOverrides?: Record<string, string>;
    /**
     * Drops the "on line [N]" source-line reference from error messages, since input
     * mode has no source editor for it to point at. Defaults to `enableUi`.
     */
    hideErrorLines?: boolean;
}
