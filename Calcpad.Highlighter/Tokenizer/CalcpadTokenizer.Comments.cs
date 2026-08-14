using System;
using Calcpad.Highlighter.Tokenizer.Models;

namespace Calcpad.Highlighter.Tokenizer
{
    public partial class CalcpadTokenizer
    {
        // ── Comment and tag state ──────────────────────────────────

        private enum TagState
        {
            None,
            Starting,
            Closing,
            SelfClosing
        }

        private enum SpecialContentType
        {
            None,
            Script,      // Inside <script> tags
            Style,       // Inside <style> tags
            Svg          // Inside <svg> tags
        }

        private TagState _tagState;

        // For tracking the current HTML tag being parsed
        private string _currentTagName = string.Empty;

        // Carries comment state across line continuations
        // When a line ends with " _" inside a comment, these preserve the quote context for the next line
        private char _continueTextComment;
        private char _continueTagComment;

        // Tracks whether we are inside an open <!-- ... --> block spanning multiple comment lines
        private bool _inHtmlComment;

        /// <summary>
        /// True while inside an unclosed &lt;!-- block — either carried over from a previous
        /// line, or opened by the current buffer, which starts at the '&lt;' of the opener.
        /// The body of an HTML comment is not markup, so '&lt;' and '&gt;' inside it must not
        /// start a tag or end the token; only "--&gt;" closes it.
        /// </summary>
        private bool InOpenHtmlComment =>
            _inHtmlComment ||
            (_builder.Length >= 4 && _builder[0] == '<' && _builder[1] == '!'
                                  && _builder[2] == '-' && _builder[3] == '-');

        /// <summary>
        /// Resets all comment/tag state for a new tokenization pass.
        /// </summary>
        private void ResetCommentState()
        {
            _inHtmlComment = false;
            _currentTagName = string.Empty;
            _tagState = TagState.None;
            _continueTextComment = '\0';
            _continueTagComment = '\0';
        }

        // ── Comment parsing ────────────────────────────────────────

        private void ParseComment(char c)
        {
            // Skip comment parsing when in Include mode (quoted filename is not a comment) or
            // SettingsJson mode (quotes belong to the JSON payload, not a comment)
            if (_state.CurrentType == TokenType.Include || _state.CurrentType == TokenType.SettingsJson)
                return;

            // Cyrillic auto-comment
            if (c >= 'А' && c <= 'я')
            {
                if (_state.TextComment == '\0' && !_state.IsSubscript)
                {
                    Append(_state.CurrentType);
                    _state.TextComment = '\'';
                    _builder.Clear();
                    _builder.Append(_state.TextComment);
                    _state.CurrentType = TokenType.Comment;
                }
            }
            else if (c == '\'' || c == '"')
            {
                if (_state.TextComment == '\0')
                {
                    _state.IsUnits = false;
                    _state.TextComment = c;
                    _state.TagComment = c == '\'' ? '"' : '\'';
                    // Only reset HTML state if not continuing from previous line
                    if (_state.InSpecialContent == SpecialContentType.None)
                    {
                        _state.HasHtmlContent = false;
                    }
                    Append(_state.CurrentType);
                    if (_state.IsTag)
                    {
                        if (_tagState == TagState.Starting)
                            _state.CurrentType = TokenType.Tag;
                        else
                        {
                            _state.CurrentType = _state.InSpecialContent switch
                            {
                                SpecialContentType.Script => TokenType.JavaScript,
                                SpecialContentType.Style => TokenType.Css,
                                SpecialContentType.Svg => TokenType.Svg,
                                _ => _state.HasHtmlContent ? TokenType.HtmlContent : TokenType.Comment
                            };
                            _state.IsTag = false;
                        }
                    }
                    else
                    {
                        _state.CurrentType = _state.InSpecialContent switch
                        {
                            SpecialContentType.Script => TokenType.JavaScript,
                            SpecialContentType.Style => TokenType.Css,
                            SpecialContentType.Svg => TokenType.Svg,
                            _ => _state.HasHtmlContent ? TokenType.HtmlContent : TokenType.Comment
                        };
                    }
                }
                else if (c == _state.TextComment)
                {
                    _state.TextComment = '\0';
                    var prevType = _state.CurrentType;
                    _state.CurrentType = TokenType.Comment;

                    if (prevType == TokenType.Comment)
                    {
                        // Regular comment — check for HTML comment markers before emitting
                        CheckHtmlComment();
                        _builder.Append(c);
                        Append(_state.CurrentType);
                    }
                    else
                    {
                        // JS/CSS/SVG/HtmlContent — flush content with its real type,
                        // then emit the closing quote as Comment
                        Append(prevType);
                        _builder.Append(c);
                        Append(TokenType.Comment);
                    }

                    _state.CurrentType = TokenType.Comment;
                    // Reset HasHtmlContent only when not inside a special content block.
                    // InSpecialContent persists until the closing tag (</script> etc.)
                    if (_state.InSpecialContent == SpecialContentType.None)
                        _state.HasHtmlContent = false;
                }
                else if (_state.IsTag)
                {
                    if (_state.IsTagComment)
                        _state.CurrentType = TokenType.Comment;
                    else
                        _builder.Append(c);

                    Append(_state.CurrentType);
                    if (_state.IsTagComment && c == _state.TagComment)
                    {
                        _builder.Append(c);
                        Append(TokenType.Tag);
                    }
                    _state.IsTagComment = !_state.IsTagComment;
                }
            }
        }

