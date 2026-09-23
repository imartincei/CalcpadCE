using System;
using System.Collections.Generic;
using Calcpad.Highlighter.Linter.Constants;
using Calcpad.Highlighter.Linter.Models;

namespace Calcpad.Highlighter.Tokenizer
{
    /// <summary>
    /// Tracks the #html/#cpd/#markdown stack as ExpressionParser does. Directive conditions only
    /// decide whether a block's content is output, so the mode of a line is always positional.
    /// </summary>
    public sealed class ParseModeTracker
    {
        private readonly Stack<ParseMode> _stack = new();

        public ParseMode Mode { get; private set; } = ParseMode.Cpd;

        /// <summary>Applies a trimmed line, returning true when it was a mode directive.</summary>
        public bool Apply(ReadOnlySpan<char> trimmedLine)
        {
            if (IsEndDirective(trimmedLine))
            {
                Mode = _stack.Count > 0 ? _stack.Pop() : ParseMode.Cpd;
                return true;
            }
            if (TryGetOpener(trimmedLine, out var mode))
            {
                _stack.Push(Mode);
                Mode = mode;
                return true;
            }
            return false;
        }

        public static bool IsModeDirective(ReadOnlySpan<char> trimmedLine) =>
            IsEndDirective(trimmedLine) || TryGetOpener(trimmedLine, out _);

        public static bool IsMacroDirective(ReadOnlySpan<char> trimmedLine) =>
            StartsWithWord(trimmedLine, "#def") || StartsWithWord(trimmedLine, "#end def") ||
            StartsWithWord(trimmedLine, "#include");

        /// <summary>
        /// True when the line starts with a known keyword. Anything else, such as a markdown
        /// heading or "#tag", is content in #html/#markdown mode, as it is for Core.
        /// </summary>
        public static bool IsDirective(ReadOnlySpan<char> trimmedLine)
        {
            if (trimmedLine.Length < 2 || trimmedLine[0] != '#' || !char.IsLetter(trimmedLine[1]))
                return false;

            var firstEnd = trimmedLine.IndexOfAny(' ', '\t');
            if (firstEnd < 0)
                return IsKnownKeyword(trimmedLine.ToString());

            if (IsKnownKeyword(trimmedLine[..firstEnd].ToString()))
                return true;

            var rest = trimmedLine[firstEnd..].TrimStart();
            var secondEnd = rest.IndexOfAny(' ', '\t');
            var second = secondEnd < 0 ? rest : rest[..secondEnd];
            return IsKnownKeyword($"{trimmedLine[..firstEnd]} {second}");
        }

        private static bool IsKnownKeyword(string keyword) =>
            CalcpadBuiltIns.Keywords.Contains(keyword) ||
            CalcpadBuiltIns.ControlBlockKeywords.Contains(keyword) ||
            CalcpadBuiltIns.EndKeywords.Contains(keyword);

        private static bool TryGetOpener(ReadOnlySpan<char> s, out ParseMode mode)
        {
            if (StartsWithWord(s, "#html"))
                mode = ParseMode.Html;
            else if (StartsWithWord(s, "#cpd"))
                mode = ParseMode.Cpd;
            else if (StartsWithWord(s, "#markdown"))
                mode = ParseMode.Markdown;
            else
            {
                mode = default;
                return false;
            }
            return true;
        }

        private static bool IsEndDirective(ReadOnlySpan<char> s) =>
            StartsWithWord(s, "#end html") || StartsWithWord(s, "#end cpd") || StartsWithWord(s, "#end markdown");

        private static bool StartsWithWord(ReadOnlySpan<char> s, ReadOnlySpan<char> word) =>
            s.StartsWith(word, StringComparison.OrdinalIgnoreCase) &&
            (s.Length == word.Length || char.IsWhiteSpace(s[word.Length]));
    }
}
