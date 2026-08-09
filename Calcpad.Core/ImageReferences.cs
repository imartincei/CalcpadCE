using System;
using System.IO;
using System.Text.RegularExpressions;

namespace Calcpad.Core
{
    public static class ImageReferences
    {
        public static readonly Regex Source =
            new(@"(<img\s[^>]*?src\s*=\s*[""'])([^""']+)([""'])",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// A source that resolves on its own, wherever the document ends up: already absolute,
        /// inline data, or fetched over the network.
        /// </summary>
        public static bool IsExternal(string src) =>
            Path.IsPathRooted(src)
            || src.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
            || src.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || src.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || src.StartsWith("file:", StringComparison.OrdinalIgnoreCase);

        internal static string ExpandSources(
            string html,
            PathRoots roots,
            string documentDirectory,
            Action<string> reportError)
        {
            var matches = Source.Matches(html);
            if (matches.Count == 0)
                return html;

            for (var i = matches.Count - 1; i >= 0; --i)
            {
                var group = matches[i].Groups[2];
                var expanded = Expand(group.Value, roots, documentDirectory, reportError);
                if (expanded is not null)
                    html = string.Concat(
                        html.AsSpan(0, group.Index),
                        expanded,
                        html.AsSpan(group.Index + group.Length));
            }
            return html;
        }

        // Markdig percent-encodes a link destination, so an image written as Markdown
        // (![alt]({library}/logo.png)) reaches us with its leading token escaped.
        private static string DecodeLeadingToken(string src)
        {
            if (!src.StartsWith("%7B", StringComparison.OrdinalIgnoreCase))
                return src;

            var close = src.IndexOf("%7D", StringComparison.OrdinalIgnoreCase);
            return close < 0
                ? src
                : string.Concat("{", src.AsSpan(3, close - 3), "}", src.AsSpan(close + 3));
        }

        private static string Expand(
            string src,
            PathRoots roots,
            string documentDirectory,
            Action<string> reportError)
        {
            if (IsExternal(src))
                return null;

            src = DecodeLeadingToken(src);
            var hasToken = PathRoots.HasToken(src.AsSpan()) || PathRoots.IsUserToken(src.AsSpan());
            if (!roots.TryExpand(src, out var expanded, out var error))
            {
                reportError(error);
                return null;
            }
            try
            {
                expanded = Environment.ExpandEnvironmentVariables(expanded);
                if (!hasToken && !Path.IsPathRooted(expanded))
                    return expanded == src ? null : expanded;

                var full = string.IsNullOrEmpty(documentDirectory)
                    ? Path.GetFullPath(expanded)
                    : Path.GetFullPath(expanded, documentDirectory);
                return full.Replace('\\', '/');
            }
            catch
            {
                return null;
            }
        }
    }
}
