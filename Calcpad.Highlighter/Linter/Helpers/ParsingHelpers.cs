using System;
using System.Collections.Generic;
using System.Linq;
using Calcpad.Highlighter.Parsing;

namespace Calcpad.Highlighter.Linter.Helpers
{
    /// <summary>
    /// Shared parsing utilities for function definitions, parameter extraction,
    /// and block content parsing used across validators.
    /// </summary>
    public static class ParsingHelpers
    {
        /// <summary>
        /// Advances <paramref name="pos"/> past any whitespace characters in <paramref name="text"/>.
        /// </summary>
        public static void SkipWhitespace(ReadOnlySpan<char> text, ref int pos)
        {
            while (pos < text.Length && char.IsWhiteSpace(text[pos]))
                pos++;
        }

        /// <summary>
        /// Finds the position of the close character that matches the open character at
        /// <paramref name="openPos"/>, tracking nesting depth. Returns the index of the
        /// matching <paramref name="close"/>, or -1 if unbalanced.
        /// </summary>
        public static int FindMatchingClose(ReadOnlySpan<char> text, int openPos, char open, char close)
        {
            var depth = 0;
            for (int i = openPos; i < text.Length; i++)
            {
                var c = text[i];
                if (c == open) depth++;
                else if (c == close)
                {
                    depth--;
                    if (depth == 0) return i;
                }
            }
            return -1;
        }

        /// <summary>
        /// Splits "name = default" on the first '=' outside brackets. Returns false when there is no default.
        /// </summary>
        public static bool SplitParameterDefault(ReadOnlySpan<char> parameter,
            out ReadOnlySpan<char> name, out ReadOnlySpan<char> defaultValue)
        {
            var depth = 0;
            for (var i = 0; i < parameter.Length; i++)
            {
                var c = parameter[i];
                if (c == '(' || c == '[' || c == '{') depth++;
                else if (c == ')' || c == ']' || c == '}') depth--;
                else if (c == '=' && depth == 0)
                {
                    name = parameter[..i].Trim();
                    defaultValue = parameter[(i + 1)..].Trim();
                    return true;
                }
            }
            name = parameter.Trim();
            defaultValue = default;
            return false;
        }

        /// <summary>
        /// Splits a definition's parameter list into names and defaults. Defaults is null when
        /// no parameter has one; otherwise a null entry marks a required parameter.
        /// </summary>
        public static (List<string> Names, List<string> Defaults) ParseParamList(string paramsStr)
        {
            var names = new List<string>();
            var defaults = new List<string>();
            foreach (var param in ParameterParser.SplitByDelimiter(paramsStr, ';'))
            {
                if (param.Length == 0)
                    continue;

                var hasDefault = SplitParameterDefault(param, out var name, out var value);
                names.Add(name.ToString());
                defaults.Add(hasDefault ? value.ToString() : null);
            }
            return (names, defaults.Exists(d => d != null) ? defaults : null);
        }

        public static int CountRequiredParams(int paramCount, List<string> defaults) =>
            defaults?.Count(d => d == null) ?? paramCount;

        /// <summary>
        /// Names of required parameters that follow an optional one.
        /// </summary>
        public static IEnumerable<string> RequiredAfterOptional(List<string> names, List<string> defaults)
        {
            var first = defaults?.FindIndex(d => d != null) ?? -1;
            for (var i = first + 1; first >= 0 && i < names.Count; i++)
                if (defaults[i] == null)
                    yield return names[i];
        }

        /// <summary>
        /// Parses a "name = value" call argument. "==" is a comparison, not a keyword argument.
        /// </summary>
        public static bool TryParseKeywordArg(string arg, out string name, out string value)
        {
            name = value = null;
            if (string.IsNullOrWhiteSpace(arg) ||
                !SplitParameterDefault(arg.AsSpan().Trim(), out var nameSpan, out var valueSpan) ||
                valueSpan.StartsWith('=') || !IsIdentifier(nameSpan))
                return false;

            name = nameSpan.ToString();
            value = valueSpan.ToString();
            return true;
        }

