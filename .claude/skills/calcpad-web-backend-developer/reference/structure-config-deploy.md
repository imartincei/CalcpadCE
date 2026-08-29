# Structure, Config & Deployment Reference

## Project Structure

> **Note:** This is the localhost-only branch. Hosted-mode work (auth, JWT, EF Core / SQLite, multi-user) lives on `calcpad-experimental` and is intentionally absent here.

```
Calcpad.Web/backend/
├── Controllers/
│   └── CalcpadController.cs        # Every endpoint, plus the request/response models inline
├── Services/
│   ├── CalcpadApiService.cs        # Shared app builder config (DI, CORS, auth, Host check)
│   ├── CalcpadService.cs           # Core conversion/calculation logic (HTML generation)
│   ├── PdfGeneratorService.cs      # Puppeteer render + PDFsharp header/footer overlay
│   ├── ContentResolutionCache.cs   # Memoized include resolution shared by the analysis endpoints
│   ├── CpdzCodec.cs                # Compiled .cpdz encode/decode (deflate + composite archive)
│   ├── PortableWorksheet.cs        # Self-contained rewrite for compiling
│   ├── PortablePackage.cs          # ZIP export with a refs folder beside the document
│   ├── WorksheetReferences.cs      # Shared reference discovery for both portable paths
│   ├── OutputTargets.cs            # #write/#append target collection
│   ├── BundledFonts.cs             # Inlines bundled fonts for PDF rendering
│   ├── BundledUiAssets.cs          # Inlines the #UI control assets
│   └── UiPreviewScript.cs          # Script injected into the interactive #UI preview
├── Models/
│   └── Pdf/
│       └── PdfGenerateRequest.cs   # { Html, Options } — PdfSettingsDto comes from Calcpad.Highlighter
├── Program.cs                      # Entry point: arg parsing, watchdogs, loopback guard, port file
├── FileLogger.cs                   # File-based crash/error logging
├── template.html                   # HTML output template for rendered calculations
├── Fonts/, UiAssets/               # Assets inlined by BundledFonts / BundledUiAssets
├── appsettings.json                # Browser path, Chromium download policy, logging
├── Calcpad.Server.csproj           # .NET 10 project (version from root Directory.Build.props)
├── Calcpad.Server.sln
└── scripts/
    ├── restart-dev-server.sh       # Kill + rebuild + start on port 9420
    ├── build-linux.sh              # Linux build script
    ├── build-linux-console.sh      # Linux console build
    ├── build-slim-bundle.sh        # Slim bundle build (Linux)
    ├── build-slim-bundle.ps1       # Slim bundle build (Windows)
    ├── deploy-slim-bundle.ps1      # Deploy slim bundle
    └── build-and-deploy-extension.sh / .ps1
```

## Binding

With neither `CALCPAD_PORT` nor `--urls` set, `Program.cs` appends `--urls http://127.0.0.1:0` and the OS assigns a free port, so instances never collide. The bound URL is on the `Now listening on:` line and in the port file (`--port-file <path>`, defaulting to `.calcpad-server.port` beside the binary).

`ASPNETCORE_URLS` is **ignored** — the host calls `UseUrls` explicitly, which overrides it. Use `--urls` or `CALCPAD_PORT`.

A non-loopback bind is rejected twice: once on the URL string before startup, once against `app.Urls` after Kestrel binds (which catches a `Kestrel:Endpoints` section that bypasses `UseUrls`).

## Environment Variables

| Variable | Default | Purpose |
|----------|---------|---------|
| `CALCPAD_PORT` | *(unset — OS-assigned)* | Pins the listening port |
| `CALCPAD_HOST` | `127.0.0.1` | Bind address. Must resolve to loopback |
| `CALCPAD_ENABLE_HTTPS` | (unset) | Serves `https`. Only applies on the `CALCPAD_PORT` path |
| `CALCPAD_API_TOKEN` | *(unset — unauthenticated)* | Required in `X-Calcpad-Token` on every `/api` request when set |
| `CALCPAD_DETACHED` | (unset) | `1` disables the stdin-EOF watchdog and the default port file |
| `CALCPAD_CONTENT_CACHE_SIZE_LIMIT` | `100` | Entries in the resolved-content cache |
| `BROWSER_PATH` | *(auto-detect)* | Chromium-family executable for PDF export |
| `ALLOW_CHROMIUM_DOWNLOAD` | `false` | Lets the render path download Chromium on its own |

**CLI flags:** `--urls`, `--port-file <path>`, `--parent-pid <pid>`, `--exit-on-stdin-close` / `--no-exit-on-stdin-close`.

The stdin-EOF watchdog is on by default whenever stdin is piped, so a server launched from a script with redirected stdin exits immediately unless it sets `CALCPAD_DETACHED=1` or passes `--no-exit-on-stdin-close`.

## External Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| Microsoft.AspNetCore.OpenApi | 10.0.0 | OpenAPI spec generation |
| Swashbuckle.AspNetCore | 10.0.1 | Swagger UI |
| Microsoft.OpenApi | 2.9.0 | Patched OpenAPI (GHSA-v5pm-xwqc-g5wc) |
| PuppeteerSharp | 21.1.1 | HTML-to-PDF rendering (Chromium) |
| PDFsharp | 6.2.0 | PDF post-processing |

## Testing

### Starting the Dev Server
```bash
Calcpad.Web/backend/scripts/restart-dev-server.sh   # pins port 9420, ASPNETCORE_ENVIRONMENT=Development
# Or directly, pinning the port so the curls below work:
CALCPAD_PORT=9420 dotnet run --project Calcpad.Web/backend/Calcpad.Server.csproj
```

A bare `dotnet run` gets a random port instead — read it off the `Now listening on:` line.

### Testing Endpoints
```bash
# Convert (HTML body; calculation errors arrive in the X-Calcpad-Errors header)
curl -i -X POST http://localhost:9420/api/calcpad/convert \
  -H "Content-Type: application/json" \
  -d '{"content": "x = 5\ny = x + 3", "theme": "light"}'

# Lint
curl -X POST http://localhost:9420/api/calcpad/lint \
  -H "Content-Type: application/json" \
  -d '{"content": "a = undefined_var"}'

# Highlight
curl -X POST http://localhost:9420/api/calcpad/highlight \
  -H "Content-Type: application/json" \
  -d '{"content": "x = sin(45)", "includeText": true}'

# Definitions
curl -X POST http://localhost:9420/api/calcpad/definitions \
  -H "Content-Type: application/json" \
  -d '{"content": "f(x) = x^2\na = 5"}'

# Symbol under a cursor, with every occurrence
curl -X POST http://localhost:9420/api/calcpad/symbol-at-position \
  -H "Content-Type: application/json" \
  -d '{"content": "a = 5\nb = a + 1", "line": 1, "column": 4}'

# Snippets
curl http://localhost:9420/api/calcpad/snippets

# PDF Health
curl http://localhost:9420/api/calcpad/pdf/health
```

Add `-H "X-Calcpad-Token: $CALCPAD_API_TOKEN"` when testing against a server a host launched — those always set the token, and every `/api` call without it gets `401`.

### Swagger UI
`http://localhost:9420/swagger`, **Development environment only**.

## Deployment

- **Self-contained:** Single-file publish via `build-slim-bundle.sh` / `.ps1`
- **Console:** Standalone executable, graceful shutdown on SIGINT/SIGTERM
- **Sidecar:** Published as a target-triple apphost for the Tauri desktop bundle; the VS Code extension ships its own copy under `vscode-calcpad/bin/`
