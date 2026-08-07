# Calcpad.Web Backend API Schema

> **Localhost-only build.** This branch (`calcpad-web`) only supports loopback bindings. The startup guard in [Program.cs](Program.cs) throws if the bind URL is not `localhost`, `127.0.0.0/8`, or `::1`. Hosted/Docker/auth/storage live on `calcpad-experimental`. The auth, user-management, file-storage, content-resolution, and cache endpoints from the hosted branch are not present here.

## Base URL

```
http://localhost:9420/api/calcpad
```

Default port is `9420` (override with `CALCPAD_PORT`).

---

## Table of Contents

- [POST /convert](#post-convert)
- [POST /convert-unwrapped](#post-convert-unwrapped)
- [GET /sample](#get-sample)
- [GET /debug-crash](#get-debug-crash)
- [POST /pdf](#post-pdf)
- [GET /pdf/health](#get-pdfhealth)
- [POST /docx](#post-docx)
- [POST /highlight](#post-highlight)
- [POST /highlight-line](#post-highlight-line)
- [POST /lint](#post-lint)
- [POST /definitions](#post-definitions)
- [POST /find-references](#post-find-references)
- [POST /prettify](#post-prettify)
- [POST /portable/bundle](#post-portablebundle)
- [POST /portable/package](#post-portablepackage)
- [GET /snippets](#get-snippets)
- [Usage Notes](#usage-notes)

---

## POST /convert

Convert Calcpad source code to HTML. Processes macros, includes, and calculations.

**Request:**
```typescript
interface CalcpadRequest {
  content: string;              // The Calcpad source code to convert
  settings?: Settings;          // Optional Calcpad settings (math, plot, units)
  forceUnwrappedCode?: boolean; // If true, return code without calculation (default: false)
  theme?: string;               // "light" or "dark" (default: "light")
  sourceFilePath?: string;      // Full path of source file on client (used to resolve relative #include against the parent file's directory)

  // Render mode. #pre is hidden when forPrint is true; #post is hidden when enableUi is true.
  forPrint?: boolean;           // Report layout (default: false)
  enableUi?: boolean;           // Render #UI lines as interactive controls (default: false).
                                //   Ignored when forPrint is true — print output carries no controls.
  uiOverrides?: Record<string, string>;
                                // Values entered into #UI controls, keyed by the control identity
                                //   the preview reports in data-ui-var ("L:1", or "L:1:2" for the
                                //   second pass of a loop; a bare "L" covers every declaration).
                                //   Replaces the right-hand side of the annotated assignment, so a
                                //   report reflects what was entered.
  includeLineAnchors?: boolean; // Per-line anchors + error-summary boxes for in-preview line links.
                                //   Defaults to !forPrint. Set true for an on-screen report, false
                                //   for anything written to a file.
  hideErrorLines?: boolean;    // Drops the "on line [N]" reference from error messages, since
                                //   input mode has no source editor for it to point at. Defaults
                                //   to enableUi; the report pane shown beside the form overrides
                                //   it to true even though enableUi is false there.
}
```

The four renderings the front ends expose map onto these as:

| Variant | `forPrint` | `enableUi` | `uiOverrides` | `includeLineAnchors` | `hideErrorLines` |
|---------|-----------|-----------|---------------|----------------------|-------------------|
| Preview | `false` | `false` | — | on screen: default; export: `false` | default (`false`) |
| Report | `true` | `false` | yes | on screen: `true`; export: `false` | default, unless shown beside input form: `true` |
| Input form | `false` | `true` | yes | export: `false` | default (`true`) |
| Unwrapped | — | — | — | n/a (`?unwrap=true`) | n/a |

**Response:** HTML content (`text/html`)

Every local `<img src>` comes back with its `{project}`/`{library}`/`{user}` token and any environment variable already expanded to an absolute forward-slash path, resolved against the roots declared anywhere in the `#include` chain. A source with no token is returned as authored, so a relative one still needs joining against `sourceFilePath`'s folder — the only path work left to a client that has to read the file off disk (to base64-inline it for a sandboxed preview, say). An undeclared root is reported as a normal render error and the source is left as written.

---

## POST /convert-unwrapped

Convert Calcpad source code to HTML without calculation (raw code with syntax highlighting). Automatically processes `data-text` links so they remain functional.

**Request:** Same as `/convert` (uses `CalcpadRequest`)

**Response:** HTML content (`text/html`)

---

## GET /sample

Get a sample Calcpad source code document.

**Response:**
```typescript
interface CalcpadRequest {
  content: string;  // Sample Calcpad source code
  // ... other fields at defaults
}
```

---

## GET /debug-crash

Write a debug crash event from the client to the server's on-disk crash log. Used by the VS Code extension and desktop wrapper to surface client-side failures into the server log stream.

**Response:** `200 OK`

---

## POST /pdf

Generate a PDF from HTML content using Playwright browser automation and PDFsharp.

**Request:**
```typescript
interface PdfGenerateRequest {
  html: string;              // HTML content to convert to PDF (required)
  browserPath?: string;      // Custom browser executable path
  options?: PdfOptions;      // PDF generation settings
}

interface PdfOptions {
  // Page settings
  format?: string;           // Letter | Legal | Tabloid | Ledger | A0-A6 (default: "Letter")
  orientation?: string;      // "portrait" or "landscape" (default: "portrait")

  // Margins — a length with a unit, e.g. "2cm", "0.5in", "12mm"
  marginTop?: string;        // Top margin (default: "0.75in")
  marginRight?: string;      // Right margin (default: "0.5in")
  marginBottom?: string;     // Bottom margin (default: "0.75in")
  marginLeft?: string;       // Left margin (default: "0.5in")

  // Header and footer content. The bands are always drawn; these control what
  // goes in them, and an empty/omitted value simply leaves that slot blank.
  documentTitle?: string;    // Header, left, bold
  showPageNumbers?: boolean; // "Page n of m", footer right (default: true)
  showDate?: boolean;        // Timestamp, header right (default: true)
  dateTimeFormat?: string;   // .NET format string for the timestamp (empty uses "g")
}
```

Unrecognized properties are ignored, so a settings blob written by an older client
is still accepted.

**Response:** PDF binary (`application/pdf`, filename: `document.pdf`)

---

## GET /pdf/health

Health check for the PDF generation service.

**Response:**
```json
{
  "status": "ok",
  "service": "calcpad-pdf",
  "version": "2.0.0"
}
```

---

## POST /docx

Generate a Word `.docx` from the source. Runs the calcpad → HTML pipeline and feeds the result through `Calcpad.OpenXml.OpenXmlWriter`.

**Request:** Same as `/convert` (uses `CalcpadRequest`). `forPrint` and `uiOverrides` are honored, so the caller chooses between a report and the preview layout. `enableUi` and `includeLineAnchors` are ignored — a Word document has neither controls nor navigation anchors.

**Response:** Word binary (`application/vnd.openxmlformats-officedocument.wordprocessingml.document`)

---

## POST /highlight

Tokenize Calcpad source code for syntax highlighting. Supports include file resolution for accurate macro-aware tokenization.

**Request:**
```typescript
interface HighlightRequest {
  content: string;          // The Calcpad source code to tokenize
  includeText?: boolean;    // Whether to include token text in response (default: false)
  sourceFilePath?: string;  // Full path of source file on client (for resolving relative #include)
}
```

**Response:**
```typescript
interface HighlightResponse {
  tokens: HighlightToken[];
}

interface HighlightToken {
  line: number;      // Zero-based line number
  column: number;    // Zero-based column (character offset from start of line)
  length: number;    // Length of the token in characters
  type: string;      // Token type name for display/debugging
  typeId: number;    // Token type ID for efficient processing
  text?: string;     // Actual token text (only if includeText is true)
}
```

**Token Types (typeId):**
| ID | Type | Description |
|----|------|-------------|
| 0 | None | Whitespace or unknown content |
| 1 | Const | Numeric constants (e.g., 123, 3.14, 1e-5) |
| 2 | Units | Unit identifiers (e.g., m, kg, N/m^2) |
| 3 | Operator | Operators (e.g., +, -, *, /, =) |
| 4 | Variable | Variable identifiers |
| 5 | Function | Function names (built-in or user-defined) |
| 6 | Keyword | Keywords starting with # (e.g., #if, #else, #def) |
| 7 | Command | Commands starting with $ (e.g., $Plot, $Root, $Sum) |
| 8 | Bracket | Brackets: (), [], {} |
| 9 | Comment | Comments enclosed in ' or " |
| 10 | Tag | HTML tags within comments |
| 11 | Input | Input markers (? or #{...}) |
| 12 | Include | Include file paths |
| 13 | Macro | Macro names and parameters (ending with $) |
| 14 | HtmlComment | HTML comments |
| 15 | Format | Format specifiers (e.g., :f2, :e3) |
| 16 | LocalVariable | Local variables scoped to expressions (function params, #for vars, command scope vars) |
| 17 | FilePath | File paths in data exchange keywords (#read, #write, #append) |
| 18 | DataExchangeKeyword | Sub-keywords in data exchange statements (from, to, sep, type) |

**Example Request:**
```json
{
  "content": "a = 5*m\nb = sin(45)",
  "includeText": true
}
```

**Example Response:**
```json
{
  "tokens": [
    { "line": 0, "column": 0, "length": 1, "type": "Variable", "typeId": 4, "text": "a" },
    { "line": 0, "column": 2, "length": 1, "type": "Operator", "typeId": 3, "text": "=" },
    { "line": 0, "column": 4, "length": 1, "type": "Const", "typeId": 1, "text": "5" },
    { "line": 0, "column": 5, "length": 1, "type": "Operator", "typeId": 3, "text": "*" },
    { "line": 0, "column": 6, "length": 1, "type": "Units", "typeId": 2, "text": "m" },
    { "line": 1, "column": 0, "length": 1, "type": "Variable", "typeId": 4, "text": "b" },
    { "line": 1, "column": 2, "length": 1, "type": "Operator", "typeId": 3, "text": "=" },
    { "line": 1, "column": 4, "length": 3, "type": "Function", "typeId": 5, "text": "sin" },
    { "line": 1, "column": 7, "length": 1, "type": "Bracket", "typeId": 8, "text": "(" },
    { "line": 1, "column": 8, "length": 2, "type": "Const", "typeId": 1, "text": "45" },
    { "line": 1, "column": 10, "length": 1, "type": "Bracket", "typeId": 8, "text": ")" }
  ]
}
```

---

## POST /highlight-line

Tokenize a single line of Calcpad source code (for incremental updates).

**Request:**
```typescript
interface HighlightLineRequest {
  line: string;          // The line content to tokenize
  lineNumber?: number;   // Zero-based line number (default: 0)
  includeText?: boolean; // Whether to include token text (default: false)
}
```

**Response:** Same as `/highlight`

---

## POST /lint

Lint Calcpad source code and return diagnostics (errors, warnings, and informational messages). Supports lint-ignore regions via comments.

**Request:**
```typescript
interface LintRequest {
  content: string;          // The Calcpad source code to lint
  sourceFilePath?: string;  // Full path of source file on client
}
```

**Response:**
```typescript
interface LintResponse {
  errorCount: number;
  warningCount: number;
  diagnostics: LintDiagnostic[];
}

interface LintDiagnostic {
  line: number;        // Zero-based line number
  column: number;      // Zero-based column (start position)
  endColumn: number;   // Zero-based end column position
  code: string;        // Error code (e.g., "CPD-3301")
  message: string;
  severity: string;    // "error", "warning", or "information"
  severityId: number;  // 0=Error, 1=Warning, 2=Information
  source: string;      // Default: "Calcpad Linter"
}
```

**Error Codes:**

| Code | Category | Description |
|------|----------|-------------|
| **Stage 1: Pre-include validation (CPD-11xx)** |||
| CPD-1101 | Include | Malformed #include statement |
| CPD-1102 | Include | Missing #include filename |
| **Stage 2: Macro definitions (CPD-22xx)** |||
| CPD-2201 | Macro | Duplicate macro definition |
| CPD-2202 | Macro | Macro name must end with '$' |
| CPD-2203 | Macro | Macro parameter must end with '$' |
| CPD-2204 | Macro | Invalid macro name (must start with a letter) |
| CPD-2205 | Macro | Malformed #def syntax |
| CPD-2206 | Macro | Unmatched #def or #end def |
| CPD-2207 | Macro | Nested macro definition not allowed |
| CPD-2208 | Macro | Macro parameter must start with a letter |
| CPD-2209 | Macro | Macro definition inside a control block has no effect |
| CPD-2210 | Macro | Invalid character in macro name |
| CPD-2211 | Macro | Invalid character in macro parameter |
| CPD-2212 | Macro | Duplicate macro parameter |
| **Stage 3: Balance (CPD-31xx)** |||
| CPD-3101 | Balance | Unmatched opening parenthesis |
| CPD-3102 | Balance | Unmatched closing parenthesis |
| CPD-3103 | Balance | Unmatched opening square bracket |
| CPD-3104 | Balance | Unmatched closing square bracket |
| CPD-3105 | Balance | Unmatched opening curly brace or control block |
| CPD-3106 | Balance | Unmatched closing curly brace |
| **Stage 3: Naming (CPD-32xx)** |||
| CPD-3201 | Naming | Invalid variable name (must start with a letter) |
| CPD-3202 | Naming | Invalid function name |
| CPD-3203 | Naming | Function name conflicts with a built-in function |
| CPD-3204 | Naming | Variable name conflicts with a keyword |
| CPD-3205 | Naming | Variable name conflicts with a built-in constant |
| CPD-3206 | Naming | Function must have at least one parameter |
| **Stage 3: Usage (CPD-33xx)** |||
| CPD-3301 | Usage | Undefined variable |
| CPD-3302 | Usage | Function called with the wrong number of parameters |
| CPD-3303 | Usage | Undefined macro |
| CPD-3304 | Usage | Macro called with the wrong number of parameters |
| CPD-3305 | Usage | Undefined function |
| CPD-3306 | Usage | Invalid element access |
| CPD-3307 | Usage | Too few parameters |
| CPD-3308 | Usage | Too many parameters |
| CPD-3309 | Usage | Parameter type mismatch |
| CPD-3310 | Usage | Undefined unit |
| CPD-3311 | Usage | Empty parameter in a function call |
| CPD-3312 | Usage | Unused variable |
| CPD-3313 | Usage | Redefinition of existing function |
| **Stage 3: Semantic (CPD-34xx)** |||
| CPD-3401 | Semantic | Invalid operator usage |
| CPD-3402 | Semantic | Unknown command name |
| CPD-3403 | Semantic | Unknown directive |
| CPD-3404 | Semantic | Invalid assignment |
| CPD-3405 | Semantic | # directive not allowed inside a command block |
| CPD-3406 | Semantic | Invalid command syntax |
| CPD-3407 | Semantic | Incomplete expression |
| CPD-3408 | Semantic | Command variable mismatch |
| CPD-3409 | Semantic | Reassignment of a constant |
| CPD-3410 | Semantic | Outer-scope assignment (←) to an undefined variable |
| CPD-3411 | Semantic | Invalid paramType value in a metadata comment |
| CPD-3412 | Semantic | Invalid metadata-comment JSON |
| **Stage 3: Format (CPD-36xx)** |||
| CPD-3601 | Format | Invalid format specifier |

**Example Request:**
```json
{
  "content": "a = 5\nb = unknownVar\nc = sin()"
}
```

**Example Response:**
```json
{
  "errorCount": 2,
  "warningCount": 0,
  "diagnostics": [
    {
      "line": 1,
      "column": 4,
      "endColumn": 14,
      "code": "CPD-3301",
      "message": "Undefined variable: 'unknownVar'",
      "severity": "error",
      "severityId": 0,
      "source": "Calcpad Linter"
    },
    {
      "line": 2,
      "column": 4,
      "endColumn": 9,
      "code": "CPD-3307",
      "message": "Too few parameters: 'sin' requires at least 1 parameter(s), got 0",
      "severity": "error",
      "severityId": 0,
      "source": "Calcpad Linter"
    }
  ]
}
```

---

## POST /definitions

Get detailed definitions (macros, functions, variables, custom units) from Calcpad source code. Returns type information, parameters, return types, source locations, and metadata from doc comments.

**Request:**
```typescript
interface DefinitionsRequest {
  content: string;          // The Calcpad source code to analyze
  sourceFilePath?: string;  // Full path of source file on client
}
```

**Response:**
```typescript
interface DefinitionsResponse {
  macros: MacroDefinitionDto[];
  functions: FunctionDefinitionDto[];
  variables: VariableDefinitionDto[];
  customUnits: CustomUnitDefinitionDto[];
  projectPath: string | null;  // Resolved absolute #ProjectPath, or null when undeclared/unresolvable
  libraryPath: string | null;  // Resolved absolute #LibraryPath, or null when undeclared/unresolvable
}

interface MacroDefinitionDto {
  name: string;
  parameters: string[];
  isMultiline: boolean;
  content: string[];
  lineNumber: number;
  source: string;                  // "local" or "include"
  sourceFile?: string;
  description?: string;
  paramTypes?: string[];
  paramDescriptions?: string[];
  defaults?: (string | null)[];
}

interface FunctionDefinitionDto {
  name: string;
  parameters: string[];
  expression?: string;
  returnType: string;
  returnTypeId: number;
  hasCommandBlock: boolean;
  commandBlockType?: string;          // "Inline", "Block", or "While"
  commandBlockStatements?: string[];
  lineNumber: number;
  source: string;
  sourceFile?: string;
  description?: string;
  paramTypes?: string[];
  paramDescriptions?: string[];
  defaults?: (string | null)[];
}

interface VariableDefinitionDto {
  name: string;
  expression?: string;
  type: string;
  typeId: number;
  lineNumber: number;
  source: string;
  sourceFile?: string;
  description?: string;
}

interface CustomUnitDefinitionDto {
  name: string;
  expression?: string;
  lineNumber: number;
  source: string;
  sourceFile?: string;
}
```

**Type IDs (typeId / returnTypeId):**
| ID | Type | Description |
|----|------|-------------|
| 0 | Unknown | Type could not be determined |
| 1 | Value | Scalar numeric value |
| 2 | Vector | Vector (1D array) |
| 3 | Matrix | Matrix (2D array) |
| 5 | Various | Type varies (assigned different types in different places) |
| 6 | Function | Function type |
| 7 | InlineMacro | Inline macro |
| 8 | MultilineMacro | Multiline macro |
| 9 | CustomUnit | Custom unit definition |

**Example Request:**
```json
{
  "content": "#def double$(x$) = 2*x$\nmyFunc(a; b) = a + b\nvec = [1; 2; 3]\n.ksi = 1000*psi"
}
```

**Example Response:**
```json
{
  "macros": [
    {
      "name": "double$",
      "parameters": ["x$"],
      "isMultiline": false,
      "content": ["2*x$"],
      "lineNumber": 0,
      "source": "local"
    }
  ],
  "functions": [
    {
      "name": "myFunc",
      "parameters": ["a", "b"],
      "expression": "a + b",
      "returnType": "Value",
      "returnTypeId": 1,
      "hasCommandBlock": false,
      "lineNumber": 1,
      "source": "local"
    }
  ],
  "variables": [
    {
      "name": "vec",
      "expression": "[1; 2; 3]",
      "type": "Vector",
      "typeId": 2,
      "lineNumber": 2,
      "source": "local"
    }
  ],
  "customUnits": [
    {
      "name": "ksi",
      "expression": "1000*psi",
      "lineNumber": 3,
      "source": "local"
    }
  ]
}
```

**Command Block Functions Example:**

Note: Command blocks use function syntax like `if()`, `$Repeat{}`, etc. instead of `#if`, `#for` directives.

```json
{
  "content": "filterVec(v; val) = $Inline{result = vector(0); $Repeat{result = if(v.(i) > val; join(result; v.(i)); result) @ i = 1 : len(v)}; result}"
}
```

Response includes:
```json
{
  "functions": [
    {
      "name": "filterVec",
      "parameters": ["v", "val"],
      "expression": "$Inline{result = vector(0); ...}",
      "returnType": "Vector",
      "returnTypeId": 2,
      "hasCommandBlock": true,
      "commandBlockType": "Inline",
      "commandBlockStatements": [
        "result = vector(0)",
        "$Repeat{result = if(v.(i) > val; join(result; v.(i)); result) @ i = 1 : len(v)}",
        "result"
      ],
      "lineNumber": 0,
      "source": "local"
    }
  ]
}
```

---

## POST /find-references

Get all symbol occurrence locations (definitions, reassignments, and usages) for go-to-definition and find-all-references features. Returns dictionaries mapping symbol names to all their occurrences with original source line positions.

**Request:** Same as `/definitions` (uses `DefinitionsRequest`)

**Response:**
```typescript
interface FindReferencesResponse {
  variables: Record<string, SymbolLocationDto[]>;
  functions: Record<string, SymbolLocationDto[]>;
  macros: Record<string, SymbolLocationDto[]>;
}

interface SymbolLocationDto {
  line: number;          // Mapped back through all pipeline stages
  column: number;
  length: number;
  source: string;        // "local" or "include"
  sourceFile?: string;
  isAssignment: boolean; // True for definitions and reassignments
}
```

**Example Request:**
```json
{
  "content": "a = 5\nb = a + 1\nc = a * b"
}
```

**Example Response:**
```json
{
  "variables": {
    "a": [
      { "line": 0, "column": 0, "length": 1, "source": "local", "isAssignment": true },
      { "line": 1, "column": 4, "length": 1, "source": "local", "isAssignment": false },
      { "line": 2, "column": 4, "length": 1, "source": "local", "isAssignment": false }
    ],
    "b": [
      { "line": 1, "column": 0, "length": 1, "source": "local", "isAssignment": true },
      { "line": 2, "column": 8, "length": 1, "source": "local", "isAssignment": false }
    ],
    "c": [
      { "line": 2, "column": 0, "length": 1, "source": "local", "isAssignment": true }
    ]
  },
  "functions": {},
  "macros": {}
}
```

---

## POST /prettify

Pretty-print Calcpad source code (consistent spacing, indentation for control blocks, etc.).

**Request:**
```typescript
interface PrettifyRequest {
  content: string;
}
```

**Response:** Plain text (`text/plain`) — the prettified source code.

---

## POST /portable/bundle

Rewrite a worksheet into the self-contained form a compiled `.cpdz` needs: macros and `#include`d files expanded in place, every `#read` replaced by the data it imports (hidden, so it stays out of the report), and an included file's relative image paths made absolute. Images themselves are left for the caller to embed — the host owns the filesystem the editor sees — so the order is bundle, embed images, then `POST /cpdz/encode`.

**Request:**
```typescript
interface PortableBundleRequest {
  content: string;
  sourceFilePath?: string;   // relative #include and #read paths resolve against its folder
}
```

An absolute `#write`/`#append` target is always collapsed to its bare filename, so the output lands beside wherever the worksheet ends up rather than a folder that may not exist there; a relative target already does that and is untouched. `{project}`/`{library}`/`{user}` references are always resolved against this machine's own declared roots.

**Response:**
```typescript
interface PortableBundleResponse {
  content: string;           // no includes, no data files, images by absolute path
}
```

**400** when the worksheet still depends on something that cannot be read (missing `.csv`, unresolved `#include`, an undeclared `{project}`/`{library}` root, or a relative path with no `sourceFilePath` to resolve against), or when two `#write`/`#append` targets collapse onto the same file: `{ error, messages: string[] }`. Nothing is skipped — a worksheet that would fail, or overwrite one output with another, for whoever receives it is not written.

---

## POST /portable/package

Pack a worksheet and the files it references into a ZIP that stays text — the middle ground between a `.cpd`, which only runs where it was written, and a `.cpdz`, which runs anywhere but cannot be read. The document keeps its directives; only their paths change, each rewritten to reach the copy bundled beside it:

```
calc.zip
  calc.cpd
  calc.cpd.refs/  logo.png  library.cpd  loads.csv
```

`#include`, `#read` and local `<img src>` paths are rewritten and their files bundled, recursively through included files. Images given as `http(s):`/`data:` are left as written. Note the asymmetry the engine imposes: an `#include` resolves against the file holding it, while everything else resolves against the *root* document, because includes are expanded before anything else runs — so a path inside a bundled include is rewritten relative to the document, not to the include.

An absolute `#write`/`#append` target is collapsed to its bare filename so the output lands beside wherever the package is unpacked; a relative target is untouched. A `{project}`/`{library}`/`{user}` reference is resolved against this machine's own declared roots and bundled like any other — a package that still needed a root declared on the recipient's side would not be portable, and its root must therefore be declared here or the export is refused.

**Request:** `PortableBundleRequest` (as above).

**Response:**
```typescript
interface PortablePackageResponse {
  data: string;              // base64 of the .zip
  name: string;              // suggested file name, from the document's own
  refsFolder: string;        // "<document>.refs"
  bundled: string[];         // the entries packed beside the document
}
```

**400** when the package cannot be built: `{ error, messages: string[] }`. Two refusals, both naming what to fix — a reference that cannot be read (including one through an undeclared `{project}`/`{library}` root), and two `#write`/`#append` targets that would collapse onto the same file. Two references sharing a file name is not one of them: the flat refs folder renames the second, and any further one, `name-1.ext`, `name-2.ext` and so on instead. Nothing partial is ever returned.

---

## GET /snippets

Get all available snippets for autocomplete/intellisense. Returns snippet definitions with insert text, descriptions, categories, and parameter info.

**Query Parameters:**
| Parameter | Type | Description |
|-----------|------|-------------|
| category | string | Optional. Filter snippets by category prefix (e.g., "Functions", "Functions/Trigonometric") |

**Response:**
```typescript
interface SnippetsResponse {
  count: number;
  snippets: SnippetDto[];
}

interface SnippetDto {
  insert: string;                     // Use '§' as cursor placeholder
  description: string;
  label?: string;
  category: string;                   // e.g. "Functions/Trigonometric"
  quickType?: string;                 // Shortcut without ~ prefix (e.g., "a" means ~a -> insert)
  parameters?: SnippetParameterDto[]; // Parameter info for functions (null for non-functions)
}

interface SnippetParameterDto {
  name: string;
  description?: string;
}
```

**Snippet Categories:**
| Category | Description |
|----------|-------------|
| Constants | Mathematical constants (e, pi, etc.) |
| Operators | Arithmetic and comparison operators |
| Functions/Trigonometric | sin, cos, tan, etc. |
| Functions/Hyperbolic | sinh, cosh, tanh, etc. |
| Functions/Exponential | exp, ln, log, etc. |
| Functions/Rounding | round, floor, ceil, trunc |
| Functions/Aggregate | min, max, sum, average, etc. |
| Functions/Conditional | if, switch, and, or, not |
| Functions/Other | abs, sign, random, etc. |
| Functions/Vector | len, range, join, fill, etc. |
| Functions/Matrix | matrix, identity, transpose, etc. |
| Program Flow Control | #if, #else, #for, #while, etc. |
| Modules and Macros | #include, #def, #local |
| Commands | $Plot, $Root, $Sum, etc. |
| Units | Length, mass, time units |

**Example Request:**
```
GET /api/calcpad/snippets
GET /api/calcpad/snippets?category=Functions/Trigonometric
```

**Example Response:**
```json
{
  "count": 3,
  "snippets": [
    {
      "insert": "sin(§)",
      "description": "Sine of angle in radians",
      "category": "Functions/Trigonometric",
      "parameters": [
        { "name": "x", "description": "Angle in radians" }
      ]
    },
    {
      "insert": "min(§; §)",
      "description": "Minimum of multiple scalar values",
      "category": "Functions/Aggregate",
      "parameters": [
        { "name": "values", "description": "Scalar values" }
      ]
    },
    {
      "insert": "#if",
      "description": "Conditional block",
      "category": "Program Flow Control"
    }
  ]
}
```

---

## Usage Notes

1. **Line and column numbers are zero-based** — The first line is line 0, and the first character is column 0.

2. **Source file path** — Pass `sourceFilePath` when the client knows the full path of the source file. This is used to resolve relative `#include` and `#read` paths against the parent file's directory.

3. **Token positions** — For syntax highlighting, use `column` and `length` to determine the exact span of each token for colorization.

4. **Error ranges** — For the linter, use `column` and `endColumn` to underline or highlight the problematic code region.

5. **Incremental updates** — Use `/highlight-line` for real-time syntax highlighting as the user types, then periodically call `/lint` for full validation.

6. **PDF / DOCX generation** — Call `/convert` to obtain HTML and pass it to `/pdf`, or call `/docx` directly with Calcpad source. Check `/pdf/health` to verify the PDF service is available.

---

## Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `CALCPAD_PORT` | `9420` | Server port (host always loopback) |
| `ASPNETCORE_URLS` / `--urls` | `http://localhost:9420` | Full bind URL (must resolve to loopback) |
