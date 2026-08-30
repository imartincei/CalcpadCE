# Calcpad.Highlighter

A .NET library that provides syntax analysis, linting, content resolution, prettifying, and autocomplete data for Calcpad source code. Calcpad.Server consumes it to power the `/highlight`, `/lint`, `/definitions`, `/symbol-at-position`, `/prettify`, and `/snippets` endpoints; every editor (VS Code extension, web editor, desktop app) gets its language features from there.

No NuGet dependencies — only the .NET BCL and a project reference to Calcpad.Core (shared settings/PDF types and a handful of parsing rules kept in sync with the engine).

This file covers the library's internals. The behavior it produces is documented for users in [docs/](../docs/) — [diagnostics and CPD codes](../docs/new-linter.md), [includes and path roots](../docs/new-includes.md), [metadata comments](../docs/new-metadata-comments.md). The endpoint payloads, including the full token-type table, are in [../Calcpad.Web/backend/API_SCHEMA.md](../Calcpad.Web/backend/API_SCHEMA.md).

## Layout

```
Calcpad.Highlighter/
├── ContentResolution/   Three-stage content pipeline (the core of the library) + SymbolResolver
├── Tokenizer/           Syntax tokenization for highlighting and analysis
├── Linter/              Error detection with 12 validators across 3 stages
├── Snippets/            Autocomplete/IntelliSense data
├── HtmlComment/         HTML/metadata comment block parsing
├── Prettifier/          Re-indentation of Calcpad source
└── Parsing/             Low-level parsing utilities (CharClassifier, LineEnumerator, SplitEnumerator)
```

Tests live in [../Calcpad.Tests/Highlighter/](../Calcpad.Tests/Highlighter/).

---

## Content Resolution Pipeline

`ContentResolver` processes raw source through three stages, each building on the previous, and every stage keeps a source map so errors trace back to the original lines.

```csharp
var resolver = new ContentResolver();
var staged = resolver.GetStagedContent(
    content: sourceCode,
    includeFiles: includeDict,         // filename -> content (plain text)
    clientFileCache: cacheDict,        // filename -> raw bytes (base64-decoded)
    sourceFilePath: "/path/to/file.cpd"
);
```

### Stage 1 — Line continuations

Merges multi-line expressions into single lines. Explicit continuation is a trailing ` _`; implicit continuation happens when a line's last non-comment character is one of `;|&@:({[` (`CharClassifier.IsLineExtension`).

`Stage1Result` carries `Lines`, `SourceMap`, and `LineContinuationSegments` — where each original line's content starts inside a merged line, which is what makes diagnostic columns accurate.

### Stage 2 — Include resolution and macro collection

**Pass 1** replaces `#include` lines with the referenced file's content (recursive, 20 levels deep, circular includes detected), resolves `#ProjectPath`/`#LibraryPath` roots and environment variables, and tracks source origin (`local` vs `include`) plus original file line numbers per line. A file that can't be found becomes an error comment and processing continues.

**Pass 2** tokenizes in **Macro** mode to collect every `#def`: name, parameters, defaults, content, description, and source location. It also computes *comment parameters* — parameters used inside comments, with transitive closure through nested macro calls — so the tokenizer highlights the matching call-site arguments as comments rather than expressions.

`Stage2Result` carries `Lines`, `SourceMap`, `IncludeMap`, `PathRoots`, `MacroDefinitions`, `MacroCommentParameters`, `MacroParameterOrder`, `MacroBodies`, `UserDefinedMacros`, and `DuplicateMacros`.

### Stage 3 — Macro expansion and definition collection

**Phase 1** expands macro calls by substituting arguments into macro bodies (skipping the already-collected `#def` blocks), recording expansion metadata so errors inside expanded content map back to the call site.

**Phase 2** tokenizes the expanded content in **Lint** mode to extract variables, functions, custom units (`.unitName`), and command-block functions (`$Inline{}`, `$Block{}`, `$While{}`); builds the `TypeTracker`; and builds symbol indices mapping every name to all its occurrences with source-file info.

`Stage3Result` carries the fully expanded `Lines` plus `MacroExpansions`, `UserDefinedFunctions`/`FunctionsWithParams`, `VariablesWithDefinitions`, `CustomUnits`, `CommandBlockFunctions`, `TypeTracker`, `VariableAssignments`/`VariableUsages`/`VariableReassignments`, and `VariableIndex`/`FunctionIndex`/`MacroIndex`.

### How content is fetched

`ContentResolver` performs no I/O of its own beyond the filesystem probe below; the caller pre-fetches everything and passes it in. `ResolveFileContent` tries, in order:

1. **Filesystem** — `Path.GetFullPath` after environment-variable expansion, relative to the source file's directory.
2. **`includeFiles`** — `Dictionary<string, string>` of plain-text content, resolved path first, then raw filename.
3. **`clientFileCache`** — `Dictionary<string, byte[]>` decoded as UTF-8, for files a client (e.g. the VS Code extension) holds in memory that the server can't read.

### Source mapping

```
Stage 3 line → Stage3.SourceMap → Stage 2 line → Stage2.SourceMap → Stage 1 line → Stage1.SourceMap → Original line
```

