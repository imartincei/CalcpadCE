using System;
using System.Text;
using Calcpad.Highlighter.Linter.Constants;
using Calcpad.Highlighter.Linter.Helpers;
using Calcpad.Highlighter.Parsing;
using Calcpad.Highlighter.Tokenizer.Models;

namespace Calcpad.Highlighter.Tokenizer
{
    /// <summary>
    /// Tokenizes Calcpad source code for syntax highlighting, producing tokens with line/column
    /// positions a frontend can colorize. Error detection is handled separately by CalcpadLinter.
    ///
    /// Partial class split:
    ///   CalcpadTokenizer.cs                - Core state, public API, main loop, token emission
    ///   CalcpadTokenizer.Comments.cs       - Comment/HTML tag parsing, tag/special content state
    ///   CalcpadTokenizer.Macros.cs         - Macro state, setup, definitions, parameters, arguments
    ///   CalcpadTokenizer.Parsing.cs        - Brackets, operators, delimiters, units, line breaks
    ///   CalcpadTokenizer.TypeResolution.cs - Definition tracking, type resolution, lookups
    ///   CalcpadTokenizer.Helpers.cs        - Static utility methods
    ///   CalcpadTokenizer.Definitions.cs    - Lint-mode definition extraction, include map
    ///   CalcpadTokenizer.MacroCollection.cs - Macro collection mode
    /// </summary>
    public partial class CalcpadTokenizer
    {
        private TokenizerMode _mode;

        private TokenizerResult _result = new();
        private StringBuilder _builder = new(100);
        private TokenizerState _state;

        // Carries structural state across line continuations, so bracket/command nesting
        // opened on one physical line is still in effect on the lines that continue it.
        private bool _explicitLineBreak;
        private bool _continueLine;

        private struct TokenizerState
        {
            public int Line;
            public int TokenStartColumn;
            public ReadOnlyMemory<char> Text;
            public char TextComment;
            public char TagComment;
            public bool IsSubscript;
            public bool IsLeading;
            public bool IsUnits;
            public bool IsPlot;
            public bool IsTag;
            public bool IsTagComment;
            public bool IsMacro;
            public bool IsFunction;
            public string Keyword;
            public bool HasMacro;
            public int MacroArgs;
            public int CommandCount;
            public int BracketCount;
            public int MatrixCount;
            public TokenType CurrentType;
            public TokenType PreviousType;
            public bool AllowUnaryMinus;

            // Local variable tracking
            public bool IsFunctionDefinition;       // True when parsing f(x;y) = ... before the =
            public bool IsInFunctionParams;         // True when inside function definition parentheses
            public bool IsDataExchangeKeyword;      // True for #read, #write, #append
            public bool ExpectingFilePath;          // True after #read/#write/#append keyword
            public bool IsAfterAtOrAmp;             // True after @ or & in command block
            public bool IsInCommandBlock;           // True when inside $Command{...}

            // Special content block tracking
            public SpecialContentType InSpecialContent;  // Tracks if inside script/style/svg tags
            public bool HasHtmlContent;             // True if any HTML tags detected in comment

            // Macro call tracking
            public string CurrentMacroCall;         // Name of macro being called (for tokenizing args)
            public int CurrentMacroArgIndex;        // Current argument index (0-based) in macro call

            public void RetainType()
            {
                if (CurrentType != TokenType.None)
                    PreviousType = CurrentType;
            }

            public readonly TokenType CurrentTypeOrPrevious =>
                CurrentType == TokenType.None ? PreviousType : CurrentType;
        }

        /// <summary>
        /// Tokenize source code and return tokens with positions (Highlight mode).
        /// </summary>
        public TokenizerResult Tokenize(string source)
        {
            return Tokenize(source, TokenizerMode.Highlight);
        }

