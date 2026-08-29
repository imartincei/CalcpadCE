using Calcpad.Core;
using Calcpad.Server.Controllers;

namespace Calcpad.Server.Services
{
    public class CalcpadService
    {
        private readonly string _tempDirectory;

        // Scoped service, so this would otherwise re-read the ~200KB template on every request.
        private static readonly string _htmlTemplate = LoadHtmlTemplate();

        public CalcpadService()
        {
            _tempDirectory = Path.GetTempPath();
        }

        /// <summary>
        /// Creates the Include delegate for MacroParser. Core only invokes this when the
        /// resolved path exists on disk, so a bare File.ReadAllText is sufficient and any I/O
        /// failure surfaces through the catch as a Calcpad error comment (URL-hosted includes
        /// are not currently supported).
        /// </summary>
        internal static Func<string, Queue<string>, string> CreateIncludeDelegate()
        {
            return (fileName, fields) =>
            {
                try
                {
                    return ProcessIncludedContent(File.ReadAllText(fileName));
                }
                catch (Exception ex)
                {
                    FileLogger.LogError($"Error reading include file: {fileName}", ex);
                    return $"' Error reading file: {fileName} - {ex.Message}";
                }
            };
        }

        public (string Html, IReadOnlyList<string> OpenXmlExpressions, IReadOnlyList<CalcpadError> Errors) Convert(
            string calcpadContent,
            Settings? settings = null,
            bool forceUnwrappedCode = false,
            string theme = "light",
            string? sourceFilePath = null,
            bool forPrint = false,
            bool captureOpenXml = false,
            bool enableUi = false,
            Dictionary<string, string>? uiOverrides = null,
            bool? debug = null,
            bool? hideErrorLines = null,
            bool write = false,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(calcpadContent))
            {
                FileLogger.LogWarning("Convert called with empty content");
                throw new ArgumentException("Content cannot be null or empty", nameof(calcpadContent));
            }

            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                Console.WriteLine($"=== CALCPAD SERVICE: Starting conversion, length: {calcpadContent.Length} ===");
                FileLogger.LogInfo("Starting conversion", $"Content length: {calcpadContent.Length}, Has settings: {settings != null}, Force unwrapped: {forceUnwrappedCode}, For print: {forPrint}");
                FileLogger.LogInfo("Content preview:", calcpadContent.Substring(0, Math.Min(200, calcpadContent.Length)));

                // 1. Use Calcpad.Core settings directly (defaults are set in constructors).
                //    Per-file overrides now come from the #settings directive, handled in Core.
                Settings coreSettings = settings ?? new Settings();

                // Printed output is a report by definition, so it never carries controls.
                // Overrides still apply - the report shows the values that were entered.
                enableUi &= !forPrint;

                // Input mode hides the source editor, so its "on line [N]" links point at
                // nothing reachable. Defaults to following enableUi; the report pane beside
                // the form passes its own override since it isn't gated on enableUi.
                var showErrorLines = !(hideErrorLines ?? enableUi);

                // 2. Parse macros and includes (server reads referenced files from disk).
                var macroParser = new MacroParser
                {
                    Include = CreateIncludeDelegate(),
                    SourceFilePath = sourceFilePath
                };

                string outputText;
                var hasMacroErrors = macroParser.Parse(calcpadContent, out outputText, null, 0, true);

                cancellationToken.ThrowIfCancellationRequested();

                string htmlResult;
                IReadOnlyList<string> openXmlExpressions = Array.Empty<string>();
                var errors = new List<CalcpadError>(macroParser.Errors);

