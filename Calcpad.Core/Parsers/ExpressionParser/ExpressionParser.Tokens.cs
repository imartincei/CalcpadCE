using System;
using System.Collections.Generic;

namespace Calcpad.Core
{
    public partial class ExpressionParser
    {
        private sealed class Token
        {
            internal string Value { get; set; }
            internal TokenTypes Type;
            internal int CacheID = -1;
            internal Token(string value, TokenTypes type)
            {
                Value = value;
                Type = type;
            }
            public override string ToString() => Value;
        }

        private enum TokenTypes
        {
            Expression,
            Heading,
            Text,
            Html,
            Error
        }

        private List<Token> GetTokens(ReadOnlySpan<char> s)
        {
            var tokens = new List<Token>();
            var ts = new TextSpan(s);
            var currentSeparator = ' ';
            for (int i = 0, len = s.Length; i < len; ++i)
            {
                var c = s[i];
                if (c == '\'' || c == '\"')
                {
                    if (currentSeparator == ' ' || currentSeparator == c)
                    {
                        if (currentSeparator == c)
                        {
                            var i1 = i + 1;
                            if (i1 < len && s[i1] == currentSeparator)
                            {
                                ts.Expand();
                                ts.Expand();
                                i = i1;
                                continue;
                            }
                        }
                        if (!ts.IsEmpty)
                            AddToken(tokens, ts.Cut(), currentSeparator);

                        ts.Reset(i + 1);
                        currentSeparator = currentSeparator == c ? ' ' : c;
                    }
                    else if (currentSeparator != ' ')
                        ts.Expand();
                }
                else
                    ts.Expand();
            }
            if (!ts.IsEmpty)
                AddToken(tokens, ts.Cut(), currentSeparator);

            return tokens;
        }

        private void AddToken(List<Token> tokens, ReadOnlySpan<char> value, char separator)
        {
            var tokenValue = value.ToString().Replace("\"\"", "&quot;").Replace("''", "&apos;");
            var tokenType = GetTokenType(separator);
            if (tokenType == TokenTypes.Expression)
            {
                if (value.IsWhiteSpace())
                    return;
            }
            else if (_isVal < 1)
            {
                if (tokens.Count == 0)
                    tokenValue += " ";
                else
                    tokenValue = string.Concat(" ", tokenValue," ");
            }

            var token = new Token(tokenValue, tokenType);
            if (token.Type == TokenTypes.Text)
            {
                tokenValue = tokenValue.TrimStart();
                if (tokenValue.Length > 0 && tokenValue[0] == '<')
                    token.Type = TokenTypes.Html;
            }
            tokens.Add(token);
        }

        /// <summary>
        /// Resolves the image paths a line's tokens carry, so the output holds a path that already
        /// resolves rather than a <c>{project}</c>/<c>{library}</c> token only the host could
        /// expand. Called on a cache miss, so it runs once per line however many times a loop
        /// revisits it, and late enough that <c>ParseKeyword</c> has declared any root written
        /// above — or <see cref="PathRoots"/> was handed the whole document's from outside.
        /// </summary>
        private void ExpandImageSources(List<Token> tokens)
        {
            var directory = string.IsNullOrEmpty(SourceFilePath)
                ? null : System.IO.Path.GetDirectoryName(SourceFilePath);
            foreach (var token in tokens)
            {
                // Any token but an expression can carry the tag: a comment starting with '<' is
                // retyped Html above, but Markdown renders ![alt](path) into whichever token it
                // came from, which is still Text.
                if (token.Type == TokenTypes.Expression ||
                    !token.Value.Contains("<img", StringComparison.OrdinalIgnoreCase))
                    continue;

                var value = token.Value;
                token.Value = ImageReferences.ExpandSources(value, _pathRoots, directory,
                    error => AppendError(value, error, _currentLine));
            }
        }

        private static TokenTypes GetTokenType(char separator)
        {
            return separator switch
            {
                ' ' => TokenTypes.Expression,
                '\"' => TokenTypes.Heading,
                '\'' => TokenTypes.Text,
                _ => TokenTypes.Error,
            };
        }
    }
}