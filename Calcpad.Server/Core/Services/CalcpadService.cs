using Calcpad.Core;
using Calcpad.Server.Controllers;
using System.Text.RegularExpressions;

namespace Calcpad.Server.Services
{
    public class CalcpadService
    {
        private readonly string _tempDirectory;
        private readonly string _htmlTemplate;

        public CalcpadService()
        {
            _tempDirectory = Path.GetTempPath();
            _htmlTemplate = LoadHtmlTemplate();
        }

        public Task<string> ConvertAsync(string calcpadContent, CalcpadSettings? settings = null, bool forceUnwrappedCode = false, string theme = "light")
        {
            if (string.IsNullOrWhiteSpace(calcpadContent))
            {
                FileLogger.LogWarning("ConvertAsync called with empty content");
                throw new ArgumentException("Content cannot be null or empty", nameof(calcpadContent));
            }

            try
            {
                FileLogger.LogInfo("Starting conversion", $"Content length: {calcpadContent.Length}, Has settings: {settings != null}, Force unwrapped: {forceUnwrappedCode}");
                // 1. Create and configure Calcpad.Core settings
                var coreSettings = new Settings();
                ConfigureCoreSettings(coreSettings, settings);

                // 2. Parse macros and includes (following WPF pattern)
                var macroParser = new MacroParser();
                
                if (CalcpadApiService.IsPublicMode)
                {
                    // #include is disabled in public mode to prevent path traversal attacks.
                    macroParser.Include = (fileName, fields) =>
                    {
                        FileLogger.LogWarning("Blocked #include directive in public mode", fileName);
                        return "' #include is not supported in public mode";
                    };
                }
                else
                {
                    // Local mode: allow #include with default file-reading behavior
                    macroParser.Include = (fileName, fields) =>
                    {
                        try
                        {
                            if (!File.Exists(fileName))
                                return $"' File not found: {fileName}";
                            return File.ReadAllText(fileName);
                        }
                        catch (Exception ex)
                        {
                            FileLogger.LogError($"Error reading include file: {fileName}", ex);
                            return $"' Error reading file: {fileName} - {ex.Message}";
                        }
                    };
                }
                string outputText;
                var hasMacroErrors = macroParser.Parse(calcpadContent, out outputText, null, 0, true);
                
                string htmlResult;
                
                if (hasMacroErrors || forceUnwrappedCode)
                {
                    // If there are macro errors or unwrapped code is requested, convert code directly to HTML
                    htmlResult = ConvertCodeToHtml(outputText);
                }
                else
                {
                    try
                    {
                        // 3. Parse expressions and calculate with a wall-clock timeout
                        var timeoutSeconds = CalcpadApiService.IsPublicMode ? 15 : 300;
                        var parser = new ExpressionParser { Settings = coreSettings };
                        var parseTask = Task.Run(() =>
                        {
                            parser.Parse(outputText, true, false);
                        });
                        if (!parseTask.Wait(TimeSpan.FromSeconds(timeoutSeconds)))
                        {
                            parser.Cancel();
                            FileLogger.LogWarning($"Computation timeout after {timeoutSeconds} seconds");
                            throw new TimeoutException($"Computation exceeded the maximum allowed time of {timeoutSeconds} seconds.");
                        }
                        // Re-throw any exception from the parse task
                        parseTask.GetAwaiter().GetResult();
                        htmlResult = parser.HtmlResult;
                    }
                    catch (TimeoutException)
                    {
                        throw;
                    }
                    catch (Exception parseEx)
                    {
                        FileLogger.LogWarning("Expression parsing failed, falling back to unwrapped code", parseEx.Message);
                        // If parsing fails, fall back to unwrapped code display
                        htmlResult = ConvertCodeToHtml(outputText);
                    }
                }

                // 4. Apply basic HTML wrapper with theme support
                var finalHtml = WrapHtmlResult(htmlResult, theme);
                FileLogger.LogInfo("Conversion completed successfully", $"Output length: {finalHtml.Length}");
                
                return Task.FromResult(finalHtml);
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

        private void ConfigureCoreSettings(Settings coreSettings, CalcpadSettings? settings)
        {
            // Configure Math settings
            if (settings?.Math != null)
            {
                coreSettings.Math.Decimals = settings.Math.Decimals ?? 6;
                coreSettings.Math.Degrees = settings.Math.Degrees ?? 0;
                coreSettings.Math.IsComplex = settings.Math.IsComplex ?? false;
                coreSettings.Math.Substitute = settings.Math.Substitute ?? true;
                coreSettings.Math.FormatEquations = settings.Math.FormatEquations ?? true;
            }
            else
            {
                // Set defaults
                coreSettings.Math.Decimals = 6;
                coreSettings.Math.Degrees = 0;
                coreSettings.Math.IsComplex = false;
                coreSettings.Math.Substitute = true;
                coreSettings.Math.FormatEquations = true;
            }

            // Configure Plot settings
            if (settings?.Plot != null)
            {
                coreSettings.Plot.IsAdaptive = settings.Plot.IsAdaptive ?? true;
                coreSettings.Plot.ScreenScaleFactor = settings.Plot.ScreenScaleFactor ?? 2.0;
                coreSettings.Plot.ImagePath = settings.Plot.ImagePath ?? "";
                coreSettings.Plot.ImageUri = settings.Plot.ImageUri ?? "";
                coreSettings.Plot.VectorGraphics = settings.Plot.VectorGraphics ?? false;
                
                // Parse ColorScale enum
                if (Enum.TryParse<Calcpad.Core.PlotSettings.ColorScales>(settings.Plot.ColorScale ?? "Rainbow", true, out var colorScale))
                    coreSettings.Plot.ColorScale = colorScale;
                else
                    coreSettings.Plot.ColorScale = Calcpad.Core.PlotSettings.ColorScales.Rainbow;
                
                coreSettings.Plot.SmoothScale = settings.Plot.SmoothScale ?? false;
                coreSettings.Plot.Shadows = settings.Plot.Shadows ?? true;
                
                // Parse LightDirection enum
                if (Enum.TryParse<Calcpad.Core.PlotSettings.LightDirections>(settings.Plot.LightDirection ?? "NorthWest", true, out var lightDirection))
                    coreSettings.Plot.LightDirection = lightDirection;
                else
                    coreSettings.Plot.LightDirection = Calcpad.Core.PlotSettings.LightDirections.NorthWest;
            }
            else
            {
                // Set defaults
                coreSettings.Plot.IsAdaptive = true;
                coreSettings.Plot.ScreenScaleFactor = 2.0;
                coreSettings.Plot.ImagePath = "";
                coreSettings.Plot.ImageUri = "";
                coreSettings.Plot.VectorGraphics = false;
                coreSettings.Plot.ColorScale = Calcpad.Core.PlotSettings.ColorScales.Rainbow;
                coreSettings.Plot.SmoothScale = false;
                coreSettings.Plot.Shadows = true;
                coreSettings.Plot.LightDirection = Calcpad.Core.PlotSettings.LightDirections.NorthWest;
            }

            // Configure Units
            coreSettings.Units = settings?.Units ?? "m";
        }

        private string ConvertCodeToHtml(string code)
        {
            // Convert code to HTML with line numbers and syntax highlighting (following WPF pattern)
            const string ErrorString = "#Error";
            
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
                
                stringBuilder.Append($"<p class=\"line-text\" id=\"line-{lineNumber}\"><span class=\"line-num\" title=\"Source line {sourceLine}\">{paddedSpaces}{lineNumber}</span>&emsp;│&emsp;");
                
                if (lineText.StartsWith(ErrorString))
                {
                    errors.Enqueue(lineNumber);
                    var errorText = lineText.Length > ErrorString.Length ? lineText.Substring(ErrorString.Length) : "";
                    // MacroParser already HTML encoded the error text, but we need to preserve HTML links
                    // So we decode it first, then re-encode only the parts that aren't HTML links
                    var decodedError = System.Web.HttpUtility.HtmlDecode(errorText);
                    stringBuilder.Append($"<span class=\"err\">{decodedError}</span>");
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
            
            // Add error summary if there are many errors
            if (errors.Count != 0 && lineNumber > 30)
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

        private string LoadHtmlTemplate()
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

        private string GetFallbackTemplate()
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

        private static readonly string CspMetaTag =
            "<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'; img-src data:; font-src data:;\">";

        private string WrapHtmlResult(string htmlContent, string theme = "light")
        {
            // Sanitize dangerous HTML tags from Calcpad output before wrapping
            htmlContent = SanitizeHtml(htmlContent);

            // Use the comprehensive HTML template with theme support
            var themeClass = theme.ToLower() == "dark" ? " class=\"dark-theme\"" : "";
            var templateWithTheme = _htmlTemplate.Replace("<body>", $"<body{themeClass}>");
            // Inject Content-Security-Policy to block inline scripts and external resources
            templateWithTheme = templateWithTheme.Replace("<head>", $"<head>\n    {CspMetaTag}");
            return templateWithTheme.Replace("{{CONTENT}}", htmlContent);
        }

        private static readonly Regex DangerousTagsRegex = new(
            @"<\s*/?\s*(script|iframe|object|embed|form|base|link|meta|applet|svg\s+[^>]*on\w+)[^>]*>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex EventHandlerRegex = new(
            @"\s+on\w+\s*=\s*[""'][^""']*[""']",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static string SanitizeHtml(string html)
        {
            // Strip dangerous tags: script, iframe, object, embed, form, base, link, meta, applet
            html = DangerousTagsRegex.Replace(html, string.Empty);
            // Strip event handler attributes (onclick, onerror, etc.)
            html = EventHandlerRegex.Replace(html, string.Empty);
            return html;
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
    }
}