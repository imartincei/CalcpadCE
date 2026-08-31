using System;
using System.Text;
using Calcpad.Highlighter.Linter.Constants;

namespace Calcpad.Highlighter.Prettifier
{
    /// <summary>
    /// Re-indents Calcpad source by tracking control-block depth across
    /// <c>#if</c>/<c>#else</c>/<c>#end if</c>, <c>#for</c>/<c>#while</c>/<c>#repeat</c>/<c>#loop</c>,
    /// multiline <c>#def</c>/<c>#end def</c>, and HTML <c>&lt;div&gt;</c>/<c>&lt;/div&gt;</c> blocks.
    /// Inline <c>#def name = ...</c> and <c>&lt;div&gt;...&lt;/div&gt;</c> do not open blocks.
    ///
    /// The prettifier only adjusts leading whitespace; line content, comments, and
    /// the original line-ending style (CRLF vs LF) are preserved.
    /// </summary>
    public static class CalcpadPrettifier
    {
        public static string Prettify(string source, PrettifierOptions options = null)
        {
            if (string.IsNullOrEmpty(source))
                return source ?? string.Empty;

            options ??= PrettifierOptions.Default;
            var indentUnit = options.IndentUnit ?? "\t";
            var sb = new StringBuilder(source.Length);
            var depth = 0;
            var divDepth = 0;
            var pos = 0;

            while (pos < source.Length)
            {
                // Find end of line and capture the line ending so it can be preserved
                var lineStart = pos;
                while (pos < source.Length && source[pos] != '\n' && source[pos] != '\r')
                    pos++;
                var contentEnd = pos;

                string lineEnding = "";
                if (pos < source.Length)
                {
                    if (source[pos] == '\r' && pos + 1 < source.Length && source[pos + 1] == '\n')
                    {
                        lineEnding = "\r\n";
                        pos += 2;
                    }
                    else
                    {
                        lineEnding = source[pos].ToString();
                        pos++;
                    }
                }

                var rawLine = source.Substring(lineStart, contentEnd - lineStart);
                var trimmed = rawLine.Trim();

                if (options.TrimTrailingWhitespace)
                    trimmed = TrimTrailingWhitespace(trimmed);

                if (trimmed.Length == 0)
                {
                    sb.Append(lineEnding);
                    continue;
                }

                var divTags = GetDivTags(trimmed);
                divDepth = Math.Max(0, divDepth - divTags.LeadingClosures);

                var blockType = CalcpadBuiltIns.GetBlockType(trimmed);
                int renderDepth;

                switch (blockType)
                {
                    // Block enders: dedent first, render at the new (outer) depth.
                    case ControlBlockType.EndIf:
                    case ControlBlockType.Loop:
                    case ControlBlockType.EndDef:
                        depth = Math.Max(0, depth - 1);
                        renderDepth = depth;
                        break;

                    // Mid-block keywords: visually dedent for this line, but keep
                    // the inner depth for following lines.
                    case ControlBlockType.Else:
                    case ControlBlockType.ElseIf:
                        renderDepth = Math.Max(0, depth - 1);
                        break;

                    // Block starters: render at current depth, then indent following lines.
                    case ControlBlockType.If:
                    case ControlBlockType.Repeat:
                    case ControlBlockType.For:
                    case ControlBlockType.While:
                        renderDepth = depth;
                        depth++;
                        break;

                    // #def is a starter only when multiline. Inline form (#def name = ...
                    // or #def f(x) = ...) contains '=' and does not open a block --
                    // mirrors BalanceValidator.
                    case ControlBlockType.Def:
                        renderDepth = depth;
                        if (!trimmed.Contains('='))
                            depth++;
                        break;

                    default:
                        renderDepth = depth;
                        break;
                }

                AppendIndent(sb, indentUnit, renderDepth + divDepth);
                sb.Append(trimmed);
                sb.Append(lineEnding);

                divDepth += divTags.Openings;
                divDepth = Math.Max(0, divDepth - (divTags.Closures - divTags.LeadingClosures));
            }

            return sb.ToString();
        }

        private static (int LeadingClosures, int Openings, int Closures) GetDivTags(string line)
        {
            if (line.Length < 2 || line[0] != '\'')
                return (0, 0, 0);

            var html = line.AsSpan(1).TrimStart();
            var leadingClosures = 0;
            var openings = 0;
            var closures = 0;
            var onlyLeadingClosures = true;

            for (int pos = 0; pos < html.Length;)
            {
                var relativeStart = html.Slice(pos).IndexOf('<');
                if (relativeStart < 0)
                    break;

                var tagStart = pos + relativeStart;
                if (!IsWhitespace(html.Slice(pos, relativeStart)))
                    onlyLeadingClosures = false;

                var tag = html.Slice(tagStart);
                var isClosing = tag.StartsWith("</div", StringComparison.OrdinalIgnoreCase);
                var nameLength = isClosing ? 5 : 4;
                var isOpening = !isClosing && tag.StartsWith("<div", StringComparison.OrdinalIgnoreCase);
                var end = tag.IndexOf('>');

                if ((isOpening || isClosing) &&
                    tag.Length > nameLength &&
                    (tag[nameLength] == '>' || char.IsWhiteSpace(tag[nameLength])) &&
                    end >= nameLength)
                {
                    if (isClosing)
                    {
                        closures++;
                        if (onlyLeadingClosures)
                            leadingClosures++;
                    }
                    else
                    {
                        openings++;
                        onlyLeadingClosures = false;
                    }

                    pos = tagStart + end + 1;
                }
                else
                {
                    onlyLeadingClosures = false;
                    pos = tagStart + 1;
                }
            }

            return (leadingClosures, openings, closures);
        }

        private static bool IsWhitespace(ReadOnlySpan<char> text)
        {
            foreach (var c in text)
                if (!char.IsWhiteSpace(c))
                    return false;

            return true;
        }

        private static void AppendIndent(StringBuilder sb, string indentUnit, int depth)
        {
            for (int i = 0; i < depth; i++)
                sb.Append(indentUnit);
        }

        private static string TrimTrailingWhitespace(string s)
        {
            int end = s.Length;
            while (end > 0 && (s[end - 1] == ' ' || s[end - 1] == '\t'))
                end--;
            return end == s.Length ? s : s.Substring(0, end);
        }
    }
}
