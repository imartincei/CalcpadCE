# Input mode: drop line-link arrows and source-line references in errors

## Context

Input mode is the "fill in this worksheet" view: `#UI` lines render as controls, the source
editor is hidden, and the person using it is a form filler, not the document's author.
Two leftovers from the authoring views still leak source-line detail into it:

1. **Hover line-link arrows** (`←` in the left gutter of every output line) navigate to a
   source line in an editor that input mode has hidden. The report pane *beside* the form
   already drops them (`calcpad-web/src/App.vue:1525-1530`,
   `vscode-calcpad/src/extension.ts:975-976`), but the input form itself still gets them.
2. **`Error in "x" on line [5]: ...`** — the `[5]` is a clickable source-line link, again
   pointing at nothing reachable. Emitted by Calcpad.Core for every expression error.

Intended outcome: in input mode, and in the report rendered beside the input form, output
lines have no arrows and error messages read `Error in "x": Undefined variable`. The
error-summary block (`Found 2 errors on lines: [5] [7]`) **stays**, and its chips must keep
scrolling the input/report document to the erroring output via the existing `err-N` /
`line-N` anchors. Everything outside input mode (preview, report mode, exports, PDF/Word)
is unchanged.

## Part 1 — Calcpad.Core: an opt-out for the source-line reference

`Calcpad.Core\Parsers\ExpressionParser\ExpressionParser.cs`

- Add a flag beside the existing render flags (near `Debug`/`ForPrint`/`ShowWarnings`, L49-51):
  `public bool ShowErrorLines { get; set; } = true;` — default true so every existing
  construction site keeps today's output.
- Add a small helper next to `LineHtml`/`ErrHtml` (L667-668) that picks the format:
  `ShowErrorLines ? string.Format(Messages.Error_in_0_on_line_1_2, expr, LineHtml(line), msg)`
  else `string.Format(Messages.Error_in_0_1, expr, msg)`.
- Route the two producers through it: the `MathParserException` catch at
  `ExpressionParser.cs:633` and `AppendError` at `ExpressionParser.cs:649` (the ~15
  keyword-level error sites all funnel through `AppendError`).

Deliberately untouched:
- `Id(line)` (L669-676) still emits `id="err-N" data-source-line="N"`, and `AppendErrors()`
  (L538-565) still emits the header and `roundBox` chips — that is what keeps chip
  navigation alive. Both remain gated on `Debug`, which is already true for input mode and
  is forced true for the report pane.
- `MacroParser.AppendError` (`Calcpad.Core\Parsers\MacroParser.cs:463`). Macro/include errors
  send the whole document down the unwrapped code-view path
  (`CalcpadService.ConvertCodeToHtml`), which is source-facing by construction.

### Resource string

New key `Error_in_0_1` = `Error in "{0}": {1}` in `Calcpad.Core\Messages.resx`, plus the
localized files next to the existing `Error_in_0_on_line_1_2` entry (resx L331,
bg L325, zh L322):

- `Messages.bg.resx`: `Грешка в "{0}": {1}`
- `Messages.zh.resx`: existing zh string reorders placeholders (`第 {1}：{2} 行"{0}"出错`) —
  use `"{0}"出错：{1}`
- Add the matching accessor to `Messages.Designer.cs` (mirror the `Error_in_0_on_line_1_2`
  block at L435-439).

No golden-stub churn: the `Tests\**\*.html.stub` fixtures containing `on line [` render with
the default `ShowErrorLines = true`.

## Part 2 — Backend plumbing

`Calcpad.Web\backend\Services\CalcpadService.cs`
- Add `bool? hideErrorLines = null` to `Convert` (L39-50), after `debug`.
- Resolve it right below the existing `enableUi &= !forPrint` normalization (L72-74),
  mirroring the `debug ?? !forPrint` idiom: input mode opts in by default, and the caller
  can force it for the report that accompanies the form.
  `ShowErrorLines = !(hideErrorLines ?? enableUi)` on the `ExpressionParser` built at L122-130.

