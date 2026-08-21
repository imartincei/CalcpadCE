# Calcpad.Server API Schema

> **Localhost-only build.** This branch (`calcpad-web`) only supports a loopback-bound backend. Hosted/Docker/auth/storage live on `calcpad-experimental`. This document mirrors the backend schema for frontend consumers — see [../backend/API_SCHEMA.md](../backend/API_SCHEMA.md) for the canonical reference.

## Base URL

```
http://localhost:9420/api/calcpad
```

Default port is `9420` (override with `CALCPAD_PORT` when running the backend).

---

## Authentication

When the backend is launched with `CALCPAD_API_TOKEN` set, every `/api/**` request must carry that value in an `X-Calcpad-Token` header, or it gets `401 Unauthorized`. `CalcpadApiClient` sends it on every request once `setAuthToken` has been called, and exposes `authHeaders()` for the few call sites that use `fetch` directly. The header is withheld for a non-loopback base URL, so pointing the client at a configured remote server never hands that host the local token.

Hosts obtain the token from whichever process spawned the server: the Tauri shell's `server_token` command, or `BaseServerManager.getAuthToken()` in the VS Code extension. A backend launched without the variable stays unauthenticated and `authHeaders()` is simply empty.

---

## Syntax Highlighter Endpoints

### POST /highlight

Tokenize Calcpad source code for syntax highlighting.

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
  line: number;
  column: number;
  length: number;
  type: string;
  typeId: number;
  text?: string;
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
| 16 | LocalVariable | Local variables scoped to expressions |
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

### POST /highlight-line

Tokenize a single line of Calcpad source code (for incremental updates).

**Request:**
```typescript
interface HighlightLineRequest {
  line: string;
  lineNumber?: number;
  includeText?: boolean;
}
```

**Response:** Same as `/highlight`

---

## Linter Endpoint

### POST /lint

Lint Calcpad source code and return diagnostics.

**Request:**
```typescript
interface LintRequest {
  content: string;
  sourceFilePath?: string;
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
  line: number;
  column: number;
  endColumn: number;
  code: string;        // e.g., "CPD-3301"
  message: string;
  severity: string;    // "error", "warning", or "information"
  severityId: number;  // 0=Error, 1=Warning, 2=Information
  source: string;      // Default: "Calcpad Linter"
}
```

**Error Codes:** See the full table in [../backend/API_SCHEMA.md](../backend/API_SCHEMA.md). Categories: CPD-11xx (include), CPD-22xx (macro), CPD-31xx (balance), CPD-32xx (naming), CPD-33xx (usage), CPD-34xx (semantic).

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

## Definitions Endpoint

### POST /definitions

Get detailed definitions (macros, functions, variables, custom units) from Calcpad source code.

**Request:**
```typescript
interface DefinitionsRequest {
  content: string;
  sourceFilePath?: string;
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
  source: string;       // "local" or "include"
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
| 5 | Various | Type varies across assignments |
| 6 | Function | Function type |
| 7 | InlineMacro | Inline macro |
| 8 | MultilineMacro | Multiline macro |
| 9 | CustomUnit | Custom unit definition |

---

## Find References Endpoint

### POST /find-references

Get all symbol occurrence locations for go-to-definition and find-all-references.

**Request:** Same as `/definitions` (uses `DefinitionsRequest`)

**Response:**
```typescript
interface FindReferencesResponse {
  variables: Record<string, SymbolLocationDto[]>;
  functions: Record<string, SymbolLocationDto[]>;
  macros: Record<string, SymbolLocationDto[]>;
}

interface SymbolLocationDto {
  line: number;
  column: number;
  length: number;
  source: string;        // "local" or "include"
  sourceFile?: string;
  isAssignment: boolean;
}
```

---

## Convert Endpoint

### POST /convert

Convert Calcpad source code to HTML. Processes macros, includes, and calculations.

**Request:**
```typescript
interface CalcpadRequest {
  content: string;
  settings?: Settings;
  forceUnwrappedCode?: boolean;
  theme?: string;               // "light" or "dark"
  sourceFilePath?: string;      // Used to resolve relative #include against the parent file's directory

  // Render mode. #pre is hidden when forPrint is true; #post is hidden when enableUi is true.
  forPrint?: boolean;           // Report layout (default: false)
  enableUi?: boolean;           // Render #UI lines as controls (default: false). Ignored when forPrint.
  uiOverrides?: Record<string, string>;
                                // Entered #UI values, keyed by the identity in data-ui-var ("L:1").
  includeLineAnchors?: boolean; // Line anchors + error boxes. Defaults to !forPrint; set true for an
                                //   on-screen report, false for anything written to a file.
  hideErrorLines?: boolean;    // Drops "on line [N]" from error messages. Defaults to enableUi.
  write?: boolean;             // Run #write/#append for this render (default: false).
}
```

See [../backend/API_SCHEMA.md](../backend/API_SCHEMA.md#post-convert) for how the four
renderings (preview, report, input form, unwrapped) map onto these fields.

**Response:** HTML content (`text/html`).

### POST /convert-unwrapped

Same request shape as `/convert`. Returns the raw code rendered as HTML (no calculation).

---

## DOCX / PDF Endpoints

### POST /docx

Generate a Word `.docx` from Calcpad source.

**Request:** Same as `/convert` (uses `CalcpadRequest`). `forPrint` and `uiOverrides` apply; `enableUi` and `includeLineAnchors` are ignored.

**Response:** `application/vnd.openxmlformats-officedocument.wordprocessingml.document`

### POST /pdf

Generate a PDF from HTML content using Playwright + PDFsharp. The standard flow is `/convert` → `/pdf`.

See [../backend/API_SCHEMA.md](../backend/API_SCHEMA.md) for the full `PdfGenerateRequest` / `PdfOptions` shape.

Answers **503** with `code: "BROWSER_NOT_FOUND"` when no Chromium-family browser is usable.
The server never downloads one behind the user's back, so the host prompts and then calls
`/pdf/browser/install`. `calcpad-frontend` ships the client half of this
(`pdfResponseError`, `isBrowserNotFound`, `fetchPdfBrowserStatus`, `installPdfBrowser`).

### GET /pdf/health

Health check for the PDF generation service.

### GET /pdf/browser

Which browser PDF export would use: `{ available, source, path, downloadAllowed, downloadSizeMb }`.

### POST /pdf/browser/install

Downloads the bundled headless Chromium. Call only after the user has agreed.

---

## Prettify Endpoint

### POST /prettify

Pretty-print Calcpad source code.

**Request:**
```typescript
interface PrettifyRequest {
  content: string;
}
```

**Response:** `text/plain` — the prettified source.

---

## Snippets Endpoint

### GET /snippets

Get all available snippets for autocomplete/intellisense.

**Query Parameters:**
| Parameter | Type | Description |
|-----------|------|-------------|
| category | string | Optional. Filter snippets by category prefix |

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
  category: string;
  quickType?: string;
  parameters?: SnippetParameterDto[];
}

interface SnippetParameterDto {
  name: string;
  description?: string;
}
```

---

## Usage Notes

1. **Line and column numbers are zero-based.**
2. **Source file path** — Pass `sourceFilePath` so the server can resolve relative `#include` against the parent file's directory.
3. **Token positions** — Use `column` and `length` for syntax highlighting spans.
4. **Error ranges** — Use `column` and `endColumn` for lint underlines.
5. **Incremental updates** — Use `/highlight-line` for real-time highlighting and `/lint` for full validation.
