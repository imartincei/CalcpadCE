# Route PathRoots through `/definitions`, drop the convert-header export metadata

> **Sequencing:** the image-inlining-to-Core refactor has landed and **Phase 2 is done with
> it** — the `X-Calcpad-PathRoots` header, `CalcpadService.Convert`'s root members,
> `parseConvertPathRootsHeader`, `ConvertResult.projectPath`/`libraryPath`, the
> `renderForExport` metadata and every `resolvedRoots` parameter are already gone. It also
> **deleted `createReferenceResolverFromRoots`**, which had no caller left; Phases 3 and 5 below
> still cite it, so re-add it (or give `createReferenceResolver` an optional roots override)
> when you get there. Start at Phase 1.

## Context

`{project}`/`{library}` completions and include navigation resolve their roots by scanning
**only the entry document's own text** (`calcpad-frontend/src/text/path-roots.ts:106`,
`scanDeclaredPathRoots`). A `#LibraryPath` declared inside an `#include`d file — the
library-module pattern in `docs/new-includes.md` — is therefore invisible to the editor: the
completion provider bails with zero suggestions
(`calcpad-web/src/editor/include-completions.ts:182`,
`vscode-calcpad/src/calcpadIncludeCompletionProvider.ts:159-170`) and go-to-definition /
include links fail the same way.

The server already computes the include-chain roots —
`Calcpad.Highlighter/ContentResolution/ContentResolver.Stage2.cs:36` shares one `PathRoots`
across the root file and every include, exactly like `MacroParser` — but the value is a local
variable that never reaches `Stage2Result`. The only path roots have to the client today is the
`X-Calcpad-PathRoots` response header on `/convert`, and carrying it forced **every export** to
thread `{ html, projectPath, libraryPath }` where plain HTML would do.

Intended outcome: `/definitions` (already refreshed per document and cached per document key)
reports the resolved roots; completions and include navigation read them, with the local text
scan as fallback; the convert header and the export metadata it caused are deleted. VS Code
additionally never triggers path completion on `{`, so token paths cannot complete there at
all — fixed here.

**Out of scope:** image inlining. The separate refactor moves it into Core so the frontend only
ever sees absolute paths.

All paths below are repo-root-relative.

## Phase 1 — Backend: report the resolved roots from `/definitions`

1. `Calcpad.Highlighter/ContentResolution/ContentResolverResult.cs:256` — add
   `public PathRoots PathRoots { get; set; }` to `Stage2Result` (`Calcpad.Core.PathRoots`,
   already referenced by this assembly).
2. `Calcpad.Highlighter/ContentResolution/ContentResolver.Stage2.cs:93` — assign the
   `pathRoots` instance from line 36 into the returned `Stage2Result`.
3. `Calcpad.Web/backend/Controllers/CalcpadController.cs:508` (`GetDefinitions`) — populate two
   new `DefinitionsResponse` members (`ProjectPath`, `LibraryPath`, nullable strings; the class
   is at `:1049`) from `staged.Stage2.PathRoots?.Project` / `?.Library`.
4. Document both fields in the `/definitions` section of `Calcpad.Web/backend/API_SCHEMA.md`.

Roots stay `null` when nothing is declared, when the document is untitled (a relative
`#LibraryPath` has no directory to resolve against), or in browser mode where the server cannot
read the included files — every consumer below keeps its text-scan fallback for those cases.

## Phase 2 — Remove the `/convert` path-roots metadata

**Backend**

- `Calcpad.Web/backend/Controllers/CalcpadController.cs:63-66` — delete the
  `X-Calcpad-PathRoots` header block.
- `Calcpad.Web/backend/Services/CalcpadService.cs:39` and `:148-150` — revert `Convert` to
  `(Html, OpenXmlExpressions, Errors)`; update the two call sites (`CalcpadController.cs:47`,
  `:337`).
- `Calcpad.Web/backend/Services/CalcpadApiService.cs:40` — drop `X-Calcpad-PathRoots` from
  `WithExposedHeaders`.
- Remove the header from `Calcpad.Web/backend/API_SCHEMA.md`.

**calcpad-frontend**

- `calcpad-frontend/src/api/client.ts:452-466` — delete `parseConvertPathRootsHeader` and its
  spread in `convert` (`:283`) and `convertUnwrapped` (`:351`); drop the export from `index.ts`.