`SourceMapper` walks the full chain, including continuation-segment mapping for columns inside merged lines. `SymbolResolver.ResolveSymbolAt` answers "what symbol is at this cursor?" from the Stage 3 indices — the single implementation shared by every editor integration.

---

## Tokenizer

`CalcpadTokenizer` turns source into typed tokens. It is split across 8 partial class files (core, comments, macros, parsing, type resolution, helpers, definitions, macro collection) and runs in three modes:

| Mode | Purpose | Used by |
|------|---------|---------|
| **Highlight** | Tokens for syntax coloring only | `/highlight`, `/highlight-line` |
| **Macro** | Tokens + full macro definitions | Stage 2 |
| **Lint** | Tokens + variables, functions, units, command blocks, loop and `#read` variables | Stage 3 |

`TokenType` ordinals are part of the wire format and are tabled in [API_SCHEMA.md](../Calcpad.Web/backend/API_SCHEMA.md) under `POST /highlight`; ordinals 29–30 are reserved so serialized values never renumber.

Parsing is allocation-light: `ReadOnlySpan<char>` throughout, a pre-computed ASCII classification table (`CharClassifier`) for O(1) character lookups, and ref-struct enumerators (`LineEnumerator`, `SplitEnumerator`) for splitting.

---

## Linter

`CalcpadLinter` validates a `StagedResolvedContent` and returns diagnostics with CPD codes, severities, and original-source positions.

```csharp
var ignoreRegions = new LintIgnoreRegionParser().ExtractRegions(sourceCode);
var result = new CalcpadLinter().Lint(staged, ignoreRegions);
// result.ErrorCount, result.WarningCount, result.Diagnostics
```

Twelve validators run in stage order:

| Stage | Validators |
|-------|-----------|
| 1 | `IncludeValidator` — `#include` syntax and file resolution |
| 2 | `MacroValidator` — duplicates, parameter syntax, naming, nesting |
| 3 | `BalanceValidator`, `NamingValidator`, `UsageValidator`, `SemanticValidator`, `FunctionTypeValidator`, `CommandBlockValidator`, `FormatValidator`, `HtmlCommentValidator`, `SettingsValidator`, `UiValidator` |

Every diagnostic code, its severity, and the `LintIgnore`/`EndLintIgnore` markers that `LintIgnoreRegionParser` reads are documented in [docs/new-linter.md](../docs/new-linter.md). Codes are defined in [Linter/Constants/ErrorCodes.cs](Linter/Constants/ErrorCodes.cs) — keep the two in sync when adding one.

`HtmlCommentParser` groups comment tokens into HTML/metadata blocks and parses their JSON payloads; `HtmlCommentValidator`, `SettingsValidator`, `UiValidator`, and the lint-ignore parser all work from those blocks rather than from individual tokens.

---

## Snippets

`SnippetRegistry` aggregates the 13 arrays under [Snippets/Data/](Snippets/Data/) into frozen collections at static-init time. Each `SnippetItem` has insert text (`§` marks the cursor), description, documentation, example, a hierarchical `Category`, optional `Parameters` and `ReturnType`, and an optional `QuickType` shortcut for `~`-prefix insertion. `InternalOnly` items exist purely to feed the linter's built-in sets and are excluded from the API payload.

It serves three consumers:

1. **Autocomplete** — `GetAllSnippetsArray()` / `GetSnippetsByCategory(prefix)` back `GET /snippets` ([schema](../Calcpad.Web/backend/API_SCHEMA.md)).
2. **Built-in name sets** — `GetFunctionNames()`, `GetKeywordNames()`, `GetCommandNames()`, `GetUnitNames()`, `GetConstantNames()`, `GetSettingNames()`, `GetControlBlockKeywordNames()`, `GetEndKeywordNames()`, `GetOperators()` return `FrozenSet`s used by `NamingValidator` to catch conflicts (a variable named `sin`) and by the tokenizer to classify identifiers.
3. **Signature checking** — `GetFunctionSnippetsByName()`, `GetFunctionOverloads()`, `GetVectorReturningFunctions()`, `GetMatrixReturningFunctions()` give `FunctionTypeValidator` argument counts, types, and return types.

Adding a built-in function anywhere means adding its snippet here; nothing else knows the name exists.

---

## Type Tracking

`TypeTracker` records an inferred `CalcpadType` for every definition found in Stage 3:

| Value | Meaning |
|-------|---------|
| `Unknown` | Type could not be determined |
| `Value` | Scalar (real or complex) |
| `Vector` / `Matrix` | 1D / 2D array |
| `CustomUnit` | `.unitName = expression` |
| `Function` | User-defined function |
| `InlineMacro` / `MultilineMacro` | `#def` forms |
| `Various` | Reassigned to a different type — less strict linting applies |

Inference reads the defining expression: `[1; 2; 3]` is a Vector, `[1|2; 3|4]` a Matrix, `sin(x)` a Value, `take(v; 3)` a Vector via the vector-returning function set.

---

## Prettifier

`CalcpadPrettifier.Prettify` re-indents source by tracking control-block depth (`#if`/`#else`/`#end if`, `#for`/`#while`/`#repeat`/`#loop`, multiline `#def`/`#end def`; inline `#def name = ...` opens nothing). It touches leading whitespace only — content, comments, and CRLF/LF style are preserved. Backs `POST /prettify`.
