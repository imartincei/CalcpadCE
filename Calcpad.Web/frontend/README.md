# CalcPad Web Frontend

> **Localhost-only build.** This branch (`calcpad-web`) only supports running Calcpad.Server bound to a loopback address. The backend's startup loopback guard in [Program.cs](../backend/Program.cs) throws `InvalidOperationException` if the resolved bind URL is anything other than `localhost`, `127.0.0.0/8`, or `::1`. Multi-user / hosted / Docker deployment, auth, and shared file storage live on the `calcpad-experimental` branch.

A monorepo containing all frontend projects for CalcPad: a VS Code extension, a standalone web editor, a desktop application, and a shared library that powers them all.

This file covers building and packaging. What the editors *do* is documented for users in [docs/](../../docs/) — the [editor](../../docs/new-calcpadce-editor.md), the [VS Code extension](../../docs/new-vscode-extension.md), the [desktop app](../../docs/new-desktop-app.md), and [running the server](../../docs/new-web-deployment.md). The REST API is documented in [../backend/API_SCHEMA.md](../backend/API_SCHEMA.md).

## Projects

| Directory                              | Description                                                      |
| -------------------------------------- | ---------------------------------------------------------------- |
| [calcpad-frontend/](calcpad-frontend/) | Shared TypeScript library (API client, services, Vue components) |
| [vscode-calcpad/](vscode-calcpad/)     | VS Code extension with live preview and full language support    |
| [calcpad-web/](calcpad-web/)           | Standalone web editor built with Monaco Editor + Vue             |
| [calcpad-desktop/](calcpad-desktop/)   | Desktop application via Tauri wrapping the web editor            |

All projects depend on **Calcpad.Server** (the .NET backend at [../backend/](../backend/)) for computation, linting, and rendering via REST API. The extension and the desktop app spawn and manage their own copy; the standalone web editor needs one running already — see [../backend/README.md](../backend/README.md).

---

## Quick Start

The shared library builds first; everything else consumes it as a local dependency.

```bash
cd calcpad-frontend && npm install && npm run build
```

**VS Code Extension** — builds the extension host and the Vue webview, then install the generated `.vsix`:
```bash
cd vscode-calcpad && npm install && npm run package
```

Use the **Extension: Run** launch configuration in VS Code to debug the extension.

**Web Editor:**

Set `VITE_SERVER_URL` to the standalone server's URL before starting the web editor.

```bash
cd calcpad-web && npm install && npm run dev
```

The editor takes its server URL from a `?server=` query parameter, else `VITE_SERVER_URL`, else the page's own origin.

**Desktop App:**
```bash
cd calcpad-desktop && npm install && npm run dev
```

`npm run dev` stages the server and starts Tauri on the current platform.

---

## Architecture

```
calcpad-frontend/          Shared library (TypeScript + Vue components)
    ├── src/api/           HTTP client for Calcpad.Server REST API
    ├── src/services/      Linting, definitions, highlighting, snippets, file cache
    ├── src/text/          Operator replacement, quick typing, auto-indent
    ├── src/types/         TypeScript interfaces (API, settings, PDF, UI)
    └── src/vue/           Shared Vue 3 components (Insert, Settings, TOC, Variables, PDF tabs)

vscode-calcpad/            VS Code extension
    ├── src/               Extension host (language providers, server manager, preview)
    └── src/CalcpadVuePanel/  Vue 3 webview (sidebar UI)

calcpad-web/               Web editor
    ├── src/editor/        Monaco Editor setup, language, themes, completions
    └── src/services/      Message bridge between Monaco and Vue sidebar

calcpad-desktop/           Tauri desktop wrapper
    ├── src-tauri/         Rust shell (window, menu, sidecar spawn) + tauri.conf.json
    ├── stage-sidecar.sh   Publishes Calcpad.Server for the host RID and stages the
    │   stage-sidecar.ps1  apphost as src-tauri/binaries/calcpad-server-<triple>[.exe]
    ├── build-desktop.sh   Full bundle: sidecar → `tauri build` → msi/nsis/deb/rpm/dmg
    │   build-desktop.ps1
    ├── build-portable.ps1 Windows portable: `tauri build --no-bundle` → flat folder → zip
    └── packaging/arch/    PKGBUILD that packages the build-desktop.sh output for pacman
```

The desktop app includes the bundled calculation server.

### Desktop control channel

On Unix, the desktop shell listens on `$XDG_RUNTIME_DIR/calcpad-desktop.sock`
(mode 0600) so a terminal can drive the running window. If `XDG_RUNTIME_DIR`
is unavailable it creates a private 0700 directory owned by the current user
under the temp dir and refuses to reuse a socket owned by anyone else. The app binary is also
the client, so no extra tool is required:

```bash
calcpad-desktop --ctl state
calcpad-desktop --ctl open ./worksheet.cpd
calcpad-desktop --ctl save
calcpad-desktop --ctl export pdf ./report.pdf report
calcpad-desktop --ctl result > preview.html
```