`Calcpad.Web\backend\Controllers\CalcpadController.cs`
- Add `public bool? HideErrorLines { get; set; }` to `CalcpadRequest` (near
  `IncludeLineAnchors`, L858) with a doc comment stating it defaults to `EnableUi`.
- Pass `request.HideErrorLines` in the `/convert` call (L47-50). The `/docx` and `/pdf`
  paths pass `enableUi: false` and no override, so they keep the line references.

Docs: add the flag to the variant table in `Calcpad.Web\backend\API_SCHEMA.md` (L50-73) and
the mirrored `Calcpad.Web\frontend\API_SCHEMA.md` (L323-328).

## Part 3 — Frontend

### Shared client
- `calcpad-frontend\src\types\api.ts`: add `hideErrorLines?: boolean` to `UiConvertOptions`
  (L366-376) — it belongs with the other input-mode options rather than becoming a ninth
  positional arg.
- `calcpad-frontend\src\api\client.ts`: forward it in the `convert` body (L276-281) and note
  it in the `@param ui` doc block (L251-258).

### Web / desktop editor (`calcpad-web`)
- `App.vue` — `setPreviewHtml` (L1507-1516) passes `lineLinks: false` to the existing
  `injectLineLinks` when `resultMode.value === 'ui'`. `injectLineLinks` already has the
  `lineLinks` parameter and already keeps the `roundBox` handler and editor→preview sync
  outside that gate (L1618-1645 vs L1646-1656), so no new script plumbing is needed. Update
  the `lineLinks` doc comment (L1568-1570) to say both the form and its report drop the arrows.
- `main.ts` — `refreshUiPrintFor` (L597-599) adds `hideErrorLines: true` to the `ui` options
  object it already passes. The input-form render itself (L551-553, `enableUi: mode === 'ui'`)
  needs nothing — the server default covers it.

### VS Code extension (`vscode-calcpad`)
- `extension.ts` — arrows: extend the `getLineLinkScript` condition at L975-976 with
  `&& !enableUi` so the input-form panel drops them too; update the comment above it.
- `extension.ts` — request body at L889-901: add
  `hideErrorLines: forPrint && uiPanel !== undefined ? true : undefined`, matching the
  neighbouring `includeLineAnchors` line's shape.
- `extension.ts` — chip parity: the webview's `roundBox` handler (L734-740) only resolves
  `data-line` → `#line-N`, which does not exist on an error paragraph. Adopt App.vue's
  ordering (L1646-1656): prefer `data-error` → `#err-N`, fall back to `#line-N`. Without
  this, chips in the VS Code input form/report often scroll nowhere — which is the
  navigation that has to be preserved. `template.html` already gives `[id^="err-"]` the
  `scroll-margin-top` that clears the fixed error header (L653-656).

## Verification

1. `dotnet build` at the repo root; `dotnet test Calcpad.Tests`.
2. Add a case to `Calcpad.Tests\UiDirectiveTests.cs` (its `Render` helper at L11-18 already
   toggles `EnableUi`/`UiOverrides`): a document whose line errors, asserting the output
   contains `Error in` but not `on line` / `data-text=` when `EnableUi` is on, and still
   contains both when it is off.
3. Web/desktop: start the backend and `calcpad-web`, open a document with a `#UI` line plus a
   deliberate error (e.g. an undefined variable) and switch results to **Input**:
   - no `←` arrows on hover over output lines or in the left gutter,
   - errors read `Error in "…": …` with no bracketed line number,
   - the crimson `Found N error(s) on line:` bar is still there and clicking a chip scrolls
     the form to the erroring output,
   - toggle the report pane beside the form: same three checks.
   Then switch to **Preview** and **Report** — arrows back, `on line [N]` back.
4. VS Code: `Calcpad: Show Input Form` and `Calcpad: Show Report` on the same document, repeat
   the checks; confirm closing the input panel restores the report's arrows (the existing
   disposal re-render at extension.ts:1552-1566).
5. Export a PDF/Word file from report mode to confirm the export paths are untouched.
