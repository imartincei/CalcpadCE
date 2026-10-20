using System;
using System.Collections.Generic;
using Calcpad.Highlighter.ContentResolution;
using Calcpad.Highlighter.Linter.Constants;
using Calcpad.Highlighter.Linter.Helpers;
using Calcpad.Highlighter.Tokenizer.Models;

namespace Calcpad.Highlighter.Tokenizer
{
    public partial class CalcpadTokenizer
    {
        // ── Macro state ────────────────────────────────────────────

        private bool _inMacroDefinition;  // True when inside #def...#end def or after = in inline #def
        private readonly HashSet<string> _macroParameters = new(StringComparer.Ordinal);

        // Ordered parameter names per macro, for keyword arguments at call sites
        private readonly Dictionary<string, List<string>> _macroParameterOrder = new(StringComparer.Ordinal);

        // Pre-tokenized argument tokens for the current macro call, with call-site columns
        private List<List<Token>> _macroCallPreTokenized;

        // Whether the current macro argument's pre-computed tokens have been emitted
        private bool _macroArgPreEmitted;

        // Tracks the name of the macro being defined (#def name$) for body/param capture
        private string _pendingMacroDefName;

        // Tracks the last macro name seen at a call site (before ( clears the builder)
        private string _lastMacroCallName;

        // True when macro parameters were set externally, so tokenizing keeps them
        private bool _hasExternalMacroInfo;

        // MacroParser's expansion of each call line, keyed by original line
        private IReadOnlyDictionary<int, List<CallSiteExpansion>> _callSiteExpansions;

        // Argument tokens for the line in _argTokensLine: projected from its expansion, and literal
        private int _argTokensLine = -1;
        private List<Token> _projectedTokens;
        private List<Token> _literalTokens;

        /// <summary>
        /// Sets the macros' parameter names, as Stage 2 collected them including macros from
        /// includes. Call this before Tokenize().
        /// </summary>
        public void SetMacroParameters(Dictionary<string, List<string>> paramOrder)
        {
            _macroParameterOrder.Clear();
            if (paramOrder != null)
            {
                foreach (var kvp in paramOrder)
                {
                    _macroParameterOrder[kvp.Key] = new List<string>(kvp.Value);
                    _definedMacros.Add(kvp.Key);
                }
            }
            _hasExternalMacroInfo = true;
        }

        /// <summary>
        /// Resets all macro state for a new tokenization pass.
        /// Must be called after ResetTypeResolutionState (which clears _definedMacros).
        /// </summary>
        private void ResetMacroState()
        {
            if (!_hasExternalMacroInfo)
            {
                _macroParameterOrder.Clear();
            }
            else
            {
                // Re-populate _definedMacros from externally-set macro info (e.g., from includes)
                // since ResetTypeResolutionState cleared them
                foreach (var macroName in _macroParameterOrder.Keys)
                    _definedMacros.Add(macroName);
            }

            _macroCallPreTokenized = null;
            _macroArgPreEmitted = false;
            _pendingMacroDefName = null;
            _lastMacroCallName = null;
            _argTokensLine = -1;
        }

        /// <summary>
        /// Appends comment text while extracting macro parameters as separate tokens.
        /// Scans for literal macro parameter matches (sorted by length descending).
        /// </summary>
        private void AppendCommentWithMacroParams(TokenType commentType, string text)
        {
            if (string.IsNullOrEmpty(text))
                return;

            // Sort parameters by length descending so longer params match first
            var sortedParams = new List<string>(_macroParameters);
            sortedParams.Sort((a, b) => b.Length.CompareTo(a.Length));

            if (sortedParams.Count == 0)
            {
                AddToken(commentType, text);
                return;
            }

            var segmentStart = 0;
            var i = 0;

            while (i < text.Length)
            {
                bool matched = false;
                foreach (var param in sortedParams)
                {
                    if (i + param.Length <= text.Length &&
                        text.AsSpan(i, param.Length).SequenceEqual(param.AsSpan()))
                    {
                        // Emit text before the match
                        if (i > segmentStart)
                        {
                            AddToken(commentType, text.Substring(segmentStart, i - segmentStart));
                        }
                        // Emit the macro parameter
                        AddToken(TokenType.MacroParameter, param);
                        i += param.Length;
                        segmentStart = i;
                        matched = true;
                        break;
                    }
                }
                if (!matched)
                    i++;
            }

            // Emit remaining text
            if (segmentStart < text.Length)
            {
                AddToken(commentType, text.Substring(segmentStart));
            }
        }

