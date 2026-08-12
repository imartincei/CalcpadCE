# Calcpad.Web Security Audit

Findings from a security audit of `Calcpad.Web/backend` (ASP.NET Core server), `Calcpad.Web/frontend/calcpad-desktop` (Tauri v2 shell + sidecar), and `Calcpad.Web/frontend/vscode-calcpad` (VS Code extension). `calcpad-web` and `calcpad-frontend` were not audited in this pass and remain open.

> **Snapshot note (2026-08-12, branch `ui-updates`):** Line numbers are current as of this branch. Findings marked **[verified]** were reproduced directly — bypass executed, archive enumerated, or call chain traced end to end. Findings marked **[reported]** carry file:line evidence from the discovery sweep but were not independently re-derived; spot-check before acting.

## Threat model

The server binds to loopback only and this is enforced twice (see *What holds up* below), so the adversary is not a remote attacker. It is:

1. A **malicious `.cpd`/`.cpdz` worksheet** the user opens — worksheets legitimately carry author HTML/JS, so this is untrusted input by design.
2. **Any other local process**, which reaches the API with no `Origin` header and therefore no CORS check.
3. **Any page served from loopback**, which `IsAllowedOrigin` admits on any port.

Findings are rated against those three, not against internet exposure.

## Critical

-   **VS Code preview webviews execute arbitrary JS from any opened worksheet.** [extension.ts:495-506](../frontend/vscode-calcpad/src/extension.ts#L495-L506) — `sanitizeServerHtml` only escapes stray `<`, which is exactly what its own comment at [:492-494](../frontend/vscode-calcpad/src/extension.ts#L492-L494) says it does; the misnomer is the function name and the call-site comment at [:983](../frontend/vscode-calcpad/src/extension.ts#L983). Running its regex verbatim, 5 of 6 payloads pass through unchanged: `<script>`, `<img onerror>`, `<svg onload>`, `<a href="javascript:">`, `<iframe>`. Only `<h` is modified. Output is assigned to `panel.webview.html` on a panel created with `enableScripts: true` and no `localResourceRoots` ([:1518-1521](../frontend/vscode-calcpad/src/extension.ts#L1518-L1521)); `grep -c "Content-Security-Policy" src/extension.ts` returns **0**. Same shape in the `.cpdz` custom editor ([calcpadCompiledEditorProvider.ts:115-120, :144](../frontend/vscode-calcpad/src/calcpadCompiledEditorProvider.ts#L115-L120)). **[verified]**

    No user action is needed beyond opening the file: `autoInputMode` defaults to `true` ([extension.ts:208](../frontend/vscode-calcpad/src/extension.ts#L208)), so a `.cpd` containing `#UI` auto-opens the form webview. A `.cpdz` needs one click — it is bound at `"priority": "default"` in [package.json:31-42](../frontend/vscode-calcpad/package.json#L31-L42).

    Impact is confined to the webview — full network egress and exfiltration of everything rendered (including server-side `#include`d files), plus in-editor phishing. It does **not** reach the extension host: message handlers are a closed switch, `enableCommandUris` is never set, and the backend rejects `vscode-webview://` origins.

    Note the tension before fixing: author HTML/JS in worksheets is a Calcpad *feature* (`Examples/Engineering/Fractals/Koch Snowflake Animated.cpd` ships a jQuery `<script>` tag). Stripping scripts breaks the product. The fix is containment, not filtering.

## High

-   **Arbitrary executable launch via the PDF endpoint (backend).** `browserPath` is accepted on the request body ([PdfGenerateRequest.cs:8](../backend/Models/Pdf/PdfGenerateRequest.cs#L8)), passed through [CalcpadController.cs:289](../backend/Controllers/CalcpadController.cs#L289), returned unvalidated as the first-choice path at [PdfGeneratorService.cs:316](../backend/Services/PdfGeneratorService.cs#L316), and reaches `Puppeteer.LaunchAsync(ExecutablePath = …)` at [:280-285](../backend/Services/PdfGeneratorService.cs#L280-L285). The process spawns before Puppeteer determines it is not Chromium, so the payload runs regardless of launch success. UNC paths work on Windows. **[verified]**

    **No client anywhere in the repo sets this field** — only `API_SCHEMA.md` mentions it; the `appsettings`/`BROWSER_PATH` resolution below it is what is actually used. Deleting the property is a zero-breakage fix. Predates this branch (commit `b62416b`).

-   **Tauri webview holds whole-disk read.** [capabilities/default.json:65, :84, :103](../frontend/calcpad-desktop/src-tauri/capabilities/default.json#L65) — `{ "path": "**" }` on `fs:allow-read-text-file`, `fs:allow-read-file`, `fs:allow-read-dir`. The four scoped entries beside each (`$APPDATA/**`, `$APPCONFIG/**`, `$APPLOG/**`, `$HOME/**`) are fully subsumed, and `fs:deny-default` is never included, so nothing is denied — including the WebView2 data folder that `fs:deny-default` normally protects. The likely motivation is the `/etc/os-release` read at [tauri-bridge.ts:938](../frontend/calcpad-web/src/services/tauri-bridge.ts#L938), which sits outside every named base directory. **[verified]**

-   **Tauri CSP cannot stop an injection.** [tauri.conf.json:23](../frontend/calcpad-desktop/src-tauri/tauri.conf.json#L23) — `script-src 'self' 'unsafe-inline' 'unsafe-eval' https:` and `connect-src 'self' ipc: http://127.0.0.1:* https:`. `dangerousDisableAssetCspModification: ["script-src", "style-src"]` at [:24](../frontend/calcpad-desktop/src-tauri/tauri.conf.json#L24) is what keeps `'unsafe-inline'` effective — Tauri's nonce injection would otherwise neutralise it per the CSP spec. Bare `https:` makes any HTTPS host both a script origin and an exfiltration sink. **[verified]**

    No live injection sink exists today (Vue escapes by default; `innerHTML` appears only with constant strings). This is the amplifier: it is what would turn any future injection into whole-disk read plus exfiltration with no CSP step in between.

-   **Published VSIX archives ship developer server logs.** All three built archives contain them:

    | Archive | Contents |
    | --- | --- |
    | `vscode-calcpad-0.3.0.vsix` | 2 log files, `.env.example`, `bin/appsettings.Development.json` |
    | `vscode-calcpad-0.4.0.vsix` | 4 log files, same |
    | `vscode-calcpad-0.4.2.vsix` | 4 log files, same |

    Logs leak worksheet source under `Content preview:` and local source-tree paths. `bin/` **is** gitignored ([.gitignore:32](../../.gitignore#L32)) so these are not committed — but `.vscodeignore` re-includes it with `!bin/**` and has no `bin/logs/**` exclusion, so every package build sweeps them in. **[verified]**

-   **Downloaded runtimes and DLLs are executed without integrity verification.** [dotnetRuntimeManager.ts:141-173](../frontend/vscode-calcpad/src/dotnetRuntimeManager.ts#L141-L173) downloads, extracts, and later executes the .NET runtime with no checksum and no signature check; Microsoft's release metadata carries a `hash` field the schema at [:263-274](../frontend/vscode-calcpad/src/dotnetRuntimeManager.ts#L263-L274) never reads. The URL is taken from fetched JSON with no host allowlist ([:297, :303](../frontend/vscode-calcpad/src/dotnetRuntimeManager.ts#L297)). Same pattern for `.nupkg` fetches at [calcpadServerManager.ts:229-242](../frontend/vscode-calcpad/src/calcpadServerManager.ts#L229-L242) and the native `libSkiaSharp` at [:352-368](../frontend/vscode-calcpad/src/calcpadServerManager.ts#L352-L368), which is `chmod 0o755` on POSIX. Versions are pinned; content is not. **[reported]**

## Medium

-   **`.cpdz` decompression bomb (backend).** [CpdzCodec.cs:84-93](../backend/Services/CpdzCodec.cs#L84-L93) inflates into an unbounded `MemoryStream`, reachable from `/api/calcpad/cpdz/decode` with attacker-chosen base64. No `MaxRequestBodySize` is configured anywhere, so Kestrel's 30 MB default applies — at deflate ratios that is tens of GB into memory. New on this branch. **[verified]**

-   **Second, stronger arbitrary-file-read primitive (backend).** The `#include` reader at [CalcpadService.cs:29](../backend/Services/CalcpadService.cs#L29) is unconfined by design and documented as such. `/api/calcpad/portable/package` is now a second one: [PortablePackage.cs:463-478](../backend/Services/PortablePackage.cs#L463-L478) honours absolute paths, `..`, and expands environment variables, then [:140](../backend/Services/PortablePackage.cs#L140) `File.ReadAllBytes` returns **raw bytes** in the ZIP — binary files, not just text rendered into HTML. New on this branch. Needs a decision on whether this stays inside the accepted threat model. **[verified]**

-   **`shell:allow-open` reopens the scope `opener` closes.** [capabilities/default.json:20](../frontend/calcpad-desktop/src-tauri/capabilities/default.json#L20) grants it while `opener:allow-open-url` at [:32](../frontend/calcpad-desktop/src-tauri/capabilities/default.json#L32) is scoped to a single URL prefix. With no `plugins.shell` config the default validator permits any `http(s)://`, `mailto:`, `tel:`. No JS imports the shell plugin — an unused grant, so removal is free. **[verified — grant present; unused-ness reported]**

-   **`open_path_native` bypasses the opener scope on Linux.** [lib.rs:372-404](../frontend/calcpad-desktop/src-tauri/src/lib.rs#L372-L404) shells to `xdg-open` with no scope validation, called unconditionally on Linux from [tauri-bridge.ts:693-699](../frontend/calcpad-web/src/services/tauri-bridge.ts#L693-L699). The Windows/macOS branch goes through `opener::open_path`, which does enforce scope. Not command injection — `Command::new` + `cmd.arg` passes through `execve` — but the allow-list at [capabilities/default.json:21-30](../frontend/calcpad-desktop/src-tauri/capabilities/default.json#L21-L30) is simply not applied on the primary target platform. **[reported]**

-   **`$HOME` write plus `$HOME` open-path is a write-then-execute chain.** `fs:allow-write-file` → `$HOME/**` combined with `opener:allow-open-path` → `$HOME/**` lets webview script write `x.hta` (or `.js`, `.bat`, `.cmd`, `.scr`) and then hand it to `ShellExecute`. `$RESOURCE/**` in the open-path list separately allows launching the sidecar directly, outside `spawn_sidecar`'s managed lifecycle. **[reported]**

-   **Local server is unauthenticated.** [baseServerManager.ts:195, :289-290](../frontend/vscode-calcpad/src/baseServerManager.ts#L195) starts it with no token; the port is published in plaintext at `bin/.calcpad-server.lock`, and on the Tauri side in `std::env::temp_dir()` ([lib.rs:471-478](../frontend/calcpad-desktop/src-tauri/src/lib.rs#L471-L478)), mode 1777 on Linux. `CALCPAD_DETACHED=1` plus `detached`/`unref` makes it outlive VS Code. Any local process gets arbitrary-file-read-as-the-user via `#include`. The backend documents this exact risk at [CalcpadApiService.cs:63-70](../backend/Services/CalcpadApiService.cs#L63-L70). **[reported]**

-   **`#include` resolution has no workspace containment.** [calcpadLocationResolver.ts:59-68](../frontend/vscode-calcpad/src/calcpadLocationResolver.ts#L59-L68) resolves relative paths, then falls through to an explicit absolute-path branch, with no check that the result stays under the workspace root. `expandEnvVars` at [:9-14](../frontend/vscode-calcpad/src/calcpadLocationResolver.ts#L9-L14) expands `%VAR%`/`$VAR` against the extension host's environment first, so `#include %USERPROFILE%\.aws\credentials` resolves. This is the file-read half of the Critical finding's exfiltration chain. **[reported]**

-   **Preview `srcdoc` frames inherit the host CSP (desktop).** Frames at [App.vue:415-423, :467-475](../frontend/calcpad-web/src/App.vue#L415-L423) are correctly `sandbox="allow-scripts"` without `allow-same-origin`, but `srcdoc` documents inherit the parent's CSP — so worksheet script gets `connect-src … https:` and can exfiltrate the rendered calculation. The opaque origin correctly blocks IPC and the API rejects `Origin: null`; only network egress is open. Fixing the host CSP fixes this as a side effect. **[reported]**

-   **Zip extraction without traversal protection.** [dotnetRuntimeManager.ts:167-170](../frontend/vscode-calcpad/src/dotnetRuntimeManager.ts#L167-L170) uses PowerShell `Expand-Archive`, which does not reliably reject `../` entries. Only exploitable in combination with the missing download verification above. The sibling paths already use `ZipFile::ExtractToDirectory` and `unzip`, both of which do validate. **[reported]**

## Low

-   `get_env` ([lib.rs:351-354](../frontend/calcpad-desktop/src-tauri/src/lib.rs#L351-L354)) returns any environment variable to the webview and is reachable from document-derived strings via `expandEnvVars`; a worksheet containing `$AWS_SECRET_ACCESS_KEY` in an include path leaks it into an error message. **[reported]**
-   Updater config is a half-state: `"active": false` at [tauri.conf.json:75-81](../frontend/calcpad-desktop/src-tauri/tauri.conf.json#L75-L81) is a **Tauri v1** key and is silently discarded in v2. Inert today (`endpoints: []`), but the plugin is registered and `updater:default` is granted, so a maintainer adding endpoints later inherits an exposed `download_and_install`. **[reported]**
-   `loadPreset` joins an unvalidated name ([calcpadSettings.ts:263-266](../frontend/vscode-calcpad/src/calcpadSettings.ts#L263-L266)); `savePreset` validates but the `switchConfig` webview message path does not. **[reported]**
-   Lock-file PID is type-checked but not range-checked ([baseServerManager.ts:437-445](../frontend/vscode-calcpad/src/baseServerManager.ts#L437-L445)); a poisoned `{"pid": -1}` reaches `process.kill(-1, 'SIGTERM')` on POSIX. **[reported]**
-   Child server inherits full `process.env` with no `ASPNETCORE_ENVIRONMENT` pin, and `appsettings.Development.json` ships in the VSIX — a developer with that variable exported gets Swagger and the `debug-crash` endpoint enabled. **[reported]**
-   Preview panels leave `localResourceRoots` at default (every workspace folder). They load no local resources — images are inlined as data URIs — so `localResourceRoots: []` is free. **[reported]**
-   CSP nonce in the Vue panel uses `Math.random()`, not a CSPRNG ([calcpadVueUIProvider.ts:753-760](../frontend/vscode-calcpad/src/calcpadVueUIProvider.ts#L753-L760)). **[reported]**
-   `--no-sandbox` on the PDF Chromium ([PdfGeneratorService.cs:284](../backend/Services/PdfGeneratorService.cs#L284)) runs attacker-controlled HTML unsandboxed. Acceptable for a loopback desktop tool; a blocker if server mode is ever finished. **[verified]**
-   No rate limiting anywhere. Fine locally, not fine in server mode. **[verified]**

## Non-findings

Recorded so they are not re-investigated:

-   `dotnet list package --vulnerable --include-transitive` is clean; `Microsoft.OpenApi` is pinned against GHSA-v5pm-xwqc-g5wc.
-   No hardcoded secrets across `.cs`/`.ts`/`.json`/`.rs`/`.sh`. The one thumbprint in `.env.example` is a commented-out code-signing cert SHA1 — a public identifier.
-   No `shell: true`, `eval`, or `new Function` in any extension-host code. All spawns use argv arrays.
-   `withGlobalTauri` and `dangerousRemoteDomainIpcAccess` are absent from all three Tauri configs. Core ACL grants are read-only; no `shell:allow-execute`/`allow-spawn`.
-   Both `unsafe` blocks in `lib.rs` (Win32 job-object FFI) are correct — null-handle checks, correct `size_of`, deliberate handle leak required by `KILL_ON_JOB_CLOSE`.
-   Cargo advisories `glib 0.18.5` (RUSTSEC-2024-0429) and `proc-macro-error` (unmaintained) are transitive through the Tauri v2 Linux stack and not fixable from this manifest. `ring 0.17.14` is **not** affected by RUSTSEC-2025-0009.
-   `capabilities.untrustedWorkspaces` is absent from the extension manifest. VS Code's default for an undeclared extension is *disabled in Restricted Mode* — fail-safe, but implicit. Worth declaring explicitly so it survives future default changes.

## Remediation plan

Five passes, ordered by risk reduction per unit of work. Each is independently landable.

### Pass 1 — Contain webview script execution (Critical)

1. Emit a `<meta http-equiv="Content-Security-Policy">` with a per-render nonce on all three preview HTML paths (preview panel, report panel, `.cpdz` custom editor), and nonce the extension's own injected scripts. [calcpadVueUIProvider.ts:722-747](../frontend/vscode-calcpad/src/calcpadVueUIProvider.ts#L722-L747) already does this correctly and is the in-repo model.
2. Because author scripts are a product feature, render the document body in a sandboxed `<iframe srcdoc>` with an opaque origin rather than at webview top level — mirroring what [App.vue:415-423](../frontend/calcpad-web/src/App.vue#L415-L423) already does on desktop.
3. Set `localResourceRoots: []` on both preview panels and the custom editor.
4. Rename `sanitizeServerHtml` to something honest (`repairStrayAngleBrackets`) and fix the call-site comment, so it is never again mistaken for a security control.

### Pass 2 — One-line, zero-breakage config fixes

1. Delete `BrowserPath` from `PdfGenerateRequest` and drop the parameter from `GeneratePdfAsync`/`GenerateBasicPdfAsync`/`GetOrCreateBrowserAsync`/`ResolveBrowserPathAsync`. Keep the `appsettings`/`BROWSER_PATH` resolution. Update `API_SCHEMA.md`.
2. Delete `{"path": "**"}` from the three `fs` read scopes; add `fs:deny-default`. Replace the `/etc/os-release` read with a narrow `#[tauri::command]` or scope it to `{"path": "/etc/*-release"}`.
3. Remove `dangerousDisableAssetCspModification`; drop `'unsafe-inline'`/`'unsafe-eval'` from `script-src` and bare `https:` from `script-src`/`connect-src`. If Monaco genuinely needs `'unsafe-eval'`, scope it to `worker-src`.
4. Delete `shell:allow-open` from the capability, plus `tauri_plugin_shell::init()` and the `Cargo.toml` dependency if nothing else uses it.
5. Add `bin/logs/**`, `.env.example`, and `bin/appsettings.Development.json` to `.vscodeignore` after the `!bin/**` line. Delete the local `bin/logs/`. **Check whether 0.3.0 / 0.4.0 / 0.4.2 were published to the Marketplace and republish if so.**

### Pass 3 — Supply chain

1. Read the `hash` field from the .NET release entry and verify SHA512 before writing the archive; assert `new URL(file.url).host` against an allowlist (`builds.dotnet.microsoft.com`, `download.visualstudio.microsoft.com`).
2. Verify SHA512 per `.nupkg`, or move to the NuGet v3 flat-container endpoint with published hashes, or ship the DLLs in the VSIX and drop the runtime download entirely.
3. Switch the Windows runtime extraction to `ZipFile::ExtractToDirectory` (already used elsewhere in the same file) so entry paths are validated.

### Pass 4 — Backend hardening

1. Cap `.cpdz` inflation with a counting stream that aborts past a fixed ceiling; set an explicit `MaxRequestBodySize` on the decode endpoint.
2. Decide whether `/portable/package`'s raw-bytes read stays in the accepted threat model. If not, confine resolution to the source file's directory tree plus the declared `#ProjectPath`/`#LibraryPath` roots.
3. Apply the same containment to [calcpadLocationResolver.ts:59-68](../frontend/vscode-calcpad/src/calcpadLocationResolver.ts#L59-L68), and stop expanding environment variables from untrusted document text.

### Pass 5 — Authentication and residual hardening

1. Generate a random per-launch token, pass it to the sidecar via environment (**not** argv — argv is world-readable in `/proc`), require it as a header on every `/api/**` route, and hand it to the webview via `server_url` or a dedicated command. Create the port file mode `0600`.
2. Validate `path` inside `open_path_native` against the opener allow-list; reject leading `-`; pass `--` before the path.
3. Narrow `opener:allow-open-path` to `$APPDATA/**` and `$APPLOG/**`; drop `$RESOURCE/**`; prefer `reveal_item_in_dir` over `open_path` for user documents.
4. Restrict `get_env` to an allow-list (`HOME`, `USERPROFILE`, `APPDATA`, `XDG_*`, `CALCPAD_*`).
5. Resolve the updater half-state — remove the plugin, capability, and dependency, or configure a real `pubkey` and HTTPS endpoints.
6. Clear the Low table: preset-name validation, PID range check, `ASPNETCORE_ENVIRONMENT` pin, CSPRNG nonce, explicit `untrustedWorkspaces` declaration.

## What holds up

Worth preserving through any refactor — several of these are why the Critical finding stays contained.

-   **Loopback is enforced twice, on intent and on outcome.** A pre-bind check on the intended URL ([Program.cs:192-199](../backend/Program.cs#L192-L199)) and a post-bind re-check against `app.Urls` ground truth ([Program.cs:236-249](../backend/Program.cs#L236-L249)) that specifically catches a `Kestrel:Endpoints` override slipping past `UseUrls`. Both hard-fail.
-   **The threat model is written down.** [CalcpadApiService.cs:58-113](../backend/Services/CalcpadApiService.cs#L58-L113) names the exact attack, states plainly that the random port is not a defense and why, explains that `[FromBody]` + `[ApiController]` forces a preflight, and rejects `"null"` origin because that is what the app's own sandboxed frames send. The `Host`-header check at [:146-159](../backend/Services/CalcpadApiService.cs#L146-L159) closes DNS rebinding, which is the piece most projects miss.
-   **The desktop untrusted-content boundary is correct.** `sandbox="allow-scripts"` deliberately without `allow-same-origin`, with [App.vue:379-382](../frontend/calcpad-web/src/App.vue#L379-L382) explaining why adding it would hand worksheet script the Tauri IPC. The postMessage handler validates by window identity rather than origin — correct, since every opaque frame reports `origin === "null"`.
-   **Draft path handling.** `validate_draft_id` rejects anything outside `[A-Za-z0-9_-]`, and is re-applied on the *listing* path to filter ids that reached disk some other way.
-   **Extension settings live in `globalStorage`, not workspace configuration** ([calcpadSettings.ts:258-271](../frontend/vscode-calcpad/src/calcpadSettings.ts#L258-L271)), so a malicious repo's `.vscode/settings.json` cannot set `dotnetPath` or the server URL. This closes the single most common settings-to-RCE path in VS Code extensions.
-   **No `shell: true` anywhere; all spawns use argv arrays.** Windows zip extraction passes paths via environment variables specifically to dodge shell quoting, with a comment saying so.
-   **`debug-crash` is gated on `IsDevelopment()`** ([CalcpadController.cs:210-216](../backend/Controllers/CalcpadController.cs#L210-L216)), with a comment noting that a preflight-free GET would otherwise let any visited page kill the local server.
-   **Sidecar lifecycle is leak-free** — Windows job object with `KILL_ON_JOB_CLOSE`, `--parent-pid` polling, stdin-EOF watchdog, and a generation counter that prevents a dying old sidecar from clobbering a newly spawned one.
