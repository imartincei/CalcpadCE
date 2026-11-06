using System;

namespace Calcpad.Server.Services
{
    /// <summary>The file metadata footer CalcpadCE editors keep at the end of a .cpd file.</summary>
    internal static class MetadataFooter
    {
        public const string Anchor = "<!--CalcpadCE Metadata-->";

        /// <summary>Cuts the trailing footer off <paramref name="source"/>, leaving every line above it as is.</summary>
        public static string Remove(string source)
        {
            if (string.IsNullOrEmpty(source))
                return source;

            var cut = source.Length;
            while (true)
            {
                var end = cut;
                while (end > 0 && char.IsWhiteSpace(source[end - 1]))
                    end--;
                if (end == 0)
                    break;

                var start = LineStart(source, end);
                while (start > 0)
                {
                    var prevEnd = start - 1;
                    if (source[prevEnd] == '\n' && prevEnd > 0 && source[prevEnd - 1] == '\r')
                        prevEnd--;
                    var prevStart = LineStart(source, prevEnd);
                    if (!source.AsSpan(prevStart, prevEnd - prevStart).TrimEnd().EndsWith("_"))
                        break;
                    start = prevStart;
                }

                if (!IsFooterComment(source.AsSpan(start, end - start)))
                    break;
                cut = start;
            }
            return cut == source.Length ? source : source[..cut];
        }

        /// <summary>The anchor, or a metadata comment carrying 'uiOverrides' or 'fileInfo'. Substring check, since editors write the keys verbatim.</summary>
        public static bool IsFooterComment(ReadOnlySpan<char> text)
        {
            text = text.TrimStart();
            return text.Length > 0 && (text[0] == '\'' || text[0] == '"')
                && text.Contains("<!--", StringComparison.Ordinal)
                && (text.Contains("\"uiOverrides\"", StringComparison.Ordinal)
                    || text.Contains("\"fileInfo\"", StringComparison.Ordinal)
                    || text.Contains(Anchor, StringComparison.Ordinal));
        }

        private static int LineStart(string source, int pos) =>
            pos == 0 ? 0 : source.LastIndexOfAny(['\n', '\r'], pos - 1) + 1;
    }
}