        // ── Macro parsing ──────────────────────────────────────────

        private void ParseMacro()
        {
            _builder.Append('$');
            if (_state.IsMacro)
            {
                _state.CurrentType = TokenType.Macro;
                // Capture macro definition name before Append clears the builder
                _pendingMacroDefName = _builder.ToString();
                _definedMacros.Add(_pendingMacroDefName);
            }
            else if (_state.IsInFunctionParams && _state.HasMacro)
            {
                // Inside macro definition parameters: #def macro$(param1$; param2)
                // Identifiers (with or without $) are macro parameters
                // Add to _macroParameters so they're recognized in the macro body
                var paramName = _builder.ToString();
                _macroParameters.Add(paramName);
                _state.CurrentType = TokenType.MacroParameter;
            }
            else if (_inMacroDefinition && _macroParameters.Contains(_builder.ToString()))
            {
                // Using a macro parameter in the macro body (exact match)
                _state.CurrentType = TokenType.MacroParameter;
            }
            else if (_inMacroDefinition && TrySplitMacroParameterSuffix())
            {
                // A macro parameter matched a suffix of the builder (e.g., "st$" split into "s" + "t$")
                // Prefix was already emitted; builder now contains just the parameter suffix.
                // Fall through to Append() below to emit it as MacroParameter.
            }
            else if (TrySplitDefinedMacroSuffix())
            {
                // A defined macro matched a suffix of the builder (e.g., "innote$" → "in" + "note$")
                // Prefix was already emitted; builder now contains just the macro name.
                // Fall through to Append() below to emit it as Macro.
            }
            else if (_state.CurrentType == TokenType.Variable || _state.CurrentType == TokenType.Units)
            {
                var name = _builder.ToString();

                _state.CurrentType = TokenType.Macro;
                // Capture call-site macro name before Append clears the builder
                _lastMacroCallName = name;
            }

            Append(_state.CurrentType);
            if (_state.IsMacro)
                _state.HasMacro = true;

            _state.IsMacro = false;
        }

        /// <summary>
        /// Peeks ahead in the current line to check if the next non-whitespace character is '('.
        /// Used to distinguish string function calls from string variable references.
        /// </summary>
        private bool IsFollowedByOpenParen()
        {
            var text = _state.Text.Span;
            var pos = _state.TokenStartColumn + _builder.Length;
            ParsingHelpers.SkipWhitespace(text, ref pos);
            return pos < text.Length && text[pos] == '(';
        }

        /// <summary>
        /// Checks if any defined macro name matches a suffix of the builder, longest match first;
        /// when one does, the prefix is emitted as the current type and the suffix set up as Macro
        /// for the caller to emit. Example: builder "innote$" with defined macro "note$" emits
        /// "in" as Units and leaves "note$" in the builder.
        /// </summary>
        private bool TrySplitDefinedMacroSuffix()
        {
            if (_definedMacros.Count == 0)
                return false;

            var text = _builder.ToString();
            string bestMatch = null;

            foreach (var macro in _definedMacros)
            {
                // Use <= to include full matches (e.g., "unhide$" matches "unhide$" exactly)
                // This ensures the longest defined macro wins over shorter suffixes
                // (e.g., "unhide$" is preferred over "hide$" when both are defined)
                if (macro.Length <= text.Length && text.EndsWith(macro, StringComparison.Ordinal))
                {
                    if (bestMatch == null || macro.Length > bestMatch.Length)
                        bestMatch = macro;
                }
            }

            if (bestMatch == null)
                return false;

            // Split: emit prefix as current type, set up suffix as Macro
            var prefixLen = text.Length - bestMatch.Length;
            if (prefixLen > 0)
            {
                _builder.Clear();
                _builder.Append(text.AsSpan(0, prefixLen));
                Append(_state.CurrentType);  // Emit prefix (Units, Variable, etc.)
            }

            _builder.Clear();
            _builder.Append(bestMatch);
            _state.CurrentType = TokenType.Macro;
            _lastMacroCallName = bestMatch;
            return true;
        }

