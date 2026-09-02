# Models & Services Reference

Request/response models live inline at the bottom of `Controllers/CalcpadController.cs`, not in `Models/` — that folder holds only `Pdf/PdfGenerateRequest.cs`. Follow the existing convention when adding one.

The canonical field-by-field schema is [../../../../Calcpad.Web/backend/API_SCHEMA.md](../../../../Calcpad.Web/backend/API_SCHEMA.md); this file covers the shapes you touch most and the service surface behind them.

## Request/Response Models

### CalcpadRequest (convert, docx)
```csharp
public class CalcpadRequest
{
    public string Content { get; set; }
    public Settings? Settings { get; set; }
    public bool ForceUnwrappedCode { get; set; }
    public string Theme { get; set; }                       // "light" or "dark"
    public bool ForPrint { get; set; }                      // #pre hidden, #post shown
    public string? SourceFilePath { get; set; }             // resolves relative #include / #read
    public bool EnableUi { get; set; }                      // #UI lines become controls
    public Dictionary<string, string>? UiOverrides { get; set; }  // keyed by data-ui-var identity
    public bool? IncludeLineAnchors { get; set; }           // defaults to !ForPrint
    public bool? HideErrorLines { get; set; }               // defaults to EnableUi
    public bool Write { get; set; }                         // may run #write/#append
}
```

There is no client file cache. Include resolution reads from disk, resolved against `SourceFilePath`'s folder.

### HighlightRequest / LintRequest / DefinitionsRequest
```csharp
public class HighlightRequest
{
    public string Content { get; set; }
    public bool IncludeText { get; set; }       // omit token text by default to shrink the payload
    public string? SourceFilePath { get; set; }
}
```

`LintRequest` and `DefinitionsRequest` are the same minus `IncludeText`. `SymbolAtPositionRequest` adds zero-based `Line` and `Column`.

### PdfGenerateRequest
```csharp
public class PdfGenerateRequest
{
    public string Html { get; set; }
    public PdfSettingsDto? Options { get; set; }
}
```

`PdfSettingsDto` lives in **Calcpad.Highlighter** (`HtmlComment/PdfSettingsDto.cs`), shared with the `pdf` metadata-comment parser so a document and the settings UI validate identically: `Format`, `Orientation`, the four margins, `ShowPageNumbers?`, `ShowDate?`, `DocumentTitle`, `DateTimeFormat`.

The browser executable is never taken from the request — that would be an arbitrary-process-launch primitive. It resolves server-side from `BrowserPath` in `appsettings.json`, the `BROWSER_PATH` environment variable, or auto-detection.

## Key Services

### CalcpadService (Scoped)
Core conversion. One method does every rendering; the flags pick which:
```csharp
public (string Html, IReadOnlyList<string> OpenXmlExpressions, IReadOnlyList<CalcpadError> Errors) Convert(
    string calcpadContent,
    Settings? settings = null,
    bool forceUnwrappedCode = false,
    string theme = "light",
    string? sourceFilePath = null,
    bool forPrint = false,
    bool captureOpenXml = false,          // emit OMML for the DOCX writer
    bool enableUi = false,
    Dictionary<string, string>? uiOverrides = null,
    bool? debug = null,                   // line anchors + error boxes
    bool? hideErrorLines = null,
    bool write = false,
    CancellationToken cancellationToken = default);

public string GetSampleContent();
```

Errors are returned, not thrown — the controller serializes them into the `X-Calcpad-Errors` response header and still returns the HTML.

### CalcpadApiService (Static)
Shared configuration for the web application:
```csharp
public static WebApplicationBuilder ConfigureBuilder(string[] args);
public static WebApplication ConfigureApp(WebApplication app);
public static (WebApplication app, string serverUrl) CreateConfiguredApp(string[] args);
public static string GetServerUrl();
public static Task<bool> TestServerAsync(string serverUrl, int timeoutSeconds = 3);
```

Configures controllers, Swagger (Development only), the memory cache, DI (`CalcpadService` scoped, `PdfGeneratorService` and `ContentResolutionCache` singleton), and the request pipeline: exception handler → loopback `Host`-header check (421 otherwise) → CORS → `X-Calcpad-Token` check.

### PdfGeneratorService (Singleton)
Browser pooling for PDF generation:
```csharp
public Task<byte[]> GeneratePdfAsync(string html, PdfSettingsDto? options = null);
public BrowserStatus GetBrowserStatus();     // available, source, path, downloadAllowed, downloadSizeMb
public Task<string> InstallBrowserAsync();   // downloads ChromeHeadlessShell
```

Throws `BrowserUnavailableException` when no Chromium-family browser can be resolved; the controller turns that into a `503` carrying `code: "BROWSER_NOT_FOUND"` so clients can offer the download.

### ContentResolutionCache (Singleton)
```csharp
public StagedResolvedContent GetOrResolve(string content, string? sourceFilePath);
```

Keyed by `sourceFilePath|sha256(content)` and invalidated when an included file changes on disk. Every analysis endpoint goes through it, so lint / highlight / definitions / symbol-at-position on the same buffer resolve includes once. Size limit from `CALCPAD_CONTENT_CACHE_SIZE_LIMIT` (default 100 entries).

### Highlighter Integration
```csharp
// Content resolution (always via the cache, never ContentResolver directly)
var staged = _contentResolutionCache.GetOrResolve(request.Content, request.SourceFilePath);

// Linting — lint-ignore regions come from the raw source, not the staged content
var ignoreRegions = _lintIgnoreRegionParser.ExtractRegions(request.Content);
var lintResult = new CalcpadLinter().Lint(staged, ignoreRegions);

// Tokenization — feed it macro info from includes when a source path is known
var tokenizer = new CalcpadTokenizer();
tokenizer.SetMacroCommentParameters(staged.Stage2.MacroCommentParameters,
    staged.Stage2.MacroParameterOrder, staged.Stage2.MacroBodies);
var tokens = tokenizer.Tokenize(request.Content).Tokens;

// Definitions come off the staged pipeline, types off its TypeTracker
var typeTracker = staged.Stage3.TypeTracker;
staged.Stage2.MacroDefinitions; staged.Stage3.FunctionsWithParams;
staged.Stage3.VariablesWithDefinitions; staged.Stage3.CustomUnits;

// Symbol resolution
var hit = SymbolResolver.ResolveSymbolAt(staged.Stage3, request.Line, request.Column);

// Snippets
SnippetRegistry.GetAllSnippetsArray();
SnippetRegistry.GetSnippetsByCategory(category);
```

## Cancellation

The analysis and conversion endpoints take a `CancellationToken` and return `499` on `OperationCanceledException` — a superseded request under rapid typing or tab-switching is expected, not an error, and must not be logged as one.
