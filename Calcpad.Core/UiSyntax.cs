using System;
using System.Collections.Generic;

namespace Calcpad.Core
{
    public static class UiSyntax
    {
        public static List<(string Name, string Rhs)> EnumerateAssignments(ReadOnlySpan<char> s)
        {
            var found = new List<(string, string)>();
            foreach (var segment in s.EnumerateComments())
            {
                if (!IsCode(segment))
                    continue;

                var i = segment.IndexOf('=');
                if (i < 1)
                    continue;

                var name = segment[..i].Trim().ToString();
                if (name.Length != 0)
                    found.Add((name, segment[(i + 1)..].Trim().ToString()));
            }
            return found;
        }

        /// <summary>True for a segment of <see cref="CommentEnumerator"/> that is code, not a comment.</summary>
        public static bool IsCode(ReadOnlySpan<char> segment) =>
            !segment.IsEmpty && segment[0] != '\'' && segment[0] != '"';

        public static bool IsValue(ReadOnlySpan<char> rhs)
        {
            rhs = rhs.Trim();
            if (rhs.IsEmpty)
                return true;

            if (rhs[0] == '[')
                return IsMatrixLiteral(rhs);

            return IsConstructor(rhs, "vector") ||
                IsConstructor(rhs, "matrix") ||
                IsNumber(rhs);
        }

        private static bool IsMatrixLiteral(ReadOnlySpan<char> s)
        {
            if (s[^1] != ']')
                return false;

            foreach (var row in s[1..^1].EnumerateSplits('|'))
                foreach (var cell in row.EnumerateSplits(';'))
                {
                    var value = cell.Trim();
                    if (!value.IsEmpty && !IsNumber(value))
                        return false;
                }

            return true;
        }

        private static bool IsConstructor(ReadOnlySpan<char> s, ReadOnlySpan<char> name)
        {
            if (!s.StartsWith(name, StringComparison.OrdinalIgnoreCase))
                return false;

            var rest = s[name.Length..].TrimStart();
            return rest.Length > 1 && rest[0] == '(' && ClosesAtEnd(rest);
        }

        private static bool ClosesAtEnd(ReadOnlySpan<char> s)
        {
            var depth = 0;
            for (var i = 0; i < s.Length; ++i)
            {
                if (s[i] == '(')
                    ++depth;
                else if (s[i] == ')' && --depth == 0)
                    return i == s.Length - 1;
            }
            return false;
        }

        private static bool IsNumber(ReadOnlySpan<char> s)
        {
            if (s.IsEmpty)
                return false;

            var i = s[0] is '+' or '-' or '−' ? 1 : 0;
            var digits = SkipDigits(s, ref i);
            if (i < s.Length && s[i] == '.')
            {
                ++i;
                digits += SkipDigits(s, ref i);
            }
            return digits > 0 && IsUnits(s[i..]);
        }

        private static bool IsUnits(ReadOnlySpan<char> s)
        {
            if (s.IsWhiteSpace())
                return true;

            var expectName = true;
            for (var i = 0; i < s.Length;)
            {
                var c = s[i];
                if (c == ' ')
                    ++i;
                else if (expectName)
                {
                    if (!IsUnitChar(c))
                        return false;

                    while (i < s.Length && IsUnitChar(s[i]))
                        ++i;

                    expectName = false;
                }
                else if (c == '^')
                {
                    ++i;
                    if (!IsPower(s, ref i))
                        return false;
                }
                else if (c is '*' or '/' or '·' or '×' or '∙')
                {
                    ++i;
                    expectName = true;
                }
                else
                    return false;
            }
            return !expectName;
        }

        private static bool IsPower(ReadOnlySpan<char> s, ref int i)
        {
            while (i < s.Length && s[i] == ' ')
                ++i;

            if (i < s.Length && s[i] is '+' or '-' or '−')
                ++i;

            var digits = SkipDigits(s, ref i);
            if (i < s.Length && s[i] == '.')
            {
                ++i;
                digits += SkipDigits(s, ref i);
            }
            return digits > 0;
        }

        private static int SkipDigits(ReadOnlySpan<char> s, ref int i)
        {
            var start = i;
            while (i < s.Length && char.IsAsciiDigit(s[i]))
                ++i;

            return i - start;
        }

        private static bool IsUnitChar(char c) =>
            char.IsLetter(c) || c is '°' or '%' or '‰' or '‱' or '′' or '″' or '℧' or '_';
    }
}
