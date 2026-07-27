# CalcPad Web Frontend

> **Localhost-only build.** This branch (`calcpad-web`) only supports running Calcpad.Server bound to a loopback address. The backend's startup loopback guard in [Program.cs](../backend/Program.cs) throws `InvalidOperationException` if the resolved bind URL is anything other than `localhost`, `127.0.0.0/8`, or `::1`. Multi-user / hosted / Docker deployment, auth, and shared file storage live on the `calcpad-experimental` branch.

A monorepo containing all frontend projects for CalcPad: a VS Code extension, a standalone web editor, a desktop application, and a shared library that powers them all.

## Projects

| Directory | Description |
|-----------|-------------|
| [calcpad-frontend/](calcpad-frontend/) | Shared TypeScript library (API client, services, Vue components) |
| [vscode-calcpad/](vscode-calcpad/) | VS Code extension with live preview and full language support |
| [calcpad-web/](calcpad-web/) | Standalone web editor built with Monaco Editor + Vue |
| [calcpad-desktop/](calcpad-desktop/) | Desktop application via Tauri wrapping the web editor |

All projects depend on **Calcpad.Server** (the .NET backend at `../backend/`) for computation, linting, and rendering via REST API.

---

## Quick Start

### 1. Install Calcpad.Server

**Calcpad.Server is required** for all frontend projects. Download the latest release from:

