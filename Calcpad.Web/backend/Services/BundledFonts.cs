using System.Reflection;
using System.Text;

namespace Calcpad.Server.Services
{
    /// <summary>
    /// Loads font files bundled with the backend (filesystem Fonts/ folder or
    /// embedded resources under <c>Calcpad.Server.Fonts.*</c>) and generates
    /// @font-face rules for the rendered page. Drop a .woff / .woff2 / .ttf / .otf file into the Fonts/
    /// directory and it gets picked up automatically.
    /// </summary>
    internal static class BundledFonts
    {
        private readonly record struct FontFace(string Family, string? Weight = null, string? Style = null);

        // Filename -> the @font-face rule(s) it should be registered as. Unlisted files are ignored.
        private static readonly Dictionary<string, FontFace[]> FontFaces =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["Jost-100-Hairline.otf"] = [new("Jost* Hairline")],
                ["Jost-200-Thin.otf"] = [new("Jost* Thin")],
                ["DejaVuSerifCondensed.woff2"] = [new("DejaVu Serif Condensed", "normal", "normal")],
                ["DejaVuSerifCondensed-Italic.woff2"] = [new("DejaVu Serif Condensed", "normal", "italic")],
                ["DejaVuSerifCondensed-Bold.woff2"] = [new("DejaVu Serif Condensed", "bold", "normal")],
                ["DejaVuSerifCondensed-BoldItalic.woff2"] = [new("DejaVu Serif Condensed", "bold", "italic")],
                ["CalcpadCEVector.woff2"] = [new("CalcpadCE Vector", "normal", "normal")],
            };

        private static IReadOnlyDictionary<string, string>? _cachedDataUrls;
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
        /// Returns a <c>&lt;style&gt;</c> tag with an <c>@font-face</c> rule for
        /// every bundled font listed in <see cref="FontFaces"/>, or an
        /// empty string if none apply.
        /// </summary>
        public static string GetFontFaceStyleTag()
        {
            if (_cachedFontFaceStyleTag != null) return _cachedFontFaceStyleTag;

            var fonts = GetDataUrls();
            var sb = new StringBuilder();
            foreach (var (fileName, dataUrl) in fonts)
            {
                if (!FontFaces.TryGetValue(fileName, out var faces)) continue;
                var format = FormatForFont(fileName);
                foreach (var face in faces)
                {
                    sb.Append("@font-face{font-family:\"").Append(CssEscape(face.Family))
                      .Append("\";src:url(").Append(dataUrl).Append(") format(\"").Append(format).Append("\")");
                    if (face.Weight is not null) sb.Append(";font-weight:").Append(face.Weight);
                    if (face.Style is not null) sb.Append(";font-style:").Append(face.Style);
                    sb.Append(";}");
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
    }
}
