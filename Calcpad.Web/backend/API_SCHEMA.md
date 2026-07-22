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
}
```

**Response:** HTML content (`text/html`)

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
  format?: string;           // Page format (default: "A4")
  orientation?: string;      // "portrait" or "landscape" (default: "portrait")
  printBackground?: boolean; // Print background graphics (default: true)
  scale?: number;            // Scale factor (default: 1.0)

  // Margins
  marginTop?: string;        // Top margin (default: "2cm")
  marginRight?: string;      // Right margin (default: "1.5cm")
  marginBottom?: string;     // Bottom margin (default: "2cm")
  marginLeft?: string;       // Left margin (default: "1.5cm")

  // Headers and footers
  enableHeader?: boolean;    // Enable page header
  enableFooter?: boolean;    // Enable page footer

  // Document metadata
  documentTitle?: string;
  documentSubtitle?: string;
  author?: string;
  company?: string;
  project?: string;

  // Custom content
  headerCenter?: string;
  footerCenter?: string;

  // Timestamp format (null/empty uses system default)
  dateTimeFormat?: string;

  // Background PDF (base64-encoded or file path)
  backgroundPdf?: string;
}
```

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

Generate a Word `.docx` from the source. Runs the calcpad → HTML pipeline (`forPrint: true`) and feeds it through `Calcpad.OpenXml.OpenXmlWriter`.

**Request:** Same as `/convert` (uses `CalcpadRequest`)

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