        /// <summary>
        /// When inside a macro body, checks if any macro parameter matches a suffix of the builder,
        /// longest match first to match Calcpad.Core's parameter substitution; when one does, the
        /// prefix is emitted as the current type and the suffix set up as MacroParameter. Example:
        /// builder "st$" with parameter "t$" emits "s" as Variable and leaves "t$" in the builder.
        /// </summary>
        private bool TrySplitMacroParameterSuffix()
        {
            if (_macroParameters.Count == 0)
                return false;

            var text = _builder.ToString();
            string bestMatch = null;

            foreach (var param in _macroParameters)
            {
                if (param.Length < text.Length && text.AsSpan().EndsWith(param.AsSpan(), StringComparison.Ordinal))
                {
                    if (bestMatch == null || param.Length > bestMatch.Length)
                        bestMatch = param;
                }
            }

            if (bestMatch == null)
                return false;

            // Split: emit prefix as current type, set up suffix as MacroParameter
            var prefixLen = text.Length - bestMatch.Length;
            _builder.Clear();
            _builder.Append(text.AsSpan(0, prefixLen));
            Append(_state.CurrentType);  // Emit prefix (Variable, etc.)

            _builder.Append(bestMatch);
            _state.CurrentType = TokenType.MacroParameter;
            return true;
        }

        private void ParseMacroInComment(TokenType type)
        {
            var len = _builder.Length;
            if (len < 2)
                return;

            // Find the leftmost extent of identifier characters before the trailing $
            int identStart = len - 1;
            for (int i = len - 2; i >= 0; i--)
            {
                if (!IsMacroIdentChar(_builder[i], i))
                    break;
                identStart = i;
            }

            // Try each possible starting position, longest candidate first
            var s = _builder.ToString();
            for (int j = identStart; j < len - 1; j++)
            {
                var candidate = s[j..];
                // Check for macro parameters first (when inside a macro definition)
                if (_inMacroDefinition && _macroParameters.Contains(candidate))
                {
                    _builder.Remove(j, len - j);
                    Append(type);
                    _builder.Append(candidate);
                    Append(TokenType.MacroParameter);
                    _state.CurrentType = type;
                    return;
                }
                // Check for defined macros
                if (_definedMacros.Contains(candidate))
                {
                    _builder.Remove(j, len - j);
                    Append(type);
                    _builder.Append(candidate);
                    Append(TokenType.Macro);
                    _state.CurrentType = type;
                    return;
                }
            }

            // Fallback: highlight $-suffixed identifier as potential macro
            // This enables both syntax highlighting and linter validation of undefined macros
            for (int j = identStart; j < len - 1; j++)
            {
                if (char.IsLetter(s[j]) || s[j] == '_')
                {
                    _builder.Remove(j, len - j);
                    Append(type);
                    _builder.Append(s[j..]);
                    Append(TokenType.Macro);
                    _state.CurrentType = type;
                    return;
                }
            }
        }

