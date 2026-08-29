# Calcpad.Web Code Quality Investigation

Findings from an investigation of `Calcpad.Web/frontend` (monorepo: `calcpad-frontend`, `calcpad-web`, `calcpad-desktop`, `vscode-calcpad`) and `Calcpad.Web/backend` (ASP.NET Core server). Line numbers are as reported by the discovery sweep and should be spot-checked before acting.

> **Snapshot note (2026-07-08):** This document predates two structural changes. (1) The desktop wrapper switched from Neutralino to Tauri — the old `neutralino-bridge.ts` is now [`tauri-bridge.ts`](../frontend/calcpad-web/src/services/tauri-bridge.ts) and `NeutralinoMessageBridge` is now `TauriMessageBridge`. (2) Pass 2 (bridge unification) was partially implemented: [`calcpad-frontend/src/services/message-bridge/base.ts`](../frontend/calcpad-frontend/src/services/message-bridge/base.ts) exists as `BaseMessageBridge` and both web/desktop bridges extend it. Verify current line numbers before acting on any specific finding below.

## Frontend findings

### High severity

-   **Bridge duplication.** [message-bridge.ts](../frontend/calcpad-web/src/services/message-bridge.ts) and [tauri-bridge.ts](../frontend/calcpad-web/src/services/tauri-bridge.ts) contain two parallel `MessageBridge` classes with ~40 near-identical handlers (`handleGetSettings`, `handleUpdateSettings`, `handleGetVariables`, `handleGeneratePdf`, `handleSaveSourceHtml`, …). Only the storage/IPC backend differs.
-   **Sync polling loop.** [server-manager.ts:370](../frontend/calcpad-web/src/services/server-manager.ts#L370) — `while (Date.now() < deadline)` blocks the main thread during health checks.
-   **Scattered PDF-settings defaults.** Two identical 12-field default objects at [message-bridge.ts:277-296](../frontend/calcpad-web/src/services/message-bridge.ts#L277-L296) and [tauri-bridge.ts:768-787](../frontend/calcpad-web/src/services/tauri-bridge.ts#L768-L787).

### Medium severity

-   **VS Code settings wrapper reimplements shared logic.** [calcpadSettings.ts:19-117](../frontend/vscode-calcpad/src/calcpadSettings.ts#L19-L117) re-wraps helpers already in [settings.ts:50-169](../frontend/calcpad-frontend/src/types/settings.ts#L50-L169).
-   **Image-insert helpers duplicated.** [image-insert.ts:32-54](../frontend/calcpad-web/src/services/image-insert.ts#L32-L54) vs [imageInserter.ts](../frontend/vscode-calcpad/src/imageInserter.ts) vs inline MIME map in the Tauri bridge.
-   **Regex state hazard.** Global regex reused without resetting `lastIndex` at [tauri-bridge.ts:83](../frontend/calcpad-web/src/services/tauri-bridge.ts#L83).
-   **`structuredClone` on hot path** in [settings.ts:51,64,100](../frontend/calcpad-frontend/src/types/settings.ts#L51) — called on every load/save rather than only on mutation.
-   **`server-manager.ts` shipped to all packages** from `calcpad-frontend` but only used by the VS Code extension; bloats web/desktop bundles.

### Low severity

-   Auto-indent dedent-keyword logic re-implemented in `vscode-calcpad/src/autoIndenter.ts` and `calcpad-web/src/editor/auto-indent.ts` instead of calling the shared util.
-   `Color`/`LightDirection` enums in `types/settings.ts` not re-exported from `index.ts`.

## Backend findings

### Medium severity

-   ~~**Shadowed endpoints.** `/convert` and `/convert-unwrapped` differ only by one flag; merge with a query param.~~ **[done]** — merged behind `POST /convert?unwrap=true`.
-   **Fake async.** [CalcpadController.cs:41,62,207](../backend/Controllers/CalcpadController.cs#L41) — `ConvertAsync` wraps sync work in `Task.FromResult`.
-   **Duplicated response mapping** between `GetHighlightTokens` and `GetHighlightTokensForLine` at [CalcpadController.cs:233-315](../backend/Controllers/CalcpadController.cs#L233-L315).
-   **Duplicated draw setup** between `DrawHeader`/`DrawFooter` at [PdfGeneratorService.cs:446-545](../backend/Services/PdfGeneratorService.cs#L446-L545).
-   **`ConvertAsync` mutates caller-supplied `openXmlExpressions` list** ([CalcpadService.cs:50-51](../backend/Services/CalcpadService.cs#L50)) — should return a tuple.
-   Missing `ConfigureAwait(false)` on library-side awaits in [PdfGeneratorService.cs](../backend/Services/PdfGeneratorService.cs).
-   **Duplicated collapse-loop** in [NoPrintRegionStripper.cs:70-81](../backend/Services/NoPrintRegionStripper.cs#L70-L81).

### Low severity / dead code

-   `_tempDirectory` field assigned but unused ([CalcpadService.cs:8,15](../backend/Services/CalcpadService.cs#L8)).
-   `TryDeleteFile()` never invoked ([CalcpadService.cs:479-490](../backend/Services/CalcpadService.cs#L479-L490)).
-   `Recurse()` only referenced by debug-crash endpoint ([CalcpadController.cs:156](../backend/Controllers/CalcpadController.cs#L156)).
-   `await Task.Delay(-1, cts.Token)` shutdown wait in [Program.cs:260](../backend/Program.cs#L260).

## Remediation plan

Five focused passes; each independently landable.

### Pass 1 — Backend correctness

1. Introduce a singleton / `IHttpClientFactory`-backed `HttpClient` in `Router` and `CalcpadApiService`.
2. Make `CreateIncludeDelegate` return an async include callback (or resolve includes async at parse time) so we can delete the `.GetAwaiter().GetResult()` in `CalcpadService`.
3. Convert the fake `ConvertAsync` to real async or rename to `Convert` — pick one and stop lying about it.

### Pass 2 — Frontend bridge unification

1. Extract an abstract `BaseMessageBridge` into `calcpad-frontend/src/services/` holding all 40 handlers, parameterized over small `Storage` + `Fs` interfaces.
2. `MessageBridge` (web) and `TauriMessageBridge` (desktop) become ~50-line adapters implementing those interfaces.
3. Move the PDF-settings default object to `calcpad-frontend/src/defaults/pdf-settings.ts` and import from both sites.

### Pass 3 — Frontend polish

1. Replace the `Date.now()` polling loop in `server-manager.ts` with an async `waitFor(predicate, {intervalMs, timeoutMs})` helper.
2. Cache the `getDefaultSettings()` return value; only `structuredClone` on write.
3. Reset regex `lastIndex` before each use in `tauri-bridge.ts` (or construct the regex per-call — it's not hot).
4. Move `server-manager.ts` out of `calcpad-frontend` into `vscode-calcpad` (its only consumer) so web/desktop bundles shrink.
5. Consolidate image-insert helpers into a shared `image-utils.ts`.
6. Delete the VS Code `calcpadSettings.ts` wrapper; call the shared functions directly.

### Pass 4 — Backend refactors

1. ~~Merge `/convert` + `/convert-unwrapped` behind an `unwrap=true` query parameter.~~ **Done** — `/convert-unwrapped` is gone.
2. Extract `MapTokensToResponse(...)` for the two highlight endpoints.
3. Extract `SetupHeaderFooterContext(...)` for PDF draw code.
4. Add `ConfigureAwait(false)` to the PDF service awaits.
5. Change `ConvertAsync` signature to return `(string html, IReadOnlyList<string> openXmlExpressions)`.

### Pass 5 — Dead-code sweep

1. Delete `_tempDirectory`, `TryDeleteFile`, and (subject to review) the debug `Recurse` / `DebugCrash` pair — or gate them behind `#if DEBUG`.
2. Replace `Task.Delay(-1, cts.Token)` with `await tcs.Task`.
