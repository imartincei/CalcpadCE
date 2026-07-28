using System.Reflection;
using System.Text;

namespace Calcpad.Server.Services
{
    /// <summary>
    /// Loads the datagrid widget libraries bundled with the backend (filesystem
    /// UiAssets/ folder or embedded resources under <c>Calcpad.Server.UiAssets.*</c>)
    /// and inlines them into the rendered page. Inlining rather than linking keeps
    /// #UI datagrids working offline and inside the srcdoc iframe the preview uses,
    /// where relative URLs do not resolve.
    /// </summary>
    internal static class BundledUiAssets
    {
        // Load order matters: jspreadsheet reads the jSuites global at parse time.
        private static readonly string[] StyleSheets = ["jsuites.min.css", "jspreadsheet.min.css"];
        private static readonly string[] Scripts = ["jsuites.min.js", "jspreadsheet.min.js"];

        private static string? _cachedHeadMarkup;
        private static readonly object _lock = new();

        /// <summary>
        /// Returns the <c>&lt;style&gt;</c> and <c>&lt;script&gt;</c> tags for the datagrid
        /// libraries, or an empty string when they are not bundled. Insert into
        /// <c>&lt;head&gt;</c>, and only when the document actually contains a datagrid -
        /// the payload is several hundred kilobytes.
        /// </summary>
        public static string GetHeadMarkup()
        {
            if (_cachedHeadMarkup != null) return _cachedHeadMarkup;
            lock (_lock)
            {
                if (_cachedHeadMarkup != null) return _cachedHeadMarkup;

                var sb = new StringBuilder();
                var missing = new List<string>();
                foreach (var (fileName, tag) in StyleSheets.Select(f => (f, "style"))
                             .Concat(Scripts.Select(f => (f, "script"))))
                {
                    var content = Load(fileName);
                    if (content == null)
                    {
                        missing.Add(fileName);
                        continue;
                    }
                    sb.Append('<').Append(tag).Append('>').Append(content).Append("</").Append(tag).Append('>');
                }

                if (missing.Count > 0)
                {
                    // Half a widget is worse than none - a grid container with the CSS but
                    // no library would render as an empty box with no error.
                    FileLogger.LogWarning("Bundled UI assets are incomplete, datagrids will not render",
                        string.Join(", ", missing));
                    _cachedHeadMarkup = string.Empty;
                }
                else
                {
                    FileLogger.LogInfo("Bundled UI assets loaded", $"{sb.Length} chars");
                    _cachedHeadMarkup = sb.ToString();
                }
                return _cachedHeadMarkup;
            }
        }

        private static string? Load(string fileName) =>
            LoadFromFilesystem(fileName) ?? LoadFromEmbeddedResource(fileName);

        private static string? LoadFromFilesystem(string fileName)
        {
            try
            {
                var path = Path.Combine(AppContext.BaseDirectory, "UiAssets", fileName);
                return File.Exists(path) ? File.ReadAllText(path) : null;
            }
            catch (Exception ex)
            {
                FileLogger.LogWarning($"Failed to load bundled UI asset {fileName}", ex.Message);
                return null;
            }
        }

        private static string? LoadFromEmbeddedResource(string fileName)
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                using var stream = assembly.GetManifestResourceStream($"Calcpad.Server.UiAssets.{fileName}");
                if (stream == null) return null;

                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }
            catch (Exception ex)
            {
                FileLogger.LogWarning($"Failed to load embedded UI asset {fileName}", ex.Message);
                return null;
            }
        }
    }
}
