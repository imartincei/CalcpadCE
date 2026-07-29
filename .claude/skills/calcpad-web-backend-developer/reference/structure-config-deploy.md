# Structure, Config & Deployment Reference

## Project Structure

> **Note:** This is the localhost-only branch. Hosted-mode work (auth, JWT, EF Core / SQLite, multi-user) lives on `calcpad-experimental` and is intentionally absent here.

```
Calcpad.Web/backend/
├── Controllers/
│   └── CalcpadController.cs        # API controller (convert, lint, highlight, definitions, snippets, PDF)
├── Services/
│   ├── CalcpadApiService.cs        # Shared app builder configuration (DI, CORS, middleware)
│   ├── CalcpadService.cs           # Core conversion/calculation logic (HTML generation, caching)
│   ├── PdfGeneratorService.cs      # PDF generation (PuppeteerSharp browser pool + PDFsharp)
│   ├── FileSettingsExtractor.cs    # Extracts settings from source files
│   ├── PdfGeneratorService.cs      # Puppeteer render + PDFsharp header/footer overlay
│   └── BundledFonts.cs             # Inlines bundled fonts for PDF rendering
├── Models/
│   └── Pdf/
│       └── PdfOptions.cs           # PDF format, orientation, margins, headers/footers
├── Program.cs                      # Console entry point, graceful shutdown
├── FileLogger.cs                   # File-based crash/error logging
├── template.html                   # HTML output template for rendered calculations
├── appsettings.json                # Browser path, logging config
├── Calcpad.Server.csproj           # .NET 10 project (v7.6.1)
├── Calcpad.Server.sln
└── scripts/
    ├── restart-dev-server.sh       # Start/restart dev server
    ├── build-linux.sh              # Linux build script
    ├── build-linux-console.sh      # Linux console build
    ├── build-slim-bundle.sh        # Slim bundle build (Linux)
    ├── build-slim-bundle.ps1       # Slim bundle build (Windows)
    └── deploy-slim-bundle.ps1      # Deploy slim bundle
```

## Environment Variables

| Variable | Default | Purpose |
|----------|---------|---------|
| `CALCPAD_PORT` | `9420` | Server listening port |
| `CALCPAD_HOST` | `0.0.0.0` | Server bind address |
| `CALCPAD_ENABLE_HTTPS` | (unset) | Enable HTTPS |
| `CALCPAD_DETACHED` | (unset) | Run detached (no console prompts) |

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
./scripts/Calcpad.Server/restart-dev-server.sh
# Or directly:
dotnet run --project Calcpad.Web/backend/Calcpad.Server.csproj
```

### Testing Endpoints
```bash
# Convert
curl -X POST http://localhost:9420/api/calcpad/convert \
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

# Snippets
curl http://localhost:9420/api/calcpad/snippets

# PDF Health
curl http://localhost:9420/api/calcpad/pdf/health
```

### Swagger UI
Navigate to `http://localhost:9420/swagger` when the server is running.

## Deployment

- **Self-contained:** Single-file publish via `build-slim-bundle.sh` / `.ps1`
- **Console:** Standalone executable with graceful Ctrl+C shutdown
- **DLL:** Can be referenced directly by frontend projects (e.g., VS Code extension server manager)
