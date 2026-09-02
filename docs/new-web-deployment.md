# Running the CalcpadCE Server

The desktop app and the VS Code extension start the CalcpadCE calculation server for you, so most people will never run it by hand.
However, people who are familiar with coding can also run it directly — for example, to point several tools at one shared instance, or to script conversions and calls against its API.
This page covers running the server and the API it exposes.

> **Localhost only.** This build runs the server bound to your own machine (`localhost`, `127.0.0.1`, or `::1`) only. If you point it at any other address, it refuses to start. There is no multi-user hosting, user-based authentication, or shared file storage in this build.

## Running

```bash
cd Calcpad.Web/backend
dotnet run
```

With no port set, the server takes a free port from the operating system rather than a fixed one, so two instances never collide.
The port it picked is on the `Now listening on:` line in the startup output, and is also written to `.calcpad-server.port` next to the binary (`bin/Debug/net10.0/` for a `dotnet run` launch) for as long as the server is up.

To pin the port instead, set `CALCPAD_PORT`:

```bash
CALCPAD_PORT=9420 dotnet run
```

That serves `http://127.0.0.1:9420`.
`CALCPAD_HOST` changes the host part and defaults to `127.0.0.1`; passing a full bind URL with `--urls` works too.
Either way the address has to stay on a loopback interface, or the server refuses to start.

### Launching from a script

The server watches its stdin and exits when it reaches EOF, so that it doesn't outlive the process that spawned it.
That means a launch with redirected or piped stdin (`dotnet run | tee`, most CI steps) shuts down immediately.
Set `CALCPAD_DETACHED=1` to opt out and keep the server running independently of its parent — this is what the VS Code extension does so one server can be shared across windows.

## Authentication

When `CALCPAD_API_TOKEN` is set, every `/api` request must present that value in an `X-Calcpad-Token` header, or it is rejected with `401`.
The desktop app and the VS Code extension always set it, so a server they launched will not answer an unauthenticated request.
A bare `dotnet run` leaves the variable unset, and the API is then open to anything on the machine.

Two restrictions apply either way:

- Requests whose `Host` header is not a loopback name are rejected with `421`, which blocks DNS-rebinding attacks from a browser.
- Browser requests are only accepted from loopback origins and the desktop app's own `tauri://` origin. Native callers send no `Origin` header and are unaffected.

## API endpoints

All paths are relative to `/api/calcpad`.

| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/convert` | POST | Convert a document to an HTML report (with theme + settings) |
| `/docx` | POST | Generate a Word `.docx` document |
| `/pdf` | POST | Generate a PDF from rendered HTML |
| `/pdf/health` | GET | PDF service health check |
| `/pdf/browser` | GET | Which browser PDF export would use, and whether one is available |
| `/pdf/browser/install` | POST | Download the bundled headless Chromium |
| `/sample` | GET | Fetch a sample document |
| `/highlight` | POST | Tokenize a full document for syntax highlighting |
| `/highlight-line` | POST | Tokenize a single line (incremental) |
| `/lint` | POST | Run the linter and return diagnostics |
| `/definitions` | POST | List macros, functions, variables, and units |
| `/symbol-at-position` | POST | The symbol under a cursor position and all its occurrences |
| `/prettify` | POST | Pretty-print CalcpadCE source |
| `/snippets` | GET | Snippets, optionally filtered by category |
| `/cpdz/decode` | POST | Decode a compiled `.cpdz` worksheet to its source |
| `/cpdz/encode` | POST | Encode source as a `.cpdz` worksheet |
| `/portable/bundle` | POST | Rewrite a worksheet into self-contained form, ready to compile |
| `/portable/package` | POST | Pack a worksheet and the files it references into a ZIP |
| `/debug-crash` | GET | Crash-path testing. Only served in the Development environment |

Pass `?unwrap=true` to `/convert` for HTML of the raw, fully expanded source, with its line links rewritten for error navigation.

The full request/response schema lives at [Calcpad.Web/backend/API_SCHEMA.md](https://github.com/imartincei/CalcpadCE/blob/main/Calcpad.Web/backend/API_SCHEMA.md); the common shapes are summarized below.

## Common request fields

`/convert` and `/docx` take a document request:

- `content` — the contents of the CalcpadCE file
- `settings` — math / plot / unit configuration
- `theme` — `"light"` or `"dark"`
- `sourceFilePath` — the document's file path, used to resolve relative `#include` and `#read` paths against the file's folder
- `forPrint` — when `true`, `#pre` regions are hidden and `#post` regions shown (used by PDF export)
- `enableUi` — when `true`, `#UI` lines render as interactive controls and `#post` is hidden
- `uiOverrides` — values entered into `#UI` controls, keyed by the control identity the preview reports in `data-ui-var`
- `includeLineAnchors` — emits the per-line anchors and error boxes the preview navigates by. Defaults to the opposite of `forPrint`
- `hideErrorLines` — drops the "on line [N]" reference from error messages. Defaults to `enableUi`
- `write` — whether this request may run `#write`/`#append`. `false` by default, so a preview refresh doesn't rewrite output on every keystroke