        /// <summary>
        /// Tokenize source code with the specified mode.
        /// In Lint mode, also extracts full definition metadata (variable expressions,
        /// function params/body, custom units, command blocks, #for/#read variables).
        /// </summary>
        public TokenizerResult Tokenize(string source, TokenizerMode mode)
        {
            if (string.IsNullOrEmpty(source))
                return new TokenizerResult();

            _mode = mode;
            _result = new TokenizerResult();
            _builder = new StringBuilder(100);

            ResetCommentState();
            ResetTypeResolutionState();
            ResetMacroState();
            _explicitLineBreak = false;
            _continueLine = false;

            if (_mode == TokenizerMode.Lint)
                InitLintState();
            if (_mode == TokenizerMode.Macro)
                InitMacroCollectionState();

            int lineNum = 0;
            // ReadOnlyMemory<char> slices survive across nested helper calls — a ref-struct
            // ReadOnlySpan cannot, since TokenizerState stores Text as a field.
            foreach (var lineMemory in new LineMemoryEnumerator(source.AsMemory()))
            {
                TokenizeLineInternal(lineMemory, lineNum++);
            }

            return _result;
        }

        /// <summary>
        /// Tokenize a single line (for incremental updates)
        /// </summary>
        public TokenizerResult TokenizeSingleLine(string line, int lineNumber)
        {
            _result = new TokenizerResult();
            _builder = new StringBuilder(100);
            _inHtmlComment = false;
            _explicitLineBreak = false;
            _continueLine = false;
            TokenizeLineInternal(line.AsMemory(), lineNumber);
            return _result;
        }

