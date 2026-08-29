using System;
using System.Text;
using Calcpad.Highlighter.Parsing;

namespace Calcpad.Highlighter.Tokenizer
{
    public partial class CalcpadTokenizer
    {
        private static bool IsBracket(char c) => CharClassifier.IsBracket(c);

        private static bool IsDelimiter(char c) => CharClassifier.IsDelimiter(c);

        // Tokenizer-internal macro-identifier scan: permissive (Unicode letters allowed, '$'
        // allowed after position 0), used to find an identifier boundary in the token builder
        // before emitting. Distinct from CalcpadCharacterHelpers.IsMacroLetter, which matches
        // Core's strict Latin-only validator and answers whether a name *is* a valid macro
        // identifier.
        private static bool IsMacroIdentChar(char c, int position)
        {
            if (position == 0)
                return char.IsLetter(c) || c == '_';
            return char.IsLetterOrDigit(c) || c == '_' || c == '$';
        }

        private static bool IsPlotLine(ReadOnlySpan<char> text)
        {
            var trimmed = text.TrimStart();
            return trimmed.StartsWith("$plot", StringComparison.OrdinalIgnoreCase) ||
                   trimmed.StartsWith("$map", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Checks if position i in text starts the closing tag for the current special content block.
        /// Maps the SpecialContentType to its tag name and delegates to IsClosingSpecialTag.
        /// </summary>
        private static bool IsClosingSpecialContentTag(ReadOnlySpan<char> text, int i, SpecialContentType content)
        {
            var tagName = content switch
            {
                SpecialContentType.Script => "script",
                SpecialContentType.Style => "style",
                SpecialContentType.Svg => "svg",
                _ => null
            };
            return tagName != null && IsClosingSpecialTag(text, i, tagName);
        }

        /// <summary>
        /// Checks if position i in text starts a closing tag for the given tag name (e.g., "&lt;/script&gt;").
        /// Requires the full pattern &lt;/tagName&gt; and ignores matches inside string literals (" or `).
        /// </summary>
        private static bool IsClosingSpecialTag(ReadOnlySpan<char> text, int i, string tagName)
        {
            // Need at least </tagName> characters remaining
            var needed = 3 + tagName.Length; // "</" + tagName + ">"
            if (i + needed > text.Length)
                return false;

            if (text[i] != '<' || text[i + 1] != '/')
                return false;

            if (!text.Slice(i + 2, tagName.Length).Equals(tagName.AsSpan(), StringComparison.OrdinalIgnoreCase))
                return false;

            if (text[i + 2 + tagName.Length] != '>')
                return false;

            // Ignore closing tags inside JS/CSS string literals (preceded by " or `)
            if (i > 0 && (text[i - 1] == '"' || text[i - 1] == '`'))
                return false;

            return true;
        }

        private static bool IsContinuedCondition(string text)
        {
            return text.Equals("#else", StringComparison.OrdinalIgnoreCase) ||
                   text.Equals("#end", StringComparison.OrdinalIgnoreCase) ||
                   text.Equals("#md", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Checks if a StringBuilder contains a continued condition keyword without allocating a string.
        /// Avoids the StringBuilder.ToString() allocation in the hot ParseSpace path.
        /// </summary>
        private static bool IsContinuedConditionBuilder(StringBuilder builder)
        {
            var len = builder.Length;
            if (len < 3 || builder[0] != '#')
                return false;

            if (len == 5 && builder[1] == 'e' && builder[2] == 'l' && builder[3] == 's' && builder[4] == 'e')
                return true;
            if (len == 5 && builder[1] == 'E' && builder[2] == 'L' && builder[3] == 'S' && builder[4] == 'E')
                return true;
            if (len == 4 && builder[1] == 'e' && builder[2] == 'n' && builder[3] == 'd')
                return true;
            if (len == 4 && builder[1] == 'E' && builder[2] == 'N' && builder[3] == 'D')
                return true;
            if (len == 3 && builder[1] == 'm' && builder[2] == 'd')
                return true;
            if (len == 3 && builder[1] == 'M' && builder[2] == 'D')
                return true;

            return false;
        }

        /// <summary>
        /// Checks if the current position in a data exchange line marks the end of a file path.
        /// File paths end when followed by keywords like "sep", "type", or a comment.
        /// </summary>
        private static bool IsFilePathEndMarker(ReadOnlySpan<char> text, int spaceIndex)
        {
            if (spaceIndex + 1 >= text.Length)
                return false;

            var remaining = text[(spaceIndex + 1)..].TrimStart();
            if (remaining.Length == 0)
                return false;

            // Check for data exchange option keywords
            if (remaining.StartsWith("sep", StringComparison.OrdinalIgnoreCase) ||
                remaining.StartsWith("type", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Checks if a line contains a function definition pattern: name(params) = expression
        /// Used to determine if parameters inside parentheses should be marked as LocalVariable.
        /// </summary>
        private static bool IsFunctionDefinitionLine(ReadOnlySpan<char> text, int startPos)
        {
            // Look for pattern: identifier( ... ) =
            // We need to find the matching close paren and check if = follows
            var parenDepth = 0;
            var foundOpenParen = false;

            for (int i = startPos; i < text.Length; i++)
            {
                var c = text[i];

                // Skip if we're in a comment
                if (c == '\'' || c == '"')
                    return false;

                if (c == '(')
                {
                    parenDepth++;
                    foundOpenParen = true;
                }
                else if (c == ')')
                {
                    parenDepth--;
                    if (foundOpenParen && parenDepth == 0)
                    {
                        // Found matching close paren, check for = after it
                        var afterParen = text[(i + 1)..].TrimStart();
                        return afterParen.Length > 0 && afterParen[0] == '=' &&
                               (afterParen.Length == 1 || afterParen[1] != '='); // Exclude ==
                    }
                }
            }

            return false;
        }
    }
}
