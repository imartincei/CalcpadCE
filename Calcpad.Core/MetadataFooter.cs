using System;
using System.Linq;

namespace Calcpad.Core
{
    /// <summary>The footer CalcpadCE editors keep at the end of a .cpd file: anchor, then 'uiOverrides', then 'fileInfo'.</summary>
    public static class MetadataFooter
    {
        public const string Anchor = "<!--CalcpadCE Metadata-->";

        /// <summary>Cuts the trailing footer off <paramref name="source"/>, leaving every line above it as is.</summary>
        /// <param name="keepLineCount">Blank the footer's lines instead of removing them.</param>
        public static string Remove(string source, bool keepLineCount = false)
        {
            if (string.IsNullOrEmpty(source))
                return source;

            var lines = source.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
            var start = FindStart(lines);
            if (start == lines.Length)
                return source;

            var cut = LineOffset(source, start);
            return keepLineCount
                ? source[..cut] + new string(source[cut..].Where(c => c is '\r' or '\n').ToArray())
                : source[..cut];
        }

        // Mirrors the editors' findMetadataFooter.
        private static int FindStart(string[] lines)
        {
            var start = lines.Length;
            var end = LastContentLine(lines, lines.Length);
            var block = CommentStart(lines, end);
            if (block >= 0 && BlockContains(lines, block, end, "\"fileInfo\""))
            {
                start = block;
                end = LastContentLine(lines, block);
                block = CommentStart(lines, end);
            }
            if (block >= 0 && BlockContains(lines, block, end, "\"uiOverrides\""))
            {
                start = block;
                end = LastContentLine(lines, block);
            }
            if (end >= 0 && lines[end].Trim() is "'" + Anchor or "\"" + Anchor)
                start = end;

            return start;
        }

        private static int LastContentLine(string[] lines, int before)
        {
            for (var i = before - 1; i >= 0; --i)
                if (!string.IsNullOrWhiteSpace(lines[i]))
                    return i;

            return -1;
        }

        // Opening line of the metadata comment that ends on 'end', following '_' continuations.
        private static int CommentStart(string[] lines, int end)
        {
            if (end < 0 || !lines[end].Contains("-->", StringComparison.Ordinal))
                return -1;

            var i = end;
            while (!IsOpener(lines[i]) && i > 0 && lines[i - 1].TrimEnd().EndsWith('_'))
                --i;

            return IsOpener(lines[i]) ? i : -1;
        }

        private static bool IsOpener(string line)
        {
            var trimmed = line.TrimStart();
            return (trimmed.StartsWith('\'') || trimmed.StartsWith('"'))
                && trimmed.Contains("<!--{", StringComparison.Ordinal);
        }

        private static bool BlockContains(string[] lines, int start, int end, string key)
        {
            for (var i = start; i <= end; ++i)
                if (lines[i].Contains(key, StringComparison.Ordinal))
                    return true;

            return false;
        }

        private static int LineOffset(string source, int line)
        {
            var i = 0;
            for (; line > 0; --line)
            {
                i = source.IndexOfAny(['\r', '\n'], i);
                i += source[i] == '\r' && i + 1 < source.Length && source[i + 1] == '\n' ? 2 : 1;
            }
            return i;
        }
    }
}
