using System;

namespace Calcpad.Highlighter.Linter.Helpers
{
    /// <summary>
    /// Shared character classification utilities for Calcpad identifiers.
    /// Handles Greek letters, special math characters, subscripts, and superscripts.
    /// </summary>
    public static class CalcpadCharacterHelpers
    {
        /// <summary>
        /// Checks if a character is a Greek letter (lowercase α-ω or uppercase Α-Ω).
        /// </summary>
        public static bool IsGreekLetter(char c)
        {
            return (c >= '\u03B1' && c <= '\u03C9') || (c >= '\u0391' && c <= '\u03A9');
        }

        /// <summary>
        /// Checks if a character is a special math character that can appear in identifiers.
        /// Includes: ° (degree), ø/Ø (diameter), ∡ (angle), ℧ (mho)
        /// </summary>
        public static bool IsSpecialMathChar(char c)
        {
            return c == '°' || c == 'ø' || c == 'Ø' || c == '∡' || c == '℧';
        }

        /// <summary>
        /// Checks if a character is a subscript digit (₀-₉).
        /// </summary>
        public static bool IsSubscriptDigit(char c)
        {
            return c >= '₀' && c <= '₉';
        }

        /// <summary>
        /// Checks if a character is a superscript digit (⁰-⁹).
        /// </summary>
        public static bool IsSuperscriptDigit(char c)
        {
            return c >= '⁰' && c <= '⁹';
        }

        /// <summary>
        /// Checks if a character can START an identifier in Calcpad.
        /// Valid: letters, Greek letters, special math chars (°, ø, Ø, ∡, ℧)
        /// NOT valid as start: underscore, digits, subscripts, superscripts
        /// </summary>
        public static bool IsIdentifierStartChar(char c)
        {
            return char.IsLetter(c) || IsGreekLetter(c) || IsSpecialMathChar(c);
        }

        /// <summary>
        /// Checks if a character can START an identifier in Calcpad, including underscore.
        /// Used for variable assignment detection where underscore is valid.
        /// </summary>
        public static bool IsIdentifierStartCharWithUnderscore(char c)
        {
            return char.IsLetter(c) || c == '_' || IsGreekLetter(c);
        }

        /// <summary>
        /// Checks if a character can appear inside an identifier (after the first character).
        /// Valid: letters, digits, underscore, Greek letters, subscripts, superscripts,
        /// combining marks (acute, macron, dot above, diaeresis).
        /// </summary>
        public static bool IsIdentifierChar(char c)
        {
            return char.IsLetterOrDigit(c) || c == '_' ||
                   IsGreekLetter(c) ||
                   IsSubscriptDigit(c) || IsSuperscriptDigit(c) ||
                   IsCombiningMark(c);
        }

        /// <summary>
        /// Combining marks supported as identifier-continuation characters: acute (̲́),
        /// macron (̄), dot above (̇), and diaeresis (̈). They attach to the preceding
        /// base letter, so they are valid inside an identifier but cannot start one.
        /// </summary>
        public static bool IsCombiningMark(char c)
        {
            return c == '́' || c == '̄' || c == '̇' || c == '̈';
        }

        /// <summary>
        /// Checks if a character can appear inside an identifier, including $ for macros.
        /// </summary>
        public static bool IsIdentifierCharWithDollar(char c)
        {
            return IsIdentifierChar(c) || c == '$';
        }

        /// <summary>
        /// Checks if a character is a "letter" for tokenization purposes.
        /// Includes letters, underscore, Greek letters, and special math chars.
        /// </summary>
        public static bool IsLetterForTokenizer(char c)
        {
            return char.IsLetter(c) || c == '_' || IsGreekLetter(c) || IsSpecialMathChar(c);
        }

        /// <summary>
        /// Checks if a character can start a unit symbol.
        /// Includes: ° (degree), µ/μ (micro), ‰ (per mille), ‱ (per ten thousand)
        /// </summary>
        public static bool IsUnitStart(char c)
        {
            return c == '°' || c == 'µ' || c == 'μ' || c == '‰' || c == '‱';
        }

        /// <summary>
        /// Extracts an identifier from the start of a string, stopping at the first non-identifier
        /// character. Uses span slicing instead of StringBuilder for zero intermediate allocations.
        /// </summary>
        public static string ExtractIdentifier(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            var span = text.AsSpan();
            int end = 0;
            while (end < span.Length && IsIdentifierChar(span[end]))
                end++;

            return end == 0 ? string.Empty : span[..end].ToString();
        }

        /// <summary>
        /// Checks if a character is valid in a macro name: letters, underscores, and digits after
        /// the first position. Matches Calcpad.Core's Validator.IsMacroLetter behavior.
        /// </summary>
        public static bool IsMacroLetter(char c, int position)
        {
            return (c >= 'a' && c <= 'z') ||
                   (c >= 'A' && c <= 'Z') ||
                   c == '_' ||
                   (char.IsDigit(c) && position > 0);
        }
    }
}