                if (hasMacroErrors || forceUnwrappedCode)
                {
                    // Forced-unwrapped with no macro errors: silently run
                    // ExpressionParser so the code view can highlight lines that
                    // would error in the wrapped preview. Skip when macro errors
                    // already exist since the expression pass would just fail.
                    if (forceUnwrappedCode && !hasMacroErrors)
                    {
                        try
                        {
                            // Hard-coded rather than threaded from `write`: this pass exists only to
                            // collect error lines, so it must never repeat the document's writes.
                            var silent = new ExpressionParser { Settings = coreSettings, SourceFilePath = sourceFilePath, PathRoots = macroParser.PathRoots, Debug = true, AllowDataWrite = false };
                            silent.Parse(outputText, true, false);
                            errors.AddRange(silent.Errors);
                        }
                        catch (Exception silentEx)
                        {
                            FileLogger.LogWarning("Silent expression parse for error collection failed", silentEx.Message);
                        }
                    }
                    htmlResult = ConvertCodeToHtml(outputText, errors);
                }
                else
                {
                    try
                    {
                        // Debug mode makes Calcpad.Core emit per-line anchors (id="line-N"
                        // class="line") and the error-summary boxes the interactive preview
                        // uses for line links. It defaults to the opposite of forPrint but is
                        // independently settable: the on-screen report is a print layout that
                        // still wants line links.
                        var parser = new ExpressionParser
                        {
                            Settings = coreSettings,
                            SourceFilePath = sourceFilePath,
                            PathRoots = macroParser.PathRoots,
                            Debug = debug ?? !forPrint,
                            ForPrint = forPrint,
                            EnableUi = enableUi,
                            UiOverrides = uiOverrides,
                            ShowErrorLines = showErrorLines,
                            AllowDataWrite = write
                        };
                        parser.Parse(outputText, true, captureOpenXml);
                        htmlResult = RemoveEmptyParagraphs(parser.HtmlResult);
                        errors.AddRange(parser.Errors);
                        if (captureOpenXml)
                            openXmlExpressions = parser.OpenXmlExpressions.ToList();
                    }
                    catch (Exception parseEx)
                    {
                        FileLogger.LogWarning("Expression parsing failed, falling back to unwrapped code", parseEx.Message);
                        htmlResult = ConvertCodeToHtml(outputText, errors);
                    }
                }

                // 6. Apply HTML wrapper with theme support
                var finalHtml = WrapHtmlResult(htmlResult, theme, enableUi);
                FileLogger.LogInfo("Conversion completed successfully", $"Output length: {finalHtml.Length}");