        private void ParseMacroArgs(char c, int position)
        {
            if (_macroCallPreTokenized != null)
            {
                ParseMacroArgsPreTokenized(c, position);
                return;
            }

            if (_state.TextComment == '\0' && (c == '\'' || c == '"'))
            {
                Append(_state.CurrentTypeOrPrevious);
                _state.TextComment = c;
                _builder.Append(c);
                _state.CurrentType = TokenType.Comment;
                _state.PreviousType = _state.CurrentType;
                return;
            }

            if (_state.TextComment != '\0')
            {
                if (c == _state.TextComment)
                {
                    _builder.Append(c);
                    Append(TokenType.Comment);
                    _state.TextComment = '\0';
                }
                else if (c == '$' && _builder.Length > 0)
                {
                    _builder.Append('$');
                    ParseMacroInComment(TokenType.Comment);
                }
                else
                {
                    _builder.Append(c);
                    _state.CurrentType = TokenType.Comment;
                }
                _state.PreviousType = _state.CurrentType;
                return;
            }

            if (c == '$' && _builder.Length > 0)
            {
                _builder.Append('$');
                if (IsMacroKeywordArgumentName(position))
                    Append(TokenType.MacroParameter);
                else
                    ParseMacroInComment(_state.CurrentType);
            }
            else if (c == '(' || c == ')')
            {
                if (c == '(')
                    _state.BracketCount++;
                else
                {
                    if (_state.BracketCount <= _state.MacroArgs)
                    {
                        _state.MacroArgs = 0;
                        _state.CurrentMacroCall = null;
                        _state.CurrentMacroArgIndex = 0;
                        _state.TextComment = '\0';
                    }

                    _state.BracketCount--;
                }

                Append(_state.CurrentTypeOrPrevious);
                _builder.Append(c);
                _state.CurrentType = TokenType.Bracket;
                Append(_state.CurrentType);
                if (_state.TextComment != '\0' && c == ')' && _state.BracketCount == 0)
                    _state.CurrentType = TokenType.Comment;
            }
            else if (c == ';')
            {
                Append(_state.CurrentTypeOrPrevious);
                _builder.Append(c);
                _state.CurrentType = TokenType.Operator;
                Append(_state.CurrentType);
                _state.CurrentMacroArgIndex++;
            }
            else if (CalcpadBuiltIns.Operators.Contains(c))
            {
                Append(_state.CurrentTypeOrPrevious);
                _builder.Append(c);
                _state.CurrentType = TokenType.Operator;
                Append(_state.CurrentType);
            }
            else if (char.IsWhiteSpace(c))
            {
                Append(_state.CurrentTypeOrPrevious);
                _builder.Append(c);
                Append(TokenType.None);
                _state.CurrentType = TokenType.None;
            }
            else if (_builder.Length == 0)
            {
                _state.CurrentType = InitType(c, _state.CurrentType);
                _builder.Append(c);
            }
            else
            {
                _builder.Append(c);
            }

            _state.PreviousType = _state.CurrentType;
        }

        /// <summary>True for "name$" followed by '=' that names a parameter of the macro being called.</summary>
        private bool IsMacroKeywordArgumentName(int dollar)
        {
            if (_state.CurrentMacroCall is null ||
                !_macroParameterOrder.TryGetValue(_state.CurrentMacroCall, out var parameters) ||
                !parameters.Contains(_builder.ToString()))
                return false;

            var span = _state.Text.Span;
            var i = dollar + 1;
            ParsingHelpers.SkipWhitespace(span, ref i);
            return i < span.Length && span[i] == '=' && (i + 1 >= span.Length || span[i + 1] != '=');
        }

        /// <summary>
        /// Extracts raw argument strings and their start columns from a macro call, scanning from
        /// startPos (just after the opening '(') until the matching ')'. Returns a list of
        /// (argText, argStartCol).
        /// </summary>
        private static List<(string Text, int StartCol)> ExtractMacroCallArgsWithPositions(ReadOnlySpan<char> text, int startPos)
        {
            var args = new List<(string, int)>();
            var depth = 1;
            var argStart = startPos;

            for (int i = startPos; i < text.Length && depth > 0; i++)
            {
                var c = text[i];
                if (c == '(') depth++;
                else if (c == ')')
                {
                    depth--;
                    if (depth == 0)
                    {
                        args.Add((text.Slice(argStart, i - argStart).ToString(), argStart));
                        break;
                    }
                }
                else if (c == ';' && depth == 1)
                {
                    args.Add((text.Slice(argStart, i - argStart).ToString(), argStart));
                    argStart = i + 1;
                }
            }

            return args;
        }

        /// <summary>
        /// Takes argument tokens from MacroParser's expansion of the line, projected back to it;
        /// text the expansion doesn't carry (nested calls, keyword names, parameters in a #def
        /// body) keeps its literal tokens. Without an expansion the arguments are tokenized literally.
        /// </summary>
        private void ResolveMacroCallArgTokens(int openParenCol)
        {
            _macroCallPreTokenized = null;
            _macroArgPreEmitted = false;
            if (_callSiteExpansions is null || !_callSiteExpansions.TryGetValue(_state.Line, out var expansions))
                return;

            if (_argTokensLine != _state.Line)
            {
                _argTokensLine = _state.Line;
                _projectedTokens = ProjectExpansions(expansions);
                var literal = CreateArgumentTokenizer();
                literal._inMacroDefinition = _inMacroDefinition;
                literal._macroParameters.UnionWith(_macroParameters);
                _literalTokens = literal.TokenizeSingleLine(_state.Text.ToString(), _state.Line).Tokens;
            }

            _macroCallPreTokenized = [];
            foreach (var (text, start) in ExtractMacroCallArgsWithPositions(_state.Text.Span, openParenCol + 1))
                _macroCallPreTokenized.Add(MergeArgumentTokens(start, start + text.Length));
        }

