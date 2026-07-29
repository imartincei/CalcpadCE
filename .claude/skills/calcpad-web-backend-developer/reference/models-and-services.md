# Models & Services Reference

## Request/Response Models

### CalcpadRequest (convert, convert-unwrapped, debug-raw-code)
```csharp
public class CalcpadRequest
{
    public string Content { get; set; }
    public Settings? Settings { get; set; }
    public bool ForceUnwrappedCode { get; set; }
    public string? Theme { get; set; }
    public Dictionary<string, string>? ClientFileCache { get; set; }  // base64-encoded
}
```

### HighlightRequest
```csharp
public class HighlightRequest
{
    public string Content { get; set; }
    public bool IncludeText { get; set; }
    public Dictionary<string, string>? IncludeFiles { get; set; }
    public Dictionary<string, string>? ClientFileCache { get; set; }  // base64-encoded
}
```

### PdfGenerateRequest
```csharp
public class PdfGenerateRequest
{
    public string Html { get; set; }
    public string? BrowserPath { get; set; }
    public PdfOptions? Options { get; set; }
}

public class PdfOptions
{
    public string Format { get; set; }
    public string Orientation { get; set; }
    public float Scale { get; set; }
    public string MarginTop { get; set; }
    // ... MarginRight, MarginBottom, MarginLeft
    public bool ShowPageNumbers { get; set; }
    public bool ShowDate { get; set; }
    public string? DocumentTitle { get; set; }
    public string? HeaderCenter { get; set; }
    public string? FooterCenter { get; set; }
    public string? DateTimeFormat { get; set; }
}
```

## Key Services

### CalcpadService
Core business logic for converting Calcpad source to HTML output:
```csharp
public class CalcpadService
{
    // Convert source to HTML using Calcpad.Core MathParser
    public async Task<string> ConvertAsync(string content, Settings? settings,
        bool forceUnwrapped, string? theme, WebFetchContext ctx);
    // Sample content generation
}
```

### CalcpadApiService (Static)
Shared configuration for the web application builder:
```csharp
public static class CalcpadApiService
{
    public static WebApplicationBuilder ConfigureBuilder(string[] args);
    public static WebApplication ConfigureApp(WebApplicationBuilder builder);
    public static (WebApplication, string) CreateConfiguredApp(string[] args);
}
```
Configures: Controllers, Swagger, CORS, DI (CalcpadService, PdfGeneratorService).

### PdfGeneratorService (Singleton)
Browser instance pooling for PDF generation:
```csharp
public class PdfGeneratorService
{
    public async Task<byte[]> GeneratePdfAsync(string html, PdfOptions? options);
    // Uses PuppeteerSharp for HTML-to-PDF rendering
    // PDFsharp for post-processing (headers, footers, pagination)
}
```

### Highlighter Integration
The controller calls Calcpad.Highlighter directly:
```csharp
// Content resolution pipeline
var staged = ContentResolver.GetStagedContent(content, fileCache);

// Linting
var lintResult = CalcpadLinter.Lint(staged);

// Tokenization
var tokens = CalcpadTokenizer.Tokenize(staged);

// Definitions extraction
var definitions = CalcpadLinter.GetDefinitions(staged);

// Snippets
var snippets = SnippetGenerator.Generate();
```

## Client File Cache Pattern

Frontend sends base64-encoded file contents for `#include` resolution:
```csharp
// Controller boundary: decode base64 → raw bytes
private static Dictionary<string, byte[]> DecodeClientFileCache(
    Dictionary<string, string>? base64Cache)
{
    var result = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
    if (base64Cache == null) return result;
    foreach (var kvp in base64Cache)
    {
        try { result[kvp.Key] = Convert.FromBase64String(kvp.Value); }
        catch (FormatException) { /* skip malformed entries */ }
    }
    return result;
}
```