The analysis endpoints (`/highlight`, `/lint`, `/definitions`, `/symbol-at-position`) take `content` and `sourceFilePath` only.

## Response shapes

### Convert

`text/html` — the rendered document, not JSON.
Calculation errors come back alongside it in an `X-Calcpad-Errors` response header, URL-encoded JSON of:

```typescript
Array<{ sourceLine, outputLine, message, source: "Macro" | "Expression" }>
```

### Definitions

Four parallel arrays, plus the resolved `projectPath` and `libraryPath` when the document's `#include` chain declares them:

- `macros[]` — name, parameters, isMultiline, content, lineNumber, source, sourceFile, description, paramTypes, paramDescriptions
- `functions[]` — name, parameters, expression, returnType, returnTypeId, hasCommandBlock, commandBlockType, commandBlockStatements, lineNumber, source, sourceFile, description, paramTypes, paramDescriptions
- `variables[]` — name, expression, type, typeId, lineNumber, source, sourceFile, description
- `customUnits[]` — name, expression, lineNumber, source, sourceFile, description

`source` is `"local"` or `"include"`; `sourceFile` names the file for the latter.
`typeId` values: 0 Unknown, 1 Value, 2 Vector, 3 Matrix, 4 CustomUnit, 5 Function, 6 InlineMacro, 7 MultilineMacro, 8 Various.

### Symbol-at-position

Takes `line` and `column` alongside the content, and resolves the user-defined symbol under that cursor position.
One round-trip serves go-to-definition, find-all-references, and rename:

```typescript
{
  symbolName: string,
  kind: "variable" | "function" | "macro",
  locations: Array<{ line, column, length, source, sourceFile?, isAssignment }>
}
```

`isAssignment: true` marks a definition or reassignment.
The response is `null` when no symbol sits under the position.

### Highlight

```typescript
{ tokens: Array<{ line, column, length, type, typeId, text? }> }
```

The `text` field is omitted by default; pass `includeText: true` to include it.

### Lint

```typescript
{
  errorCount: number,
  warningCount: number,
  diagnostics: Array<{
    line: number, column: number, endColumn: number,
    code: string,        // "CPD-XXXX"
    message: string,
    severity: "error" | "warning" | "information",
    severityId: 0 | 1 | 2,
    source: "Calcpad Linter"
  }>
}
```

See [Linter and Diagnostics](new-linter.md) for what each code means.

### Snippets

```typescript
{
  count: number,
  snippets: Array<{
    insert: string,        // § marks cursor placement
    description: string,
    documentation?: string,
    example?: string,
    label?: string,
    category: string,      // e.g. "Functions/Trigonometric"
    quickType?: string,    // e.g. "a" for ~a → α
    keywordType?: string,  // "Function", "Keyword", "Command", "Constant", "Unit", ...
    returnType?: string,
    returnTypeDescription?: string,
    isElementWise: boolean,
    acceptsAnyCount: boolean,
    parameters?: Array<{ name, description?, type?, typeDescription?, isOptional, isVariadic }>
  }>
}
```

Filter with a query string, e.g. `?category=Functions/Trigonometric`.

## See also

- [Includes and File Reads](new-includes.md) · [Linter and Diagnostics](new-linter.md) · [Exports](new-exports.md)
- [Using the Desktop App](new-desktop-app.md) · [Using the VS Code Extension](new-vscode-extension.md)