                return (finalHtml, openXmlExpressions, errors);
            }
            catch (OperationCanceledException)
            {
                // Let this propagate as-is rather than the wrapped InvalidOperationException
                // below — it's an expected supersede/abort, not a conversion failure.
                throw;
            }
            catch (MathParserException ex)
            {
                FileLogger.LogError("Math parsing error during conversion", ex);
                throw new InvalidOperationException($"Math parsing error: {ex.Message}", ex);
            }
            catch (Exception ex)
            {
                FileLogger.LogError("Calcpad conversion failed", ex);
                throw new InvalidOperationException($"Calcpad conversion failed: {ex.Message}", ex);
            }
        }

        public string GetSampleContent()
        {
            return @"""Sample Mathematical Calculations
'<hr/>
'Basic arithmetic operations:
a = 10
b = 5
'Addition:
sum = a + b
'Subtraction:  
diff = a - b
'Multiplication:
prod = a * b
'Division:
ratio = a / b
'Power:
power = a^2
'Square root:
sqrt_a = sqr(a)
'Trigonometric functions (angle in degrees):
angle = 45
sin_angle = sin(angle°)
cos_angle = cos(angle°)
tan_angle = tan(angle°)";
        }


        private string ConvertCodeToHtml(string code, IReadOnlyList<CalcpadError>? errorList = null)
        {
            // Convert code to HTML with line numbers and syntax highlighting (following WPF pattern)
            const string ErrorString = "#Error";

            // Key by OutputLine so macro-expanded loops only highlight the exact
            // erroring row, not every iteration that came from the same source
            // line. Fall back to SourceLine when OutputLine isn't known (0).
            var errorsByOutputLine = new Dictionary<int, string>();
            var errorsBySourceLine = new Dictionary<int, string>();
            if (errorList != null)
            {
                foreach (var err in errorList)
                {
                    if (err.OutputLine > 0)
                    {
                        errorsByOutputLine[err.OutputLine] = errorsByOutputLine.TryGetValue(err.OutputLine, out var prevO)
                            ? prevO + "\n" + err.Message
                            : err.Message;
                    }
                    else
                    {
                        errorsBySourceLine[err.SourceLine] = errorsBySourceLine.TryGetValue(err.SourceLine, out var prevS)
                            ? prevS + "\n" + err.Message
                            : err.Message;
                    }
                }
            }

            var errors = new Queue<int>();
            var stringBuilder = new System.Text.StringBuilder();
            var lines = code.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            stringBuilder.AppendLine("<div class=\"code\">");
            var lineNumber = 0;

            foreach (var line in lines)
            {
                ++lineNumber;
                var i = line.IndexOf('\v');
                var lineText = i < 0 ? line : line.Substring(0, i);
                var sourceLine = i < 0 ? lineNumber.ToString() : line.Substring(i + 1);

                // Format line number with proper padding
                var lineNumText = lineNumber.ToString();
                var n = lineNumText.Length;
                var spaceCount = Math.Max(0, 6 - n);
                var paddedSpaces = new string(' ', spaceCount).Replace(" ", "&nbsp;");

                errorsByOutputLine.TryGetValue(lineNumber, out var errMsg);
                if (errMsg == null && int.TryParse(sourceLine, out var srcLineNum))
                    errorsBySourceLine.TryGetValue(srcLineNum, out errMsg);
                var errClass = errMsg != null ? " err-line" : "";
                var errTitle = errMsg != null ? $" title=\"{System.Web.HttpUtility.HtmlAttributeEncode(errMsg)}\"" : "";
                if (errMsg != null) errors.Enqueue(lineNumber);

                // The paragraph id is the (output) line number for in-preview scrolling; the line-num
                // anchor carries data-text=sourceLine so clicking it navigates the editor to the
                // original source line (mirrors WPF's CodeToHtml).
                stringBuilder.Append($"<p class=\"line-text{errClass}\" id=\"line-{lineNumber}\"{errTitle}><a class=\"line-num\" href=\"#0\" data-text=\"{sourceLine}\" title=\"Source line {sourceLine}\">{paddedSpaces}{lineNumber}</a>&emsp;│&emsp;");

                if (lineText.StartsWith(ErrorString))
                {
                    var errorText = lineText.Length > ErrorString.Length ? lineText.Substring(ErrorString.Length) : "";
                    // MacroParser already HTML encoded the error text, but we need to preserve HTML links
                    // So we decode it first, then re-encode only the parts that aren't HTML links
                    var decodedError = System.Web.HttpUtility.HtmlDecode(errorText);
                    stringBuilder.Append($"<span class=\"err\">{decodedError}</span>");
                }
                else if (lineText.Contains("<a href=\"#0\" data-text=\"") || lineText.StartsWith("Error in"))
                {
                    // Line contains error HTML with data-text links - don't apply syntax highlighting
                    // The HTML tags are NOT encoded in the raw text from MacroParser
                    stringBuilder.Append($"<span class=\"err\">{lineText}</span>");
                }
                else
                {
                    // Apply syntax highlighting like WPF version
                    var highlightedLine = ApplySyntaxHighlighting(lineText);
                    stringBuilder.Append(highlightedLine);
                }
                stringBuilder.AppendLine("</p>");
            }
            
            stringBuilder.AppendLine("</div>");
            
            // Add error summary
            if (errors.Count != 0)
            {
                stringBuilder.AppendLine($"<div class=\"errorHeader\">Found <b>{errors.Count}</b> errors in modules and macros:");
                var count = 0;
                while (errors.Count != 0 && ++count < 20)
                {
                    var errorLine = errors.Dequeue();
                    stringBuilder.Append($" <span class=\"roundBox\" data-line=\"{errorLine}\">{errorLine}</span>");
                }
                if (errors.Count > 0)
                    stringBuilder.Append(" ...");
                
                stringBuilder.AppendLine("</div>");
                stringBuilder.AppendLine("<style>body {padding-top:1.1em;} .code p {margin:0; line-height:1.15em;}</style>");
            }
            else
            {
                stringBuilder.AppendLine("<style>.code p {margin:0; line-height:1.15em;}</style>");
            }
            
            return stringBuilder.ToString();
        }

        private string ApplySyntaxHighlighting(string line)
        {
            if (string.IsNullOrEmpty(line))
                return "";

            var result = new System.Text.StringBuilder();
            var i = 0;
            var len = line.Length;

            while (i < len)
            {
                var c = line[i];
                
                // Comments (pink/magenta for includes, green for text, purple for HTML tags)
                if (c == '\'' || c == '"')
                {
                    var commentEnd = FindCommentEnd(line, i);
                    var commentText = line.Substring(i, commentEnd - i + 1);
                    
                    // Check if it's an include comment
                    if (commentText.Contains("#include"))
                        result.Append($"<span class=\"include\">{System.Web.HttpUtility.HtmlEncode(commentText)}</span>");
                    // Check if it contains HTML tags
                    else if (commentText.Contains("<") && commentText.Contains(">"))
                        result.Append($"<span class=\"htmltag\">{System.Web.HttpUtility.HtmlEncode(commentText)}</span>");
                    else
                        result.Append($"<span class=\"comment\">{System.Web.HttpUtility.HtmlEncode(commentText)}</span>");
                    
                    i = commentEnd + 1;
                    continue;
                }
                
                // Keywords (magenta/pink)
                if (c == '#')
                {
                    var keywordEnd = FindKeywordEnd(line, i);
                    var keyword = line.Substring(i, keywordEnd - i + 1);
                    result.Append($"<span class=\"keyword\">{System.Web.HttpUtility.HtmlEncode(keyword)}</span>");
                    i = keywordEnd + 1;
                    continue;
                }
                
                // Commands (magenta)
                if (c == '$')
                {
                    var commandEnd = FindCommandEnd(line, i);
                    var command = line.Substring(i, commandEnd - i + 1);
                    result.Append($"<span class=\"command\">{System.Web.HttpUtility.HtmlEncode(command)}</span>");
                    i = commandEnd + 1;
                    continue;
                }
                
                // Numbers (black)
                if (char.IsDigit(c))
                {
                    var numberEnd = FindNumberEnd(line, i);
                    var number = line.Substring(i, numberEnd - i + 1);
                    result.Append($"<span class=\"number\">{System.Web.HttpUtility.HtmlEncode(number)}</span>");
                    i = numberEnd + 1;
                    continue;
                }
                
                // Variables and functions (blue)
                if (char.IsLetter(c) || c == '_')
                {
                    var identifierEnd = FindIdentifierEnd(line, i);
                    var identifier = line.Substring(i, identifierEnd - i + 1);
                    
                    // Check if it's followed by '(' to identify functions
                    var isFunction = identifierEnd + 1 < len && line[identifierEnd + 1] == '(';
                    if (isFunction)
                        result.Append($"<span class=\"function\">{System.Web.HttpUtility.HtmlEncode(identifier)}</span>");
                    else
                        result.Append($"<span class=\"variable\">{System.Web.HttpUtility.HtmlEncode(identifier)}</span>");
                    
                    i = identifierEnd + 1;
                    continue;
                }
                
                // Operators (goldenrod)
                if (IsOperator(c))
                {
                    result.Append($"<span class=\"operator\">{System.Web.HttpUtility.HtmlEncode(c)}</span>");
                    i++;
                    continue;
                }
                
                // Default: just encode the character
                result.Append(System.Web.HttpUtility.HtmlEncode(c));
                i++;
            }

            return result.ToString();
        }

        private int FindCommentEnd(string line, int start)
        {
            var quote = line[start];
            for (int i = start + 1; i < line.Length; i++)
            {
                if (line[i] == quote)
                    return i;
            }
            return line.Length - 1;
        }

        private int FindKeywordEnd(string line, int start)
        {
            for (int i = start + 1; i < line.Length; i++)
            {
                if (char.IsWhiteSpace(line[i]))
                    return i - 1;
            }
            return line.Length - 1;
        }

        private int FindCommandEnd(string line, int start)
        {
            for (int i = start + 1; i < line.Length; i++)
            {
                if (!char.IsLetterOrDigit(line[i]) && line[i] != '_')
                    return i - 1;
            }
            return line.Length - 1;
        }

        private int FindNumberEnd(string line, int start)
        {
            for (int i = start + 1; i < line.Length; i++)
            {
                if (!char.IsDigit(line[i]) && line[i] != '.')
                    return i - 1;
            }
            return line.Length - 1;
        }

        private int FindIdentifierEnd(string line, int start)
        {
            for (int i = start + 1; i < line.Length; i++)
            {
                if (!char.IsLetterOrDigit(line[i]) && line[i] != '_')
                    return i - 1;
            }
            return line.Length - 1;
        }

        private bool IsOperator(char c)
        {
            return "!^/÷\\⦼*-+<>≤≥≡≠=∧∨⊕(){}[]|&@:;".Contains(c);
        }

        private static string LoadHtmlTemplate()
        {
            try
            {
                // First try to read from embedded resource (for single-file deployments)
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                var resourceName = "Calcpad.Server.template.html";
                
                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream != null)
                {
                    using var reader = new StreamReader(stream);
                    var template = reader.ReadToEnd();
                    FileLogger.LogInfo("Loaded HTML template from embedded resource");
                    return template;
                }
                
                // Fallback to file system (for development and non-single-file deployments)
                var templatePath = Path.Combine(AppContext.BaseDirectory, "template.html");
                if (File.Exists(templatePath))
                {
                    var template = File.ReadAllText(templatePath);
                    FileLogger.LogInfo("Loaded HTML template from file system", templatePath);
                    return template;
                }
                
                throw new FileNotFoundException($"Template file not found at {templatePath}");
            }
            catch (Exception ex)
            {
                FileLogger.LogError("Failed to load HTML template, using fallback", ex);
                return GetFallbackTemplate();
            }
        }

        private static string GetFallbackTemplate()
        {
            return @"<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <title>Calcpad Calculation</title>
    <style>
        body { font-family: Arial, sans-serif; margin: 20px; line-height: 1.6; }
        .math { font-family: 'Times New Roman', serif; }
        .error { color: red; }
        table { border-collapse: collapse; margin: 10px 0; }
        td, th { padding: 5px 10px; border: 1px solid #ccc; }
        .center { text-align: center; }
    </style>
</head>
<body>
    {{CONTENT}}
</body>
</html>";
        }

        private string RemoveEmptyParagraphs(string htmlContent)
        {
            // Remove blank-line paragraphs (only content is &nbsp;). Debug mode adds
            // per-line anchors (<p id="line-N" class="line">&nbsp;</p>) so the opening
            // tag can carry attributes — match those too, not just a bare <p>.
            return System.Text.RegularExpressions.Regex.Replace(
                htmlContent,
                @"<p\b[^>]*>&nbsp;</p>(\r?\n)?",
                string.Empty
            );
        }

        private string WrapHtmlResult(string htmlContent, string theme = "light", bool enableUi = false)
        {
            // Use the comprehensive HTML template with theme support
            var themeClass = theme.ToLower() == "dark" ? " class=\"dark-theme\"" : "";
            var templateWithTheme = _htmlTemplate.Replace("<body>", $"<body{themeClass}>");

            // Register @font-face rules for fonts referenced by name in the
            // template CSS (e.g. Jost*), so they don't depend on OS installation,
            // and expose window.__calcpadFonts for any other client-side use.
            // Injected before </head> so it runs before any module scripts in body.
            var headMarkup = BundledFonts.GetFontFaceStyleTag() + BundledFonts.GetInjectionScript();

            // The datagrid libraries are large, so they only go in when a grid is
            // actually present. jspreadsheet has to be defined before the event
            // script runs, hence <head> rather than alongside it.
            if (enableUi && htmlContent.Contains("calcpad-ui-datagrid", StringComparison.Ordinal))
                headMarkup += BundledUiAssets.GetHeadMarkup();

            if (!string.IsNullOrEmpty(headMarkup))
            {
                var headCloseIdx = templateWithTheme.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
                if (headCloseIdx >= 0)
                    templateWithTheme = templateWithTheme.Insert(headCloseIdx, headMarkup);
            }

            // Report and print output carry no controls, so they get no event script.
            if (enableUi)
            {
                var bodyCloseIdx = templateWithTheme.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
                if (bodyCloseIdx >= 0)
                    templateWithTheme = templateWithTheme.Insert(bodyCloseIdx, UiPreviewScript.GetScriptTag());
            }

            return templateWithTheme.Replace("{{CONTENT}}", htmlContent);
        }

        private static void TryDeleteFile(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                    File.Delete(filePath);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }

        /// <summary>
        /// Processes included file content to respect #local and #global directives.
        /// Following the pattern from Calcpad.Cli.CalcpadReader.Include
        /// <para>
        /// A saved 'uiOverrides' comment only has any effect on the file that carries it -
        /// the host restores it by scanning the document you have open, before #include is
        /// expanded, so one coming from an included file would otherwise sit inert in the
        /// flattened text. Stripped the same way #local content is, whether or not the
        /// including file wrapped it in #local/#global itself.
        /// </para>
        /// </summary>
        private static string ProcessIncludedContent(string content)
        {
            if (string.IsNullOrEmpty(content))
                return content;

            var isLocal = false;
            var isUiOverridesComment = false;
            var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.None);
            var outputLines = new List<string>();

            foreach (var line in lines)
            {
                if (isUiOverridesComment)
                {
                    if (line.Contains("-->", StringComparison.Ordinal))
                        isUiOverridesComment = false;

                    continue;
                }
                if (Validator.IsKeyword(line, "#local"))
                {
                    isLocal = true;
                }
                else if (Validator.IsKeyword(line, "#global"))
                {
                    isLocal = false;
                }
                else if (IsUiOverridesCommentStart(line))
                {
                    isUiOverridesComment = !line.Contains("-->", StringComparison.Ordinal);
                }
                else
                {
                    // Only include lines that are not marked as local
                    if (!isLocal)
                    {
                        outputLines.Add(line);
                    }
                }
            }

            return string.Join(Environment.NewLine, outputLines);
        }

        /// <summary>
        /// True for the first line of a metadata comment ('<!--{...}-->) whose JSON carries
        /// a 'uiOverrides' key. A cheap substring check rather than a JSON parse, matching
        /// the rest of this method's line-based approach - good enough since the host always
        /// writes the key verbatim.
        /// </summary>
        private static bool IsUiOverridesCommentStart(string line)
        {
            var trimmed = line.TrimStart();
            return (trimmed.StartsWith('\'') || trimmed.StartsWith('"'))
                && trimmed.Contains("<!--", StringComparison.Ordinal)
                && trimmed.Contains("uiOverrides", StringComparison.Ordinal);
        }
    }
}