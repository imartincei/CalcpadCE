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
        /// braces and brackets and preserving empty parameters.
        /// </summary>
        public static List<string> ParseParameters(string paramsStr) =>
            string.IsNullOrWhiteSpace(paramsStr) ? new List<string>() : SplitByDelimiter(paramsStr, ';');

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
