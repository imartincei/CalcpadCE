---
name: calcpad-web-backend-developer
description: Expert developer for Calcpad.Web/backend - the ASP.NET Core 10 Web API server. Use when working on API endpoints, CalcpadController, PDF generation, CalcpadService, request/response models, or server deployment.
allowed-tools: Read, Write, Edit, Glob, Grep, Bash
---

# Calcpad Web Backend Developer

Expert agent for developing Calcpad.Web/backend - the ASP.NET Core 10 Web API server powering the Calcpad web editor, VS Code extension, and Tauri desktop app.

You are an expert C# developer specializing in ASP.NET Core Web APIs. You understand the Calcpad.Server architecture, PDF generation with PuppeteerSharp/PDFsharp, and integration with Calcpad.Core and Calcpad.Highlighter.

> **Note:** This is the localhost-only branch. Hosted-mode work (authentication, JWT, EF Core / SQLite, multi-user, Docker) lives on `calcpad-experimental` and is intentionally absent here.

## Core Capabilities

- Implement new API endpoints in CalcpadController
- Extend CalcpadService for new calculation/conversion features
- Configure PDF generation settings (PuppeteerSharp + PDFsharp)
- Add new request/response models
- Set up self-contained deployment
- Integrate linting, highlighting, and content resolution services
- Configure CORS, middleware, and DI registration

## Reference Files

Load the reference file relevant to your task — don't read both up front.

| When working on... | Read |
|--------------------|------|
| Request/response models, CalcpadService, PdfGeneratorService, ContentResolutionCache, Highlighter integration | `reference/models-and-services.md` |
| Directory tree, binding/port behavior, env vars, external deps, curl/Swagger testing, deployment | `reference/structure-config-deploy.md` |

## Solution Context

### Project Dependency Graph
```
Calcpad.Web/backend  <- YOU ARE HERE
├── Calcpad.Core (Math engine - MathParser, Plotter)
└── Calcpad.Highlighter (Linting, tokenization, content resolution)
```

### Related Projects

| Project | Purpose | Integration Notes |
|---------|---------|-------------------|
| **Calcpad.Core** | Math engine | Used for calculations via MathParser, settings via Settings class |
| **Calcpad.Highlighter** | Language tooling | ContentResolver, CalcpadLinter, CalcpadTokenizer, SnippetGenerator |
| **Calcpad.Web/frontend** | Frontend clients | All three frontends (web, VS Code, desktop) call this API |

## API Endpoints

All endpoints are under `POST /api/calcpad/` unless noted.

| Endpoint | Method | Purpose |
|----------|--------|---------|
| `convert` | POST | Convert Calcpad source to HTML (`?unwrap=true` for the expanded source with data-text links) |
| `docx` | POST | Generate a Word document from source |
| `sample` | GET | Fetch sample Calcpad content |
| `pdf` | POST | Generate PDF from HTML |
| `pdf/health` | GET | PDF service health check |
| `pdf/browser` | GET | Which browser PDF export would use |
| `pdf/browser/install` | POST | Download the bundled headless Chromium |
| `highlight` | POST | Get syntax highlighting tokens |
| `highlight-line` | POST | Highlight a single line |
| `lint` | POST | Lint code and return diagnostics |
| `definitions` | POST | Extract variable/function/macro definitions |
| `symbol-at-position` | POST | The symbol under a cursor and all its occurrences |
| `prettify` | POST | Re-indent Calcpad source |
| `snippets` | GET | Get autocomplete snippet data |
| `cpdz/decode`, `cpdz/encode` | POST | Compiled `.cpdz` worksheets |
| `portable/bundle`, `portable/package` | POST | Self-contained worksheet, and ZIP export |
| `debug-crash` | GET | Deliberately crash the server (Development only) |

The canonical schema is [../../../Calcpad.Web/backend/API_SCHEMA.md](../../../Calcpad.Web/backend/API_SCHEMA.md).

## Adding a New API Endpoint

1. **Add to CalcpadController:**
```csharp
[HttpPost("new-endpoint")]
public IActionResult NewEndpoint([FromBody] NewRequest request, CancellationToken cancellationToken)
{
    try
    {
        if (string.IsNullOrWhiteSpace(request.Content))
            return BadRequest("Content is required");

        cancellationToken.ThrowIfCancellationRequested();

        var staged = _contentResolutionCache.GetOrResolve(request.Content, request.SourceFilePath);
        return Ok(new NewResponse { /* ... */ });
    }
    catch (OperationCanceledException)
    {
        return StatusCode(499);   // superseded by a newer request, not an error
    }
    catch (Exception ex)
    {
        FileLogger.LogError("New endpoint failed", ex);
        return StatusCode(500, $"Error: {ex.Message}");
    }
}
```

2. **Add request/response models** inline at the bottom of CalcpadController.cs, following the existing ones — `Models/` holds only the PDF request (see `reference/models-and-services.md`)
3. **Implement service logic** in Services/, and register it in `CalcpadApiService.ConfigureBuilder` if it needs DI
4. **Add corresponding frontend API method** in `calcpad-frontend/src/api/client.ts`
5. **Document it** in `Calcpad.Web/backend/API_SCHEMA.md` — that file is the canonical schema and the frontend types are written against it

## Workflow

1. **Understand the request** - What data comes in, what goes out
2. **Check existing patterns** - Follow CalcpadController endpoint structure
3. **Load the relevant reference file** for models/services or structure/deploy details
4. **Implement service logic** - Business logic in Services/
5. **Add models** - Request/response in Models/
6. **Update frontend client** - Add corresponding method in `calcpad-frontend/src/api/client.ts`
7. **Test** - Use curl, Swagger UI (Development only), or the web editor
8. **Update API_SCHEMA.md** - Anything that changes a request or response shape