        private void TokenizeLineInternal(ReadOnlyMemory<char> textMemory, int lineNumber)
        {
            InitState(textMemory, lineNumber);
            var text = textMemory.Span;
            _builder.Clear();
            _tagState = TagState.None;

            for (int i = 0, len = text.Length; i < len; i++)
            {
                var c = text[i];

                // Normalize whitespace (except in comments)
                if (_state.CurrentType != TokenType.Comment)
                {
                    if (char.IsWhiteSpace(c))
                        c = ' ';
                    else if (c == '·')
                        c = '*';

                    if (i > 0 && _state.IsLeading && !char.IsWhiteSpace(c))
                        Append(TokenType.None);
                }

                // Line continuation check - works in both code and comments
                if (c == '_' && (i == len - 1 || text[(i + 1)..].IsWhiteSpace()) && i > 0 && text[i - 1] == ' ')
                {
                    ParseLineBreak(i);
                    break;
                }

                // Format string handling - accumulate format specifier characters
                // Stops at whitespace, comment chars, '$' (a macro reference may immediately
                // follow, e.g. "5ft|in:f2text$"), or end of line
                if (_state.CurrentType == TokenType.Format && c != '\'' && c != '"' && c != '$' && !char.IsWhiteSpace(c))
                {
                    _builder.Append(c);
                    continue;
                }

                // Units mode tracking
                if (!_state.IsPlot && _state.MatrixCount == 0 &&
                    _state.CurrentType != TokenType.Comment &&
                    _state.CurrentType != TokenType.SettingsJson && c == '|')
                {
                    _state.IsUnits = true;
                }

                // Comment parsing
                if (_state.MacroArgs == 0)
                    ParseComment(c);

                // Main parsing logic
                if (_state.MacroArgs > 0)
                {
                    ParseMacroArgs(c, i);
                }
                else if (_state.TextComment != '\0')
                {
                    // Inside special content blocks (script/style/svg), skip tag parsing
                    // for all < except the matching closing tag (</script>, </style>, </svg>).
                    if (c == '<' && _state.InSpecialContent != SpecialContentType.None &&
                        !IsClosingSpecialContentTag(text, i, _state.InSpecialContent))
                    {
                        _builder.Append(c);
                    }
                    else
                    {
                        ParseTagInComment(c);
                    }
                }
                else if (_state.CurrentType == TokenType.Include)
                {
                    // #include filename - filename can have spaces, ends at comment or newline
                    if (c == '#')
                    {
                        Append(TokenType.Include);
                        _state.CurrentType = TokenType.Bracket;
                        _builder.Append(c);
                    }
                    else if (c == '\'' || c == '"')
                    {
                        // Check if this is the start of a quoted filename or a trailing comment
                        if (_builder.Length == 0 || _builder.ToString().Trim().Length == 0)
                        {
                            // Start of quoted filename - include the quote
                            _builder.Append(c);
                        }
                        else
                        {
                            // Trailing comment - end the include token
                            Append(TokenType.Include);
                            ParseComment(c);
                        }
                    }
                    else
                    {
                        _builder.Append(c);
                    }
                }
                else if (_state.CurrentType == TokenType.FilePath)
                {
                    // Consume file path characters until we hit a delimiter or comment
                    // File paths can contain spaces (e.g., "8 Masonry.csv")
                    // They end at: @ (range), ' or " (comment), or keywords like TYPE/SEP
                    if (c == '\'' || c == '"')
                    {
                        // End of file path, comment starting
                        Append(TokenType.FilePath);
                        _state.ExpectingFilePath = false;
                        // Let comment parsing handle this
                        ParseComment(c);
                    }
                    else if (c == '@')
                    {
                        // @ marks start of range specification (e.g., @R5C1:R10C6)
                        Append(TokenType.FilePath);
                        _state.ExpectingFilePath = false;
                        _state.CurrentType = TokenType.None;
                        _builder.Append(c);
                        Append(TokenType.Operator);
                    }
                    else if (c == ' ' && IsFilePathEndMarker(text, i))
                    {
                        // Space followed by keyword like "sep", "type" etc.
                        Append(TokenType.FilePath);
                        _state.ExpectingFilePath = false;
                        _state.CurrentType = TokenType.None;
                        _builder.Append(c);
                        Append(TokenType.None);
                    }
                    else
                    {
                        _builder.Append(c);
                    }
                }
                else if (_state.CurrentType == TokenType.SettingsJson)
                {
                    // #settings {json} - consume the whole JSON payload as a single token;
                    // quotes, braces and colons all belong to the JSON, not to Calcpad syntax.
                    _builder.Append(c);
                }
                else if (c == '$' && _builder.Length > 0)
                {
                    ParseMacro();
                }
                else if (IsDoubleOp(c, '%'))
                {
                    AppendDoubleOperatorShortcut('⦼');
                }
                else if (IsDoubleOp(c, '<'))
                {
                    AppendDoubleOperatorShortcut('∠');
                }
                else if (char.IsWhiteSpace(c))
                {
                    ParseSpace(c, i);
                }
                else if (IsBracket(c))
                {
                    ParseBrackets(c, i);
                }
                else if (CalcpadBuiltIns.Operators.Contains(c))
                {
                    if (c == '<' && i < len - 1 && text[i + 1] == '<')
                    {
                        _builder.Append('<');
                    }
                    else
                    {
                        ParseOperator(c);
                        if (ParseMacroContent(c, i, len))
                            break;
                    }
                }
                else if (IsDelimiter(c))
                {
                    ParseDelimiter(c);
                }
                else if (c == '.' && _builder.Length == 0 &&
                         (_state.CurrentType == TokenType.MacroParameter || _state.CurrentType == TokenType.Macro))
                {
                    // Element access dot after macro parameter/call (e.g., l_v$.iζ, m$.(i; j))
                    // The macro token was already emitted by ParseMacro(), treat . as operator
                    _state.TokenStartColumn = i;
                    _builder.Append(c);
                    _state.CurrentType = TokenType.Operator;
                    Append(TokenType.Operator);
                }
                else if (c == '.' && _builder.Length == 0 && _state.IsLeading)
                {
                    // Custom unit prefix dot at start of line (e.g., .USD = 1)
                    _state.TokenStartColumn = i;
                    _builder.Append(c);
                    _state.CurrentType = TokenType.Operator;
                    Append(TokenType.Operator);
                }
                else if (_builder.Length == 0)
                {
                    _state.CurrentType = InitType(c, _state.CurrentType);
                    // Characters that resolve to Comment here are dropped rather than buffered. A
                    // quote was already emitted by ParseComment, so the cursor is past it and must
                    // not be rewound — otherwise the next token (typically the bracket in
                    // 'text'(expr)) lands a column early, while anything else is dropped outright
                    // and the cursor has to step over it to stay aligned with the source.
                    if (_state.CurrentType != TokenType.Comment)
                    {
                        _state.TokenStartColumn = i;
                        _builder.Append(c);
                    }
                    else if (c != '\'' && c != '"')
                    {
                        _state.TokenStartColumn = i + 1;
                    }

                    if (_state.CurrentType == TokenType.Input)
                        Append(TokenType.Input);
                }
                else if (_state.CurrentType == TokenType.Const && c == 'i')
                {
                    ParseImaginary(c, i, len, text);
                }
                else if (_state.CurrentType == TokenType.Const && CalcpadCharacterHelpers.IsLetterForTokenizer(c))
                {
                    ParseUnits(c);
                }
                else if (_state.CurrentType == TokenType.Variable && c == '.')
                {
                    // Dots after an identifier are always element access. Names cannot contain dots.
                    Append(TokenType.Variable);
                    _state.TokenStartColumn = i;
                    _builder.Append(c);
                    _state.CurrentType = TokenType.Operator;
                    Append(TokenType.Operator);
                }
                else
                {
                    if (_state.CurrentType == TokenType.None && _builder.Length > 0)
                    {
                        _state.CurrentType = _state.PreviousType;
                        Append(_state.CurrentType);
                        _state.TokenStartColumn = i;
                        _state.CurrentType = InitType(c, _state.CurrentType);
                    }
                    _builder.Append(c);
                }

                // Note: #format keyword handling is done in ParseSpace (sets CurrentType = Format
                // after flushing the keyword), which feeds into the format accumulation at the top
                // of this loop. No per-character override needed here.

                _state.RetainType();

                if (!char.IsWhiteSpace(c))
                    _state.IsLeading = false;

                if (_state.CurrentType == TokenType.Units || _state.CurrentType == TokenType.Variable)
                {
                    if (c == '_')
                        _state.IsSubscript = true;
                }
                else
                {
                    _state.IsSubscript = false;
                }
            }

            // Finalize remaining buffer
            _state.CurrentType = _state.CurrentTypeOrPrevious;
            if (_state.CurrentType == TokenType.Comment || _inHtmlComment)
                CheckHtmlComment();

            Append(_state.CurrentType);

            // In Lint mode, finalize any pending definition at end of line
            if (_mode == TokenizerMode.Lint)
                FinalizeLineLint();

            // In Macro mode, finalize any pending macro definition at end of line
            if (_mode == TokenizerMode.Macro)
                FinalizeLineMacroCollection();

            // For multiline macro definitions: if we saw #def macro$(...) but no = on this line,
            // enter macro definition mode for subsequent lines
            if (_state.HasMacro && !_inMacroDefinition && _macroParameters.Count > 0)
            {
                _inMacroDefinition = true;
            }

            // For inline macro definitions, clear parameters at end of line
            // (they only apply to the single-line definition)
            if (_state.HasMacro && _inMacroDefinition && !_state.IsMacro)
            {
                // This was an inline macro (had =), clear params at end of line
                // Check if the line had = by seeing if IsFunction was set
                if (_state.IsFunction)
                {
                    _inMacroDefinition = false;
                    _macroParameters.Clear();
                }
            }

            _continueLine = _explicitLineBreak || HasImplicitContinuation(text);
            _explicitLineBreak = false;
        }

