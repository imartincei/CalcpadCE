# Deep Investigation: Calcpad.Highlighter

## Context

The user has refactored `Calcpad.Highlighter` many times and wants a clear-eyed view of the rot that has accumulated: where code is duplicated, where past refactors have left mismatched approaches side-by-side, and where the hot path leaks performance. The project tokenizes and lints Calcpad source on every keystroke in editor scenarios (VS Code extension, web editor), so allocations in the per-line / per-token path compound quickly.

This is an investigation report, not an implementation plan. Each finding cites exact file/line locations so it can be acted on independently. A prioritized roadmap is provided at the end.

Scope: `/home/isaiahm/repos/CalcpadCE/Calcpad.Highlighter/` — Tokenizer/, Linter/ (Helpers + Validators Stage1/2/3), ContentResolution/, Parsing/, Prettifier/, Snippets/. ~76 C# files.

---

## Already Completed

- **D1** — Whitespace-skip loops consolidated behind `ParsingHelpers.SkipWhitespace(ReadOnlySpan<char>, ref int)`. 7 sites routed through it.
- **D2** — Bracket-matching unified behind `ParsingHelpers.FindMatchingClose(ReadOnlySpan<char>, openPos, open, close)`. 6 depth-tracking loops collapsed. `HasEqualsOutsideParens` left alone — different semantics.
- **D3** — Keyword-args / parameter-defaults feature **removed entirely** (was experimental, not intended for this branch). Strips `IsFunctionKeywordArg`, `IsKeywordArg`, `TryParseMacroKeywordArg`, `ExtractFunctionParamDefaults`, `Defaults` props on `MacroDefinition`/`FunctionDefinition`, `RequiredParamCount` on `MacroInfo`/`FunctionInfo`, `ParameterDefaults` on `VariableInfo`, `defaults` params on 4 `TypeTracker.Register*` methods, error codes `CPD-3314`/`CPD-3315`, and the matching DTO properties in `CalcpadController.cs`. To be re-imported alongside the feature from the experimental branch.
- **P2** — `LinterResult` now caches `_errorCount` / `_warningCount` (incremented in new internal `AppendDiagnostic`, decremented in new `RemoveDiagnostics(predicate)`). `HasErrors`/`HasWarnings`/`ErrorCount`/`WarningCount` are O(1). `DiagnosticExtensions` routed through `AppendDiagnostic`; `CalcpadLinter.ApplyIgnoreRegions` routed through `RemoveDiagnostics` so counts stay consistent after suppression.
- **P4** — Added `CalcpadBuiltIns.CommandsWithBrace` — precomputed `(Name, NameWithBrace)[]` for the `CommandsExcludingCommandBlocks` set. `UsageValidator.ValidateCommandSyntax` and `ValidateCommandVariables` now iterate this and do `line.IndexOf(cmdWithBrace, …)` directly — no per-line `cmd + "{"` allocation.
- **P5** — Replaced `line.Substring(0, atIndex)` and `line.Substring(atIndex + 1)` in `UsageValidator` with `line.AsSpan(...)`. `Contains('|')`/`Contains('&')` and `IndexOf('=')` work on the span. Removed the unused `declaredVarSection` local along the way.
- **P7** — Replaced `expandedLine.Split('\n')` in `ContentResolver.Stage3.cs` with a single-pass span scan that keeps `Split('\n')` semantics (does not consume `\r`). Removes the `string[]` allocation per macro-expanding line; per-segment `ToString()` only happens when it goes into `lines`.
- **D5 (partial)** — Audit confirmed: all diagnostics now route through `DiagnosticExtensions` (only two `new LinterDiagnostic` literals remain, both *inside* `DiagnosticExtensions` itself). No validators construct diagnostics directly any more. The D5 finding below is stale — leaving in place pending a follow-up sweep with M1 (span-vs-string consistency).
- **P6** — Macro expansion sorts macros once at the top of `ProcessStage3` into `sortedMacros` (a `(Name, Params, Content)` list) and threads it through `ExpandMacros`, eliminating the per-call `OrderByDescending().ToList()`. Parameter substitution now mutates a single `StringBuilder` via `Replace(oldValue, newValue)` instead of chaining `string.Replace` (which allocates a new string per parameter). New `AppendJoinedLines` helper in `ContentResolver.cs` feeds the StringBuilder without going through `JoinLines → ToString` first.
- **P1** — Tokenizer migrated to `ReadOnlySpan<char>` / `ReadOnlyMemory<char>` through the hot path. `TokenizeLineInternal` now takes `ReadOnlyMemory<char>`; `_state.Text` is a `ReadOnlyMemory<char>` so it can be stored across method calls. The main `Tokenize` loop walks the source string with manual offset tracking (replacing `LineEnumerator + lineSpan.ToString()`) and slices `source.AsMemory(start, length)` per line — zero allocation per line. Helper signatures updated to span/memory: `IsFunctionDefinitionLine`, `IsClosingSpecialContentTag`, `IsClosingSpecialTag`, `IsFilePathEndMarker`, `ExtractMacroParams`, `ExtractMacroCallArgsWithPositions`, `ExtractExpressionFromLine`, `ParseImaginary`, `InitState`, `DefinitionMetadata.TryParse`. `_lintDefLineText` is now `ReadOnlyMemory<char>`; `_macroCurrContentLines` keeps materializing strings since lines accumulate across the line boundary.
- **P3 (partial — D8)** — Merged `UsageValidator.ValidateCommandSyntax` and `ValidateCommandVariables` into a single `ValidateCommands` pass. Both methods previously ran their own pass over `stage3.Lines`, repeating `IsCpdMode` / `ShouldSkipLine` / `IsDirectiveLine` / `@`-search / command-name iteration / `tokens = tokenProvider.GetTokensForLine(i)` — now done once per line, then both diagnostic phases (CPD-3406 syntax, CPD-3408 loop-variable match) share the result. Behavior preserved: `$Plot` with `|`/`&` skips both phases; `$Plot`/`$Map` skip phase 2; phase 1 missing-`=` error still reported. The broader multi-validator unification (BalanceValidator, SemanticValidator, etc. sharing a single per-line dispatcher) remains as future work — see "Remaining Work" below.
- **D4** — Removed local `IsDigit` and `IsLatinLetter` in `CalcpadTokenizer.Helpers.cs`; call sites now use `char.IsAsciiDigit` / `char.IsAsciiLetter` (.NET 7+ built-ins, identical semantics). The local `IsMacroLetter` is *not* a duplicate of `CalcpadCharacterHelpers.IsMacroLetter` — different semantics (tokenizer-side is Unicode-tolerant and allows `$` after position 0; helpers-side is Latin-only per Calcpad.Core's strict macro-name validator). Renamed the tokenizer version to `IsMacroIdentChar` so the naming no longer falsely implies duplication, and added a doc comment explaining the difference.
- **M2** — Line iteration is now uniform across `ContentResolver`. Stage3's manual `\n` scan (introduced by P7) replaced with `LineEnumerator` — semantics still match because macro content is always `\n`-joined via `JoinLines` and per-line slices never contain `\r`. The tokenizer's main loop in `CalcpadTokenizer.Tokenize` previously did its own offset-tracking scan to produce `ReadOnlyMemory<char>` slices (a ref-struct `Span` can't be stored in `TokenizerState`); now uses a new `LineMemoryEnumerator` (added to `Parsing/LineEnumerator.cs`) which is the `ReadOnlyMemory<char>` analogue of `LineEnumerator`. The M2 finding's claim that *tests* use `string.Split` is stale — Highlighter tests no longer perform manual line splitting (they read whole-file content and pass it to `ContentResolver`/`CalcpadLinter`).

---

## Findings — Code Duplication

### D4. Character-classification split between two utility classes
- [CalcpadCharacterHelpers.cs:135-141](Calcpad.Highlighter/Linter/Helpers/CalcpadCharacterHelpers.cs#L135-L141) defines `IsMacroLetter`
- [CalcpadTokenizer.Helpers.cs:13-28](Calcpad.Highlighter/Tokenizer/CalcpadTokenizer.Helpers.cs#L13-L28) re-declares local `IsDigit`, `IsMacroLetter`, `IsLatinLetter`

`CalcpadTokenizer.Helpers.cs` should delegate to `CalcpadCharacterHelpers` rather than carry its own.

### D5. `DiagnosticExtensions` exists but is half-used
[DiagnosticExtensions.cs:20-217](Calcpad.Highlighter/Linter/Helpers/DiagnosticExtensions.cs#L20-L217) provides 9 overloads (AddDiagnostic ×3, AddError ×3, AddWarning ×3, AddInformation ×3) but several validators still build `new LinterDiagnostic { ... }` literals directly, duplicating the column-mapping boilerplate. Audit the Stage3 validators and route them all through the extensions.

### D6. Stage3 validator loop boilerplate
Every Stage3 validator repeats:
```
for (int i = 0; i < stage3.Lines.Count; i++) {
    if (!tokenProvider.IsCpdMode(i)) continue;
    ...
}
```
in [BalanceValidator.cs:17-97](Calcpad.Highlighter/Linter/Validators/Stage3/BalanceValidator.cs), [UsageValidator.cs:279-398](Calcpad.Highlighter/Linter/Validators/Stage3/UsageValidator.cs#L279-L398), [SemanticValidator.cs](Calcpad.Highlighter/Linter/Validators/Stage3/SemanticValidator.cs), and others. A `foreach (var (line, idx) in stage3.CpdLines())` enumerator would remove the boilerplate AND let multiple validators share a single tokenized-line pass (see P3 below).

### D7. Two overlapping line/quote state machines
- [LineParser.cs:32-151](Calcpad.Highlighter/Linter/Helpers/LineParser.cs#L32-L151) tracks `inSingleQuote`/`inDoubleQuote` to extract code vs string segments
- [CalcpadTokenizer.Comments.cs](Calcpad.Highlighter/Tokenizer/CalcpadTokenizer.Comments.cs) tracks comment/quote state in the tokenizer

The linter sometimes re-parses lines via `LineParser.ParseLine` after the tokenizer has already produced authoritative `Token` lists. The validators should consume `TokenizerResult.TokensByLine` rather than re-segmenting strings.

### D8. `ValidateCommandSyntax` and `ValidateCommandVariables` largely duplicated
[UsageValidator.cs:35-160](Calcpad.Highlighter/Linter/Validators/Stage3/UsageValidator.cs#L35-L160) and [UsageValidator.cs:167-277](Calcpad.Highlighter/Linter/Validators/Stage3/UsageValidator.cs#L167-L277) both scan for `@`, both filter to lines containing a known `Command{`, both find the trailing `=`. The first ~30 lines of each are near-identical. Merge into one pass that emits both diagnostic categories.

---

## Findings — Mismatched / Inconsistent Approaches

### M1. `string` vs `ReadOnlySpan<char>` inconsistency at API boundaries
- `LineParser` exposes both `IsDirectiveLine(string)` and `IsDirectiveLine(ReadOnlySpan<char>)`.
- [SemanticValidator.cs:161](Calcpad.Highlighter/Linter/Validators/Stage3/SemanticValidator.cs#L161) calls the `string` overload via `.Trim()` (allocating).
- [CommandBlockValidator.cs:72-76](Calcpad.Highlighter/Linter/Validators/Stage3/CommandBlockValidator.cs#L72-L76) calls the span overload via `.AsSpan().Trim()` (no alloc), but then immediately calls `.ToString()` anyway — defeating the savings.

There's no consistent rule about which to use. Pick one: span throughout, and only materialize strings when storing in result objects.

### M2. Line iteration: three different strategies inside the same class
Inside `ContentResolver`:
- Stage1/Stage2 use `LineEnumerator` (zero-alloc ref struct) at [ContentResolver.cs](Calcpad.Highlighter/ContentResolution/ContentResolver.cs)
- Stage3 uses `string.Split('\n')` at [ContentResolver.Stage3.cs:78](Calcpad.Highlighter/ContentResolution/ContentResolver.Stage3.cs#L78)
- Tests use a different `string.Split(new[] { "\r\n", "\r", "\n" }, …)` pattern

Same class, three approaches. Stage3 should adopt `LineEnumerator`.

### M3. Stage context injection inconsistency
- Stage1 validators receive `Stage1Context` directly.
- Stage2 validators receive `Stage2Context`.
- Stage3 validators receive `Stage3Context` **plus** a `TokenizedLineProvider` helper.

Stages 1 and 2 have no analogous helper-provider. Either all stages should get an injected provider for cross-cutting utilities (tokens, line classification, parsed segments) or none should. The current shape suggests Stage3 was retrofitted while 1/2 were left behind.

### M4. `LinterResult.HasErrors` / `ErrorCount` use LINQ over the diagnostics list
[LinterResult.cs:10-13](Calcpad.Highlighter/Linter/Models/LinterResult.cs#L10-L13):
```csharp
public bool HasErrors  => Diagnostics.Any(d => d.Severity == LinterSeverity.Error);
public int  ErrorCount => Diagnostics.Count(d => d.Severity == LinterSeverity.Error);
```
Every call scans the full list. Each `LinterResult` is a one-shot object — these should be cached counters incremented in `AddDiagnostic`. See P2.

### M5. Tokenizer round-trips spans to strings at the line boundary
[CalcpadTokenizer.cs:117-119](Calcpad.Highlighter/Tokenizer/CalcpadTokenizer.cs#L117-L119):
```csharp
var sourceSpan = source.AsSpan();
foreach (var lineSpan in new LineEnumerator(sourceSpan))
    TokenizeLineInternal(lineSpan.ToString(), lineNum++);
```
The `LineEnumerator` produces zero-alloc spans, then we materialize a string per line and use `text[i]` indexing throughout `TokenizeLineInternal`. For a 10K-line file that's 10K avoidable allocations. See P1.

### M6. Result-type shapes diverge
[TokenizerResult.cs](Calcpad.Highlighter/Tokenizer/Models/TokenizerResult.cs), [LinterResult.cs](Calcpad.Highlighter/Linter/Models/LinterResult.cs), and [ContentResolverResult.cs](Calcpad.Highlighter/ContentResolution/ContentResolverResult.cs) each define their own surface with no common shape. Not a bug; a sign these were written at different times.

### M7. `SourceMapper` and `LinterResult` private boundary
`SourceMapper` methods in [SourceMapper.cs:31-46](Calcpad.Highlighter/Linter/Helpers/SourceMapper.cs#L31-L46) are `public static` but only consumed by `LinterResult.MapDiagnosticsToOriginal`. They should be `internal`.

### M8. Tokenizer partial-class boundaries are blurry
`CalcpadTokenizer.Helpers.cs` carries methods that are tightly bound to tokenizer state (e.g., `IsClosingSpecialContentTag`) and used in exactly one site — they're not really helpers, they're inlined state-machine fragments split across files. Reorganization: keep static, pure helpers in `Helpers.cs`; move state-coupled fragments to the file that owns the state.

---

## Findings — Performance (impact-ranked)

### P1. Tokenizer materializes a fresh string per line  **(highest impact)**
[CalcpadTokenizer.cs:119](Calcpad.Highlighter/Tokenizer/CalcpadTokenizer.cs#L119) — `lineSpan.ToString()` per line. The entire `TokenizeLineInternal` uses `text[i]`/`text.Length` which work identically on `ReadOnlySpan<char>`. Migrating the signature to `ReadOnlySpan<char>` and only allocating `string text` inside `Append()` (where a `Token` is actually built) saves N allocations per file, where N is line count. On a 10K-line document, eliminates ~10K string allocs and ~10K associated GC pressure events.

Knock-on: `_state.Text` ([CalcpadTokenizer.cs:37](Calcpad.Highlighter/Tokenizer/CalcpadTokenizer.cs#L37)) is `string`; would need to become `ReadOnlyMemory<char>` (struct can't hold span). Some call sites use `text.AsSpan(i+1).IsWhiteSpace()` ([line 160](Calcpad.Highlighter/Tokenizer/CalcpadTokenizer.cs#L160)), confirming the loop is already span-friendly.

### P2. `LinterResult` LINQ properties are O(n) per call
[LinterResult.cs:10-13](Calcpad.Highlighter/Linter/Models/LinterResult.cs#L10-L13). UI typically reads `HasErrors` / `ErrorCount` per render. Replace with cached `int _errorCount; int _warningCount;` fields incremented inside `AddDiagnostic`. O(1) per access.

### P3. Stage3 validators each do a full pass over `stage3.Lines`
Seven validator methods in `UsageValidator` alone, plus `BalanceValidator`, `SemanticValidator`, `FormatValidator`, `NamingValidator`, `FunctionTypeValidator`, `HtmlCommentValidator`, `CommandBlockValidator` — each iterates every line in `stage3.Lines`. Many calls into `tokenProvider.IsCpdMode(i)`, `LineParser.ShouldSkipLine`, etc. are repeated.

Restructure: one pass over lines, dispatch to validators per line. Or batch into 2-3 phases (e.g., "needs tokens", "needs cpd-only", "needs raw text"). Estimated reduction: ~3-5× fewer per-line operations on large files.

### P4. `cmd + "{"` concatenation per command per line  **(high impact, easy)**
[UsageValidator.cs:61](Calcpad.Highlighter/Linter/Validators/Stage3/UsageValidator.cs#L61):
```csharp
var cmdIndex = line.IndexOf(cmd + "{", StringComparison.OrdinalIgnoreCase);
```
Inside a `foreach (var cmd in CommandsExcludingCommandBlocks)` — for every line containing `@`, this allocates `cmd.Length + 2`-char strings ~25 times. Same pattern in `ValidateCommandVariables`.

Fix: in `CalcpadBuiltIns`, pre-compute `static FrozenDictionary<string, string> CommandWithBrace` where values are `"$Sum{"`, `"$Repeat{"`, etc. Or single-pass: find `{` after `$`, look up the command name by the preceding identifier — one O(1) lookup per `{`, not 25 IndexOf calls per line.

### P5. `string.Substring` in command syntax validator
[UsageValidator.cs:77,87](Calcpad.Highlighter/Linter/Validators/Stage3/UsageValidator.cs#L77) — `line.Substring(0, atIndex)` and `line.Substring(atIndex + 1)` allocate per line with `@`. Replace with `line.AsSpan(0, atIndex)` and `line.AsSpan(atIndex + 1)`; `Contains('|')` works on spans.

### P6. Macro expansion allocates LIST + STRING per param replacement
[ContentResolver.Stage3.cs:855-961](Calcpad.Highlighter/ContentResolution/ContentResolver.Stage3.cs#L855-L961) — `sortedMacros = macros.OrderByDescending(…).ToList()` runs once per macro expansion (could be once per resolve), and `macroContent.Replace(param, arg)` allocates a new string per parameter. For a macro with 5 params expanded 100 times: 500 string allocations just for parameter substitution.

Fix: sort macros once at the top of Stage3, not per call. Use `StringBuilder` for param substitution, or build the replacement in a single pass with `Span<char>` scratch buffer.

### P7. `Split('\n')` after macro expansion
[ContentResolver.Stage3.cs:78](Calcpad.Highlighter/ContentResolution/ContentResolver.Stage3.cs#L78) — `expandedLine.Split('\n')` allocates a `string[]` per macro-expanding line. Use the existing `LineEnumerator` (already used elsewhere in this class).

### P8. `TypeTracker` substring allocations during type inference
[TypeTracker.cs:203,298,332,354,439,495,715](Calcpad.Highlighter/Linter/Helpers/TypeTracker.cs) — 7+ `Substring()` calls per type-inference operation. With heavy type-tracked code, each variable use can trigger several. Convert to span slicing + index tracking.

### P9. `ParameterParser.ParseParameters` allocates `List<List<Token>>` per call
[FunctionTypeValidator.cs:69](Calcpad.Highlighter/Linter/Validators/Stage3/FunctionTypeValidator.cs#L69) — per function call site. For a line with 10 function calls: 10+ nested lists. Cache a pooled `List<List<Token>>` and `Clear()` between uses, or use `ArrayPool<Token>` for the inner lists.

### Lower-priority items (P11-P14)
- `string.Join(", ", usedVariables.Select(…))` in error paths — only matters if errors are common; not hot.
- `FormatRegex` — verified static + `RegexOptions.Compiled` in [CalcpadPatterns.cs](Calcpad.Highlighter/Linter/Constants/CalcpadPatterns.cs). OK.
- `CalcpadBuiltIns.CommandsExcludingCommandBlocks` lazy property — already cached after first hit ([CalcpadBuiltIns.cs:171](Calcpad.Highlighter/Linter/Constants/CalcpadBuiltIns.cs#L171)). Fine as-is.
- `CharClassifier` — well-designed already; precomputed ASCII table + Unicode fallback. No changes needed.

---

## Prioritized Roadmap

Order by ROI (impact ÷ effort). Items are independent and can land separately.

**Tier 1 — significant perf, contained scope** *(complete — see "Already Completed" above)*
1. ~~**P2** — Cache `_errorCount`/`_warningCount` in `LinterResult`~~ ✓
2. ~~**P4** — Precompute `cmd + "{"` strings~~ ✓
3. ~~**P5** — Replace `Substring` with `AsSpan` in `UsageValidator`~~ ✓ (hot sites at lines 77/87/213; remaining `Substring` calls in `TypeTracker`-style code paths are tracked under P8)
4. ~~**P7** — Replace `Split('\n')` with `LineEnumerator` in `Stage3.cs:78`~~ ✓

**Tier 2 — bigger perf wins, larger refactor** *(complete — see "Already Completed" above)*
5. ~~**P1** — Tokenizer span migration~~ ✓
6. ~~**P3** — Unify Stage3 validator passes~~ ✓ (D8 merge in `UsageValidator`; broader multi-validator unification deferred — see Remaining Work)
7. ~~**P6** — Macro expansion: sort once, replace via StringBuilder~~ ✓

**Tier 3 — cleanup / consistency**
8. **D5 + M1** — Audit all `new LinterDiagnostic` sites; route through `DiagnosticExtensions`; pick span-or-string and apply consistently
9. **D8** — Merge the two `ValidateCommandSyntax`/`ValidateCommandVariables` methods (first ~30 lines are near-identical)
10. ~~**D4** — Consolidate char-class helpers~~ ✓ (replaced `IsDigit`/`IsLatinLetter` with `char.IsAscii*`; kept local `IsMacroIdentChar` since semantics differ from `CalcpadCharacterHelpers.IsMacroLetter`)
11. ~~**M2** — Standardize line iteration on `LineEnumerator` everywhere~~ ✓ (Stage3 + tokenizer main loop, added `LineMemoryEnumerator` for the Memory case)
12. **M3, M7, M8** — Stage-context symmetry, visibility cleanup, partial-class re-organization

---

## Critical Files

Most-touched in any tier-1/2 fix:
- [Calcpad.Highlighter/Tokenizer/CalcpadTokenizer.cs](Calcpad.Highlighter/Tokenizer/CalcpadTokenizer.cs) — main loop, state struct
- [Calcpad.Highlighter/Linter/Models/LinterResult.cs](Calcpad.Highlighter/Linter/Models/LinterResult.cs) — counter caching
- [Calcpad.Highlighter/Linter/Validators/Stage3/UsageValidator.cs](Calcpad.Highlighter/Linter/Validators/Stage3/UsageValidator.cs) — biggest per-line allocator
- [Calcpad.Highlighter/Linter/Helpers/DiagnosticExtensions.cs](Calcpad.Highlighter/Linter/Helpers/DiagnosticExtensions.cs) — diagnostic factory
- [Calcpad.Highlighter/ContentResolution/ContentResolver.Stage3.cs](Calcpad.Highlighter/ContentResolution/ContentResolver.Stage3.cs) — macro expansion + Split
- [Calcpad.Highlighter/Linter/Helpers/TypeTracker.cs](Calcpad.Highlighter/Linter/Helpers/TypeTracker.cs) — Substring hotspots

Reusable existing utilities (prefer over new code):
- `Parsing/LineEnumerator.cs` — already zero-alloc; use it from Stage3 and tokenizer
- `Parsing/CharClassifier.cs` — keep using; well-designed precomputed table
- `Linter/Helpers/CalcpadCharacterHelpers.cs` — central identifier/Greek/unicode helpers
- `Linter/Helpers/DiagnosticExtensions.cs` — central diagnostic factory
- `Linter/Helpers/SourceMapper.cs` — central line/column mapping

---

## Remaining Work — P3 broader unification

D8 merged the two near-identical UsageValidator passes. The full P3 idea — *one* per-line pass that dispatches to all Stage3 validators — remains. Concretely:

- 18 separate per-line scans still exist across `BalanceValidator` (2), `NamingValidator` (2), `SemanticValidator` (5), `UsageValidator` (5 remaining), `FunctionTypeValidator`, `FormatValidator`, `HtmlCommentValidator`, `CommandBlockValidator`. Each repeats the `if (!tokenProvider.IsCpdMode(i)) continue;` boilerplate and some repeat `LineParser.ShouldSkipLine` / `IsDirectiveLine` work.
- Approach: introduce a `Stage3LineDispatcher` (or `Stage3Context.EnumerateCpdLines(tokenProvider)`) that yields `(int Index, string Line, string Trimmed, List<Token> Tokens)`. Validators expose a `ValidateLine(...)` plus optional `Finalize(...)` for cross-line state (e.g., control-block balance).
- Risk: many validators have subtle multi-line state (e.g., balance tracking, naming context). Each needs an audit to confirm what is line-local vs cross-line before swapping to the dispatcher.
- Expected reduction: ~3-5× fewer per-line operations on large files (per the original investigation estimate).

The D6 boilerplate-extraction enumerator (`stage3.CpdLines()`) is also still pending — it's a prerequisite for the broader P3 refactor.

## Verification

For each tier-1 change:
1. **Correctness**: run the test runner in [Calcpad.Highlighter/Tests/](Calcpad.Highlighter/Tests/) — `LinterTestRunner.cs` and `QuickTest.cs`. The repo already has a corpus of fixture files.
2. **Perf**: pick a representative large `.cpd` (100+ lines, with macros and command blocks). Wrap `Tokenize` and `Lint` in a `Stopwatch`/`BenchmarkDotNet` micro-benchmark; record allocations with `[MemoryDiagnoser]`. Before/after diff per change.
3. **Regression**: run the wider build — Highlighter is consumed by `Calcpad.Server`, `Calcpad.Web`, and the VS Code extension. Build the solution; run any server-side highlighter tests.
4. **UI smoke test**: load a representative file in the web editor; confirm syntax highlighting and linter squiggles are unchanged.