- `calcpad-frontend/src/types/api.ts:303-313` — drop `projectPath`/`libraryPath` from
  `ConvertResult`; add them to `DefinitionsResponse` (`:183+`).
- `calcpad-frontend/src/services/message-bridge/base.ts:756-771` — `renderForExport` back to
  `Promise<string | null>`; update callers at `:197-201` (`#UI` controls) and `:663-667`
  (HTML export).
- `calcpad-frontend/src/text/path-roots.ts:189-195` — `createReferenceResolverFromRoots` stays
  (navigation uses it in Phase 5); rewrite the doc comment that cites the deleted header.

**calcpad-web / desktop**

- `calcpad-web/src/main.ts:570-573` and `:602-605` — drop the roots argument to
  `inlineDocumentImages`.
- `calcpad-web/src/services/tauri-bridge.ts:221-250` — drop the optional `resolvedRoots`
  parameter from `inlineDocumentImages`/`inlineLocalImages`; `:374-381` — `rendered` is now the
  HTML string.
- `calcpad-web/src/services/message-bridge.ts:131-137` — no edit needed; reverting the return
  type **fixes an existing bug** here, where the browser-mode PDF request was posting the whole
  `{ html, projectPath, libraryPath }` object as `html`.

**vscode-calcpad**

- `vscode-calcpad/src/extension.ts:1028-1032` and `:1069-1071` — `renderForExport` back to
  `Promise<string>`; update callers at `:1096-1103`, `:1211`, `:1964`.
- `vscode-calcpad/src/extension.ts:370-381` — drop `buildImageCache`'s `resolvedRoots`
  parameter; remove the `pathRoots` variable at `:934` and its use at `:960`, plus the
  `parseConvertPathRootsHeader` import.

If the image-inlining refactor has already deleted these call sites, this phase reduces to the
backend header removal plus the `client.ts`/`api.ts`/`base.ts` changes.

## Phase 3 — Shared directive module in `calcpad-frontend`

`parseDirectiveLine` is currently duplicated verbatim in both hosts
(`calcpad-web/src/editor/include-completions.ts:19-54`,
`vscode-calcpad/src/calcpadIncludeCompletionProvider.ts:32-83`). Create
**`calcpad-frontend/src/text/directives.ts`** holding one copy plus the pieces both providers
need:

- `Directive`, `DirectiveParse`, `parseDirectiveLine` (moved as-is).
- `INCLUDE_EXTENSIONS`, `DATA_EXTENSIONS`, `extensionsForDirective(directive)` — replaces the
  duplicated ternaries at `include-completions.ts:126-130` and
  `calcpadIncludeCompletionProvider.ts:127-131`.
- `PATH_ROOT_TOKEN`, `PATH_ROOT_LABEL`, `USER_TOKEN`, and
  `DIRECTIVE_TRIGGER_CHARACTERS = [' ', '/', '\\', '{']` so both hosts register the same
  trigger set.
- `async resolveCompletionPathRoots({ serverRoots, sourceText, beforeLine, documentDir,
  expandEnvVars, resolve, homeDir })` → `ResolvedPathRoots`: takes each kind's `serverRoots`
  value when non-null, otherwise falls back to `scanDeclaredPathRoots(sourceText, beforeLine)`
  + `resolvePathRoot` (both already in `text/path-roots.ts`). `expandEnvVars` must accept
  `(raw) => string | Promise<string>` — Tauri's is async, VS Code's is sync — matching how
  `createReferenceResolverFromRoots` already types it.

Add to `calcpad-frontend/src/services/definitions.ts`:
`getCachedPathRoots(documentKey): ResolvedPathRoots`, reading the cached response's new fields
and returning `{ project: null, library: null }` when there is no cache entry. Mirror it as a
passthrough on the VS Code wrapper (`vscode-calcpad/src/calcpadDefinitionsService.ts:26`).

Export everything from `calcpad-frontend/src/index.ts` next to the existing `path-roots` block,
delete both local copies, and re-point importers:
`calcpad-web/src/editor/completions.ts:11`, `calcpad-web/src/editor/references.ts:5`,
`vscode-calcpad/src/calcpadDefinitionProvider.ts:5`,
`vscode-calcpad/src/calcpadIncludeLinkProvider.ts:4`.

## Phase 4 — Completions consume the server roots

**Monaco** (`calcpad-web/src/editor/include-completions.ts`)

