using System;
using Calcpad.Highlighter.Linter.Helpers;

namespace Calcpad.Highlighter.Parsing
{
    /// <summary>
    /// Character classification categories for the tokenizer's main parsing loop.
    /// Provides O(1) lookup for ASCII characters via pre-computed array,
    /// with pattern matching fallback for Unicode.
    /// Adapted from Calcpad.Core.MathParser.Input's CharTypes pattern.
    /// </summary>
    public enum CharClass : byte
    {
        Other,       // Unclassified character
        Whitespace,  // Space, tab
        Digit,       // 0-9
        Letter,      // a-z, A-Z, underscore (identifier chars)
        Operator,    // + - * / ^ ! = < > \ and Unicode operators
        Bracket,     // ( ) [ ] { }
        Delimiter,   // ; | & @ :
        Dot,         // .
        Dollar,      // $
        Hash,        // #
        Quote,       // ' "
        Question,    // ?
        Comma,       // ,
        Newline      // \r \n
    }

    /// <summary>
    /// Pre-computed character classifier for fast O(1) character type lookup.
    /// Replaces scattered char.IsWhiteSpace(), IsBracket(), IsDelimiter(),
    /// CalcpadBuiltIns.Operators.Contains() calls in the tokenizer hot loop.
    /// </summary>
    public static class CharClassifier
    {
        private static readonly CharClass[] AsciiTypes = new CharClass[128];

        static CharClassifier()
        {
            // Default is Other (0)

            // Whitespace
            AsciiTypes[' '] = CharClass.Whitespace;
            AsciiTypes['\t'] = CharClass.Whitespace;

            // Newlines
            AsciiTypes['\r'] = CharClass.Newline;
            AsciiTypes['\n'] = CharClass.Newline;

            // Digits
            for (int c = '0'; c <= '9'; c++)
                AsciiTypes[c] = CharClass.Digit;

            // Letters
            for (int c = 'a'; c <= 'z'; c++)
                AsciiTypes[c] = CharClass.Letter;
            for (int c = 'A'; c <= 'Z'; c++)
                AsciiTypes[c] = CharClass.Letter;
            AsciiTypes['_'] = CharClass.Letter;

            // ASCII Operators: ! ^ / \ * - + < > =
            AsciiTypes['!'] = CharClass.Operator;
            AsciiTypes['^'] = CharClass.Operator;
            AsciiTypes['/'] = CharClass.Operator;
            AsciiTypes['\\'] = CharClass.Operator;
            AsciiTypes['*'] = CharClass.Operator;
            AsciiTypes['-'] = CharClass.Operator;
            AsciiTypes['+'] = CharClass.Operator;
            AsciiTypes['<'] = CharClass.Operator;
            AsciiTypes['>'] = CharClass.Operator;
            AsciiTypes['='] = CharClass.Operator;

            // Brackets
            AsciiTypes['('] = CharClass.Bracket;
            AsciiTypes[')'] = CharClass.Bracket;
            AsciiTypes['['] = CharClass.Bracket;
            AsciiTypes[']'] = CharClass.Bracket;
            AsciiTypes['{'] = CharClass.Bracket;
            AsciiTypes['}'] = CharClass.Bracket;

            // Delimiters
            AsciiTypes[';'] = CharClass.Delimiter;
            AsciiTypes['|'] = CharClass.Delimiter;
            AsciiTypes['&'] = CharClass.Delimiter;
            AsciiTypes['@'] = CharClass.Delimiter;
            AsciiTypes[':'] = CharClass.Delimiter;

            // Special single-char types
            AsciiTypes['.'] = CharClass.Dot;
            AsciiTypes['$'] = CharClass.Dollar;
            AsciiTypes['#'] = CharClass.Hash;
            AsciiTypes['\''] = CharClass.Quote;
            AsciiTypes['"'] = CharClass.Quote;
            AsciiTypes['?'] = CharClass.Question;
            AsciiTypes[','] = CharClass.Comma;
        }

        /// <summary>
        /// Classifies a character in O(1) for ASCII, with fallback for Unicode.
        /// </summary>
        public static CharClass Classify(char c)
        {
            if (c < 128)
                return AsciiTypes[c];

            // Unicode operators
            return c switch
            {
                '÷' or '⦼' or '≡' or '≠' or '≤' or '≥' or '∧' or '∨' or '⊕' or '∠' or '←' => CharClass.Operator,
                '·' => CharClass.Operator, // Middle dot (multiplication) — normalized to * by tokenizer
                _ when CalcpadCharacterHelpers.IsGreekLetter(c) => CharClass.Letter,
                _ when CalcpadCharacterHelpers.IsSpecialMathChar(c) => CharClass.Letter,
                _ when CalcpadCharacterHelpers.IsSubscriptDigit(c) => CharClass.Letter, // subscript digits are part of identifiers
                _ when CalcpadCharacterHelpers.IsSuperscriptDigit(c) => CharClass.Letter,
                _ when char.IsLetter(c) => CharClass.Letter,
                _ when char.IsWhiteSpace(c) => CharClass.Whitespace,
                _ => CharClass.Other
            };
        }

        /// <summary>
        /// Quick check if a character is an operator (ASCII or Unicode).
        /// </summary>
        public static bool IsOperator(char c)
        {
            return Classify(c) == CharClass.Operator;
        }

        /// <summary>
        /// Quick check if a character is a bracket.
        /// </summary>
        public static bool IsBracket(char c)
        {
            return c < 128 && AsciiTypes[c] == CharClass.Bracket;
        }

        /// <summary>
        /// Quick check if a character is a delimiter.
        /// </summary>
        public static bool IsDelimiter(char c)
        {
            return c < 128 && AsciiTypes[c] == CharClass.Delimiter;
        }

        /// <summary>
        /// True for the characters ;|&amp;@:({[ that continue a line implicitly when they are the
        /// last non-whitespace character outside a comment. Matches Calcpad.Core's line extensions.
        /// </summary>
        public static bool IsLineExtension(char c)
        {
            return IsDelimiter(c) || c == '(' || c == '{' || c == '[';
        }
    }
}
