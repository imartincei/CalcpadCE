using Calcpad.Server.Services;
using Calcpad.Server.Models.Pdf;
using Microsoft.AspNetCore.Mvc;
using Calcpad.Core;
using Calcpad.Highlighter.ContentResolution;
using Calcpad.Highlighter.Linter;
using Calcpad.Highlighter.Linter.Models;
using Calcpad.Highlighter.Prettifier;
using Calcpad.Highlighter.Snippets;
using Calcpad.Highlighter.Snippets.Models;
using Calcpad.Highlighter.Tokenizer;
using Calcpad.Highlighter.Tokenizer.Models;
using Calcpad.OpenXml;

namespace Calcpad.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CalcpadController : ControllerBase
    {
        private readonly CalcpadService _calcpadService;
        private readonly PdfGeneratorService _pdfService;
        private readonly ParserGate _parserGate;
        private static readonly LintIgnoreRegionParser _lintIgnoreRegionParser = new();

        public CalcpadController(CalcpadService calcpadService, PdfGeneratorService pdfService, ParserGate parserGate)
        {
            _calcpadService = calcpadService;
            _pdfService = pdfService;
            _parserGate = parserGate;
        }

        [HttpPost("convert")]
        public async Task<IActionResult> ConvertToHtml(
            [FromBody] CalcpadRequest request, CancellationToken cancellationToken, [FromQuery] bool unwrap = false)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.Content))
                {
                    return BadRequest("Content is required");
                }

                using var _ = await _parserGate.AcquireAsync(cancellationToken);

                var forceUnwrapped = unwrap || request.ForceUnwrappedCode;
                var (htmlResult, _, errors, projectPath, libraryPath) = _calcpadService.Convert(
                    request.Content, request.Settings, forceUnwrapped, request.Theme, request.SourceFilePath,
                    request.ForPrint, captureOpenXml: false, request.EnableUi, request.UiOverrides,
                    request.IncludeLineAnchors, cancellationToken);
                if (unwrap)
                {
                    htmlResult = ProcessDataTextLinks(htmlResult);
                }

                var errorsJson = System.Text.Json.JsonSerializer.Serialize(errors, new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                    Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
                });
                Response.Headers["X-Calcpad-Errors"] = Uri.EscapeDataString(errorsJson);

                // Surfaces #ProjectPath/#LibraryPath even when declared inside an #include —
                // the client would otherwise have to re-scan only its own entry file's text.
                var pathRootsJson = System.Text.Json.JsonSerializer.Serialize(new { projectPath, libraryPath });
                Response.Headers["X-Calcpad-PathRoots"] = Uri.EscapeDataString(pathRootsJson);
                return Content(htmlResult, "text/html");
            }
            catch (OperationCanceledException)
            {
                // Superseded or the client gave up — expected under rapid tab-switching, not an error.
                return StatusCode(499);
            }
            catch (Exception ex)
            {
                FileLogger.LogError("Convert request failed", ex);
                return StatusCode(500, $"Error processing Calcpad content: {ex.Message}");
            }
        }

        /// <summary>
        /// Decodes a compiled <c>.cpdz</c> worksheet to its source text. Accepts both the
        /// plain deflate form and the composite archive that bundles images.
        /// </summary>
        [HttpPost("cpdz/decode")]
        public IActionResult DecodeCpdz([FromBody] CpdzDecodeRequest request)
        {
            try
            {
                var result = CpdzCodec.Decode(Convert.FromBase64String(request.Data));
                return Ok(new CpdzDecodeResponse { Content = result.Text, Composite = result.IsComposite });
            }
            catch (Exception ex)
            {
                FileLogger.LogError("cpdz decode failed", ex);
                return BadRequest(new { error = "Not a valid .cpdz file", message = ex.Message });
            }
        }

        /// <summary>
        /// Encodes source text as a <c>.cpdz</c> worksheet. Pass the file's current bytes
        /// as <c>Original</c> when overwriting, so a composite archive keeps its images.
        /// </summary>
        [HttpPost("cpdz/encode")]
        public IActionResult EncodeCpdz([FromBody] CpdzEncodeRequest request)
        {
            try
            {
                var original = string.IsNullOrEmpty(request.Original)
                    ? null
                    : Convert.FromBase64String(request.Original);
                var bytes = CpdzCodec.Encode(request.Content, original);
                return Ok(new CpdzEncodeResponse { Data = Convert.ToBase64String(bytes) });
            }
            catch (Exception ex)
            {
                FileLogger.LogError("cpdz encode failed", ex);
                return StatusCode(500, new { error = "Failed to encode .cpdz", message = ex.Message });
            }
        }

        /// <summary>
        /// Rewrites a worksheet into the self-contained form a compiled <c>.cpdz</c> needs:
        /// macros and includes expanded, <c>#read</c> data inlined, an included file's image
        /// paths made absolute. The caller embeds the images and encodes the result. Reading
        /// a data file that isn't there is an error, not a warning — the worksheet would
        /// otherwise be handed out broken.
        /// </summary>
        [HttpPost("portable/bundle")]
        public async Task<IActionResult> BundlePortable([FromBody] PortableBundleRequest request, CancellationToken cancellationToken)
        {
            try
            {
                using var _ = await _parserGate.AcquireAsync(cancellationToken);

                var result = PortableWorksheet.Build(request.Content, request.SourceFilePath);
                if (result.Errors.Count > 0)
                    return BadRequest(new { error = "The worksheet is not self-contained", messages = result.Errors });

                return Ok(new PortableBundleResponse { Content = result.Content });
            }
            catch (OperationCanceledException)
            {
                return StatusCode(499);
            }
            catch (Exception ex)
            {
                FileLogger.LogError("portable bundle failed", ex);
                return StatusCode(500, new { error = "Failed to bundle the worksheet", message = ex.Message });
            }
        }

        /// <summary>
        /// Packs a worksheet and the files it references into a ZIP that stays text: the document
        /// keeps its directives and only their paths change, each rewritten to point into a folder
        /// beside it holding the file it named. The middle ground between a worksheet that only
        /// runs where it was written and a compiled one that cannot be read.
        /// </summary>
        [HttpPost("portable/package")]
        public IActionResult PackagePortable([FromBody] PortableBundleRequest request)
        {
            try
            {
                var result = PortablePackage.Build(request.Content, request.SourceFilePath);
                if (result.Zip is null)
                    return BadRequest(new { error = "The worksheet cannot be packaged", messages = result.Errors });

                return Ok(new PortablePackageResponse
                {
                    Data = Convert.ToBase64String(result.Zip),
                    Name = result.Name,
                    RefsFolder = result.RefsFolder,
                    Bundled = result.Bundled,
                });
            }
            catch (Exception ex)
            {
                FileLogger.LogError("portable package failed", ex);
                return StatusCode(500, new { error = "Failed to package the worksheet", message = ex.Message });
            }
        }

        private static HighlightResponse MapTokensToResponse(IEnumerable<Token> tokens, bool includeText)
        {
            return new HighlightResponse
            {
                Tokens = tokens.Select(t => new HighlightToken
                {
                    Line = t.Line,
                    Column = t.Column,
                    Length = t.Length,
                    Type = t.Type.ToString(),
                    TypeId = (int)t.Type,
                    Text = includeText ? t.Text : null
                }).ToList()
            };
        }

        private string ProcessDataTextLinks(string html)
        {
            // Replace links with data-text attribute to point to actual line anchors
            // Pattern: <a href="#0" data-text="123">
            // Replace with: <a href="#line-123" data-text="123">
            var pattern = @"<a\s+href=""#0""\s+data-text=""(\d+)""";
            var replacement = @"<a href=""#line-$1"" data-text=""$1""";
            return System.Text.RegularExpressions.Regex.Replace(html, pattern, replacement);
        }

        [HttpGet("sample")]
        public IActionResult GetSample()
        {
            var sampleContent = _calcpadService.GetSampleContent();
            return Ok(new CalcpadRequest { Content = sampleContent });
        }

        // Debug-only: intentionally crashes the server to verify which crash paths
        // are picked up by FileLogger / AppDomain.UnhandledException / TaskScheduler.
        // Different modes exercise different failure paths because not all of them
        // route through the same handlers (e.g. StackOverflow and FailFast bypass
        // managed exception handling entirely).
        [HttpGet("debug-crash")]
        public IActionResult DebugCrash([FromQuery] string mode = "background-thread")
        {
            FileLogger.LogInfo("debug-crash invoked", $"mode={mode}");

            switch (mode)
            {
                case "throw":
                    // Caught by ASP.NET Core's exception middleware; process keeps running.
                    // Useful baseline to confirm the request pipeline logs at all.
                    throw new InvalidOperationException("debug-crash: synchronous controller throw");

                case "background-thread":
                    // Unhandled exception on a non-pool thread -> AppDomain.UnhandledException -> process terminates.
                    new Thread(() =>
                    {
                        Thread.Sleep(100);
                        throw new InvalidOperationException("debug-crash: background thread");
                    })
                    { IsBackground = false }.Start();
                    return Accepted(new { mode, note = "Server should terminate shortly via AppDomain.UnhandledException." });

                case "unobserved-task":
                    // Fire-and-forget Task whose exception is never observed.
                    // Surfaces via TaskScheduler.UnobservedTaskException only after GC finalizes the Task.
                    _ = Task.Run(() => throw new InvalidOperationException("debug-crash: unobserved task"));
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    return Accepted(new { mode, note = "Unobserved task exception raised; process continues unless rethrown." });

                case "stackoverflow":
                    // StackOverflowException cannot be caught and bypasses UnhandledException handlers entirely.
                    // This is the "silent crash" case — expect no FileLogger entry beyond the INFO above.
                    return Recurse(0);

                case "accessviolation":
                    // Corrupted-state exception; by default not delivered to managed handlers.
                    throw new AccessViolationException("debug-crash: simulated AV");

                case "failfast":
                    // Environment.FailFast bypasses AppDomain.UnhandledException. The message is written
                    // to the Windows Event Log / stderr but FileLogger handlers do NOT run.
                    Environment.FailFast("debug-crash: FailFast invoked");
                    return StatusCode(500); // unreachable

                case "exit":
                    // Clean exit (no exception). Useful to confirm graceful-shutdown logging.
                    Task.Run(async () => { await Task.Delay(100); Environment.Exit(1); });
                    return Accepted(new { mode, note = "Environment.Exit(1) scheduled." });

                default:
                    return BadRequest(new
                    {
                        error = $"Unknown mode '{mode}'",
                        modes = new[] { "throw", "background-thread", "unobserved-task", "stackoverflow", "accessviolation", "failfast", "exit" }
                    });
            }
        }

        private static IActionResult Recurse(int depth) => Recurse(depth + 1);

        /// <summary>
        /// Generate PDF from HTML content using Playwright and PDFsharp.
        /// </summary>
        [HttpPost("pdf")]
        public async Task<IActionResult> GeneratePdf([FromBody] PdfGenerateRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.Html))
                    return BadRequest(new { error = "HTML content is required" });

                FileLogger.LogInfo("PDF generation request received", $"HTML length: {request.Html.Length}");

                var pdfBytes = await _pdfService.GeneratePdfAsync(request.Html, request.Options, request.BrowserPath);

                FileLogger.LogInfo("PDF generated successfully", $"Size: {pdfBytes.Length} bytes");

                return File(pdfBytes, "application/pdf", "document.pdf");
            }
            catch (Exception ex)
            {
                FileLogger.LogError("PDF generation failed", ex);
                return StatusCode(500, new { error = "PDF generation failed", message = ex.Message });
            }
        }

        [HttpGet("pdf/health")]
        public IActionResult PdfHealth()
        {
            return Ok(new { status = "ok", service = "calcpad-pdf", version = "2.0.0" });
        }

        /// <summary>
        /// Convert calcpad → HTML → DOCX (Word) using Calcpad.OpenXml.OpenXmlWriter.
        /// Returns the .docx bytes; the frontend handles the download / save dialog.
        /// </summary>
        [HttpPost("docx")]
        public async Task<IActionResult> GenerateDocx([FromBody] CalcpadRequest request, CancellationToken cancellationToken)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.Content))
                    return BadRequest(new { error = "Content is required" });

                // captureOpenXml=true tells the parser to emit OMML so equations render as
                // native Word math instead of empty <m:oMath/>. A Word document is a file,
                // never a navigable preview, so line anchors are always off - but ForPrint
                // and UiOverrides come from the request, so the caller chooses between a
                // report (#pre hidden, entered #UI values applied) and the preview layout.
                // Only the Convert call itself needs the gate — the OpenXML write below is
                // CPU-bound XML generation, not shared parser state.
                string html;
                IReadOnlyList<string> openXmlExpressions;
                using (await _parserGate.AcquireAsync(cancellationToken))
                {
                    (html, openXmlExpressions, _, _, _) = _calcpadService.Convert(
                        request.Content, request.Settings, request.ForceUnwrappedCode, request.Theme, request.SourceFilePath,
                        forPrint: request.ForPrint, captureOpenXml: true, enableUi: false, uiOverrides: request.UiOverrides,
                        debug: false, cancellationToken: cancellationToken);
                }

                using var ms = new MemoryStream();
                var writer = new OpenXmlWriter(openXmlExpressions.ToList());
                writer.Convert(html, ms);
                var bytes = ms.ToArray();

                FileLogger.LogInfo("DOCX generated successfully", $"Size: {bytes.Length} bytes");
                return File(
                    bytes,
                    "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                    "document.docx");
            }
            catch (OperationCanceledException)
            {
                return StatusCode(499);
            }
            catch (Exception ex)
            {
                FileLogger.LogError("DOCX generation failed", ex);
                return StatusCode(500, new { error = "DOCX generation failed", message = ex.Message });
            }
        }

        /// <summary>
        /// Get syntax highlighting tokens for Calcpad source code.
        /// Returns tokens with line/column positions and types for frontend colorization.
        /// </summary>
        [HttpPost("highlight")]
        public IActionResult GetHighlightTokens([FromBody] HighlightRequest request, CancellationToken cancellationToken)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.Content))
                    return BadRequest("Content is required");

                // A stale request whose client already gave up shouldn't do this work at all.
                cancellationToken.ThrowIfCancellationRequested();

                FileLogger.LogInfo("Highlight request received", $"Length: {request.Content.Length}");

                var tokenizer = new CalcpadTokenizer();

                // Run content resolution to collect macro info from includes (resolver reads
                // referenced files from disk via SourceFilePath when no in-memory cache is supplied).
                if (!string.IsNullOrEmpty(request.SourceFilePath))
                {
                    var resolver = new ContentResolver();
                    var staged = resolver.GetStagedContent(request.Content, sourceFilePath: request.SourceFilePath);
                    tokenizer.SetMacroCommentParameters(
                        staged.Stage2.MacroCommentParameters,
                        staged.Stage2.MacroParameterOrder,
                        staged.Stage2.MacroBodies);
                }

                var result = tokenizer.Tokenize(request.Content);
                return Ok(MapTokensToResponse(result.Tokens, request.IncludeText));
            }
            catch (OperationCanceledException)
            {
                return StatusCode(499);
            }
            catch (Exception ex)
            {
                FileLogger.LogError("Highlight request failed", ex);
                return StatusCode(500, $"Error tokenizing content: {ex.Message}");
            }
        }

        /// <summary>
        /// Get syntax highlighting tokens for a single line (for incremental updates).
        /// </summary>
        [HttpPost("highlight-line")]
        public IActionResult GetHighlightTokensForLine([FromBody] HighlightLineRequest request)
        {
            try
            {
                if (request.Line == null)
                    return BadRequest("Line content is required");

                var tokenizer = new CalcpadTokenizer();
                var result = tokenizer.TokenizeSingleLine(request.Line, request.LineNumber);
                return Ok(MapTokensToResponse(result.Tokens, request.IncludeText));
            }
            catch (Exception ex)
            {
                FileLogger.LogError("Highlight line request failed", ex);
                return StatusCode(500, $"Error tokenizing line: {ex.Message}");
            }
        }

        /// <summary>
        /// Lint Calcpad source code and return diagnostics (errors and warnings).
        /// </summary>
        [HttpPost("lint")]
        public IActionResult LintContent([FromBody] LintRequest request, CancellationToken cancellationToken)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.Content))
                    return BadRequest("Content is required");

                cancellationToken.ThrowIfCancellationRequested();

                FileLogger.LogInfo("Lint request received", "Length: " + request.Content.Length);

                var resolver = new ContentResolver();
                var linter = new CalcpadLinter();

                var staged = resolver.GetStagedContent(request.Content, sourceFilePath: request.SourceFilePath);

                // Extract LintIgnore regions from raw source, then lint with suppression
                var ignoreRegions = _lintIgnoreRegionParser.ExtractRegions(request.Content);
                var lintResult = linter.Lint(staged, ignoreRegions);

                var response = new LintResponse
                {
                    ErrorCount = lintResult.ErrorCount,
                    WarningCount = lintResult.WarningCount,
                    Diagnostics = lintResult.Diagnostics.Select(d => new LintDiagnostic
                    {
                        Line = d.Line,
                        Column = d.Column,
                        EndColumn = d.EndColumn,
                        Code = d.Code,
                        Message = d.Message,
                        Severity = d.Severity == Highlighter.Linter.Models.LinterSeverity.Error ? "error"
                            : d.Severity == Highlighter.Linter.Models.LinterSeverity.Information ? "information" : "warning",
                        SeverityId = (int)d.Severity,
                        Source = d.Source
                    }).ToList()
                };

                return Ok(response);
            }
            catch (OperationCanceledException)
            {
                return StatusCode(499);
            }
            catch (Exception ex)
            {
                FileLogger.LogError("Lint request failed", ex);
                return StatusCode(500, "Error linting content: " + ex.Message);
            }
        }

        /// <summary>
        /// Get detailed definitions (macros, functions, variables, custom units) from Calcpad source code.
        /// Returns type information, parameters, return types, and source locations.
        /// </summary>
        [HttpPost("definitions")]
        public IActionResult GetDefinitions([FromBody] DefinitionsRequest request, CancellationToken cancellationToken)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.Content))
                    return BadRequest("Content is required");

                cancellationToken.ThrowIfCancellationRequested();

                FileLogger.LogInfo("Definitions request received", "Length: " + request.Content.Length);

                var resolver = new ContentResolver();
                var staged = resolver.GetStagedContent(request.Content, sourceFilePath: request.SourceFilePath);

                var typeTracker = staged.Stage3.TypeTracker;

                var response = new DefinitionsResponse
                {
                    Macros = staged.Stage2.MacroDefinitions.Select(m => new MacroDefinitionDto
                    {
                        Name = m.Name,
                        Parameters = m.Params ?? new List<string>(),
                        IsMultiline = m.Content.Count > 1 || (m.Content.Count == 1 && string.IsNullOrWhiteSpace(m.Content[0])),
                        Content = m.Content,
                        LineNumber = m.LineNumber,
                        Source = m.Source ?? "local",
                        SourceFile = m.SourceFile,
                        Description = m.Description,
                        ParamTypes = m.ParamTypes,
                        ParamDescriptions = m.ParamDescriptions
                    }).ToList(),

                    Functions = staged.Stage3.FunctionsWithParams.Select(f =>
                    {
                        var funcInfo = typeTracker.Functions.GetValueOrDefault(f.Name);
                        return new FunctionDefinitionDto
                        {
                            Name = f.Name,
                            Parameters = f.Params ?? new List<string>(),
                            Expression = f.Expression,
                            ReturnType = funcInfo?.ReturnType.ToString() ?? "Unknown",
                            ReturnTypeId = (int)(funcInfo?.ReturnType ?? CalcpadType.Unknown),
                            HasCommandBlock = f.CommandBlock != null,
                            CommandBlockType = f.CommandBlock?.BlockType,
                            CommandBlockStatements = f.CommandBlock?.Statements,
                            LineNumber = f.LineNumber,
                            Source = f.Source ?? "local",
                            SourceFile = f.SourceFile,
                            Description = f.Description,
                            ParamTypes = f.ParamTypes,
                            ParamDescriptions = f.ParamDescriptions
                        };
                    }).ToList(),

                    Variables = staged.Stage3.VariablesWithDefinitions.Select(v =>
                    {
                        var varInfo = typeTracker.Variables.GetValueOrDefault(v.Name);
                        return new VariableDefinitionDto
                        {
                            Name = v.Name,
                            Expression = v.Definition,
                            Type = varInfo?.Type.ToString() ?? "Unknown",
                            TypeId = (int)(varInfo?.Type ?? CalcpadType.Unknown),
                            LineNumber = v.LineNumber,
                            Source = v.Source ?? "local",
                            SourceFile = v.SourceFile,
                            Description = v.Description
                        };
                    }).ToList(),

                    CustomUnits = staged.Stage3.CustomUnits.Select(u => new CustomUnitDefinitionDto
                    {
                        Name = u.Name,
                        Expression = u.Definition,
                        LineNumber = u.LineNumber,
                        Source = u.Source ?? "local",
                        SourceFile = u.SourceFile,
                        Description = u.Description
                    }).ToList()
                };

                return Ok(response);
            }
            catch (OperationCanceledException)
            {
                return StatusCode(499);
            }
            catch (Exception ex)
            {
                FileLogger.LogError("Definitions request failed", ex);
                return StatusCode(500, "Error getting definitions: " + ex.Message);
            }
        }

        /// <summary>
        /// Resolve a cursor position to the user-defined symbol under it and
        /// return all of that symbol's occurrences. Single round-trip that
        /// powers go-to-definition, find-all-references, and rename across
        /// every editor integration — the matching heuristic lives here so
        /// the clients don't each re-implement it.
        /// </summary>
        [HttpPost("symbol-at-position")]
        public IActionResult SymbolAtPosition([FromBody] SymbolAtPositionRequest request, CancellationToken cancellationToken)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.Content))
                    return BadRequest("Content is required");

                cancellationToken.ThrowIfCancellationRequested();

                var resolver = new ContentResolver();
                var staged = resolver.GetStagedContent(request.Content, sourceFilePath: request.SourceFilePath);
                var hit = SymbolResolver.ResolveSymbolAt(staged.Stage3, request.Line, request.Column);
                if (hit == null) return Ok(null);

                SymbolLocationDto ToDto(Calcpad.Highlighter.ContentResolution.SymbolLocation loc) => new SymbolLocationDto
                {
                    Line = loc.Line,
                    Column = loc.Column,
                    Length = loc.Length,
                    Source = loc.Source,
                    SourceFile = loc.SourceFile,
                    IsAssignment = loc.IsAssignment
                };

                var response = new SymbolAtPositionResponse
                {
                    SymbolName = hit.Name,
                    Kind = hit.Kind.ToString().ToLowerInvariant(),
                    Locations = hit.Locations.Select(ToDto).ToList()
                };
                return Ok(response);
            }
            catch (OperationCanceledException)
            {
                return StatusCode(499);
            }
            catch (Exception ex)
            {
                FileLogger.LogError("Symbol-at-position request failed", ex);
                return StatusCode(500, "Error resolving symbol: " + ex.Message);
            }
        }

        /// <summary>
        /// Re-indent Calcpad source by tracking control-block depth across
        /// #if/#else/#end if, #for/#while/#repeat/#loop, and multiline #def/#end def.
        /// Only leading whitespace is adjusted; line endings and content are preserved.
        /// </summary>
        [HttpPost("prettify")]
        public IActionResult PrettifyContent([FromBody] PrettifyRequest request)
        {
            try
            {
                if (request.Content == null)
                    return BadRequest("Content is required");

                var options = new PrettifierOptions
                {
                    IndentUnit = string.IsNullOrEmpty(request.IndentUnit) ? "\t" : request.IndentUnit,
                    TrimTrailingWhitespace = request.TrimTrailingWhitespace
                };

                var formatted = CalcpadPrettifier.Prettify(request.Content, options);
                return Ok(new PrettifyResponse { Content = formatted });
            }
            catch (Exception ex)
            {
                FileLogger.LogError("Prettify request failed", ex);
                return StatusCode(500, "Error prettifying content: " + ex.Message);
            }
        }

        /// <summary>
        /// Get all available snippets for autocomplete/intellisense.
        /// Returns simplified snippet definitions with insert text, descriptions, and categories.
        /// </summary>
        [HttpGet("snippets")]
        public IActionResult GetSnippets([FromQuery] string? category = null)
        {
            try
            {
                FileLogger.LogInfo("Snippets request received", "Category: " + (category ?? "all"));

                SnippetItem[] snippets;
                if (string.IsNullOrWhiteSpace(category))
                {
                    snippets = SnippetRegistry.GetAllSnippetsArray();
                }
                else
                {
                    snippets = SnippetRegistry.GetSnippetsByCategory(category);
                }

                // Map to simplified DTOs for the API response
                var simplifiedSnippets = snippets.Select(s => new SnippetDto
                {
                    Insert = s.Insert,
                    Description = s.Description,
                    Documentation = s.Documentation,
                    Example = s.Example,
                    Label = s.Label,
                    Category = s.Category,
                    QuickType = s.QuickType,
                    KeywordType = s.KeywordType,
                    ReturnType = s.ReturnType?.ToString(),
                    ReturnTypeDescription = s.ReturnTypeDescription,
                    IsElementWise = s.IsElementWise,
                    AcceptsAnyCount = s.AcceptsAnyCount,
                    Parameters = s.Parameters?.Select(p => new SnippetParameterDto
                    {
                        Name = p.Name,
                        Description = p.Description,
                        Type = p.Type.ToString(),
                        TypeDescription = p.TypeDescription,
                        IsOptional = p.IsOptional,
                        IsVariadic = p.IsVariadic
                    }).ToArray()
                }).ToArray();

                return Ok(new SnippetsResponse
                {
                    Count = simplifiedSnippets.Length,
                    Snippets = simplifiedSnippets
                });
            }
            catch (Exception ex)
            {
                FileLogger.LogError("Snippets request failed", ex);
                return StatusCode(500, "Error getting snippets: " + ex.Message);
            }
        }
    }

    public class SnippetsResponse
    {
        /// <summary>Total number of snippets returned</summary>
        public int Count { get; set; }

        /// <summary>Array of snippet definitions</summary>
        public SnippetDto[] Snippets { get; set; } = [];
    }

    public class SnippetDto
    {
        /// <summary>Text to insert (use '§' as cursor placeholder)</summary>
        public string Insert { get; set; } = string.Empty;

        /// <summary>Short label shown in tooltips and completion detail (e.g. "Sine")</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>Long-form Markdown description for hover/completion docstrings. Null when not authored.</summary>
        public string? Documentation { get; set; }

        /// <summary>Optional Calcpad usage example, rendered as a fenced code block in hover/completion docs.</summary>
        public string? Example { get; set; }

        /// <summary>Optional display label (defaults to description if null)</summary>
        public string? Label { get; set; }

        /// <summary>Category path (e.g., "Functions/Trigonometric")</summary>
        public string Category { get; set; } = string.Empty;

        /// <summary>Quick typing shortcut without ~ prefix (e.g., "a" means ~a → α)</summary>
        public string? QuickType { get; set; }

        /// <summary>Keyword classification ("Function", "Keyword", "Command", "Constant", "Unit", "Operator", "Setting", "ControlBlockKeyword", "EndKeyword"). Null for UI-only snippets.</summary>
        public string? KeywordType { get; set; }

        /// <summary>Return type name from the CalcpadType enum (e.g. "Scalar", "Vector"). Null for non-functions.</summary>
        public string? ReturnType { get; set; }

        /// <summary>Human-readable description of the return value (e.g. "Angle in radians").</summary>
        public string? ReturnTypeDescription { get; set; }

        /// <summary>Whether the function operates element-wise on vectors/matrices.</summary>
        public bool IsElementWise { get; set; }

        /// <summary>When true, parameter count validation is skipped (e.g. switch, gcd, lcm).</summary>
        public bool AcceptsAnyCount { get; set; }

        /// <summary>Parameter info for functions (null for non-functions)</summary>
        public SnippetParameterDto[]? Parameters { get; set; }
    }

    public class SnippetParameterDto
    {
        /// <summary>Parameter name (e.g., "x", "M", "v")</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Description of the parameter's purpose</summary>
        public string? Description { get; set; }

        /// <summary>Expected type from the ParameterType enum (e.g. "Scalar", "Vector", "Matrix", "Any").</summary>
        public string? Type { get; set; }

        /// <summary>Human-readable type description (e.g. "Angle in radians"). Falls back to Type when null.</summary>
        public string? TypeDescription { get; set; }

        /// <summary>Whether this parameter is optional.</summary>
        public bool IsOptional { get; set; }

        /// <summary>Whether this parameter is variadic (the type applies to all remaining arguments).</summary>
        public bool IsVariadic { get; set; }
    }

    public class PrettifyRequest
    {
        /// <summary>The Calcpad source code to prettify</summary>
        public string Content { get; set; } = string.Empty;

        /// <summary>String emitted per indent level. Defaults to a single tab when null/empty.</summary>
        public string? IndentUnit { get; set; }

        /// <summary>Trim trailing whitespace on each line. Default true.</summary>
        public bool TrimTrailingWhitespace { get; set; } = true;
    }

    public class PrettifyResponse
    {
        /// <summary>Prettified source code</summary>
        public string Content { get; set; } = string.Empty;
    }

    public class CalcpadRequest
    {
        public string Content { get; set; } = string.Empty;
        public Settings? Settings { get; set; }
        public bool ForceUnwrappedCode { get; set; } = false;
        public string Theme { get; set; } = "light"; // "light" or "dark"

        /// <summary>
        /// When true, resolves <c>#pre</c>/<c>#post</c> directives for print output
        /// (<c>#pre</c> content is hidden, <c>#post</c> content is shown). The frontend
        /// should set this for renders destined for PDF.
        /// </summary>
        public bool ForPrint { get; set; } = false;

        /// <summary>
        /// Full path of the source file on disk. Used to resolve relative
        /// #include and #read paths against the parent file's directory.
        /// </summary>
        public string? SourceFilePath { get; set; }

        /// <summary>
        /// When true, <c>#UI</c> lines render as interactive controls and <c>#post</c>
        /// content is hidden. When false the document renders as a report, exactly as
        /// it would without the <c>#UI</c> keywords.
        /// </summary>
        public bool EnableUi { get; set; } = false;

        /// <summary>
        /// Values entered into <c>#UI</c> controls, keyed by the control identity the
        /// preview reports in <c>data-ui-var</c> (e.g. <c>"L:1"</c>). Applied in both
        /// modes, so a report shows the values that were actually entered.
        /// </summary>
        public Dictionary<string, string>? UiOverrides { get; set; }

        /// <summary>
        /// Emits per-line anchors and the error-summary boxes the preview uses for line
        /// links. Defaults to the opposite of <see cref="ForPrint"/>. Set it explicitly to
        /// break that pairing: the on-screen report wants the print layout <em>with</em>
        /// links, and every export wants a rendering <em>without</em> them.
        /// </summary>
        public bool? IncludeLineAnchors { get; set; }
    }

    public class CpdzDecodeRequest
    {
        /// <summary>Base64 of the file's raw bytes.</summary>
        public string Data { get; set; } = string.Empty;
    }

    public class CpdzDecodeResponse
    {
        /// <summary>The decoded Calcpad source.</summary>
        public string Content { get; set; } = string.Empty;

        /// <summary>
        /// True when the file is a composite archive bundling images. Its bytes must be
        /// passed back as <c>Original</c> on encode, or those images are lost.
        /// </summary>
        public bool Composite { get; set; }
    }

    public class CpdzEncodeRequest
    {
        /// <summary>The Calcpad source to compile.</summary>
        public string Content { get; set; } = string.Empty;

        /// <summary>
        /// Base64 of the file being overwritten, when there is one. A composite archive
        /// keeps every entry but the code, so bundled images survive the save.
        /// </summary>
        public string? Original { get; set; }
    }

    public class CpdzEncodeResponse
    {
        /// <summary>Base64 of the encoded file's bytes.</summary>
        public string Data { get; set; } = string.Empty;
    }

    public class PortableBundleRequest
    {
        /// <summary>The Calcpad source to bundle.</summary>
        public string Content { get; set; } = string.Empty;

        /// <summary>
        /// Full path of the source file on disk. Relative <c>#include</c> and <c>#read</c>
        /// paths resolve against its folder, as they do when the worksheet runs.
        /// </summary>
        public string? SourceFilePath { get; set; }
    }

    public class PortableBundleResponse
    {
        /// <summary>
        /// The bundled source: no includes, no data files, and images left as absolute paths
        /// for the caller to embed.
        /// </summary>
        public string Content { get; set; } = string.Empty;
    }

    public class PortablePackageResponse
    {
        /// <summary>Base64 of the archive's bytes.</summary>
        public string Data { get; set; } = string.Empty;

        /// <summary>Suggested file name, taken from the document's own.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>The folder inside the archive holding every referenced file.</summary>
        public string RefsFolder { get; set; } = string.Empty;

        /// <summary>The bundled entries, for reporting what was packed.</summary>
        public IReadOnlyList<string> Bundled { get; set; } = [];
    }

    public class HighlightRequest
    {
        /// <summary>The Calcpad source code to tokenize</summary>
        public string Content { get; set; } = string.Empty;

        /// <summary>Whether to include the token text in the response (default: false to reduce payload size)</summary>
        public bool IncludeText { get; set; } = false;

        /// <summary>
        /// Full path of the source file on disk.
        /// </summary>
        public string? SourceFilePath { get; set; }
    }

    public class HighlightLineRequest
    {
        /// <summary>The line content to tokenize</summary>
        public string Line { get; set; } = string.Empty;

        /// <summary>The zero-based line number</summary>
        public int LineNumber { get; set; } = 0;

        /// <summary>Whether to include the token text in the response</summary>
        public bool IncludeText { get; set; } = false;
    }

    public class HighlightResponse
    {
        /// <summary>List of tokens with position and type information</summary>
        public List<HighlightToken> Tokens { get; set; } = new();
    }

    public class HighlightToken
    {
        /// <summary>Zero-based line number</summary>
        public int Line { get; set; }

        /// <summary>Zero-based column (character offset from start of line)</summary>
        public int Column { get; set; }

        /// <summary>Length of the token in characters</summary>
        public int Length { get; set; }

        /// <summary>Token type name for display/debugging</summary>
        public string Type { get; set; } = string.Empty;

        /// <summary>
        /// Token type ID for efficient frontend processing.
        /// 0=None, 1=Const, 2=Units, 3=Operator, 4=Variable, 5=Function,
        /// 6=Keyword, 7=Command, 8=Bracket, 9=Comment, 10=Tag, 11=Input,
        /// 12=Include, 13=Macro, 14=HtmlComment, 15=Format
        /// </summary>
        public int TypeId { get; set; }

        /// <summary>The actual token text (only included if IncludeText is true)</summary>
        public string? Text { get; set; }
    }

    public class LintRequest
    {
        /// <summary>The Calcpad source code to lint</summary>
        public string Content { get; set; } = string.Empty;

        /// <summary>
        /// Full path of the source file on disk.
        /// </summary>
        public string? SourceFilePath { get; set; }
    }

    public class LintResponse
    {
        /// <summary>Total number of errors</summary>
        public int ErrorCount { get; set; }

        /// <summary>Total number of warnings</summary>
        public int WarningCount { get; set; }

        /// <summary>List of diagnostics (errors and warnings)</summary>
        public List<LintDiagnostic> Diagnostics { get; set; } = new();
    }

    public class LintDiagnostic
    {
        /// <summary>Zero-based line number</summary>
        public int Line { get; set; }

        /// <summary>Zero-based column (start position)</summary>
        public int Column { get; set; }

        /// <summary>Zero-based end column position</summary>
        public int EndColumn { get; set; }

        /// <summary>Error code (e.g., "CPD-3301")</summary>
        public string Code { get; set; } = string.Empty;

        /// <summary>Human-readable error/warning message</summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>Severity name: "error" or "warning"</summary>
        public string Severity { get; set; } = string.Empty;

        /// <summary>Severity ID: 0=Error, 1=Warning</summary>
        public int SeverityId { get; set; }

        /// <summary>Source of the diagnostic (default: "Calcpad Linter")</summary>
        public string Source { get; set; } = "Calcpad Linter";
    }

    public class DefinitionsRequest
    {
        /// <summary>The Calcpad source code to analyze</summary>
        public string Content { get; set; } = string.Empty;

        /// <summary>Full path of the source file on disk.</summary>
        public string? SourceFilePath { get; set; }
    }

    public class DefinitionsResponse
    {
        /// <summary>All macro definitions (inline and multiline)</summary>
        public List<MacroDefinitionDto> Macros { get; set; } = new();

        /// <summary>All function definitions</summary>
        public List<FunctionDefinitionDto> Functions { get; set; } = new();

        /// <summary>All variable definitions</summary>
        public List<VariableDefinitionDto> Variables { get; set; } = new();

        /// <summary>All custom unit definitions</summary>
        public List<CustomUnitDefinitionDto> CustomUnits { get; set; } = new();
    }

    public class MacroDefinitionDto
    {
        /// <summary>Macro name (including $ suffix)</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Parameter names</summary>
        public List<string> Parameters { get; set; } = new();

        /// <summary>True if multiline macro (#def...#end def), false if inline macro</summary>
        public bool IsMultiline { get; set; }

        /// <summary>Macro content lines (single line for inline, multiple for multiline)</summary>
        public List<string> Content { get; set; } = new();

        /// <summary>Zero-based line number where the macro is defined</summary>
        public int LineNumber { get; set; }

        /// <summary>Source: "local" or "include"</summary>
        public string Source { get; set; } = "local";

        /// <summary>Source file path if from include, null otherwise</summary>
        public string? SourceFile { get; set; }

        /// <summary>User-provided description from a metadata comment</summary>
        public string? Description { get; set; }

        /// <summary>User-provided type hints per parameter</summary>
        public List<string>? ParamTypes { get; set; }

        /// <summary>User-provided descriptions per parameter</summary>
        public List<string>? ParamDescriptions { get; set; }
    }

    public class FunctionDefinitionDto
    {
        /// <summary>Function name</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Parameter names</summary>
        public List<string> Parameters { get; set; } = new();

        /// <summary>Function body expression (right side of =)</summary>
        public string? Expression { get; set; }

        /// <summary>
        /// Inferred return type name.
        /// Values: Unknown, Value, Vector, Matrix, CustomUnit, Function, InlineMacro, MultilineMacro, Various
        /// </summary>
        public string ReturnType { get; set; } = "Unknown";

        /// <summary>
        /// Return type ID for efficient frontend processing.
        /// 0=Unknown, 1=Value, 2=Vector, 3=Matrix, 4=CustomUnit, 5=Function, 6=InlineMacro, 7=MultilineMacro, 8=Various
        /// </summary>
        public int ReturnTypeId { get; set; }

        /// <summary>True if this function uses a command block ($Inline, $Block, $While)</summary>
        public bool HasCommandBlock { get; set; }

        /// <summary>Command block type: "Inline", "Block", or "While" (null if no command block)</summary>
        public string? CommandBlockType { get; set; }

        /// <summary>Statements inside the command block, split by semicolon (null if no command block)</summary>
        public List<string>? CommandBlockStatements { get; set; }

        /// <summary>Zero-based line number where the function is defined</summary>
        public int LineNumber { get; set; }

        /// <summary>Source: "local" or "include"</summary>
        public string Source { get; set; } = "local";

        /// <summary>Source file path if from include, null otherwise</summary>
        public string? SourceFile { get; set; }

        /// <summary>User-provided description from a metadata comment</summary>
        public string? Description { get; set; }

        /// <summary>User-provided type hints per parameter</summary>
        public List<string>? ParamTypes { get; set; }

        /// <summary>User-provided descriptions per parameter</summary>
        public List<string>? ParamDescriptions { get; set; }
    }

    public class VariableDefinitionDto
    {
        /// <summary>Variable name</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Initial expression (right side of first assignment)</summary>
        public string? Expression { get; set; }

        /// <summary>
        /// Inferred type name.
        /// Values: Unknown, Value, Vector, Matrix, Various
        /// </summary>
        public string Type { get; set; } = "Unknown";

        /// <summary>
        /// Type ID for efficient frontend processing.
        /// 0=Unknown, 1=Value, 2=Vector, 3=Matrix, 8=Various
        /// </summary>
        public int TypeId { get; set; }

        /// <summary>Zero-based line number where the variable is first defined</summary>
        public int LineNumber { get; set; }

        /// <summary>Source: "local" or "include"</summary>
        public string Source { get; set; } = "local";

        /// <summary>Source file path if from include, null otherwise</summary>
        public string? SourceFile { get; set; }

        /// <summary>User-provided description from a metadata comment</summary>
        public string? Description { get; set; }
    }

    public class CustomUnitDefinitionDto
    {
        /// <summary>Unit name (without leading dot)</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Unit definition expression</summary>
        public string? Expression { get; set; }

        /// <summary>Zero-based line number where the unit is defined</summary>
        public int LineNumber { get; set; }

        /// <summary>Source: "local" or "include"</summary>
        public string Source { get; set; } = "local";

        /// <summary>Source file path if from include, null otherwise</summary>
        public string? SourceFile { get; set; }

        /// <summary>User-provided description from a metadata comment</summary>
        public string? Description { get; set; }
    }

    public class SymbolAtPositionRequest
    {
        /// <summary>The Calcpad source code to analyze</summary>
        public string Content { get; set; } = string.Empty;

        /// <summary>Zero-based line of the cursor in the original source</summary>
        public int Line { get; set; }

        /// <summary>Zero-based column of the cursor in the original source</summary>
        public int Column { get; set; }

        /// <summary>Full path of the source file on disk (used to resolve #include).</summary>
        public string? SourceFilePath { get; set; }
    }

    public class SymbolAtPositionResponse
    {
        /// <summary>The resolved symbol's name</summary>
        public string SymbolName { get; set; } = string.Empty;

        /// <summary>"variable" | "function" | "macro"</summary>
        public string Kind { get; set; } = "variable";

        /// <summary>Every occurrence of the symbol (definition, reassignments, usages).</summary>
        public List<SymbolLocationDto> Locations { get; set; } = new();
    }

    public class SymbolLocationDto
    {
        /// <summary>Original source line (0-based, mapped back through all pipeline stages)</summary>
        public int Line { get; set; }

        /// <summary>Column in the source line (0-based)</summary>
        public int Column { get; set; }

        /// <summary>Token length in characters</summary>
        public int Length { get; set; }

        /// <summary>"local" or "include"</summary>
        public string Source { get; set; } = "local";

        /// <summary>File path if from an #include, null otherwise</summary>
        public string? SourceFile { get; set; }

        /// <summary>True for definitions and reassignments, false for read-only usages</summary>
        public bool IsAssignment { get; set; }
    }
}