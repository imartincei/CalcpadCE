using System;
using System.Collections.Generic;

namespace Calcpad.Core
{
    /// <summary>
    /// What a #UI line may declare, shared by <see cref="ExpressionParser"/> and the
    /// linter so both accept the same lines. A control can bind to a number with an
    /// optional unit, a vector/matrix literal, or a vector()/matrix() constructor -
    /// not to an expression, which would be overwritten by the value it computed as
    /// soon as the control wrote an entry back into the source.
    /// </summary>
    public static class UiSyntax
    {
        /// <summary>
        /// Every assignment on the line, in source order. The line's code and comment
        /// segments alternate, so each code segment holds at most one assignment; segments
        /// that assign nothing - a bare output expression - contribute no control.
        /// </summary>
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

        /// <summary>
        /// True when <paramref name="rhs"/> is a form a control can bind to. An empty
        /// right hand side counts as one - it is a syntax error MathParser reports itself.
        /// </summary>
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

        /// <summary>
        /// True for a closed bracket literal whose every cell is a number. Empty cells are
        /// accepted - the parser fills them with zeros to match a declared grid size.
        /// </summary>
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

        /// <summary>
        /// True for a vector(...) or matrix(...) call and nothing else on the line. The
        /// arguments may be any expression - matrix(len(x); len(y)) - since the grid is
        /// sized from the vector or matrix the call evaluates to rather than from its text.
        /// The argument count is left to MathParser, which reports a wrong one itself.
        /// </summary>
        private static bool IsConstructor(ReadOnlySpan<char> s, ReadOnlySpan<char> name)
        {
            if (!s.StartsWith(name, StringComparison.OrdinalIgnoreCase))
                return false;

            var rest = s[name.Length..].TrimStart();
            return rest.Length > 1 && rest[0] == '(' && ClosesAtEnd(rest);
        }

        /// <summary>
        /// True when the bracket opening <paramref name="s"/> is closed by its last
        /// character, i.e. the call is the whole expression rather than a term of one.
        /// </summary>
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

        /// <summary>
        /// True for a signed decimal number with an optional trailing unit expression.
        /// There is no exponent form - MathParser reads the 'e' of 2.5e6 as a unit.
        /// </summary>
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

        /// <summary>
        /// True for a product, quotient and power expression of unit names - kN/m,
        /// m^2, kg*m/s^2 - or for nothing at all, a dimensionless value.
        /// </summary>
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

        /// <summary>A signed decimal exponent, as in m^-2 or m^0.5.</summary>
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
