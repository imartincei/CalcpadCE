#nullable enable

using System;
using System.Collections.Generic;
using System.Text.Json;
using Calcpad.Highlighter.Tokenizer.Models;

namespace Calcpad.Highlighter.HtmlComment
{
    public enum HtmlCommentParseStatus
    {
        Success,
        InvalidJson
    }

    public sealed class HtmlCommentBlock
    {
        /// <summary>Zero-based start line in the tokenizer result</summary>
        public int StartLine { get; init; }

        /// <summary>Zero-based end line in the tokenizer result (inclusive)</summary>
        public int EndLine { get; init; }

        /// <summary>Column where the comment starts on <see cref="StartLine"/>.</summary>
        public int StartColumn { get; init; }

        /// <summary>
        /// Column just past the comment's extent on <see cref="StartLine"/>. For a
        /// single-line block that is its end; for a multi-line one it is the end of the
        /// opening line, which is where diagnostics anchor.
        /// </summary>
        public int EndColumn { get; init; }

        /// <summary>Raw text between &lt;!-- and --&gt;, trimmed</summary>
        public string? RawJson { get; init; }

        /// <summary>Parsed JSON element (cloned); null if parsing failed</summary>
        public JsonElement? Data { get; init; }

        /// <summary>JSON parse error message; null on success</summary>
        public string? ParseError { get; init; }

        public HtmlCommentParseStatus Status { get; init; }
    }

    /// <summary>
    /// Extracts and parses JSON payloads embedded in Calcpad HTML comment syntax.
    ///
    /// Supported forms:
    ///   '&lt;!--{"json": "hello"}--&gt;                single line (optional trailing quote)
    ///   '&lt;!--{"json": "hello", _                    multi line via " _" continuation
    ///   "more": "world"}--&gt;
    ///
    /// Requires the tokenizer to have run with <see cref="CalcpadTokenizer._inHtmlComment"/> state
    /// tracking enabled, so every line of a multi-line block is typed as HtmlComment. A line gap in
    /// the HtmlComment token stream terminates an open block without emitting a result.
    /// </summary>
    public sealed class HtmlCommentParser
    {
        private const string OpenMarker  = "<!--";
        private const string CloseMarker = "-->";

        public IReadOnlyList<HtmlCommentBlock> Parse(TokenizerResult tokenizerResult)
        {
            if (tokenizerResult == null)
                throw new ArgumentNullException(nameof(tokenizerResult));

            return Parse(tokenizerResult.Tokens);
        }

        public IReadOnlyList<HtmlCommentBlock> Parse(IReadOnlyList<Token> tokens)
        {
            if (tokens == null)
                throw new ArgumentNullException(nameof(tokens));

            var results = new List<HtmlCommentBlock>();

            var state    = ParseState.Normal;
            var buffer   = new List<string>();
            int lastLine  = -1;
            Token opener  = default;

            // One buffer entry per source line: entries are later joined with "\n", so
            // several HtmlComment tokens on the same line (a macro parameter or a '>' in
            // the payload can split one) must be concatenated, not newline-separated.
            void AddFragment(int line, string text)
            {
                if (buffer.Count > 0 && line == lastLine)
                    buffer[^1] += text;
                else
                    buffer.Add(text);

                lastLine = line;
            }

            foreach (var token in tokens)
            {
                if (token.Type != TokenType.HtmlComment)
                    continue;

                var content = token.Text;

                if (state == ParseState.Normal)
                {
                    int openIdx = content.IndexOf(OpenMarker, StringComparison.Ordinal);
                    if (openIdx < 0)
                        continue;

                    var afterOpen = content[(openIdx + OpenMarker.Length)..];
                    int closeIdx  = afterOpen.IndexOf(CloseMarker, StringComparison.Ordinal);

                    if (closeIdx >= 0)
                    {
                        // Single-line block
                        var block = BuildBlock(token, token.Line, afterOpen[..closeIdx]);
                        if (block != null)
                            results.Add(block);
                    }
                    else
                    {
                        // Start of multi-line block
                        buffer.Clear();
                        AddFragment(token.Line, afterOpen);
                        opener    = token;
                        state     = ParseState.InHtmlComment;
                    }
                }
                else // InHtmlComment
                {
                    if (token.Line > lastLine + 1)
                    {
                        // Non-consecutive line — block broken, discard and re-evaluate as new opener
                        buffer.Clear();
                        state = ParseState.Normal;

                        int openIdx = content.IndexOf(OpenMarker, StringComparison.Ordinal);
                        if (openIdx < 0)
                            continue;

                        var afterOpen = content[(openIdx + OpenMarker.Length)..];
                        int closeIdx  = afterOpen.IndexOf(CloseMarker, StringComparison.Ordinal);

                        if (closeIdx >= 0)
                        {
                            var block = BuildBlock(token, token.Line, afterOpen[..closeIdx]);
                            if (block != null)
                                results.Add(block);
                        }
                        else
                        {
                            AddFragment(token.Line, afterOpen);
                            opener    = token;
                            state     = ParseState.InHtmlComment;
                        }

                        continue;
                    }

                    int closingIdx = content.IndexOf(CloseMarker, StringComparison.Ordinal);
                    if (closingIdx >= 0)
                    {
                        AddFragment(token.Line, content[..closingIdx]);
                        var block = BuildBlock(opener, token.Line, string.Join("\n", buffer));
                        if (block != null)
                            results.Add(block);
                        buffer.Clear();
                        state = ParseState.Normal;
                    }
                    else
                    {
                        AddFragment(token.Line, content);
                    }
                }
            }

            // Any open block at end of stream is silently discarded (unterminated)
            return results;
        }

        private static HtmlCommentBlock? BuildBlock(Token opener, int endLine, string rawJson)
        {
            rawJson = rawJson.Trim();
            if (rawJson.Length == 0)
                return null;

            try
            {
                using var doc = JsonDocument.Parse(rawJson);
                return new HtmlCommentBlock
                {
                    StartLine   = opener.Line,
                    EndLine     = endLine,
                    StartColumn = opener.Column,
                    EndColumn   = opener.Column + opener.Length,
                    RawJson     = rawJson,
                    Data        = doc.RootElement.Clone(),
                    Status      = HtmlCommentParseStatus.Success
                };
            }
            catch (JsonException ex)
            {
                return new HtmlCommentBlock
                {
                    StartLine   = opener.Line,
                    EndLine     = endLine,
                    StartColumn = opener.Column,
                    EndColumn   = opener.Column + opener.Length,
                    RawJson     = rawJson,
                    ParseError  = ex.Message,
                    Status      = HtmlCommentParseStatus.InvalidJson
                };
            }
        }

        private enum ParseState
        {
            Normal,
            InHtmlComment
        }
    }
}