        private void ParseTagInComment(char c)
        {
            if (c == '>')
            {
                _builder.Append(c);
                if (_state.IsTag)
                {
                    _state.CurrentType = TokenType.Tag;
                    _state.HasHtmlContent = true;

                    // Check if this is an opening tag for special content
                    // Use stored tag name (populated when tag started) instead of extracting from builder
                    // because builder may only contain ">" if tag has attributes
                    var tagName = !string.IsNullOrEmpty(_currentTagName) ? _currentTagName : GetTagName();
                    if (_tagState != TagState.Closing && _tagState != TagState.SelfClosing)
                    {
                        // Only set InSpecialContent for special tags (script, style, svg)
                        // For other tags, preserve the current state (they may be nested inside special content)
                        var newSpecialContent = tagName.ToLowerInvariant() switch
                        {
                            "script" => SpecialContentType.Script,
                            "style" => SpecialContentType.Style,
                            "svg" => SpecialContentType.Svg,
                            _ => _state.InSpecialContent  // Preserve current state for nested tags
                        };

                        // Validation: Check for invalid nesting (e.g., style tag inside script tag)
                        if (newSpecialContent != _state.InSpecialContent && _state.InSpecialContent != SpecialContentType.None)
                        {
                            var currentContext = _state.InSpecialContent switch
                            {
                                SpecialContentType.Script => "script",
                                SpecialContentType.Style => "style",
                                SpecialContentType.Svg => "svg",
                                _ => "unknown"
                            };
                            var newContext = newSpecialContent switch
                            {
                                SpecialContentType.Script => "script",
                                SpecialContentType.Style => "style",
                                SpecialContentType.Svg => "svg",
                                _ => "unknown"
                            };
                            // TODO: Could emit a linter error here about invalid tag nesting
                            // For now, just preserve the outer context
                            Console.WriteLine($"Warning: {newContext} tag inside {currentContext} tag at line {_state.Line + 1}");
                        }
                        else
                        {
                            _state.InSpecialContent = newSpecialContent;
                        }
                    }
                    else if (_tagState == TagState.Closing)
                    {
                        // Check if this is a closing tag for special content
                        var closingTagMatches = tagName.ToLowerInvariant() switch
                        {
                            "script" when _state.InSpecialContent == SpecialContentType.Script => true,
                            "style" when _state.InSpecialContent == SpecialContentType.Style => true,
                            "svg" when _state.InSpecialContent == SpecialContentType.Svg => true,
                            _ => false
                        };

                        if (closingTagMatches)
                        {
                            _state.InSpecialContent = SpecialContentType.None;
                            _state.HasHtmlContent = false;
                        }
                    }
                }
                else
                {
                    CheckHtmlComment();
                    if (_inHtmlComment)
                        return;
                }

                Append(_state.CurrentType);

                // Set appropriate token type for content after tag
                _state.CurrentType = _state.InSpecialContent switch
                {
                    SpecialContentType.Script => TokenType.JavaScript,
                    SpecialContentType.Style => TokenType.Css,
                    SpecialContentType.Svg => TokenType.Svg,
                    _ => _state.HasHtmlContent ? TokenType.HtmlContent : TokenType.Comment
                };

                _state.IsTag = false;
                // Clear stored tag name after processing the tag
                _currentTagName = string.Empty;
            }
            else if (c == '<')
            {
                if (InOpenHtmlComment)
                {
                    _builder.Append(c);
                    return;
                }

                _tagState = TagState.None;
                Append(_state.CurrentType);
                _builder.Append(c);
                _state.IsTag = true;
                _state.HasHtmlContent = true;
                // Clear previous tag name when starting a new tag
                _currentTagName = string.Empty;
            }
            else
            {
                if (_state.IsTag)
                    _state.IsTag = CheckTag(c);

                if (!(_state.IsTag && c == _state.TagComment))
                {
                    if (_state.CurrentType == TokenType.Macro)
                    {
                        if (c == '(' || c == ')')
                            ParseBrackets(c);
                        else
                            _builder.Append(c);

                        if (c != '(')
                            _state.CurrentType = _state.InSpecialContent switch
                            {
                                SpecialContentType.Script => TokenType.JavaScript,
                                SpecialContentType.Style => TokenType.Css,
                                SpecialContentType.Svg => TokenType.Svg,
                                _ => _state.HasHtmlContent ? TokenType.HtmlContent : TokenType.Comment
                            };
                    }
                    else
                    {
                        _builder.Append(c);
                    }

                    if (c == '$')
                        ParseMacroInComment(_state.CurrentType);
                }
            }
        }

