using System.Reflection;
using System.Text;

namespace Calcpad.Server.Services
{
    /// <summary>
    /// Loads font files bundled with the backend (filesystem Fonts/ folder or
    /// embedded resources under <c>Calcpad.Server.Fonts.*</c>) and generates
    /// @font-face rules plus a <c>window.__calcpadFonts</c> dict for the
    /// rendered page. Drop a .woff / .woff2 / .ttf / .otf file into the Fonts/
    /// directory and it gets picked up automatically.
    /// </summary>
    internal static class BundledFonts
    {
        // Filename -> CSS font-family name(s) it should be registered as via @font-face.
        // Files bundled but not listed here still land in window.__calcpadFonts, just without a rule.
        private static readonly Dictionary<string, string[]> FontFamilyNames =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["Jost-100-Hairline.otf"] = ["Jost* Hairline"],
                ["Jost-200-Thin.otf"] = ["Jost* Thin"],
            };

        private static IReadOnlyDictionary<string, string>? _cachedDataUrls;
        private static string? _cachedScriptTag;
        private static string? _cachedFontFaceStyleTag;
        private static readonly object _lock = new();

        public static IReadOnlyDictionary<string, string> GetDataUrls()
        {
            if (_cachedDataUrls != null) return _cachedDataUrls;
            lock (_lock)
            {
                if (_cachedDataUrls != null) return _cachedDataUrls;

                var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                LoadFromFilesystem(map);
                LoadFromEmbeddedResources(map);

                FileLogger.LogVerbose("Bundled fonts loaded", map.Count == 0 ? "(none)" : string.Join(", ", map.Keys));
                _cachedDataUrls = map;
                return _cachedDataUrls;
            }
        }

        /// <summary>
        /// Returns a <c>&lt;script&gt;</c> tag that defines
        /// <c>window.__calcpadFonts</c> with every bundled font, or an empty
        /// string if none are bundled.
        /// </summary>
        public static string GetInjectionScript()
        {
            if (_cachedScriptTag != null) return _cachedScriptTag;

            var fonts = GetDataUrls();
            if (fonts.Count == 0)
            {
                _cachedScriptTag = string.Empty;
                return _cachedScriptTag;
            }

            var sb = new StringBuilder();
            sb.Append("<script>window.__calcpadFonts={");
            bool first = true;
            foreach (var (fileName, dataUrl) in fonts)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append('"').Append(JsEscape(fileName)).Append("\":\"").Append(JsEscape(dataUrl)).Append('"');
            }
            sb.Append("};</script>");

            _cachedScriptTag = sb.ToString();
            return _cachedScriptTag;
        }

        /// <summary>
        /// Returns a <c>&lt;style&gt;</c> tag with an <c>@font-face</c> rule for
        /// every bundled font listed in <see cref="FontFamilyNames"/>, or an
        /// empty string if none apply.
        /// </summary>
        public static string GetFontFaceStyleTag()
        {
            if (_cachedFontFaceStyleTag != null) return _cachedFontFaceStyleTag;

            var fonts = GetDataUrls();
            var sb = new StringBuilder();
            foreach (var (fileName, dataUrl) in fonts)
            {
                if (!FontFamilyNames.TryGetValue(fileName, out var familyNames)) continue;
                var format = FormatForFont(fileName);
                foreach (var familyName in familyNames)
                {
                    sb.Append("@font-face{font-family:\"").Append(CssEscape(familyName))
                      .Append("\";src:url(").Append(dataUrl).Append(") format(\"").Append(format).Append("\");}");
                }
            }

            _cachedFontFaceStyleTag = sb.Length == 0 ? string.Empty : $"<style>{sb}</style>";
            return _cachedFontFaceStyleTag;
        }

        private static string FormatForFont(string path) =>
            Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".woff" => "woff",
                ".woff2" => "woff2",
                ".ttf" => "truetype",
                ".otf" => "opentype",
                _ => "opentype"
            };

        private static string CssEscape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

        private static void LoadFromFilesystem(Dictionary<string, string> map)
        {
            try
            {
                var dir = Path.Combine(AppContext.BaseDirectory, "Fonts");
                if (!Directory.Exists(dir)) return;

                foreach (var path in Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories))
                {
                    var mime = MimeForFont(path);
                    if (mime == null) continue;
                    try
                    {
                        var bytes = File.ReadAllBytes(path);
                        map[Path.GetFileName(path)] = $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
                    }
                    catch (Exception ex)
                    {
                        FileLogger.LogWarning($"Failed to load bundled font from {path}", ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                FileLogger.LogWarning("Failed to enumerate Fonts/ directory", ex.Message);
            }
        }

        private static void LoadFromEmbeddedResources(Dictionary<string, string> map)
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                foreach (var name in assembly.GetManifestResourceNames())
                {
                    if (!name.StartsWith("Calcpad.Server.Fonts.", StringComparison.OrdinalIgnoreCase)) continue;
                    var mime = MimeForFont(name);
                    if (mime == null) continue;

                    // Resource names use '.' as path separator. Recover a usable
                    // "<stem>.<ext>" filename for URL matching.
                    var leaf = name.Substring("Calcpad.Server.Fonts.".Length);
                    var lastDot = leaf.LastIndexOf('.');
                    var fileName = lastDot < 0
                        ? leaf
                        : leaf.Substring(0, lastDot).Replace('.', '_') + leaf.Substring(lastDot);
                    if (map.ContainsKey(fileName)) continue;

                    try
                    {
                        using var stream = assembly.GetManifestResourceStream(name);
                        if (stream == null) continue;
                        using var ms = new MemoryStream();
                        stream.CopyTo(ms);
                        map[fileName] = $"data:{mime};base64,{Convert.ToBase64String(ms.ToArray())}";
                    }
                    catch (Exception ex)
                    {
                        FileLogger.LogWarning($"Failed to load embedded font resource {name}", ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                FileLogger.LogWarning("Failed to enumerate embedded font resources", ex.Message);
            }
        }

        private static string? MimeForFont(string path)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            return ext switch
            {
                ".woff" => "font/woff",
                ".woff2" => "font/woff2",
                ".ttf" => "font/ttf",
                ".otf" => "font/otf",
                _ => null
            };
        }

        private static string JsEscape(string s) =>
            s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("</", "<\\/");
    }
}
