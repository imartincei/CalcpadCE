using System;
using System.IO;
using System.Text.RegularExpressions;

namespace Calcpad.Core
{
    /// <summary>
    /// The one file reference the parsers know nothing about — an image carried as
    /// <c>&lt;img src&gt;</c> inside a comment line. <see cref="ExpandSources"/> resolves such a
    /// source the same way <see cref="MacroParser"/> resolves an <c>#include</c> and
    /// <c>ExpressionParser.ReadWriteOptions</c> resolves a <c>#read</c>: a
    /// <c>{project}</c>/<c>{library}</c>/<c>{user}</c> token first, environment variables second.
    /// Doing it here rather than in each host is what lets the HTML they all receive carry a path
    /// that already resolves.
    /// </summary>
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

        /// <summary>
        /// Rewrites every <c>&lt;img src&gt;</c> of <paramref name="html"/> that names a local
        /// file, leaving an external one untouched. A source carrying a root token always comes
        /// out as an absolute forward-slash path; one carrying none is left as written unless
        /// expanding an environment variable made it absolute, so a plain relative source stays
        /// relative and the document keeps travelling with its images.
        /// </summary>
        /// <param name="documentDirectory">
        /// The folder of the worksheet being parsed, the base a token expansion is made absolute
        /// against in the edge case where it did not already resolve to a rooted path. May be null.
        /// </param>
        /// <param name="reportError">
        /// Called with the message when a token's root was never declared. The source is left as
        /// written, so the rest of the document still renders.
        /// </param>
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
        // (![alt]({library}/logo.png)) reaches us with its leading token escaped. Only the first
        // pair is decoded, and only at the very start where a token can be: running the whole
        // source through Uri.UnescapeDataString would also eat a %VAR% whose name happens to read
        // as hex, and turn a literal %20 in a file name into a space.
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
