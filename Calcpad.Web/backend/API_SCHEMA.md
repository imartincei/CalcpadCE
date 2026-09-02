# Calcpad.Web Backend API Schema

> **Localhost-only build.** This branch (`calcpad-web`) only supports loopback bindings. The startup guard in [Program.cs](Program.cs) throws if the bind URL is not `localhost`, `127.0.0.0/8`, or `::1`. Hosted/Docker/auth/storage live on `calcpad-experimental`. The auth, user-management, file-storage, content-resolution, and cache endpoints from the hosted branch are not present here.

## Base URL

```
http://127.0.0.1:{port}/api/calcpad
```

With neither `CALCPAD_PORT` nor `--urls` set, the server binds `http://127.0.0.1:0` and takes a free port from the OS, so instances never collide. The bound URL is on the `Now listening on:` startup line and is written to the port file (`--port-file <path>`, defaulting to `.calcpad-server.port` beside the binary) for clients to discover.

Setting `CALCPAD_PORT` pins the port and restores the legacy `http://127.0.0.1:9420` base. `ASPNETCORE_URLS` is **not** honored — the host calls `UseUrls` explicitly, which overrides it; pass `--urls` on the command line instead.

---

## Authentication

When the server is launched with `CALCPAD_API_TOKEN` set, every `/api/**` request must carry that value in an `X-Calcpad-Token` header. Anything else gets `401 Unauthorized`. CORS preflights (`OPTIONS`) are exempt — a preflight carries no custom headers, so checking it would fail the real request before it was sent.

```
X-Calcpad-Token: <per-launch token>
```

Both shipped hosts set the variable when they spawn the server and never put it on the command line — argv is readable by any local process through `/proc/{pid}/cmdline` and WMI. The Tauri shell generates it in Rust and hands it to the webview via the `server_token` command; the VS Code extension generates it in the extension host and publishes it in the mode-`0600` lock file so other windows sharing the server can adopt it.

Launches that do not set the variable (a `dotnet run` during development, the sample client, the test harness) stay unauthenticated, and the server logs a warning saying so. Loopback binding, the CORS origin policy, and the `Host`-header check are then the only controls.

---

## Table of Contents

