using System;
using System.Collections.Generic;
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
            if (IsEndDirective(trimmedLine, Mode))
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

        public static bool IsMacroDirective(ReadOnlySpan<char> trimmedLine) =>
            StartsWithWord(trimmedLine, "#def") || StartsWithWord(trimmedLine, "#end def") ||
            StartsWithWord(trimmedLine, "#include");

        /// <summary>
        /// The lines still parsed as Calcpad inside #html/#markdown: any mode opener, the #end of
        /// the current mode, and the macro directives Core expands beforehand. Everything else,
        /// including #if, a markdown heading and "#tag", is content, as it is for Core.
        /// </summary>
        public static bool IsDirective(ReadOnlySpan<char> trimmedLine, ParseMode mode) =>
            TryGetOpener(trimmedLine, out _) ||
            IsEndDirective(trimmedLine, mode) ||
            IsMacroDirective(trimmedLine);

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

        /// <summary>Only the #end of the mode in effect closes a block; the others are content.</summary>
        private static bool IsEndDirective(ReadOnlySpan<char> s, ParseMode mode) => mode switch
        {
            ParseMode.Html => StartsWithWord(s, "#end html"),
            ParseMode.Markdown => StartsWithWord(s, "#end markdown"),
            _ => StartsWithWord(s, "#end html") || StartsWithWord(s, "#end cpd") || StartsWithWord(s, "#end markdown")
        };

        private static bool StartsWithWord(ReadOnlySpan<char> s, ReadOnlySpan<char> word) =>
            s.StartsWith(word, StringComparison.OrdinalIgnoreCase) &&
            (s.Length == word.Length || char.IsWhiteSpace(s[word.Length]));
    }
}