        /// <summary>
        /// True when the line ends with a line extension character outside a comment and
        /// brackets, matrices or command blocks are still open, so the next line continues it.
        /// </summary>
        private bool HasImplicitContinuation(ReadOnlySpan<char> text)
        {
            if (_state.TextComment != '\0' ||
                (_state.BracketCount <= 0 && _state.MatrixCount <= 0 && _state.CommandCount <= 0))
                return false;

            var i = text.Length - 1;
            while (i >= 0 && char.IsWhiteSpace(text[i]))
                i--;

            return i >= 0 && CharClassifier.IsLineExtension(text[i]);
        }

        private void InitState(ReadOnlyMemory<char> textMemory, int lineNumber)
        {
            // Preserve special content state across lines
            var prevInSpecialContent = _state.InSpecialContent;
            var prevHasHtmlContent = _state.HasHtmlContent;
            var previous = _state;
            var isContinuation = _continueLine;
            _continueLine = false;

            if (!isContinuation)
                _localVariables.Clear();

            _pendingVariableName = null;
            _pendingVariableLine = -1;
            _pendingFunctionName = null;
            _pendingFunctionLine = -1;
            _pendingFunctionParenDepth = 0;
            _beforeFirstCodeToken = true;
            _state = new TokenizerState
            {
                Line = lineNumber,
                Text = textMemory,
                IsLeading = true,
                IsPlot = IsPlotLine(textMemory.Span),
                AllowUnaryMinus = true,
                Keyword = string.Empty,
                InSpecialContent = prevInSpecialContent,
                HasHtmlContent = prevHasHtmlContent
            };

            if (isContinuation)
            {
                _state.IsPlot |= previous.IsPlot;
                _state.IsUnits = previous.IsUnits;
                _state.BracketCount = previous.BracketCount;
                _state.MatrixCount = previous.MatrixCount;
                _state.CommandCount = previous.CommandCount;
                _state.IsInCommandBlock = previous.IsInCommandBlock;
                _state.IsAfterAtOrAmp = previous.IsAfterAtOrAmp;
                _state.IsFunction = previous.IsFunction;
                _state.IsFunctionDefinition = previous.IsFunctionDefinition;
                _state.IsInFunctionParams = previous.IsInFunctionParams;
                _state.IsMacro = previous.IsMacro;
                _state.HasMacro = previous.HasMacro;
                _state.MacroArgs = previous.MacroArgs;
                _state.IsDataExchangeKeyword = previous.IsDataExchangeKeyword;
                _state.Keyword = previous.Keyword;
                _state.CurrentMacroCall = previous.CurrentMacroCall;
                _state.CurrentMacroArgIndex = previous.CurrentMacroArgIndex;
            }

            // Restore comment state from line continuation
            // When the previous line ended with " _" inside a comment, the comment continues
            if (_continueTextComment != '\0')
            {
                _state.TextComment = _continueTextComment;
                _state.TagComment = _continueTagComment;
                _state.CurrentType = _state.InSpecialContent switch
                {
                    SpecialContentType.Script => TokenType.JavaScript,
                    SpecialContentType.Style => TokenType.Css,
                    SpecialContentType.Svg => TokenType.Svg,
                    _ => _state.HasHtmlContent ? TokenType.HtmlContent : TokenType.Comment
                };
                _continueTextComment = '\0';
                _continueTagComment = '\0';
            }
        }