`calcpadctl.sh` is a convenience wrapper that also implements `screenshot`
(needs `spectacle`, plus `kdotool` on KDE Wayland to raise the window). Install
it somewhere on `PATH` or call `calcpad-desktop --ctl` directly. Run it with no
arguments for the command list. Exports and `save-as` bypass the native save
dialog; explicit output paths are written as given, with a default extension
only when the path has none. Commands require the app to be running and answer
with JSON (`{"ok": true, "result": ...}`).

Command groups:

| Group   | Commands                                                                                                                                                                                                                 |
| ------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| Files   | `open`, `close [path] [--force]`, `save`, `save-as <path>`, `reload [path] [--force]`, `content [path]`, `set-content <file\|-> [path]`                                                                                  |
| Compute | `refresh`, `result`, `state`, `menu <id>`                                                                                                                                                                                |
| Editor  | `editor info\|focus\|undo\|redo`, `editor goto <line> [column]`, `editor select <l1> <c1> <l2> <c2>`, `editor insert <text\|->`, `editor find <text> [--regex --case --word]`, `editor replace-all <text> <new> [flags]` |
| Exports | `export <pdf\|html\|docx\|cpdz\|zip> <out> [report\|preview\|input\|unwrapped]`, `plots`, `save-plot <index> <out>`, `save-plots <out.zip>`                                                                              |
| Shell   | `windows`, `focus`, `window <minimize\|maximize\|unmaximize\|show\|hide\|close\|destroy>`, `clipboard read\|write <text\|->`, `ping`                                                                                     |
| Bridge  | `message <json> [--wait=<responseType>]`                                                                                                                                                                                 |
| Debug   | `eval <expression\|->` (requires the app started with `CALCPAD_CONTROL_EVAL=1`)                                                                                                                                          |

Use `--window=<label>` with any command to target a specific window; without
it the last focused window is used. `windows` lists the labels.

`message` is the generic bridge entry point for the sidebar operations. It
posts the same message the Vue UI posts and can wait for the matching response:

```bash
calcpad-desktop --ctl message '{"type":"getSettings"}' --wait=settingsResponse
calcpad-desktop --ctl message '{"type":"getVariables"}' --wait=updateVariables
calcpad-desktop --ctl message '{"type":"getUiControls"}' --wait=uiControls
calcpad-desktop --ctl message '{"type":"updateUiOverrides","overrides":{"x:1":"6"}}'
calcpad-desktop --ctl message '{"type":"writeFilesNow"}' --wait=writeFilesResult
```

Common response types: `settingsResponse`, `updateVariables`, `uiControls`,
`plotsResponse`, `updateHeadings`, `documentMetadata`, `metadataContext`,
`pdfSettingsResponse`, `insertDataResponse`, `folderOpened`, `folderContents`,
`writeFilesResult`, `serverLogResponse`. Messages without a response type (for
example `prettifyDocument`, `updateSettings`, `insertText`) return as soon as
they are posted; read `content` afterwards to assert the effect.

`eval` executes an expression in the webview, with access to `window`,
`window.calcpadTabs`, `window.calcpadActiveEditor`, `window.calcpadBridge` and
the DOM. It is a QA/debug escape hatch and is disabled unless explicitly
enabled at startup. The socket is per-user and mode 0600; no network port is
opened.

The test suite for this channel lives in `tools/control-channel-qa/`; it
asserts the channel contract against a running app and is separate from the
calculation-engine tests and the WebDriver desktop-UI harness. The channel is
Unix-only for now; Windows builds report it as unavailable.

## Development

### Prerequisites

- Node.js 18+
- npm
- .NET 10 SDK (for building the backend)

### Building

```bash
# Shared library (must build first)
cd calcpad-frontend && npm install && npm run build

# VS Code extension
cd vscode-calcpad && npm install && npm run package

# Web editor
cd calcpad-web && npm install && npm run build

# Desktop app (produces installers via `tauri build` under src-tauri/target/release/bundle)
cd calcpad-desktop && npm install && bash build-desktop.sh

# One Linux format at a time
bash build-desktop.sh --bundles=deb        # or rpm
```

To compile the extension for development, run `npm run compile` in `vscode-calcpad`.
Rebuild its bundled server with `npm run sync-server` in the same directory.

### Linux packaging

`build-desktop.sh` with no `--bundles` produces `deb` and `rpm`. The Arch
package is a separate step because `makepkg` is not a `tauri build` target — it wraps
the tree that `build-desktop.sh` already produced, so all three come from one build:

```bash
cd calcpad-desktop
bash build-desktop.sh --bundles=deb        # any target; supplies the binary + sidecar
cd packaging/arch && makepkg -f            # → calcpad-ce-<ver>-x86_64.pkg.tar.zst
```

Arch packaging uses the version recorded by the desktop build.

### Watching for Changes

```bash
# Shared library (auto-rebuild on changes)
cd calcpad-frontend && npm run watch

# VS Code extension (dev mode)
cd vscode-calcpad && npm run watch

# Web editor (dev server with HMR)
cd calcpad-web && npm run dev
```

For continuous web builds, run `npm run build:watch` in `calcpad-web`.

### Code Signing (Windows)

