# Splitting `core-updates-for-web` — remaining PRs 4 & 5

This file tracks the remaining work to finish splitting the original
`core-updates-for-web` branch into per-feature PRs against `main`.

## Status

| PR | Branch | Scope | State |
|----|--------|-------|-------|
| 1 | `fix/unit-electrical-kv-to-v` | Unit.cs electrical-units dictionary refactor (kV→V) | ✅ committed + pushed |
| 2 | `feat/validator-combining-marks` | Validator.cs combining diacritics in identifiers | ✅ committed |
| 3 | `feat/macro-circular-reference-detection` | Macro recursion guard + tests | ✅ committed |
| 4 | `feat/include-recursion-protection` | Circular `#include` detection + include-path resolution refactor | ⬜ TODO |
| 5 | `feat/web-include-file-cache` | `ClientFileCache` + cache lookups + csproj cleanup | ⬜ TODO |

All branches cut from `main`. The source of all changes is the `core-updates-for-web` branch.

## Important: PR 4 and PR 5 are NOT independent

Both touch the `#include` block in `Calcpad.Core/Parsers/MacroParser.cs`.

- **PR 4** rewrites the include block: source-relative path resolution
  (`SourceFilePath`, `Path.GetFullPath(expanded, sourceDir)`, the
  `ParseWithSourcePath` helper) **and** the circular-include guard
  (`_includeStack`, `PathComparer`, `Circular_include_detected_0`).
  In PR 4 the not-found path simply emits `Messages.File_not_found`.
- **PR 5** is purely additive on top of PR 4: it inserts the two
  `ClientFileCache` lookups into the not-found fallback branch and adds the
  cache classes + csproj cleanup.

**Therefore PR 5 depends on PR 4.** Merge order: 4 then 5. Build branch 5 only
after branch 4 is committed (or branch 5 directly off branch 4 and rebase onto
`main` after 4 merges).

---

## PR 4 — `feat/include-recursion-protection`

### Files & what to take from `core-updates-for-web`
- `Calcpad.Core/Parsers/MacroParser.cs` — the `#include` block rewrite +
  the new fields (`PathComparer`, `_includeStack`), the `_includeStack.Clear()`
  in the reset block, and the `ParseWithSourcePath` local helper.
  **Exclude** anything `ClientFileCache`-related (that is PR 5):
  - Do NOT add the `public ClientFileCache ClientFileCache { get; set; }` property.
  - Do NOT add the `TryGetContentMultiKey` / `TryGetErrorMultiKey` lookup branches.
  - The not-found tail in PR 4 ends with `AppendError(lineContent, Messages.File_not_found);`
- `Calcpad.Core/Exceptions.cs` — nothing needed here (the circular-include
  message is referenced inline via `string.Format(Messages.Circular_include_detected_0, ...)`,
  not through an Exceptions helper). Confirm before adding anything.
- `Calcpad.Core/Messages.resx` — add only `Circular_include_detected_0`
  ("Circular #include detected: \"{0}\".").
- `Calcpad.Core/Messages.Designer.cs` — add only the
  `Circular_include_detected_0` property.

### How to build the MacroParser include block for PR 4 (cache-free version)
Take the full include block from `core-updates-for-web` and delete the two
`ClientFileCache != null && ...` branches, leaving:

```csharp
var includeKey = resolvedPath ?? rawFileName;
if (!_includeStack.Add(includeKey))
{
    AppendError(lineContent, string.Format(Messages.Circular_include_detected_0, rawFileName));
    return;
}
try
{
    if (fileExists)
    {
        ParseWithSourcePath(Include(resolvedPath, fields), resolvedPath);
        return;
    }
    AppendError(lineContent, Messages.File_not_found);   // PR 5 inserts cache lookups above this line
}
finally
{
    _includeStack.Remove(includeKey);
}
```

Also include from the field/reset hunks (top of class):
```csharp
private static readonly StringComparer PathComparer =
    OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
private readonly HashSet<string> _includeStack = new(PathComparer);
public string SourceFilePath { get; set; }
```
and `_includeStack.Clear();` in the parse-reset block, and the
`ParseWithSourcePath` helper.

### Suggested commit message
```
Detect circular #include and resolve includes relative to source file

Resolve #include paths against the including file's directory and track an
include stack (OS-aware path comparison) so a file that includes itself —
directly or transitively — reports a clear "Circular #include detected"
error instead of recursing until the stack overflows.
```

### Open decision (from planning)
Relative-path resolution is bundled into PR 4 because the circular-include
key needs a canonicalized path. If a cleaner separation is wanted, the
`SourceFilePath` / source-dir resolution could be pulled into its own
`feat/include-relative-path-resolution` PR ahead of PR 4.

---

## PR 5 — `feat/web-include-file-cache` (depends on PR 4)

### Files & what to take from `core-updates-for-web`
- `Calcpad.Core/ClientFileCache.cs` — new file, take whole.
- `Calcpad.Core/ClientFileDiskCache.cs` — new file, take whole.
- `Calcpad.Core/Parsers/MacroParser.cs` — add ONLY:
  - `public ClientFileCache ClientFileCache { get; set; }` property.
  - the two cache-lookup branches inserted before the final
    `AppendError(lineContent, Messages.File_not_found);`:
    ```csharp
    var cacheKey = resolvedPath ?? rawFileName;
    var fallbackKey = resolvedPath != null ? rawFileName : null;
    if (ClientFileCache != null && ClientFileCache.TryGetContentMultiKey(cacheKey, fallbackKey, out var cachedContent))
    {
        ParseWithSourcePath(cachedContent, resolvedPath);
        return;
    }
    if (ClientFileCache != null && ClientFileCache.TryGetErrorMultiKey(cacheKey, fallbackKey, out var cachedError))
    {
        AppendError(lineContent, cachedError);
        return;
    }
    ```
- `Calcpad.Core/Calcpad.Core.csproj` — remove the two WPF `OutputPath`
  `PropertyGroup` blocks (Release|AnyCPU and Debug|AnyCPU). This is the
  build-enablement / CLI-crash fix, bundled here per earlier decision.

### Suggested commit message
```
Add ClientFileCache for #include resolution and drop WPF output paths

Resolve #include targets that are not on disk through an in-memory/disk
ClientFileCache (used by Calcpad.Web), falling back to a cached error when
present. Remove the WPF-specific OutputPath overrides from Calcpad.Core.csproj
so the Core library no longer emits into the WPF output tree (fixes the CLI crash).
```

---

## Verification for PRs 4 & 5
- `dotnet build Calcpad.Core/Calcpad.Core.csproj`
- `dotnet test Calcpad.Tests/Calcpad.Tests.csproj` (full suite)
- After PR 4: confirm the cache-free include path still resolves on-disk
  includes and reports circular includes.
- After PR 5: confirm cached includes resolve when the file is absent on disk.

## Cross-check against the source branch
At any point, confirm the union of PRs 1–5 equals the original branch:
```
git diff main..core-updates-for-web --stat
```
Every changed file/region in that diff should land in exactly one PR.

## Note
This doc is an untracked working file. Commit it wherever you track planning
docs, or delete it once PRs 4 & 5 are merged. Keep it OUT of the feature
commits (use `git commit` with the already-staged paths, not `git add -A`).
