using System;
using System.Buffers;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Calcpad.Core
{
    public static class Validator
    {
        // For custom currency units.
        private static readonly char[] CurrencyChars =
        [
            '€', '£', '₤', '¥', '¢', '₽', '₹', '₩', '₪',
        ];

        private static readonly char[] UnitSymbolChars =
        [
            '°', '′', '″', '%', '‰', '‱',
        ];

        // Combining marks attach to a preceding base letter, so they're allowed
        // inside an identifier but never as a starter.
        private static readonly char[] VarSymbolChars =
        [
            ',', '_', '‾', '‴', '⁗',
            '́', // combining acute
            '̄', // combining macron
            '̇', // combining dot above
            '̈', // combining diaeresis
        ];

        private static readonly char[] VarNonLetterChars = ['℧', '∡'];
        private static readonly char[] VarLetterChars = ['ϑ', 'ϕ', 'ø', 'Ø'];

        private static readonly char[] SuperscriptChars =
        [
            '⁰', '¹', '²', '³', '⁴', '⁵', '⁶', '⁷', '⁸', '⁹',
            '⁺', '⁻', '⁼', '⁽', '⁾',
        ];

        private static readonly char[] SubscriptChars =
        [
            '₀', '₁', '₂', '₃', '₄', '₅', '₆', '₇', '₈', '₉',
            '₊', '₋', '₌', '₍', '₎',
        ];

        internal static readonly SearchValues<char> UnitChars =
            SearchValues.Create([.. UnitSymbolChars, .. CurrencyChars]);

        private static readonly SearchValues<char> VarStartingChars =
            SearchValues.Create([.. UnitSymbolChars, .. CurrencyChars, .. VarNonLetterChars]);

        private static readonly SearchValues<char> VarChars = SearchValues.Create(
        [
            .. UnitSymbolChars, .. CurrencyChars, .. VarNonLetterChars,
            .. VarSymbolChars, .. SuperscriptChars, .. SubscriptChars, .. VarLetterChars,
        ]);

        private static readonly Regex MyFormatRegex = new(@"^[FCEGND]\d{0,2}$|^[0#]+(,[0#]+)?(\.[0#]+)?([eE][+-]?0+)?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static bool IsVariable(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;

            var c = name[0];
            if (!IsVarStartingChar(c))
                return false;

            for (int i = 1, len = name.Length; i < len; ++i)
            {
                c = name[i];
                if (!(IsVarChar(c)))
                    return false;
            }
            return true;
        }

        public static bool IsPlot(string text)
        {
            var s = text.TrimStart();
            if (!string.IsNullOrEmpty(s) && s[0] == '$')
                return s.StartsWith("$plot", StringComparison.OrdinalIgnoreCase);

            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsKeyword(ReadOnlySpan<char> s, ReadOnlySpan<char> keyword) =>
            s.TrimStart().StartsWith(keyword, StringComparison.OrdinalIgnoreCase);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsMacroLetter(char c, int position) =>
            c >= 'a' && c <= 'z' ||
            c >= 'A' && c <= 'Z' ||
            c == '_' ||
            char.IsDigit(c) && position > 0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsLetter(char c) =>
            char.IsLetter(c) || VarChars.Contains(c);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsLatinLetter(char c) =>
            c >= 'a' && c <= 'z' || // a - z
            c >= 'A' && c <= 'Z';   // A - Z

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsDigit(char c) =>
            c >= '0' && c <= '9' || c == MathParser.DecimalSymbol;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsUnitStart(char c) => UnitChars.Contains(c);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsWhiteSpace(char c) => c == ' ' || c == '\t';

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsVarAdditionalChar(char c) =>
            VarChars.Contains(c);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsVarStartingChar(char c) =>
            char.IsLetter(c) ||
            VarStartingChars.Contains(c);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsVarChar(char c) => IsLetter(c) || char.IsAsciiDigit(c);

        public static bool IsValidFormatString(string format)
        {
            try
            {
                if(format.StartsWith('D'))
                    1.ToString(format, CultureInfo.CurrentCulture);
                else
                    1d.ToString(format, CultureInfo.CurrentCulture);
                return MyFormatRegex.Match(format).Success;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        public static bool IsComment(ReadOnlySpan<char> s)
        {
            var count = 0;
            var commentChar = '\0';
            for (int i = 0, len = s.Length; i < len; ++i)
            {
                var c = s[i];
                if (commentChar == '\0')
                {
                    if (c == '"' || c == '\'')
                    {
                        commentChar = c;
                        count = 1;
                    }
                }
                else if (c == commentChar)
                    ++count;
            }
            return count % 2 == 1;
        }
    }
}
