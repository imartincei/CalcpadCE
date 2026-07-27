# Running the CalcpadCE Server

The desktop app and the VS Code extension start the CalcpadCE calculation server for you, so most people will never run it by hand.
However, people who are familiar with coding can also run it directly — for example, to point several tools at one shared instance, or to script conversions and calls against its API.
This page covers running the server and the API it exposes.

> **Localhost only.** This build runs the server bound to your own machine (`localhost`, `127.0.0.1`, or `::1`) only. If you point it at any other address, it refuses to start. There is no multi-user hosting, authentication, or shared file storage in this build.

## Running

```bash
cd Calcpad.Web/backend
dotnet run
```

By default the server listens on `http://localhost:9420`.
To change the port, set `CALCPAD_PORT`, or pass a full bind URL with `--urls` — as long as it still points at a loopback address.

## API endpoints

| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/api/calcpad/convert` | POST | Convert a document to an HTML report (with theme + settings) |
| `/api/calcpad/convert-unwrapped` | POST | HTML of the raw, fully expanded source (used for error navigation) |
| `/api/calcpad/sample` | GET | Fetch a sample document |
| `/api/calcpad/pdf` | POST | Generate a PDF |
| `/api/calcpad/pdf/health` | GET | PDF service health check |
| `/api/calcpad/docx` | POST | Generate a Word `.docx` document |
| `/api/calcpad/highlight` | POST | Tokenize a full document for syntax highlighting |
| `/api/calcpad/highlight-line` | POST | Tokenize a single line (incremental) |
| `/api/calcpad/lint` | POST | Run the linter and return diagnostics |
| `/api/calcpad/definitions` | POST | List macros, functions, variables, and units |
| `/api/calcpad/find-references` | POST | Every occurrence of each symbol |
| `/api/calcpad/prettify` | POST | Pretty-print CalcpadCE source |
| `/api/calcpad/snippets` | GET | Snippets, optionally filtered by category |
| `/api/calcpad/debug-crash` | GET | Record a client-side crash in the server log |

The full request/response schema lives at [Calcpad.Web/backend/API_SCHEMA.md](../Calcpad.Web/backend/API_SCHEMA.md); the common shapes are summarized below.

## Common request fields

Most POST endpoints accept a request with these fields:

- `content` — the CalcpadCE source code
- `settings` — math / plot / unit configuration
- `theme` — `"light"` or `"dark"`
- `sourceFilePath` — the document's file path, used to resolve relative `#include` paths against the file's folder
- `forPrint` — when `true`, `NoPrint` regions are stripped before conversion (used by PDF export)

## Response shapes

### Definitions

Returns four parallel arrays:

- `macros[]` — name, parameters, isMultiline, content, source, description, paramTypes, paramDescriptions, defaults
- `functions[]` — name, parameters, expression, returnType, returnTypeId, hasCommandBlock, commandBlockType, commandBlockStatements, defaults
- `variables[]` — name, expression, type, typeId
- `customUnits[]` — name, expression

`typeId` values: 0 Unknown, 1 Value, 2 Vector, 3 Matrix, 5 Various, 6 Function, 7 InlineMacro, 8 MultilineMacro, 9 CustomUnit.

### Find-references

Three dictionaries (`variables`, `functions`, `macros`), each mapping a symbol name to an array of:

```typescript
{ line, column, length, source, sourceFile?, isAssignment }
```

`isAssignment: true` marks a definition or reassignment.

### Highlight

An array of `{ line, column, length, type, typeId, text? }`.
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
    label?: string,
    category: string,      // e.g. "Functions/Trigonometric"
    quickType?: string,    // e.g. "a" for ~a → α
    parameters?: Array<{ name, description? }>
  }>
}
```

Filter with a query string, e.g. `?category=Functions/Trigonometric`.

## See also

- [Includes and File Reads](new-includes.md) · [Linter and Diagnostics](new-linter.md) · [PDF Export](new-pdf-export.md)
- [Using the Desktop App](new-desktop-app.md) · [Using the VS Code Extension](new-vscode-extension.md)