        private static bool IsIdentifier(ReadOnlySpan<char> s)
        {
            if (s.IsEmpty || !CalcpadCharacterHelpers.IsIdentifierStartCharWithUnderscore(s[0]))
                return false;

            var last = s[^1] == '$' ? s.Length - 1 : s.Length;
            for (var i = 1; i < last; i++)
                if (!CalcpadCharacterHelpers.IsIdentifierChar(s[i]))
                    return false;

            return true;
        }

        /// <summary>
        /// Extracts function parameter names from a function definition line.
        /// For example: "square(x) = x^2" returns {"x"}
        /// </summary>
        public static HashSet<string> GetFunctionParamsFromLine(string line)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            var lineSpan = line.AsSpan();

            // Look for function definition pattern: name(params) = ...
            var parenOpen = line.IndexOf('(');
            if (parenOpen < 0)
                return result;

            var parenClose = FindMatchingClose(lineSpan, parenOpen, '(', ')');
            if (parenClose < 0)
                return result;

            var equalsSign = line.IndexOf('=', parenClose);
            if (equalsSign < 0)
                return result;

            // Check that something comes before the paren (function name)
            var beforeParen = lineSpan[..parenOpen].Trim();
            if (beforeParen.Length == 0 || !char.IsLetter(beforeParen[0]))
                return result;

            // Extract the parameters using SplitEnumerator (zero-alloc splitting)
            var paramsSection = lineSpan[(parenOpen + 1)..parenClose];
            foreach (var paramSpan in new SplitEnumerator(paramsSection, ';'))
            {
                var trimmed = paramSpan.Trim();
                if (trimmed.Length > 0)
                {
                    SplitParameterDefault(trimmed, out var name, out _);
                    if (!name.IsEmpty)
                        result.Add(name.ToString());
                }
            }

            return result;
        }

        /// <summary>
        /// Extract the parameter string from a function/macro call (content between parentheses).
        /// Returns a tuple with found=true if a valid parameter string was extracted.
        /// </summary>
        public static (bool found, string paramsStr) ExtractParamsString(string line, int afterFuncName)
        {
            // Skip whitespace to find opening paren
            var pos = afterFuncName;
            SkipWhitespace(line, ref pos);

            if (pos >= line.Length || line[pos] != '(')
                return (false, string.Empty);

            var startPos = pos + 1; // After opening paren
            var closePos = FindMatchingClose(line, pos, '(', ')');

            if (closePos >= 0)
                return (true, line.Substring(startPos, closePos - startPos));

            // Unbalanced - return what we have
            return (true, line.Substring(startPos));
        }

        /// <summary>
        /// Finds the position of the closing parenthesis after a function name.
        /// Returns the position after the closing paren, or afterFuncName if no paren found.
        /// </summary>
        public static int FindClosingParen(string line, int afterFuncName)
        {
            var pos = afterFuncName;
            SkipWhitespace(line, ref pos);

            if (pos >= line.Length || line[pos] != '(')
                return afterFuncName;

            var closePos = FindMatchingClose(line, pos, '(', ')');
            return closePos >= 0 ? closePos + 1 : line.Length;
        }

        /// <summary>
        /// Extracts content inside a block (between { and matching }).
        /// Handles nested braces properly.
        /// </summary>
        public static string ExtractBlockContent(string line, int braceStart)
        {
            var start = braceStart + 1;
            var closePos = FindMatchingClose(line, braceStart, '{', '}');
            return closePos >= 0
                ? line.Substring(start, closePos - start)
                : line.Substring(start);
        }

        /// <summary>
        /// Finds the position of the closing brace matching the opening brace at braceStart.
        /// Returns the position of the closing }, or line.Length if unbalanced.
        /// </summary>
        public static int FindClosingBrace(string line, int braceStart)
        {
            var closePos = FindMatchingClose(line, braceStart, '{', '}');
            return closePos >= 0 ? closePos : line.Length;
        }
    }
}