        private bool CheckTag(char c)
        {
            // Handle tags in comments and special content (SVG, CSS, JavaScript)
            if (_state.CurrentType == TokenType.Comment ||
                _state.CurrentType == TokenType.Svg ||
                _state.CurrentType == TokenType.Css ||
                _state.CurrentType == TokenType.JavaScript ||
                _state.CurrentType == TokenType.HtmlContent)
            {
                if (_tagState == TagState.None)
                {
                    if (c == ' ')
                    {
                        if (_builder.Length == 1)
                            return false;
                        _tagState = TagState.Starting;
                        // Extract and store tag name when we hit the space after it
                        _currentTagName = GetTagName();
                    }
                    else if (c == '/')
                    {
                        _tagState = _builder.Length == 1 ? TagState.Closing : TagState.SelfClosing;
                    }
                    else if (!(char.IsAsciiLetter(c) || char.IsDigit(c) && _builder.Length == 2 && _builder[1] == 'h'))
                    {
                        return false;
                    }
                }
                else
                {
                    _state.CurrentType = TokenType.Tag;
                }
            }
            else if (_tagState == TagState.SelfClosing || (_tagState == TagState.Closing && !char.IsAsciiLetter(c)))
            {
                return false;
            }

            return true;
        }

        private string GetTagName()
        {
            // Extract tag name from _builder (e.g., "<script>" -> "script", "</div>" -> "div")
            var len = _builder.Length;
            if (len < 2)
                return string.Empty;

            var startIndex = (len >= 2 && _builder[0] == '<' && _builder[1] == '/') ? 2 : 1;  // Skip '<' or '</'

            // Find the end of tag name (before space, /, or >)
            var endIndex = len;
            for (int i = startIndex; i < len; i++)
            {
                var ch = _builder[i];
                if (ch == ' ' || ch == '/' || ch == '>')
                {
                    endIndex = i;
                    break;
                }
            }

            if (endIndex <= startIndex)
                return string.Empty;

            return _builder.ToString(startIndex, endIndex - startIndex);
        }

        private void CheckHtmlComment()
        {
            var len = _builder.Length;
            bool startsOpen = len >= 4
                && _builder[0] == '<' && _builder[1] == '!'
                && _builder[2] == '-' && _builder[3] == '-';
            bool endsClose = len >= 3
                && _builder[len - 1] == '>' && _builder[len - 2] == '-'
                && _builder[len - 3] == '-';

            if (startsOpen || endsClose || _inHtmlComment)
                _state.CurrentType = TokenType.HtmlComment;

            // Track the open/close state across lines for multi-line HTML comments
            if (startsOpen && !endsClose)
                _inHtmlComment = true;
            else if (endsClose)
                _inHtmlComment = false;
        }
    }
}