- [GET /health](#get-health)
- [POST /convert](#post-convert)
- [GET /sample](#get-sample)
- [GET /debug-crash](#get-debug-crash)
- [GET /log-level, POST /log-level](#get-log-level-post-log-level)
- [POST /pdf](#post-pdf)
- [GET /pdf/health](#get-pdfhealth)
- [GET /pdf/browser](#get-pdfbrowser)
- [POST /pdf/browser/install](#post-pdfbrowserinstall)
- [POST /docx](#post-docx)
- [POST /highlight](#post-highlight)
- [POST /highlight-line](#post-highlight-line)
- [POST /lint](#post-lint)
- [POST /definitions](#post-definitions)
- [POST /symbol-at-position](#post-symbol-at-position)
- [POST /prettify](#post-prettify)
- [POST /cpdz/decode](#post-cpdzdecode)
- [POST /cpdz/encode](#post-cpdzencode)
- [POST /portable/bundle](#post-portablebundle)
- [POST /portable/package](#post-portablepackage)
- [GET /snippets](#get-snippets)
- [Usage Notes](#usage-notes)
- [Environment Variables](#environment-variables)

---

## GET /health

Liveness probe. Answered from inside the MVC pipeline, so a server that is bound but wedged
(thread-pool starvation, a deadlock) fails it.

Does no work, logs nothing of its own, and is excluded from the hang watchdog's completed-request
accounting, so polling it costs nothing and masks nothing. At `verbose` the framework's own
request logging covers it like any other route. Requires `X-Calcpad-Token` like every
other `/api` route. For the Chromium readiness of PDF export see [GET /pdf/health](#get-pdfhealth).

**Response:**
```json
{
  "status": "ok"
}
```

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

  // Whether this request may run #write/#append. False by default, so a preview refresh does
  // not rewrite the document's output on every keystroke. The front ends resolve their
  // three-way "when to write" setting into this per request: always, only when forPrint, or
  // never until the Export tab's "Write to Disk" asks for it.
  write?: boolean;              // Run #write/#append (default: false)
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

Calculation errors do not change the status code — they come back beside the HTML in an `X-Calcpad-Errors` response header, URL-encoded JSON of:

```typescript
Array<{
  sourceLine: number;
  outputLine: number;
  message: string;
  source: "Macro" | "Expression";
}>
```

The header is listed in the CORS policy's exposed headers, so a browser client can read it.

Every local `<img src>` comes back with its `{project}`/`{library}`/`{user}` token and any environment variable already expanded to an absolute forward-slash path, resolved against the roots declared anywhere in the `#include` chain. A source with no token is returned as authored, so a relative one still needs joining against `sourceFilePath`'s folder — the only path work left to a client that has to read the file off disk (to base64-inline it for a sandboxed preview, say). An undeclared root is reported as a normal render error and the source is left as written.

---

### Unwrapped output

`POST /convert?unwrap=true` returns the raw, fully expanded source instead of a calculated report, with its `data-text` links rewritten to the per-line anchors so in-preview navigation keeps working. The body field `forceUnwrappedCode` also produces unwrapped output, but only the query parameter rewrites the links.

There is no separate `/convert-unwrapped` endpoint.

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

Deliberately crashes the server, to verify which failure paths `FileLogger` actually catches. **Served only in the Development environment** — anywhere else it returns `404`, since a plain GET needs no preflight and any page the user visits could otherwise kill their local server.

**Query Parameters:**
| Parameter | Default | Description |
|-----------|---------|-------------|
| mode | `background-thread` | `throw`, `background-thread`, `unobserved-task`, `stackoverflow`, `accessviolation`, `failfast`, or `exit` |

**Response:** `202 Accepted` with `{ mode, note }` for the modes that schedule a crash, `400` for an unknown mode, or no response at all for the modes that terminate the process immediately.

---

## GET /log-level, POST /log-level

Reads and sets the server's log verbosity for the running process. Unlike `debug-crash` this is
served in every environment — quieting a shipped server is the point — and is covered by the
`X-Calcpad-Token` check like any other `/api` route.

Levels, least to most verbose: `error`, `warning` (the default), `information`, `verbose`.
`verbose` adds a line per request, so it is noisy during normal editing, since the editor sends
requests continuously. Crash and hang reports are written regardless of the level.

**GET response:**
```json
{
  "level": "warning",
  "available": ["error", "warning", "information", "verbose"]
}
```

**POST request:**
```json
{
  "level": "verbose"
}
```

Common aliases are accepted (`warn`, `info`, `trace`, `debug`, `off`). `off` maps to `error`, not
to silence.

**POST response:** `200` with `{ level }`, or `400` with `{ error, message, available }` for a level
that does not parse. The level applies immediately and is not persisted — a restart returns to
`CALCPAD_LOG_LEVEL`, or to `warning`.

ASP.NET's own logging obeys this too: the framework's providers are replaced with one that writes
through `FileLogger`, so a change here takes effect on framework entries without a restart and
they reach `CalcpadServer-{date}.log` alongside Calcpad's own. Framework entries are far denser
than ours at the same nominal level, so its `Information` and `Debug` are both reported at
`verbose`, and its `Trace` is dropped. Kestrel's `Now listening on: <url>` is the one exception,
emitted at every level because the desktop host reads the bound URL out of stdout when the port
file is unavailable.

---

## POST /pdf

Generate a PDF from HTML content using Playwright browser automation and PDFsharp.

**Request:**
```typescript
interface PdfGenerateRequest {
  html: string;              // HTML content to convert to PDF (required)
  options?: PdfOptions;      // PDF generation settings
}
```

The browser executable is never taken from the request — an executable path off the wire
is an arbitrary-process-launch primitive. It comes from `BrowserPath` in `appsettings.json`
or the `BROWSER_PATH` environment variable, else auto-detection. See
[GET /pdf/browser](#get-pdfbrowser).

```typescript

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

**503 — no usable browser:** rendering needs a Chromium-family browser and none was found.
The server does **not** download one on its own unless `AllowChromiumDownload` is set
(`appsettings.json`, or the `ALLOW_CHROMIUM_DOWNLOAD` environment variable). Clients are
expected to ask the user and then call `POST /pdf/browser/install`.

```json
{
  "error": "No usable browser",
  "code": "BROWSER_NOT_FOUND",
  "message": "No Chromium-family browser was found. …",
  "canDownload": true,
  "downloadSizeMb": 180
}
```

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

## GET /pdf/browser

Which browser PDF export would use. Resolves without launching or downloading anything,
so a client can warn before an export rather than after one fails.

**Response:**
```json
{
  "available": true,
  "source": "system",
  "path": "C:\\Program Files\\Microsoft\\Edge\\Application\\msedge.exe",
  "downloadAllowed": false,
  "downloadSizeMb": 180
}
```

`source` is `configured` (`BrowserPath`/`BROWSER_PATH`), `system` (auto-detected Chrome/Edge/Chromium),
`downloaded` (a previously installed headless Chromium), or `none`.

---

## POST /pdf/browser/install

Downloads the bundled headless Chromium (ChromeHeadlessShell) into `chromium/` beside the
server binary and returns immediately if one is already there. Call this **only after the
user has agreed** — it is a multi-hundred-megabyte download, and it is the one path that
downloads regardless of `AllowChromiumDownload` because the caller is relaying consent.

**Response:**
```json
{ "installed": true, "path": "…/chromium/chrome-headless-shell-win64/chrome-headless-shell.exe" }
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
| 2 | Operator | Operators (e.g., +, -, *, /, =) |
| 3 | Bracket | Brackets: (), [], {} |
| 4 | LineContinuation | Line continuation marker (trailing `_`) |
| 5 | Variable | Variable identifiers |
| 6 | LocalVariable | Local variables scoped to expressions (function params, #for vars, command scope vars) |
| 7 | Function | Function names (built-in or user-defined) |
| 8 | Macro | Macro names (ending with $) |
| 9 | MacroParameter | Macro parameters in #def statements |
| 10 | Units | Unit identifiers (e.g., m, kg, N/m^2) |
| 11 | Setting | Setting variables (PlotHeight, PlotWidth, Precision, Tol, ...) |
| 12 | Keyword | Keywords starting with # (e.g., #include, #def, #hide) |
| 13 | ControlBlockKeyword | Control-block keywords (#if, #else, #for, #while, #repeat) |
| 14 | EndKeyword | Block terminators (#end if, #loop, #end def) |
| 15 | Command | Commands starting with $ (e.g., $Plot, $Root, $Sum) |
| 16 | Include | Include file paths |
| 17 | FilePath | File paths in data exchange keywords (#read, #write, #append) |
| 18 | DataExchangeKeyword | Sub-keywords in data exchange statements (from, to, sep, type) |
| 19 | Comment | Comments enclosed in ' or " |
| 20 | HtmlComment | HTML comments |
| 21 | Tag | HTML tags within comments |
| 22 | HtmlContent | Text content inside HTML tags |
| 23 | JavaScript | Script content |
| 24 | Css | Style content |
| 25 | Svg | Inline SVG markup |
| 26 | Input | Input markers (? or #{...}) |
| 27 | Format | Format specifiers (e.g., :f2, :e3) |
| 28 | SettingsJson | Embedded settings JSON in a metadata comment |

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
    { "line": 0, "column": 0, "length": 1, "type": "Variable", "typeId": 5, "text": "a" },
    { "line": 0, "column": 2, "length": 1, "type": "Operator", "typeId": 2, "text": "=" },
    { "line": 0, "column": 4, "length": 1, "type": "Const", "typeId": 1, "text": "5" },
    { "line": 0, "column": 5, "length": 1, "type": "Operator", "typeId": 2, "text": "*" },
    { "line": 0, "column": 6, "length": 1, "type": "Units", "typeId": 10, "text": "m" },
    { "line": 1, "column": 0, "length": 1, "type": "Variable", "typeId": 5, "text": "b" },
    { "line": 1, "column": 2, "length": 1, "type": "Operator", "typeId": 2, "text": "=" },
    { "line": 1, "column": 4, "length": 3, "type": "Function", "typeId": 7, "text": "sin" },
    { "line": 1, "column": 7, "length": 1, "type": "Bracket", "typeId": 3, "text": "(" },
    { "line": 1, "column": 8, "length": 2, "type": "Const", "typeId": 1, "text": "45" },
    { "line": 1, "column": 10, "length": 1, "type": "Bracket", "typeId": 3, "text": ")" }
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
| CPD-1103 | Include | Deprecated #include input values |
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
| CPD-3413 | Semantic | Invalid #settings JSON |
| CPD-3414 | Semantic | Invalid PDF settings in a metadata comment |
| CPD-3415 | Semantic | Invalid #UI format |
| CPD-3416 | Semantic | 'uiOverrides' metadata comment not on the first line |
| CPD-3417 | Semantic | Duplicate 'uiOverrides' metadata comment |
| CPD-3418 | Semantic | 'uiOverrides' sharing a comment with another key |
| CPD-3419 | Semantic | Deprecated stored input value |
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
  description?: string;
}
```

**Type IDs (typeId / returnTypeId):**
| ID | Type | Description |
|----|------|-------------|
| 0 | Unknown | Type could not be determined |
| 1 | Value | Scalar numeric value |
| 2 | Vector | Vector (1D array) |
| 3 | Matrix | Matrix (2D array) |
| 4 | CustomUnit | Custom unit definition |
| 5 | Function | Function type |
| 6 | InlineMacro | Inline macro |
| 7 | MultilineMacro | Multiline macro |
| 8 | Various | Type varies (assigned different types in different places) |

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

## POST /symbol-at-position

Resolve a cursor position to the user-defined symbol under it and return every occurrence of that symbol — definition, reassignments, and usages. One round-trip serves go-to-definition, find-all-references, and rename; the matching heuristic lives on the server so clients don't each re-implement it.

**Request:**
```typescript
interface SymbolAtPositionRequest {
  content: string;
  line: number;             // Zero-based line of the cursor in the original source
  column: number;           // Zero-based column of the cursor
  sourceFilePath?: string;  // Full path of source file on client (resolves #include)
}
```

**Response:**
```typescript
interface SymbolAtPositionResponse {
  symbolName: string;
  kind: "variable" | "function" | "macro";
  locations: SymbolLocationDto[];
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

`null` is returned when no user-defined symbol sits under the position.

**Example Request:**
```json
{
  "content": "a = 5\nb = a + 1\nc = a * b",
  "line": 1,
  "column": 4
}
```

**Example Response:**
```json
{
  "symbolName": "a",
  "kind": "variable",
  "locations": [
    { "line": 0, "column": 0, "length": 1, "source": "local", "isAssignment": true },
    { "line": 1, "column": 4, "length": 1, "source": "local", "isAssignment": false },
    { "line": 2, "column": 4, "length": 1, "source": "local", "isAssignment": false }
  ]
}
```

---

## POST /prettify

Re-indent Calcpad source by tracking control-block depth across `#if`/`#else`/`#end if`, `#for`/`#while`/`#repeat`/`#loop`, and multiline `#def`/`#end def`. Only leading whitespace is adjusted; line endings and content are preserved.

**Request:**
```typescript
interface PrettifyRequest {
  content: string;
  indentUnit?: string;            // Emitted per indent level (default: a single tab)
  trimTrailingWhitespace?: boolean; // Default: true
}
```

**Response:**
```typescript
interface PrettifyResponse {
  content: string;   // The re-indented source
}
```

---

## POST /cpdz/decode

Decode a compiled `.cpdz` worksheet to its source text. Accepts both the plain deflate form and the composite archive that bundles images.

**Request:**
```typescript
interface CpdzDecodeRequest {
  data: string;              // base64 of the file's raw bytes
}
```

**Response:**
```typescript
interface CpdzDecodeResponse {
  content: string;           // the decoded Calcpad source
  composite: boolean;        // true when the archive bundles images
}
```

A composite file's original bytes must be passed back as `original` on encode, or those images are lost.

**400** when the bytes are not a valid `.cpdz`: `{ error, message }`.

---

## POST /cpdz/encode

Encode source text as a `.cpdz` worksheet. Run [`/portable/bundle`](#post-portablebundle) first — this endpoint compresses whatever it is handed and does not make a worksheet self-contained.

**Request:**
```typescript
interface CpdzEncodeRequest {
  content: string;
  original?: string;         // base64 of the file being overwritten, when there is one
}
```

Passing `original` for a composite archive keeps every entry but the code, so bundled images survive the save.

**Response:**
```typescript
interface CpdzEncodeResponse {
  data: string;              // base64 of the encoded file's bytes
}
```

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
  description: string;                // Short label for tooltips and completion detail
  documentation?: string;             // Long-form Markdown description for hover docs
  example?: string;                   // Calcpad usage example, rendered as a fenced code block
  label?: string;                     // Display label (defaults to description)
  category: string;                   // e.g. "Functions/Trigonometric"
  quickType?: string;                 // Shortcut without ~ prefix (e.g., "a" means ~a -> insert)
  keywordType?: string;               // "Function", "Keyword", "Command", "Constant", "Unit",
                                      //   "Operator", "Setting", "ControlBlockKeyword", "EndKeyword".
                                      //   Null for UI-only snippets.
  returnType?: string;                // CalcpadType name (null for non-functions)
  returnTypeDescription?: string;     // e.g. "Angle in radians"
  isElementWise: boolean;             // Operates element-wise on vectors/matrices
  acceptsAnyCount: boolean;           // Parameter-count validation is skipped (switch, gcd, lcm, ...)
  parameters?: SnippetParameterDto[]; // Parameter info for functions (null for non-functions)
}

interface SnippetParameterDto {
  name: string;
  description?: string;
  type?: string;                      // ParameterType name ("Scalar", "Vector", "Matrix", "Any", ...)
  typeDescription?: string;           // Falls back to type when null
  isOptional: boolean;
  isVariadic: boolean;                // The type applies to all remaining arguments
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
| `CALCPAD_PORT` | *(unset — OS-assigned port)* | Pins the server port. When set but empty of a value, falls back to `9420` |
| `CALCPAD_HOST` | `127.0.0.1` | Host part of the bind URL. Must resolve to loopback |
| `CALCPAD_ENABLE_HTTPS` | `false` | Serves `https` instead of `http`. Only applies on the `CALCPAD_PORT` path |
| `CALCPAD_API_TOKEN` | *(unset — unauthenticated)* | Per-launch token required in `X-Calcpad-Token` |
| `CALCPAD_DETACHED` | *(unset)* | `1` disables the stdin-EOF watchdog and the default port file, so the server outlives its parent |
| `CALCPAD_LOG_LEVEL` | `warning` | Startup verbosity: `error`, `warning`, `information` or `verbose`. Covers ASP.NET's own logs too. Change it at runtime via [POST /log-level](#get-log-level-post-log-level); both hosts pass the user's setting here so startup entries honour it as well |
| `CALCPAD_LOG_DIR` | *(executable-adjacent `logs/`)* | Where `CalcpadServer-{date}.log` is written. Hosts set this when the install directory is read-only |
| `CALCPAD_HANG_THRESHOLD_SECONDS` | `60` | Seconds without a completed request before the hang watchdog writes a report |
| `CALCPAD_HANG_DUMP` | *(unset)* | `1` also spawns `createdump` when a hang is detected |
| `CALCPAD_CONTENT_CACHE_SIZE_LIMIT` | `50000` | Flattened source lines budgeted across the resolved-content cache shared by lint/highlight/definitions |
| `BROWSER_PATH` | *(auto-detect)* | Chromium-family executable for PDF export. Also `BrowserPath` in `appsettings.json` |
| `ALLOW_CHROMIUM_DOWNLOAD` | `false` | Lets the render path download Chromium on its own. Also `AllowChromiumDownload` in `appsettings.json` |

`ASPNETCORE_URLS` is ignored: the host always calls `UseUrls`, which overrides it. Use `--urls` on the command line.

**CLI flags:** `--urls <url>`, `--port-file <path>` (write the bound URL once Kestrel is listening), `--parent-pid <pid>` (self-exit when that process disappears), `--exit-on-stdin-close` / `--no-exit-on-stdin-close`.
