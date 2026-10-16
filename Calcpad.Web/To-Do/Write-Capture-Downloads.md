# Downloading `#write`/`#append` Output

Designed 2026-08-21 alongside the write-mode setting, then deferred. Line numbers are as of that date and should be spot-checked before acting.

The goal: capture what `#write`/`#append` would produce in memory rather than on disk, and offer it in the Export tab's Write/Append section as a list of files — per-file `Save…` plus `Download all (ZIP)` — named by bare filename with the same `-1`/`-2` collision suffixes the portable export uses. That is what makes the feature usable when the targets are somewhere the author does not want overwritten, or do not exist at all.

Prerequisite already shipped: [`ExpressionParser.AllowDataWrite`](../../Calcpad.Core/Parsers/ExpressionParser/ExpressionParser.DataWrites.cs), the setting that decides whether a given parse may write.

## Calcpad.OpenXml — stream-based Excel write

[`ExcelData.Write`](../../Calcpad.OpenXml/ExcelData.cs#L191) is path-based. Three structural edits, no behavior change on the disk path:

1.  Extract the body of `CreateSpreadsheetWorkbook` (lines 237-248) into `InitWorkbook(SpreadsheetDocument, sheetName)`; the file overload keeps calling it.
2.  Extract the body of `Write` (lines 197-231, verbatim) into `WriteToDocument(SpreadsheetDocument, sheetName, rangeStart, rangeEnd, data)`.
3.  Add `public static byte[] WriteToMemory(byte[] existing, sheetName, rangeStart, rangeEnd, data, bool append)`, where `reuse = append && existing is { Length: > 0 }` is the exact analogue of the disk guard `!File.Exists(filepath) || !append` at line 193 — `Open(stream, true)` when reusing, `Create(stream, …)` + `InitWorkbook` otherwise.

`SpreadsheetDocument.Create/Open` on a `MemoryStream` is already proven in production on this same SDK: [OpenXmlWriter.cs:26](../../Calcpad.OpenXml/OpenXmlWriter.cs#L26) does `WordprocessingDocument.Create(stream, …)` and [CalcpadController.cs:384-387](../backend/Controllers/CalcpadController.cs#L384-L387) reads `ms.ToArray()` after that document is disposed. Three load-bearing constraints:

-   The stream must be **expandable** — `new MemoryStream()` then `Write(existing)`, never `new MemoryStream(existing)`, which is fixed-length and cannot grow the package.
-   `Position = 0` before `Open`.
-   `ToArray()` **outside** the `using`, or the package's closing flush is missing. Valid on a closed `MemoryStream` by contract.

Sole caller of `ExcelData.Write` is [DataExchange.cs:248](../../Calcpad.Core/Parsers/ExpressionParser/ExpressionParser.DataExchange.cs#L248); its signature is unchanged.

## Calcpad.Core — the capture

Extend [`ExpressionParser.DataWrites.cs`](../../Calcpad.Core/Parsers/ExpressionParser/ExpressionParser.DataWrites.cs):

```csharp
public const int MaxWriteCaptureSize = 10 * 1024 * 1024;   // matches MaxEmbeddedDataSize
public bool CaptureWrites { get; set; }
public IReadOnlyList<WrittenFile> WrittenFiles => _writes.Files;
/// True when the capture gave up on retaining bytes: the paths and sizes are still right.
public bool WriteCaptureTooLarge => _writes.TooLarge;
```

`WrittenFile` is a top-level public class carrying `Path`, `Size`, `Appended`, `IsExcel`, `DirectoryExists`, `IsCaptured` and `ToArray()`. It holds a `MemoryStream` rather than concatenating `byte[]` — a `#for` loop appending 10 000 rows would otherwise be O(n²) in copying. Use `IReadOnlyList`, not the bare `public readonly List<string>` of `OpenXmlExpressions` ([ExpressionParser.cs:59](../../Calcpad.Core/Parsers/ExpressionParser/ExpressionParser.cs#L59)): this is the parse's output, not a scratch buffer a host appends to. `_writes.Clear()` goes beside `OpenXmlExpressions.Clear()` in `Initialize` (~line 518) — required, since WPF's `_parser` is long-lived.

A private nested `WriteLog` owns one entry per distinct resolved path (keyed case-insensitively on Windows, matching [OutputTargets.cs:28-29](../backend/Services/OutputTargets.cs#L28-L29)), ordered by first write; `Existing(path)` returning what an `#append` adds to — this parse's own capture for that path if there is one, else the file on disk, else empty; and the running byte total. It is nested so `DataExchange`, also nested in `ExpressionParser`, can take it as a parameter.

The gate in `ParseKeywordWrite` becomes `AllowDataWrite || CaptureWrites`, with `DataExchange.Write(options, m, _writes, CaptureWrites)` and `linked: … && !CaptureWrites` on the HTML report — a captured write keeps *was successfully* (it did happen; only the destination differs) but drops the `file:///` anchor.

### Capture in `DataExchange`

-   `Write` ([line 187](../../Calcpad.Core/Parsers/ExpressionParser/ExpressionParser.DataExchange.cs#L187)) takes `(options, data, WriteLog log, bool capture)`. Its `Exceptions.PathNotFound(dir)` check (lines 195-196) is a pre-flight for *creating* a file, so it becomes `if (!capture && !dirExists)`. The real-write path is unchanged — `capture` is false there and the expression reduces to the original. `dirExists` is recorded on the `WrittenFile` instead of thrown away, so the UI can offer the download *and* say the folder is not there. Everything upstream still validates: `ReadWriteOptions` already ran `Path.GetFullPath` (lines 184-185) and the `fileName == "."` check still runs first.
-   `WriteCSV` (lines 216-241) splits on `TextWriter` so the disk path keeps streaming (no buffer-the-whole-CSV regression) and `CaptureCSV` passes a `StringWriter`. Bytes come out identical: `StringWriter.NewLine` defaults to `Environment.NewLine` like `StreamWriter`'s, and neither `StreamWriter`'s default UTF-8 nor `Encoding.UTF8.GetBytes` emits a BOM.
-   `CaptureExcel` calls `ExcelData.WriteToMemory(options.Append ? log.Existing(path) : null, …)` inside `Write`'s existing try, so a locked or unreadable target surfaces as a normal `MathParserException` rather than a silently-wrong download.
-   Excel capture stores with `append: false` (`WriteToMemory` returns the *whole* rewritten workbook); CSV capture stores with `options.Append` (it returns only the new rows).
-   On the disk path, `file.Size = new FileInfo(fullPath).Length` — one stat on a file just closed, inside the existing try, so `WrittenFiles` is self-describing for WPF and the CLI too.
-   `ReadWriteOptions` is a `ref struct` ([ExpressionParser.ReadWriteOptions.cs:28](../../Calcpad.Core/Parsers/ExpressionParser/ExpressionParser.ReadWriteOptions.cs#L28)), so everything retained (`FullPath`, `Append`, `IsExcel`) must be copied into plain values at the write site.

### `#append` semantics

**Seed from the target and return the concatenation.** Capture means *the bytes the file would hold after this parse* — the only thing a download can honestly be. A file named `out.csv` holding only the delta looks like data loss when opened, and worse inside a ZIP. When the target does not exist, start from empty with no error: that is already what disk does — `StreamWriter(path, append: true)` creates the file (line 224) and `ExcelData.Write` calls `CreateSpreadsheetWorkbook` when `!File.Exists` (lines 193-194). `Appended` stays true so the UI can still say so.

### `#write` inside a `#for`

**One entry per distinct path, mirroring disk exactly**: `#write` truncates (`SetLength(0)`), `#append` seeks to end and accumulates. A 1000-iteration loop would otherwise produce 1000 download entries deduping to `out-1.csv` … `out-1000.csv`, none of which is the file on disk. Memory is bounded by final content rather than iteration count, and the capture answers exactly one question — *what would be on disk?* Because `Existing` prefers this parse's own capture over disk, a `#write` header followed by `#append` rows produces header-then-rows once, not a re-seed per iteration.

### Too-large capture — report, don't fail

The cap applies **only to capture** (the disk path buffers nothing). When the running total crosses `MaxWriteCaptureSize`, `WriteLog` sets `TooLarge = true`, **drops every retained buffer** (partial bytes are useless, and clearing them frees the memory and lets the frontend say one clear thing), and keeps recording each entry's `Path`, `Size`, `Appended`, `IsExcel`, `DirectoryExists` — generating subsequent files' bytes only to measure and discard them.

**The parse is not aborted and no error is raised.** The directive did everything it was asked to; only the download is unavailable. The endpoint returns the list with `Data: null` and `captureTooLarge: true`, and the UI shows *"These `#write` files total N MB — too large to download. Use Write to Disk instead."* Measuring a file means materializing it once, a bounded transient — far simpler than pre-flight estimation and no worse than what the disk path already streams.

## Backend

[`CalcpadService.Convert`](../backend/Services/CalcpadService.cs#L39) gains `bool captureWrites = false` and a fourth tuple element `IReadOnlyList<WrittenFile> Writes`; after line 143, `writes = parser.WrittenFiles.ToList()` — `.ToList()` matters, the list is live on a parser about to go out of scope. Both existing destructurings gain a `_`.

### `POST api/calcpad/data/write`

One endpoint with a `capture` flag: both operations take identical input and run an identical parse, and splitting them would duplicate the whole `Convert` plumbing and let the two lists drift.

-   `capture: false` → `write: true, captureWrites: false` → real writes; response lists the paths.
-   `capture: true` → `write: false, captureWrites: true` → nothing touches disk; response carries base64 bytes plus the flat deduped name.

Three things it must get right:

-   **`debug: true`.** `RecordError` ([ExpressionParser.cs:672-674](../../Calcpad.Core/Parsers/ExpressionParser/ExpressionParser.cs#L672-L674)) returns early when `Debug` is false — which is why the silent pass sets it. Without it the error list comes back empty.
-   **200, not 400, when there are errors.** A first bad `#write` aborts the parse (it throws `MathParserException`, `ParseKeywordWrite` does not catch, and the only handler is the outermost one at `ExpressionParser.cs:219-222`), so partial results are normal. Unlike `portable/package`, which is all-or-nothing by design, a 400 here would make the client discard the files that did succeed.
-   **`capture: false` with no `SourceFilePath` → 400.** A relative target would otherwise resolve against the server's own working directory and land there.

DTOs inline in the controller file after `CalcpadRequest`, matching the file's existing style:

```csharp
public class DataWriteRequest  { Content, Settings, SourceFilePath, ForPrint, UiOverrides, bool Capture }
public class DataWriteResponse { List<DataWriteFile> Files, List<string> Errors, bool CaptureTooLarge }
public class DataWriteFile     { Path, Name, string? Data, long Size, bool Appended, IsExcel, DirectoryExists }
```

### Flat `-1`/`-2` naming — reuse, not a third copy

The convention exists twice with an identical inner loop: [OutputTargets.cs:109-111](../backend/Services/OutputTargets.cs#L109-L111) and `PortablePackage.FlatMembers` ([PortablePackage.cs:337-351](../backend/Services/PortablePackage.cs#L337-L351)). **Extract the numbering loop only**, into a new `Services/FlatFiles.cs`:

```csharp
internal static HashSet<string> NewNameSet();
internal static string NextFreeName(string name, HashSet<string> taken, ref int index);
internal static Dictionary<string, string> NamesFor(IEnumerable<string> paths);   // new endpoint only
```

The three sites differ in their *grouping*, not their numbering: `OutputTargets.Prepare` creates `taken` per collision group (inside the loop at line 82) while `FlatMembers` shares one `taken` across all groups (line 327). Anything extracted above the `do/while` would have to pick one and break the other, so the helper takes `taken` and `index` as parameters and owns nothing. In `OutputTargets` that leaves `taken`, `byResolved`, `index`, the per-group scoping, the reserve-but-never-rename branch and the same-resolved-path sharing exactly where they are — `Prepare`'s doc comment (lines 35-60) still describes the code accurately.

`NamesFor` deliberately does **not** replace `FlatMembers`: it preserves input order and skips the `seen` dedup pass, because the write log already holds one entry per distinct path.

ZIP building stays **client-side** ([`buildZip`](../frontend/calcpad-frontend/src/services/zip-writer.ts#L14)), matching the plots ZIP: the capture response already carries the bytes, so zipping on the server would only duplicate them over the wire. `PortablePackage.Pack` is left alone.

## Frontend

New `writeData(content, settings, sourceFilePath, capture)` on [`CalcpadApiClient`](../frontend/calcpad-frontend/src/api/client.ts#L42), and `DataWriteFile` / `DataWriteResponse` in `types/api.ts`. Handlers on `BaseMessageBridge` mirroring the plots handlers at [base.ts:804-855](../frontend/calcpad-frontend/src/services/message-bridge/base.ts#L804-L855), with a `_cachedWriteOutputs` field beside `_cachedPlots`:

| Message | Does |
| --- | --- |
| `getWriteOutputs` | `writeData(capture: true)`, cache bytes, post names/sizes/`captureTooLarge` to Vue |
| `saveWriteOutput` | `saveExportedFile` from the cache |
| `saveWriteOutputsZip` | `buildZip` then `saveExportedFile` |

All of it must be mirrored in [`calcpadVueUIProvider.ts`](../frontend/vscode-calcpad/src/calcpadVueUIProvider.ts), which has its own message switch (~120-200) rather than using `BaseMessageBridge` — the same duplication its plots handlers already have (lines 508-530).

The Export tab's Write/Append section gains `Refresh`, `Download all (ZIP)` and the output list (name, size, per-file `Save…`), styled like the existing `.plots-list` / `.plot-item` block in [CalcpadExportTab.vue](../frontend/calcpad-frontend/src/vue/components/CalcpadExportTab.vue). When `captureTooLarge`, the list shows sizes with downloads disabled and the too-large message; when a file's `directoryExists` is false, a note that writing it to disk would fail.

## Tests

`Calcpad.Tests/Server/PortablePackageTests.cs:179, 200, 216, 280, 299, 321, 340` covers the full `-1`/`-2` semantics and is the safety net for the `FlatFiles` extraction: if `Prepare`'s per-group `taken` or its reserve-but-never-rename branch shifts, lines 200 and 216 fail.

New coverage, extending `Calcpad.Tests/Data/WriteDirectiveTests.cs`:

-   *CSV* — captured bytes equal the disk bytes byte-for-byte including terminators; `#append` to an existing file captures existing-plus-new and leaves the file untouched; `#append` to a missing file captures just the new content with `Appended` set; `#write` then `#append` to one path yields one entry holding both; `#write` in a `#for` over 5 iterations yields one entry equal to the last; `#append` in a `#for` yields one entry with 5 accumulated blocks.
-   *Excel* — round-trip a captured `#write` through `ExcelData.ReadFromMemory`; `#append` into a second sheet of an existing workbook keeps the first sheet (the case `WriteToMemory`'s `Open`-vs-`Create` branch exists for); two `#write`s to one xlsx in a loop leave one sheet.
-   *Edges* — capture to a non-existent folder succeeds with `DirectoryExists == false` while the same disk write throws `PathNotFound`; exceeding `MaxWriteCaptureSize` sets `WriteCaptureTooLarge`, raises **no** error, retains no bytes, and still reports every path and size.
-   *Backend* — `FlatFiles.NamesFor` gives `out-1.csv`/`out-2.csv` for two paths sharing a basename, bare names for distinct ones, and preserves input order.