[https://github.com/imartincei/CalcpadCE/releases](https://github.com/imartincei/CalcpadCE/releases)

Or build from source:
```bash
cd ../backend
dotnet publish -c Release
```

### 2. Start the Server

Run the Calcpad.Server executable. The default port is **9420** (configurable via the `CALCPAD_PORT` environment variable).

### 3. Choose a Frontend

**VS Code Extension:**
```bash
cd vscode-calcpad
npm install
npm run package    # Build extension + Vue webview
```
Then install the generated `.vsix` file in VS Code.

**Web Editor:**
```bash
cd calcpad-web
npm install
npm run dev        # Start dev server
```

**Desktop App:**
```bash
cd calcpad-desktop
npm install
npm run dev        # Start Tauri dev mode (hot-reloads the Vue frontend, rebuilds the Rust shell on change)
```

> **Note:** Running `tauri dev` requires the Calcpad.Server sidecar to be staged into `src-tauri/binaries/`. Use the `Desktop: Stage Sidecar` VS Code task, or run `stage-sidecar.sh` / `stage-sidecar.ps1` before the first dev launch.

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
    ├── build-desktop.sh   Full bundle: sidecar → `tauri build` → msi/nsis/deb/rpm/appimage/dmg
    │   build-desktop.ps1
    ├── build-portable.ps1 Windows portable: `tauri build --no-bundle` → flat folder → zip
    └── packaging/arch/    PKGBUILD that packages the build-desktop.sh output for pacman
```

All frontends import `calcpad-frontend` as a local dependency and communicate with Calcpad.Server via the REST API documented in [API_SCHEMA.md](API_SCHEMA.md).

---

## Features

### Editor

- **Live HTML Preview** with automatic updates as you type
- **Syntax Highlighting** with 18 token types (variables, functions, macros, units, keywords, etc.)
- **Comprehensive Linting** with 40+ error codes covering:
  - Parentheses, bracket, and brace balancing
  - Control block matching (`#if`/`#end`, `#for`/`#loop`)
  - Variable and function naming validation
  - Unit checking and operator syntax
  - Keyword argument validation
- **Go to Definition** (Ctrl+Click / F12) for variables, functions, macros, and custom units
- **Find All References** (Shift+Alt+F12) across the entire file and includes
- **Rename Symbol** (F2) with project-wide renaming
- **Autocomplete** with context-aware snippets and parameter documentation
- **File Path Completion** for `#include` and `#read` directives
- **PDF Export** with configurable headers, footers, margins, and background templates

### Language

- **Inline Macros** (`#def`) with parameter substitution
- **Recursive Includes** with cycle detection
- **Table of Contents** generated from heading comments

### Editing Aids

- **Operator Replacement** — automatic symbol substitution (e.g., `<=` to `≤`, `sqrt` to `√`)
- **Quick Typing** — type `~` + shortcut + space for Greek letters and symbols (e.g., `~a` -> `α`, `~theta` -> `θ`)
- **Auto-Indentation** for control blocks
- **Comment Formatting** — bold, italic, and other formatting in comment blocks

---

## VS Code Extension Details

### Installation

Download the `.vsix` file from the [releases page](https://github.com/imartincei/CalcpadCE/releases) and install in VS Code:
1. Go to Extensions view (Ctrl+Shift+X)
2. Click the "..." menu -> "Install from VSIX..."
3. Choose the downloaded `.vsix` file

### Configuration

Set the Calcpad.Server URL in VS Code settings:
```json
{
  "calcpad.server.url": "http://localhost:9420"
}
```

### Usage

1. Ensure Calcpad.Server is running
2. Open a `.cpd` file
3. Click the preview button in the editor toolbar or use `Ctrl+Shift+P` -> "CalcPad Preview"
4. Preview updates automatically as you type
5. Linting errors appear as you work

---

## Web Editor Details

The web editor provides the same features as the VS Code extension in a standalone browser environment using Monaco Editor.

### Configuration

Set the server URL via the `VITE_SERVER_URL` environment variable or through the Settings tab in the sidebar.

---

## Desktop App Details

The desktop application wraps the web editor using [Tauri](https://tauri.app/), providing native window management, a native menu bar, and an embedded Calcpad.Server sidecar. Supports macOS, Windows, and Linux.

Calcpad.Server is published as a framework-independent apphost renamed to Tauri's target-triple sidecar format (`calcpad-server-<target-triple>[.exe]`) and staged into `src-tauri/binaries/` before `tauri dev` / `tauri build` runs. `tauri.conf.json`'s `bundle.externalBin` picks it up and includes it in the packaged installer. See [`build-desktop.sh`](calcpad-desktop/build-desktop.sh) / [`build-desktop.ps1`](calcpad-desktop/build-desktop.ps1) and the `stage-sidecar` scripts.

---

## API Reference

The backend REST API is documented in [API_SCHEMA.md](API_SCHEMA.md). Key endpoints:

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/calcpad/convert` | POST | Convert to HTML |
| `/api/calcpad/lint` | POST | Lint and return diagnostics |
| `/api/calcpad/definitions` | POST | Extract symbols (variables, functions, macros, units) |
| `/api/calcpad/find-references` | POST | Find all symbol occurrences |
| `/api/calcpad/highlight` | POST | Syntax tokenization |
| `/api/calcpad/snippets` | GET | Autocomplete snippets by category |
| `/api/calcpad/pdf` | POST | Generate PDF from HTML |
| `/api/calcpad/docx` | POST | Generate DOCX from source |
| `/api/calcpad/prettify` | POST | Pretty-print Calcpad source |

---

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
bash build-desktop.sh --bundles=deb        # or rpm, appimage
```

### Linux packaging

`build-desktop.sh` with no `--bundles` produces `deb`, `rpm`, and `appimage`. The Arch
package is a separate step because `makepkg` is not a `tauri build` target — it wraps
the tree that `build-desktop.sh` already produced, so all four come from one build:

```bash
cd calcpad-desktop
bash build-desktop.sh --bundles=deb        # any target; supplies the binary + sidecar
cd packaging/arch && makepkg -f            # → calcpad-ce-<ver>-x86_64.pkg.tar.zst
```

`pkgver` is kept in sync by [`scripts/bump-versions.mjs`](scripts/bump-versions.mjs).

### Watching for Changes

```bash
# Shared library (auto-rebuild on changes)
cd calcpad-frontend && npm run watch

# VS Code extension (dev mode)
cd vscode-calcpad && npm run watch

# Web editor (dev server with HMR)
cd calcpad-web && npm run dev
```

### Code Signing (Windows)

The bundled `Calcpad.Server.exe` (and the desktop Tauri app exe / installer) are native
executables. A freshly-built, **unsigned** exe has no cloud reputation, so Windows
Defender / SmartScreen / corporate EDR can block it from running
("block-at-first-sight"). Reputation is tracked per-file-hash for unsigned binaries
— so it re-fires on every rebuild — but per-**publisher** for signed ones. Signing
with a trusted cert makes rebuilds trusted immediately.

Signing is **off by default** (CI and contributors without a cert get a working,
unsigned build). It turns on when `CALCPAD_SIGN_THUMBPRINT` is set at build time.

| Env var | Purpose |
|---------|---------|
| `CALCPAD_SIGN_THUMBPRINT` | SHA1 thumbprint of a code-signing cert in `Cert:\CurrentUser\My`. **Required to enable signing.** |
| `CALCPAD_SIGNTOOL` | Optional. Explicit path to `signtool.exe` if it isn't auto-discovered under the Windows SDK. |
| `CALCPAD_SIGN_TIMESTAMP_URL` | Optional. RFC-3161 timestamp URL (default `http://timestamp.digicert.com`). |

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

- **Calcpad.Server** running at configured URL (required)
- VS Code 1.82.0+ (for the extension)
- Modern browser (for the web editor)

## License

MIT
