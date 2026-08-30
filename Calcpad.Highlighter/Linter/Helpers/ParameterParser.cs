using System;
using System.Collections.Generic;
using Calcpad.Highlighter.Tokenizer.Models;

namespace Calcpad.Highlighter.Linter.Helpers
{
    /// <summary>
    /// Helper for parsing semicolon-separated parameter lists in Calcpad, handling nested
    /// parentheses, braces and brackets. Uses span-based index tracking instead of StringBuilder
    /// to reduce allocations.
    /// </summary>
    public static class ParameterParser
    {
        /// <summary>
        /// Parses a semicolon-separated parameter string, ignoring semicolons inside parentheses,
        /// braces and brackets and preserving empty parameters. When <paramref name="lineTokens"/>
        /// and <paramref name="tokenGroups"/> are both provided, also partitions the line's tokens
        /// into per-parameter groups on the same boundaries, with <paramref name="paramStartCol"/>
        /// the column just inside the opening paren.
        /// </summary>
        public static List<string> ParseParameters(string paramsStr,
            List<Token> lineTokens = null, int paramStartCol = 0,
            List<List<Token>> tokenGroups = null)
        {
            if (string.IsNullOrWhiteSpace(paramsStr))
                return new List<string>();

            if (lineTokens == null || tokenGroups == null)
                return SplitByDelimiter(paramsStr, ';');

            // Split the string and record column boundaries for each segment
            var parameters = new List<string>();
            var boundaries = new List<(int startCol, int endCol)>();
            var span = paramsStr.AsSpan();
            int segStart = 0;
            int parenDepth = 0, braceDepth = 0, bracketDepth = 0;

            for (int i = 0; i < span.Length; i++)
            {
                var c = span[i];
                if (c == '(') parenDepth++;
                else if (c == ')') parenDepth--;
                else if (c == '{') braceDepth++;
                else if (c == '}') braceDepth--;
                else if (c == '[') bracketDepth++;
                else if (c == ']') bracketDepth--;

                if (c == ';' && parenDepth == 0 && braceDepth == 0 && bracketDepth == 0)
                {
                    parameters.Add(span[segStart..i].Trim().ToString());
                    boundaries.Add((paramStartCol + segStart, paramStartCol + i));
                    segStart = i + 1;
                }
            }

            if (segStart < span.Length || parameters.Count > 0)
            {
                parameters.Add(span[segStart..].Trim().ToString());
                boundaries.Add((paramStartCol + segStart, paramStartCol + span.Length));
            }

            // Partition tokens into groups using the column boundaries.
            // Both tokens and boundaries are sorted by column, so merge in one pass.
            int bIdx = 0;
            var group = new List<Token>();

            foreach (var token in lineTokens)
            {
                if (bIdx >= boundaries.Count)
                    break;

                if (token.Column < boundaries[bIdx].startCol)
                    continue;

                while (bIdx < boundaries.Count && token.Column >= boundaries[bIdx].endCol)
                {
                    tokenGroups.Add(group);
                    group = new List<Token>();
                    bIdx++;
                }

                if (bIdx < boundaries.Count && token.Column >= boundaries[bIdx].startCol &&
                    token.Column < boundaries[bIdx].endCol)
                {
                    group.Add(token);
                }
            }

            if (bIdx < boundaries.Count)
                tokenGroups.Add(group);
            while (tokenGroups.Count < parameters.Count)
                tokenGroups.Add(new List<Token>());

            return parameters;
        }

        /// <summary>
        /// Counts the parameters in a semicolon-separated string, ignoring semicolons inside
        /// parentheses, braces and brackets and including empty parameters. Zero-allocation: it
        /// just counts delimiters at depth 0.
        /// </summary>
        public static int CountParameters(string paramsStr)
        {
            if (string.IsNullOrWhiteSpace(paramsStr))
                return 0;

            int count = 1;
            int parenDepth = 0;
            int braceDepth = 0;
            int bracketDepth = 0;
            var span = paramsStr.AsSpan();

            for (int i = 0; i < span.Length; i++)
            {
                var c = span[i];
                if (c == '(') parenDepth++;
                else if (c == ')') parenDepth--;
                else if (c == '{') braceDepth++;
                else if (c == '}') braceDepth--;
                else if (c == '[') bracketDepth++;
                else if (c == ']') bracketDepth--;
                else if (c == ';' && parenDepth == 0 && braceDepth == 0 && bracketDepth == 0)
                    count++;
            }
            return count;
        }

        /// <summary>
        /// Parses macro call arguments using span-based index tracking.
        /// For macros, only parentheses are considered for nesting.
        /// </summary>
        public static List<string> ParseMacroParameters(string paramsStr)
        {
            if (string.IsNullOrWhiteSpace(paramsStr))
                return new List<string> { string.Empty };

            var result = new List<string>();
            var span = paramsStr.AsSpan();
            int segStart = 0;
            int parenDepth = 0;

            for (int i = 0; i < span.Length; i++)
            {
                var c = span[i];

                if (c == '(') parenDepth++;
                else if (c == ')') parenDepth--;

                if (c == ';' && parenDepth == 0)
                {
                    result.Add(span[segStart..i].Trim().ToString());
                    segStart = i + 1;
                }
            }

            // Add the last parameter
            if (segStart < span.Length || result.Count > 0)
            {
                result.Add(span[segStart..].Trim().ToString());
            }

            return result;
        }

        /// <summary>
        /// Splits content by a delimiter using span-based index tracking, ignoring delimiters
        /// inside parentheses, braces and brackets. Empty segments are preserved.
        /// </summary>
        public static List<string> SplitByDelimiter(string content, char delimiter)
        {
            var result = new List<string>();
            var span = content.AsSpan();
            int segStart = 0;
            var parenDepth = 0;
            var braceDepth = 0;
            var bracketDepth = 0;

            for (int i = 0; i < span.Length; i++)
            {
                var c = span[i];

                if (c == '(') parenDepth++;
                else if (c == ')') parenDepth--;
                else if (c == '{') braceDepth++;
                else if (c == '}') braceDepth--;
                else if (c == '[') bracketDepth++;
                else if (c == ']') bracketDepth--;

                // Only split on delimiter when not inside any brackets
                if (c == delimiter && parenDepth == 0 && braceDepth == 0 && bracketDepth == 0)
                {
                    result.Add(span[segStart..i].Trim().ToString());
                    segStart = i + 1;
                }
            }

            // Add the last parameter
            if (segStart < span.Length || result.Count > 0)
            {
                result.Add(span[segStart..].Trim().ToString());
            }

            return result;
        }
    }
}
