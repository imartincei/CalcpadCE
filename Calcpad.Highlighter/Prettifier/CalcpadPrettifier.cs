using System;
using System.Text;
using Calcpad.Highlighter.Linter.Constants;

namespace Calcpad.Highlighter.Prettifier
{
    /// <summary>
    /// Re-indents Calcpad source by tracking control-block depth across
    /// <c>#if</c>/<c>#else</c>/<c>#end if</c>, <c>#for</c>/<c>#while</c>/<c>#repeat</c>/<c>#loop</c>,
    /// and multiline <c>#def</c>/<c>#end def</c>, where inline <c>#def name = ...</c> does not open
    /// a block. Only leading whitespace is adjusted; line content, comments and the original
    /// line-ending style are preserved.
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

                AppendIndent(sb, indentUnit, renderDepth);
                sb.Append(trimmed);
                sb.Append(lineEnding);
            }

            return sb.ToString();
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
