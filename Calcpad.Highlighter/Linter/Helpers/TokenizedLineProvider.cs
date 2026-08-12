using System;
using System.Collections.Generic;
using System.Text;
using Calcpad.Highlighter.Linter.Models;
using Calcpad.Highlighter.Tokenizer;
using Calcpad.Highlighter.Tokenizer.Models;

namespace Calcpad.Highlighter.Linter.Helpers
{
    /// <summary>
    /// Provides tokenized lines for linter validators.
    /// Caches tokenization results for efficiency when multiple validators need the same data.
    /// </summary>
    public class TokenizedLineProvider
    {
        private readonly CalcpadTokenizer _tokenizer;
        private readonly Dictionary<int, List<Token>> _tokenCache = new();
        private readonly Dictionary<int, ParseMode> _lineModes = new();
        private TokenizerResult _fullResult;

        public TokenizedLineProvider()
        {
            _tokenizer = new CalcpadTokenizer();
        }

        /// <summary>
        /// Sets macro comment parameter information from ContentResolver Stage2.
        /// Call this before Tokenize() to enable correct tokenization of macro call arguments.
        /// </summary>
        /// <param name="commentParams">Maps macro name to set of parameter names that are comment parameters</param>
        /// <param name="paramOrder">Maps macro name to ordered list of parameter names</param>
        public void SetMacroCommentParameters(
            Dictionary<string, HashSet<string>> commentParams,
            Dictionary<string, List<string>> paramOrder,
            Dictionary<string, string> macroBodies = null)
        {
            _tokenizer.SetMacroCommentParameters(commentParams, paramOrder, macroBodies);
        }

        /// <summary>
        /// Tokenize full source and cache results
        /// </summary>
        public void Tokenize(string source)
        {
            _fullResult = _tokenizer.Tokenize(source);
            _tokenCache.Clear();
            _lineModes.Clear();

            foreach (var token in _fullResult.Tokens)
            {
                if (!_tokenCache.TryGetValue(token.Line, out var lineTokens))
                {
                    lineTokens = new List<Token>();
                    _tokenCache[token.Line] = lineTokens;
                }
                lineTokens.Add(token);
            }
        }

        /// <summary>
        /// Tokenize from a list of lines. Uses pre-sized StringBuilder instead of string.Join.
        /// </summary>
        public void Tokenize(List<string> lines)
        {
            if (lines.Count == 0) { Tokenize(string.Empty); return; }
            if (lines.Count == 1) { Tokenize(lines[0]); return; }

            int totalLength = lines.Count - 1;
            for (int i = 0; i < lines.Count; i++)
                totalLength += lines[i].Length;

            var sb = new StringBuilder(totalLength);
            sb.Append(lines[0]);
            for (int i = 1; i < lines.Count; i++)
            {
                sb.Append('\n');
                sb.Append(lines[i]);
            }
            Tokenize(sb.ToString());
        }

        /// <summary>
        /// Get all tokens for a specific line
        /// </summary>
        public List<Token> GetTokensForLine(int lineNumber)
        {
            return _tokenCache.TryGetValue(lineNumber, out var tokens) ? tokens : new List<Token>();
        }

        /// <summary>
        /// Get all tokens
        /// </summary>
        public IReadOnlyList<Token> AllTokens => _fullResult?.Tokens ?? new List<Token>();

        /// <summary>
        /// Get tokens of a specific type for a line
        /// </summary>
        public IEnumerable<Token> GetTokensOfType(int lineNumber, TokenType type)
        {
            var tokens = GetTokensForLine(lineNumber);
            foreach (var token in tokens)
            {
                if (token.Type == type)
                    yield return token;
            }
        }

        /// <summary>
        /// Get tokens of specific types for a line (excludes comments, tags, etc.)
        /// </summary>
        public IEnumerable<Token> GetCodeTokensForLine(int lineNumber)
        {
            var tokens = GetTokensForLine(lineNumber);
            foreach (var token in tokens)
            {
                if (token.Type != TokenType.Comment &&
                    token.Type != TokenType.HtmlComment &&
                    token.Type != TokenType.Tag &&
                    token.Type != TokenType.None)
                {
                    yield return token;
                }
            }
        }

        /// <summary>
        /// Check if line starts with a comment (first non-whitespace token is comment)
        /// </summary>
        public bool IsCommentLine(int lineNumber)
        {
            var tokens = GetTokensForLine(lineNumber);
            foreach (var token in tokens)
            {
                if (token.Type == TokenType.None)
                    continue; // Skip whitespace
                return token.Type == TokenType.Comment || token.Type == TokenType.HtmlComment;
            }
            return false;
        }

        /// <summary>
        /// Check if line is a directive (starts with keyword token that is #...)
        /// </summary>
        public bool IsDirectiveLine(int lineNumber)
        {
            var tokens = GetTokensForLine(lineNumber);
            foreach (var token in tokens)
            {
                if (token.Type == TokenType.None)
                    continue;
                return token.Type == TokenType.Keyword;
            }
            return false;
        }

        /// <summary>
        /// Check if line has any code tokens (not empty/whitespace-only)
        /// </summary>
        public bool HasCodeTokens(int lineNumber)
        {
            foreach (var token in GetCodeTokensForLine(lineNumber))
            {
                return true;
            }
            return false;
        }

        /// <summary>
        /// Effective Calcpad parse mode for the given line. Always Cpd for now:
        /// the mode-switching directives (#cpd, #html, #markdown) are not
        /// implemented in Calcpad.Core, so nothing populates <see cref="_lineModes"/>.
        /// Restore the map here when Core gains the keywords.
        /// </summary>
        public ParseMode GetLineMode(int lineNumber) =>
            _lineModes.TryGetValue(lineNumber, out var m) ? m : ParseMode.Cpd;

        /// <summary>
        /// True when the line's effective parse mode is Calcpad. Validators
        /// should skip lines where this returns false (HTML and Markdown
        /// content is not linted).
        /// </summary>
        public bool IsCpdMode(int lineNumber) => GetLineMode(lineNumber) == ParseMode.Cpd;
    }
}
