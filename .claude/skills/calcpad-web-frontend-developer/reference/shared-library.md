# Shared Library: calcpad-frontend

The shared library is the core dependency for all three frontends (web editor, Tauri desktop, VS Code extension).

## CalcpadApiClient
Unified fetch-based HTTP client (works in Node.js 18+ and browsers):
```typescript
class CalcpadApiClient {
    constructor(baseUrl: string, logger?: ILogger);
    setBaseUrl(url: string): void;
    getBaseUrl(): string;

    // Auth — the per-launch server token. authHeaders() is withheld for a
    // non-loopback base URL, so a configured remote host never sees the local token.
    setAuthToken(token: string | null): void;
    authHeaders(): Record<string, string>;

    // Analysis. `opts.key` groups requests for supersession: a newer call with the
    // same key aborts the in-flight one and the loser resolves null.
    lint(content: string, sourceFilePath?: string, opts?: { key?: string }): Promise<LintResponse | null>;
    highlight(content: string, includeText?: boolean, sourceFilePath?: string, opts?: { key?: string }): Promise<HighlightToken[] | null>;
    definitions(content: string, sourceFilePath?: string, opts?: { key?: string }): Promise<DefinitionsResponse | null>;
    symbolAtPosition(content, line, column, sourceFilePath?, opts?): Promise<SymbolAtPositionResponse | null>;
    snippets(): Promise<SnippetsResponse | null>;
    prettify(content: string, ...): Promise<string | null>;

    // Rendering and export
    convert(content: string, settings: unknown, ...): Promise<ConvertResult | null>;
    convertUnwrapped(...): Promise<ConvertResult | null>;   // hits /convert?unwrap=true
    convertDocx(...): Promise<ArrayBuffer | null>;

    // Worksheet formats
    decodeCpdz(data: Uint8Array): Promise<CpdzDecodeResponse | null>;
    encodeCpdz(content: string, original?: Uint8Array): Promise<Uint8Array | null>;
    bundlePortable(...): Promise<PortableBundleResult>;
    packagePortable(...): Promise<PortablePackageResult>;

    runWithSupersession<T>(task: (signal: AbortSignal) => Promise<T>, opts?: { key?: string }): Promise<T | null>;
    checkHealth(): Promise<boolean>;
}
```

There is **no client file cache**. Include resolution happens server-side off disk — pass `sourceFilePath` so relative `#include` / `#read` paths resolve against the document's folder.

## Key Types (types/api.ts)
```typescript
interface LintResponse {
    errorCount: number;
    warningCount: number;
    diagnostics: LintDiagnostic[];
}

interface HighlightToken {
    line: number;      // 0-based
    column: number;    // 0-based
    length: number;
    type: string;
    typeId: number;    // CalcpadTokenType enum
    text?: string;
}

interface DefinitionsResponse {
    macros: MacroDefinition[];
    functions: FunctionDefinition[];
    variables: VariableDefinition[];
    customUnits: CustomUnitDefinition[];
    projectPath: string | null;   // resolved #ProjectPath
    libraryPath: string | null;   // resolved #LibraryPath
}

interface SymbolAtPositionResponse {
    symbolName: string;
    kind: 'variable' | 'function' | 'macro';
    locations: SymbolLocation[];   // definition, reassignments, usages
}

// Calculation errors ride the X-Calcpad-Errors response header, not the body
interface ConvertResult { html: string; errors: CalcpadError[]; }

enum CalcpadTokenType {
    None = 0, Const = 1, Operator = 2, Bracket = 3, LineContinuation = 4,
    Variable = 5, LocalVariable = 6, Function = 7, Macro = 8,
    MacroParameter = 9, Units = 10, Setting = 11, Keyword = 12,
    ControlBlockKeyword = 13, EndKeyword = 14, Command = 15,
    Include = 16, FilePath = 17, DataExchangeKeyword = 18,
    Comment = 19, HtmlComment = 20, Tag = 21, HtmlContent = 22,
    JavaScript = 23, Css = 24, Svg = 25, Input = 26, Format = 27,
    SettingsJson = 28
}

// Variable/function types — mirrors the server's CalcpadType
enum CalcpadTypeId {
    Unknown = 0, Value = 1, Vector = 2, Matrix = 3, CustomUnit = 4,
    Function = 5, InlineMacro = 6, MultilineMacro = 7, Various = 8
}
```

## CalcpadSettings (types/settings.ts)
```typescript
interface CalcpadSettings {
    math: { decimals; degrees; isComplex; substitute; formatEquations; zeroSmallMatrixElements;
            showHiddenOutput; maxOutputCount; formatString; precision; tol };
    plot: { isAdaptive; screenScaleFactor; imageUri; vectorGraphics; colorScale; smoothScale;
            shadows; lightDirection; width; height; step };
    server: { url: string; mode: 'auto' | 'local' | 'remote' };
    units: string;
    isUs: boolean;
}
```

`CalcpadSettingsBlob` wraps this as `{ core: CalcpadSettings, extras: Record<string, unknown> }` — `extras` holds host-side settings the backend never sees (PDF defaults, write policy, formatting hotkeys). The `core` block in `settings.default.json` is regenerated on build by `generate-settings-defaults.mjs`; hand-edits there are overwritten.

## Services
| Service | Purpose |
|---------|---------|
| `services/linter.ts` | Debounced linting via API, returns diagnostics |
| `services/definitions.ts` | Symbol extraction (variables, functions, macros, units) |
| `services/highlight.ts` | Token fetching for semantic highlighting |
| `services/snippets.ts` | Autocomplete snippet data from server |
| `services/cpdz.ts` | Compiled worksheet encode/decode plumbing |
| `services/headings.ts` | Table of contents from heading comments |
| `services/ui-overrides.ts` | `#UI` control values keyed by `data-ui-var` |
| `services/preview-diagnostics.ts`, `preview-limits.ts`, `scroll-anchor.ts` | Preview pane support |
| `services/paths.ts`, `image-utils.ts`, `html-body.ts`, `zip-writer.ts` | Path, asset and packaging helpers |
| `services/message-bridge/` | postMessage protocol between host and webview |

## Text Processing
| Module | Purpose |
|--------|---------|
| `operators.ts` | Replaces `>=` → `≥`, `<=` → `≤`, `!=` → `≠`, etc. |
| `quick-type.ts` | Replaces `~a` → `α`, `~b` → `β`, `~p` → `π`, etc. |
| `auto-indent.ts` | Auto-indent after `#if`, `#for`, `#def`; dedent on `#end` |
| `comment-formatting.ts` | Bold/italic/heading/sub/super inside comment blocks |
| `metadata-comment.ts` | Read/write the `pdf`, `settings`, `ui` metadata comments |
| `directives.ts`, `ui-directive.ts`, `path-roots.ts` | Directive and `{project}`/`{library}` parsing |
| `snippet-insert.ts`, `completion-format.ts` | Snippet insertion (`§` cursor marker) and completion rendering |