- Add `getServerPathRoots(): ResolvedPathRoots` to `IncludeCompletionsContext` (`:78-89`); wire
  it in `calcpad-web/src/main.ts:1015-1021` to
  `editorBridge.definitions.getCachedPathRoots(getActiveDocumentKey())` — the same key
  `completions.ts`/`hover.ts` already use.
- Replace the local resolution at `:142-150` with `resolveCompletionPathRoots`, keeping
  `beforeLine = position.lineNumber - 1` for the fallback only (a server-reported root is live
  regardless of where in the chain it was declared).
- Register the shared `DIRECTIVE_TRIGGER_CHARACTERS` at `:115` so `{` opens the widget.

**VS Code** (`vscode-calcpad/src/calcpadIncludeCompletionProvider.ts`)

- Constructor and `register()` (`:359-366`) take the definitions service; update the call at
  `vscode-calcpad/src/extension.ts:1792`. Use `getCachedPathRoots(document.uri.toString())` +
  `resolveCompletionPathRoots`, which also gives this provider the `{user}`-in-declared-root
  handling it lacks today (`:147`).
- Register the shared trigger characters, including `{`.
- `vscode-calcpad/src/calcpadCompletionProvider.ts` — add the stand-down guard at the top of
  `provideCompletionItems` (~`:62`), mirroring `calcpad-web/src/editor/completions.ts:26-29`:
  `if (parseDirectiveLine(lineToCursor)) return [];`. Without it the general provider (which
  registers `{` at `:469`) floods the list with snippets on a directive line.

Both providers keep returning an empty, `incomplete` list for a token whose root is still
unresolved — with server roots that now only happens when the root genuinely isn't declared
anywhere.

## Phase 5 — Include navigation

**VS Code**

- `vscode-calcpad/src/calcpadLocationResolver.ts:17-21` —
  `resolveDocumentPathRoots(document, serverRoots?)`, preferring each non-null server root over
  the text scan (`resolveDeclaredPathRoots` stays the fallback).
- `vscode-calcpad/src/calcpadIncludeLinkProvider.ts` and
  `vscode-calcpad/src/calcpadDefinitionProvider.ts` take the definitions service through their
  constructors/`register()` and pass the cached roots into `resolveDocumentPathRoots` /
  `resolveIncludeDirectiveLocation` (whose `roots` default already exists); update both
  registrations in `extension.ts`.

**Desktop**

- `calcpad-web/src/services/tauri-bridge.ts:742` (`resolveIncludePath`) — build the roots with
  `resolveCompletionPathRoots` (server roots from `getCachedPathRoots`, active tab text as
  fallback) and resolve through `createReferenceResolverFromRoots`, replacing the text-only
  `createReferenceResolver`. Feeds the Ctrl-click / go-to-include handlers at
  `calcpad-web/src/main.ts:925` and `:956`.

## Tests

- `Calcpad.Tests/HighlighterTests/PathRootsHighlighterTests.cs` — add a case (none exists today)
  asserting `Stage2.PathRoots.Library` is populated when `#LibraryPath` is declared **inside** an
  included file, and that a `{library}/…` include in the parent still resolves. Existing cases at
  `:36`/`:65`/`:75` show the fixture style.
- No unit-test harness exists for the frontend packages; those changes are verified by build +
  manual runs below.

## Verification

1. `dotnet build` at the repo root, then `dotnet test --filter PathRoots`.
2. Build the frontend in order (each consumes the previous `dist`): `npm run build` in
   `calcpad-frontend`, then `calcpad-web` and `vscode-calcpad`. Confirm nothing survives on the
   convert path: `rg "X-Calcpad-PathRoots|parseConvertPathRootsHeader"` returns nothing.
3. Start the backend and `POST /api/calcpad/definitions` with a document whose only `#include`d
   file declares `#LibraryPath ./lib` — the response must carry the resolved absolute
   `libraryPath`.
4. Desktop (`npm run tauri dev` in `calcpad-desktop`): open that document, type `#include {` —
   the widget must open on `{`, offer `{library}/`, and drill into the library folder. Ctrl-click
   an existing `{library}/foo.cpd` include and confirm it opens the file.
5. VS Code extension (F5): same two checks, plus confirm a directive line no longer shows
   snippet/variable completions.
6. Exports still work with the metadata gone: HTML, PDF (desktop **and** browser mode — the
   browser PDF path was previously broken), DOCX, `.cpdz`, portable `.cpd`.