        /// <summary>A tokenizer that resolves names as this one does at the current line, without expansions.</summary>
        private CalcpadTokenizer CreateArgumentTokenizer()
        {
            var tokenizer = new CalcpadTokenizer
            {
                _parent = this,
                _resolvedVariableLines = _resolvedVariableLines,
                _runtimeNames = _runtimeNames,
                _hasExternalMacroInfo = true
            };
            tokenizer._definedMacros.UnionWith(_definedMacros);
            foreach (var (name, parameters) in _macroParameterOrder)
                tokenizer._macroParameterOrder[name] = parameters;

            return tokenizer;
        }

        /// <summary>Tokens of the expanded lines, cut to the runs of characters that came from the call site.</summary>
        private List<Token> ProjectExpansions(List<CallSiteExpansion> expansions)
        {
            var tokenizer = CreateArgumentTokenizer();
            var result = new List<Token>();
            foreach (var expansion in expansions)
            {
                var columns = expansion.Columns;
                foreach (var token in tokenizer.TokenizeSingleLine(expansion.Text, expansion.CallLine).Tokens)
                {
                    if (token.Type == TokenType.None)
                        continue;

                    var end = Math.Min(token.Column + token.Length, columns.Length);
                    for (var i = token.Column; i < end;)
                    {
                        var column = columns[i];
                        var j = i + 1;
                        if (column >= 0)
                        {
                            while (j < end && columns[j] == column + j - i)
                                ++j;

                            result.Add(Slice(token, i - token.Column, j - token.Column, column));
                        }
                        i = j;
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// Projected tokens within [start, end), expression types winning over comment text where
        /// an argument appears more than once, with literal tokens filling the rest.
        /// </summary>
        private List<Token> MergeArgumentTokens(int start, int end)
        {
            var tokens = new List<Token>();
            foreach (var token in _projectedTokens)
                if (!IsText(token.Type))
                    AddProjected(token);
            foreach (var token in _projectedTokens)
                if (IsText(token.Type))
                    AddProjected(token);

            tokens.Sort((a, b) => a.Column.CompareTo(b.Column));
            var projected = tokens.ToArray();
            foreach (var token in _literalTokens)
            {
                var from = Math.Max(token.Column, start);
                var to = Math.Min(token.Column + token.Length, end);
                foreach (var covered in projected)
                {
                    if (from >= to || covered.Column >= to)
                        break;

                    if (covered.Column > from)
                        AddLiteral(token, from, covered.Column);

                    from = Math.Max(from, covered.Column + covered.Length);
                }
                if (from < to)
                    AddLiteral(token, from, to);
            }
            tokens.Sort((a, b) => a.Column.CompareTo(b.Column));
            return tokens;

            void AddLiteral(Token token, int from, int to)
            {
                var piece = Slice(token, from - token.Column, to - token.Column, from);
                if (!string.IsNullOrWhiteSpace(piece.Text))
                    tokens.Add(piece);
            }

            void AddProjected(Token token)
            {
                var from = Math.Max(token.Column, start);
                var to = Math.Min(token.Column + token.Length, end);
                if (from >= to)
                    return;

                foreach (var t in tokens)
                    if (t.Column < to && from < t.Column + t.Length)
                        return;

                tokens.Add(Slice(token, from - token.Column, to - token.Column, from));
            }
        }

        private static bool IsText(TokenType type) =>
            type is >= TokenType.Comment and <= TokenType.Svg;

        /// <summary>Characters [from, to) of a token, placed at a column on the current line.</summary>
        private Token Slice(Token token, int from, int to, int column) =>
            new(_state.Line, column, to - from, token.Type,
                from == 0 && to == token.Length ? token.Text : token.Text.Substring(from, to - from));

        /// <summary>
        /// Handles macro argument characters when pre-tokenized data is available, emitting the
        /// pre-computed tokens for each argument plus the call's own separators and closing paren.
        /// Parens and semicolons nested deeper than the call belong to an argument and are already
        /// covered by its tokens, so they are only counted here, never emitted again.
        /// </summary>
        private void ParseMacroArgsPreTokenized(char c, int position)
        {
            // Emit pre-computed tokens for current arg if not yet done
            if (!_macroArgPreEmitted &&
                _state.CurrentMacroArgIndex < _macroCallPreTokenized.Count)
            {
                foreach (var token in _macroCallPreTokenized[_state.CurrentMacroArgIndex])
                {
                    _result.AddToken(token);
                }
                _macroArgPreEmitted = true;
            }

            // Structural characters carry their own column, so the token cursor is set from
            // the character position instead of accumulating over skipped argument content.
            // Only the call's own closing paren and its argument separators sit at call depth;
            // the opening paren was already emitted by ParseBrackets.
            var atCallDepth = _state.BracketCount <= _state.MacroArgs;

            if (c == '(' || c == ')')
            {
                if (c == '(')
                    _state.BracketCount++;
                else
                {
                    if (atCallDepth)
                    {
                        _state.MacroArgs = 0;
                        _state.CurrentMacroCall = null;
                        _state.CurrentMacroArgIndex = 0;
                        _macroCallPreTokenized = null;
                    }

                    _state.BracketCount--;
                }

                if (c == ')' && atCallDepth)
                {
                    _builder.Clear();
                    _builder.Append(c);
                    _state.TokenStartColumn = position;
                    _state.CurrentType = TokenType.Bracket;
                    Append(TokenType.Bracket);
                }

                if (_state.TextComment != '\0' && c == ')' && _state.BracketCount == 0)
                    _state.CurrentType = TokenType.Comment;
            }
            else if (c == ';' && atCallDepth)
            {
                _builder.Clear();
                _builder.Append(c);
                _state.TokenStartColumn = position;
                _state.CurrentType = TokenType.Operator;
                Append(TokenType.Operator);

                // Move to next argument
                _state.CurrentMacroArgIndex++;
                _macroArgPreEmitted = false;
            }
            // else: skip - tokens were already emitted via pre-computed data

            _state.PreviousType = _state.CurrentType;
        }

        private bool ParseMacroContent(char c, int i, int len)
        {
            // Inside the parameter list '=' introduces a default, not the macro body.
            if (c == '=' && _state.HasMacro && !_state.IsInFunctionParams && _state.BracketCount == 0)
            {
                if (_pendingMacroDefName != null)
                {
                    if (!_macroParameterOrder.ContainsKey(_pendingMacroDefName))
                    {
                        // Declared order
                        _macroParameterOrder[_pendingMacroDefName] = ExtractMacroParams(_state.Text.Span);
                    }
                    _pendingMacroDefName = null;
                }

                // Return false to let the main loop tokenize the body normally,
                // matching multi-line macro behavior.
                return false;
            }
            return false;
        }

        private static List<string> ExtractMacroParams(ReadOnlySpan<char> lineSpan) =>
            ExtractMacroParamsWithDefaults(lineSpan).Names;

        /// <summary>
        /// Extracts macro parameter names and defaults from a #def line (inline or multiline form).
        /// </summary>
        private static (List<string> Names, List<string> Defaults) ExtractMacroParamsWithDefaults(ReadOnlySpan<char> lineSpan)
        {
            var dollarIndex = lineSpan.IndexOf('$');
            if (dollarIndex < 0) return ([], null);

            // Check that '(' immediately follows '$' (with optional whitespace).
            // If '=' appears before '(', the '(' is part of the body, not params.
            // e.g., #def emptyV$ = find(vector(1); 1; 1) — no params
            var afterDollar = lineSpan[(dollarIndex + 1)..].TrimStart();
            if (afterDollar.IsEmpty || afterDollar[0] != '(')
                return ([], null);

            var openParen = lineSpan.Length - afterDollar.Length;  // absolute index of '('
            var closeParen = ParsingHelpers.FindMatchingClose(lineSpan, openParen, '(', ')');
            if (closeParen < 0) return ([], null);

            return ParsingHelpers.ParseParamList(lineSpan[(openParen + 1)..closeParen].ToString());
        }
    }
}