        private void Append(TokenType type)
        {
            if (_builder.Length == 0)
                return;

            var text = _builder.ToString();
            _builder.Clear();

            if (type == TokenType.Input)
            {
                AddToken(type, text);
                return;
            }

            // Resolve types based on context (no error detection - that's the linter's job)
            type = ResolveType(type, text);

            // Check for macro parameters in comment content when inside a macro definition
            if (_inMacroDefinition && _macroParameters.Count > 0 &&
                (type == TokenType.Comment || type == TokenType.HtmlContent ||
                 type == TokenType.Css || type == TokenType.JavaScript || type == TokenType.Svg))
            {
                AppendCommentWithMacroParams(type, text);
                _state.AllowUnaryMinus = true;
                return;
            }

            if (!string.IsNullOrEmpty(text))
            {
                AddToken(type, text);
            }

            _state.AllowUnaryMinus =
                type == TokenType.Comment || type == TokenType.Operator ||
                text == "(" || text == "{" || text == "[";
        }

        private void AddToken(TokenType type, string text)
        {
            // Skip None tokens - they don't need syntax highlighting
            if (type == TokenType.None)
            {
                _state.TokenStartColumn += text.Length;
                return;
            }

            // Track that a code token has been seen, before TrackDefinitions so the flag is
            // available for the next token's ResolveType call. The flag marks the first identifier
            // position on the line, so only a token that can occupy a value position ends it —
            // directive keywords, brackets and operators do not, since the expression a directive
            // introduces starts after the keyword, and after its JSON block in the case of #UI.
            if (_beforeFirstCodeToken &&
                type != TokenType.Comment && type != TokenType.Tag &&
                type != TokenType.HtmlComment && type != TokenType.HtmlContent &&
                type != TokenType.Css && type != TokenType.JavaScript &&
                type != TokenType.Svg &&
                type != TokenType.Keyword && type != TokenType.ControlBlockKeyword &&
                type != TokenType.Bracket && type != TokenType.Operator)
            {
                _beforeFirstCodeToken = false;
            }

            // Track potential definitions
            TrackDefinitions(type, text);

            var token = new Token(
                _state.Line,
                _state.TokenStartColumn,
                text.Length,
                type,
                text
            );
            _result.AddToken(token);
            _state.TokenStartColumn += text.Length;
        }
    }
}