The bundled `Calcpad.Server.exe` (and the desktop Tauri app exe / installer) are native
executables. A freshly-built, **unsigned** exe has no cloud reputation, so Windows
Defender / SmartScreen / corporate EDR can block it from running
("block-at-first-sight"). Reputation is tracked per-file-hash for unsigned binaries
— so it re-fires on every rebuild — but per-**publisher** for signed ones. Signing
with a trusted cert makes rebuilds trusted immediately.

Signing is **off by default** (CI and contributors without a cert get a working,
unsigned build). It turns on when `CALCPAD_SIGN_THUMBPRINT` is set at build time.

| Env var                      | Purpose                                                                                           |
| ---------------------------- | ------------------------------------------------------------------------------------------------- |
| `CALCPAD_SIGN_THUMBPRINT`    | SHA1 thumbprint of a code-signing cert in `Cert:\CurrentUser\My`. **Required to enable signing.** |
| `CALCPAD_SIGNTOOL`           | Optional. Explicit path to `signtool.exe` if it isn't auto-discovered under the Windows SDK.      |
| `CALCPAD_SIGN_TIMESTAMP_URL` | Optional. RFC-3161 timestamp URL (default `http://timestamp.digicert.com`).                       |

What gets signed:

- **Extension** — [`signApphost()`](vscode-calcpad/scripts/sync-bundled-server.mjs) signs `vscode-calcpad/bin/Calcpad.Server.exe` during `npm run sync-server*` / `npm run package*`.
- **Desktop** — Tauri signs the bundled app exe and installers via the `bundle.windows.signCommand` block in [tauri.conf.json](calcpad-desktop/src-tauri/tauri.conf.json), which shells out to `signtool` when `CALCPAD_SIGN_THUMBPRINT` is set. The embedded server exe is signed by the same `signApphost()` script before it's staged into `src-tauri/binaries/`.

See also [vscode-calcpad/.env.example](vscode-calcpad/.env.example).

#### Local dev setup (self-signed cert)

For local development you can use a self-signed cert. **This only trusts the binary
on machines where the cert is imported** — for the shipped `.vsix` / desktop zip that
end users install, you need a CA-issued (EV) cert or [Azure Trusted Signing](https://learn.microsoft.com/azure/trusted-signing/).

1. **Create a code-signing cert** (normal PowerShell — lands in `Cert:\CurrentUser\My`):

   ```powershell
   $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject 'CN=Calcpad Dev Signing' `
       -KeyUsage DigitalSignature -KeyExportPolicy Exportable `
       -CertStoreLocation Cert:\CurrentUser\My -NotAfter (Get-Date).AddYears(5)
   Export-Certificate -Cert $cert -FilePath "$HOME\.calcpad-signing\calcpad-dev-signing.cer" -Force
   $cert.Thumbprint   # use this for CALCPAD_SIGN_THUMBPRINT
   ```

2. **Trust the cert** so Defender/AppLocker accept anything signed with it
   (**elevated** PowerShell, one time — import into both stores):

   ```powershell
   $cer = "$HOME\.calcpad-signing\calcpad-dev-signing.cer"
   Import-Certificate -FilePath $cer -CertStoreLocation Cert:\LocalMachine\Root
   Import-Certificate -FilePath $cer -CertStoreLocation Cert:\LocalMachine\TrustedPublisher
   ```

3. **Exclude the build output from Defender.** `signtool` needs *write* access to embed
   the signature, but Defender denies write to a blocked unsigned exe — a chicken-and-egg
   that an exclusion breaks (**elevated** PowerShell). Adjust the repo path:

   ```powershell
   Add-MpPreference -ExclusionPath `
     "<repo>\Calcpad.Web\backend\bin", `
     "<repo>\Calcpad.Web\frontend\vscode-calcpad\bin", `
     "<repo>\Calcpad.Web\frontend\calcpad-desktop\src-tauri\binaries", `
     "<repo>\Calcpad.Web\frontend\calcpad-desktop\src-tauri\target"
   # or, more simply, exclude the whole repo:
   #   Add-MpPreference -ExclusionPath "<repo>"
   ```

   Keep the exclusion in place for the dev loop — every rebuild produces a fresh unsigned
   exe that needs the write window to sign.

4. **Set the thumbprint and build** (`setx` persists it for future terminals; open a new
   terminal afterward):

   ```powershell
   setx CALCPAD_SIGN_THUMBPRINT <thumbprint-from-step-1>
   # new terminal, then e.g.:
   cd vscode-calcpad && npm run sync-server:slim
   ```

> **Managed / corporate machines:** if `Add-MpPreference` errors or the exe stays blocked
> after signing, Defender is managed by policy (Defender for Endpoint / Intune) and local
> exclusions are overridden. In that case IT must allowlist the publisher
> (`CN=Calcpad Dev Signing`) or the build path, or you must sign on an unmanaged machine / CI.

## Requirements

- **Calcpad.Server** (bundled with the extension and the desktop app; run separately for the web editor)
- VS Code 1.82.0+ (for the extension)
- Modern browser (for the web editor)

## License

MIT
