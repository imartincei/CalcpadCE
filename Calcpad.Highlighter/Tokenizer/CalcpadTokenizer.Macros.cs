using System;
using System.Collections.Generic;
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

        // Tracks which parameters for each macro are "comment parameters" (used in comment text)
        // Key: macro name (case-insensitive), Value: set of parameter names that appear in comments
        private readonly Dictionary<string, HashSet<string>> _macroCommentParameters = new(StringComparer.OrdinalIgnoreCase);

        // Tracks ordered parameter lists per macro (for call-site argument matching)
        private readonly Dictionary<string, List<string>> _macroParameterOrder = new(StringComparer.OrdinalIgnoreCase);

        // Stores inline macro bodies for argument type resolution via substitution + tokenization
        // Key: macro name (case-insensitive), Value: body text (everything after = in #def)
        private readonly Dictionary<string, string> _macroBodies = new(StringComparer.OrdinalIgnoreCase);

        // Pre-tokenized argument tokens for the current macro call (populated by look-ahead)
        // Each entry is a list of tokens for one argument, with call-site-adjusted columns
        private List<List<Token>> _macroCallPreTokenized;

        // Whether the current macro argument's pre-computed tokens have been emitted
        private bool _macroArgPreEmitted;

        // Tracks the name of the macro being defined (#def name$) for body/param capture
        private string _pendingMacroDefName;

        // Tracks the last macro name seen at a call site (before ( clears the builder)
        private string _lastMacroCallName;

        // True if macro comment parameters were set externally via SetMacroCommentParameters
        // When true, CollectDefinitions will not overwrite the external info
        private bool _hasExternalMacroCommentInfo;

        // Macros currently being resolved on the arg-tokenization recursion stack.
        // Shared across nested tokenizer instances created in ResolveMacroCallArgTokens so that
        // a parameterized self-reference (#def grow$(x$) = grow$(x$)) does not recurse forever.
        private HashSet<string> _resolvingMacros;

        /// <summary>
        /// Shares the "currently-resolving macros" set with a nested tokenizer.
        /// Used by ResolveMacroCallArgTokens to propagate cycle state into the fresh
        /// tokenizer that re-tokenizes the substituted macro body.
        /// </summary>
        internal void SetResolvingMacros(HashSet<string> resolvingMacros)
        {
            _resolvingMacros = resolvingMacros;
        }

        /// <summary>
        /// Sets macro comment parameter information from ContentResolver Stage2.
        /// Call this before Tokenize() when you have pre-computed comment parameter info.
        /// This enables correct tokenization of macro call arguments as Comment vs expression.
        /// </summary>
        /// <param name="commentParams">Maps macro name to set of parameter names that are comment parameters</param>
        /// <param name="paramOrder">Maps macro name to ordered list of parameter names</param>
        public void SetMacroCommentParameters(
            Dictionary<string, HashSet<string>> commentParams,
            Dictionary<string, List<string>> paramOrder,
            Dictionary<string, string> macroBodies = null)
        {
            _macroCommentParameters.Clear();
            _macroParameterOrder.Clear();
            _macroBodies.Clear();

            if (commentParams != null)
            {
                foreach (var kvp in commentParams)
                {
                    _macroCommentParameters[kvp.Key] = new HashSet<string>(kvp.Value, StringComparer.Ordinal);
                }
            }

            if (paramOrder != null)
            {
                foreach (var kvp in paramOrder)
                {
                    _macroParameterOrder[kvp.Key] = new List<string>(kvp.Value);
                    _definedMacros.Add(kvp.Key);
                }
            }

            if (macroBodies != null)
            {
                foreach (var kvp in macroBodies)
                {
                    _macroBodies[kvp.Key] = kvp.Value;
                }
            }

            _hasExternalMacroCommentInfo = true;
        }

        /// <summary>
        /// Resets all macro state for a new tokenization pass.
        /// Must be called after ResetTypeResolutionState (which clears _definedMacros).
        /// </summary>
        private void ResetMacroState()
        {
            // Only clear macro comment info if not set externally
            if (!_hasExternalMacroCommentInfo)
            {
                _macroCommentParameters.Clear();
                _macroParameterOrder.Clear();
                _macroBodies.Clear();
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
        /// Checks if any defined macro name matches a suffix of the builder.
        /// If found, splits the builder: emits the prefix as current type and sets up
        /// the suffix as Macro for the caller to emit. Uses longest-match-first.
        /// Example: builder "innote$" with defined macro "note$" → emits "in" as Units, leaves "note$" in builder.
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
                if (macro.Length <= text.Length && text.EndsWith(macro, StringComparison.OrdinalIgnoreCase))
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
        /// When inside a macro body, checks if any macro parameter matches a suffix of the builder.
        /// If found, splits the builder: emits the prefix as current type and sets up the suffix
        /// as MacroParameter for the caller to emit. Uses longest-match-first to match
        /// Calcpad.Core's parameter substitution behavior.
        /// Example: builder "st$" with parameter "t$" → emits "s" as Variable, leaves "t$" in builder.
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
            // If we have pre-tokenized data from body substitution, use that instead
            if (_macroCallPreTokenized != null)
            {
                ParseMacroArgsPreTokenized(c, position);
                return;
            }

            // Determine if current argument is a "comment parameter"
            var isCommentArg = IsCurrentMacroArgComment();

            // Handle comment delimiters within expression arguments (fallback path)
            if (!isCommentArg && _state.TextComment == '\0' && (c == '\'' || c == '"'))
            {
                // Start comment within macro argument
                Append(_state.CurrentTypeOrPrevious);
                _state.TextComment = c;
                _builder.Append(c);
                _state.CurrentType = TokenType.Comment;
                _state.PreviousType = _state.CurrentType;
                return;
            }

            // Inside a comment within macro args - accumulate as Comment
            if (!isCommentArg && _state.TextComment != '\0')
            {
                if (c == _state.TextComment)
                {
                    // End of comment
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
                ParseMacroInComment(isCommentArg ? TokenType.Comment : _state.CurrentType);
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
                        // Clear macro call tracking when we exit the macro args
                        _state.CurrentMacroCall = null;
                        _state.CurrentMacroArgIndex = 0;
                        _state.TextComment = '\0'; // Clear any unclosed quote from macro args
                    }

                    _state.BracketCount--;
                }

                // Flush buffer before bracket
                if (isCommentArg)
                {
                    Append(TokenType.Comment);
                }
                else
                {
                    // For expression arguments, flush as current type
                    Append(_state.CurrentTypeOrPrevious);
                }

                _builder.Append(c);
                _state.CurrentType = TokenType.Bracket;
                Append(_state.CurrentType);
                if (_state.TextComment != '\0' && c == ')' && _state.BracketCount == 0)
                    _state.CurrentType = TokenType.Comment;
            }
            else if (c == ';')
            {
                // Flush buffer before semicolon
                if (isCommentArg)
                {
                    Append(TokenType.Comment);
                }
                else
                {
                    Append(_state.CurrentTypeOrPrevious);
                }

                _builder.Append(c);
                _state.CurrentType = TokenType.Operator;
                Append(_state.CurrentType);

                // Move to next argument
                _state.CurrentMacroArgIndex++;
            }
            else if (isCommentArg)
            {
                // Comment argument - just accumulate everything
                _builder.Append(c);
                _state.CurrentType = TokenType.Comment;
            }
            else
            {
                // Expression argument - tokenize normally
                // Handle operators
                if (CalcpadBuiltIns.Operators.Contains(c))
                {
                    Append(_state.CurrentTypeOrPrevious);
                    _builder.Append(c);
                    _state.CurrentType = TokenType.Operator;
                    Append(_state.CurrentType);
                }
                else if (char.IsWhiteSpace(c))
                {
                    // Flush buffer on whitespace
                    Append(_state.CurrentTypeOrPrevious);
                    _builder.Append(c);
                    Append(TokenType.None);
                    _state.CurrentType = TokenType.None;
                }
                else if (_builder.Length == 0)
                {
                    // Start new token
                    _state.CurrentType = InitType(c, _state.CurrentType);
                    _builder.Append(c);
                }
                else
                {
                    _builder.Append(c);
                }
            }

            _state.PreviousType = _state.CurrentType;
        }

        /// <summary>
        /// Checks if the current macro argument position corresponds to a "comment parameter".
        /// </summary>
        private bool IsCurrentMacroArgComment()
        {
            if (string.IsNullOrEmpty(_state.CurrentMacroCall))
                return false;

            // Check if we have parameter order info for this macro
            if (!_macroParameterOrder.TryGetValue(_state.CurrentMacroCall, out var paramOrder))
                return false;

            // Check if current argument index is valid
            if (_state.CurrentMacroArgIndex >= paramOrder.Count)
                return false;

            var paramName = paramOrder[_state.CurrentMacroArgIndex];

            // Check if this parameter is a "comment parameter"
            if (_macroCommentParameters.TryGetValue(_state.CurrentMacroCall, out var commentParams))
            {
                return commentParams.Contains(paramName);
            }

            return false;
        }

        /// <summary>
        /// Extracts raw argument strings and their start columns from a macro call.
        /// Scans from startPos (just after the opening '(') until the matching ')'.
        /// Returns list of (argText, argStartCol).
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
        /// Resolves macro call argument tokens by substituting each argument into the macro body,
        /// tokenizing the expanded body, and extracting tokens that correspond to the argument region.
        /// Token columns are adjusted to map back to the original call-site positions.
        /// </summary>
        private void ResolveMacroCallArgTokens(string macroName, int openParenCol)
        {
            _macroCallPreTokenized = null;
            _macroArgPreEmitted = false;

            if (!_macroBodies.TryGetValue(macroName, out var body))
                return;
            if (!_macroParameterOrder.TryGetValue(macroName, out var paramOrder))
                return;
            if (paramOrder.Count == 0)
                return;

            // Cycle guard: a macro whose body re-invokes itself (directly or transitively)
            // would otherwise drive ResolveMacroCallArgTokens -> TokenizeSingleLine -> ParseBrackets
            // back into ResolveMacroCallArgTokens forever and overflow the stack.
            // Falling through with _macroCallPreTokenized = null routes ParseMacroArgs through
            // the non-pre-tokenized path, which tokenizes the call literally without re-expanding.
            if (_resolvingMacros != null && _resolvingMacros.Contains(macroName))
                return;

            // Look ahead to extract arguments from the current line
            var args = ExtractMacroCallArgsWithPositions(_state.Text.Span, openParenCol + 1);
            if (args.Count == 0)
                return;

            _resolvingMacros ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _resolvingMacros.Add(macroName);
            try
            {
                _macroCallPreTokenized = new List<List<Token>>();

                for (int i = 0; i < args.Count && i < paramOrder.Count; i++)
                {
                    var (argText, argStartCol) = args[i];
                    var paramName = paramOrder[i];

                    // Find parameter position in body, searching line-by-line to avoid
                    // cross-line state leakage (PreviousType carrying over from prior lines
                    // would cause identifiers after Const tokens to be misclassified as Units)
                    string targetLine = null;
                    int posInLine = -1;
                    if (body.IndexOf('\n') >= 0)
                    {
                        // Multi-line body: find the specific line containing the parameter
                        int lineStart = 0;
                        while (lineStart < body.Length)
                        {
                            int lineEnd = body.IndexOf('\n', lineStart);
                            if (lineEnd < 0) lineEnd = body.Length;
                            var lineSpan = body.AsSpan(lineStart, lineEnd - lineStart);
                            int idx = lineSpan.IndexOf(paramName.AsSpan(), StringComparison.Ordinal);
                            if (idx >= 0)
                            {
                                targetLine = lineSpan.ToString();
                                posInLine = idx;
                                break;
                            }
                            lineStart = lineEnd + 1;
                        }
                    }
                    else
                    {
                        // Single-line body: use as-is
                        var idx = body.AsSpan().IndexOf(paramName.AsSpan(), StringComparison.Ordinal);
                        if (idx >= 0)
                        {
                            targetLine = body;
                            posInLine = idx;
                        }
                    }

                    if (targetLine == null || posInLine < 0)
                    {
                        _macroCallPreTokenized.Add(new List<Token>());
                        continue;
                    }

                    // Substitute parameter with argument in the target line only
                    var expanded = targetLine.Substring(0, posInLine) + argText + targetLine.Substring(posInLine + paramName.Length);

                    // Tokenize the expanded line using a fresh tokenizer with macro context
                    // so nested macro calls (e.g., outer$(inner$(x))) are properly resolved
                    var tokenizer = new CalcpadTokenizer();
                    if (_hasExternalMacroCommentInfo)
                        tokenizer.SetMacroCommentParameters(_macroCommentParameters, _macroParameterOrder, _macroBodies);
                    tokenizer.SetResolvingMacros(_resolvingMacros);
                    var result = tokenizer.TokenizeSingleLine(expanded, _state.Line);

                    // Extract tokens overlapping with the argument region in the expanded string
                    int argStartInExpanded = posInLine;
                    int argEndInExpanded = posInLine + argText.Length;
                    var argTokens = new List<Token>();

                    foreach (var token in result.Tokens)
                    {
                        int tokStart = token.Column;
                        int tokEnd = token.Column + token.Length;

                        // Skip tokens entirely outside the arg range
                        if (tokEnd <= argStartInExpanded || tokStart >= argEndInExpanded)
                            continue;

                        // Clip to arg range
                        int clipStart = Math.Max(tokStart, argStartInExpanded);
                        int clipEnd = Math.Min(tokEnd, argEndInExpanded);
                        int clipLen = clipEnd - clipStart;

                        if (clipLen <= 0)
                            continue;

                        // Adjust column to original call-site line
                        int originalCol = clipStart - argStartInExpanded + argStartCol;

                        // Extract clipped text
                        int textOffset = clipStart - tokStart;
                        string clipText = (textOffset == 0 && clipLen == token.Length)
                            ? token.Text
                            : token.Text.Substring(textOffset, clipLen);

                        argTokens.Add(new Token(_state.Line, originalCol, clipLen, token.Type, clipText));
                    }

                    // Fix up tokens: if inside a macro definition, reclassify any tokens
                    // matching outer macro parameters from Macro to MacroParameter
                    if (_inMacroDefinition && _macroParameters.Count > 0)
                    {
                        for (int j = 0; j < argTokens.Count; j++)
                        {
                            var t = argTokens[j];
                            if (t.Type == TokenType.Macro && _macroParameters.Contains(t.Text))
                            {
                                argTokens[j] = new Token(t.Line, t.Column, t.Length, TokenType.MacroParameter, t.Text);
                            }
                        }
                    }

                    _macroCallPreTokenized.Add(argTokens);
                }
            }
            finally
            {
                _resolvingMacros.Remove(macroName);
            }
        }

        /// <summary>
        /// Handles macro argument characters when pre-tokenized data is available.
        /// Emits pre-computed tokens for each argument and handles the call's own structural
        /// characters (the argument separators and the closing paren). Parens and semicolons
        /// nested deeper than the call belong to an argument and are already covered by its
        /// pre-tokenized tokens, so they are only counted here, never emitted again.
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
            if (c == '=' && _state.HasMacro && !_state.IsInFunctionParams)
            {
                var afterEquals = i + 1 < len ? _state.Text.Span[(i + 1)..].TrimStart().ToString() : string.Empty;

                // Store macro body and param order for argument type resolution (all modes)
                if (_pendingMacroDefName != null)
                {
                    _macroBodies[_pendingMacroDefName] = afterEquals;
                    if (!_macroParameterOrder.ContainsKey(_pendingMacroDefName))
                    {
                        _macroParameterOrder[_pendingMacroDefName] = new List<string>(_macroParameters);
                    }
                    _pendingMacroDefName = null;
                }

                // In Macro mode, capture the inline content before tokenizing the rest
                if (_mode == TokenizerMode.Macro && _macroCurrName != null)
                {
                    _macroCurrInlineContent = afterEquals;
                    _macroCurrIsInline = true;
                    var paramNames = ExtractMacroParams(_state.Text.Span);
                    _macroCurrParams = paramNames;
                    // Ensure _macroParameterOrder reflects the ordered param names
                    if (!_macroParameterOrder.ContainsKey(_macroCurrName))
                        _macroParameterOrder[_macroCurrName] = paramNames;
                }

                // Return false to let the main loop tokenize the body normally,
                // matching multi-line macro behavior.
                return false;
            }
            return false;
        }

        /// <summary>
        /// Extracts macro parameter names from a #def line.
        /// Handles both inline (#def macro$(x$; y$) = ...) and multiline forms.
        /// </summary>
        private static List<string> ExtractMacroParams(ReadOnlySpan<char> lineSpan)
        {
            var names = new List<string>();

            var dollarIndex = lineSpan.IndexOf('$');
            if (dollarIndex < 0) return names;

            // Check that '(' immediately follows '$' (with optional whitespace).
            // If '=' appears before '(', the '(' is part of the body, not params.
            // e.g., #def emptyV$ = find(vector(1); 1; 1) — no params
            var afterDollar = lineSpan[(dollarIndex + 1)..].TrimStart();
            if (afterDollar.IsEmpty || afterDollar[0] != '(')
                return names;

            var openParen = lineSpan.Length - afterDollar.Length;  // absolute index of '('
            var closeParen = ParsingHelpers.FindMatchingClose(lineSpan, openParen, '(', ')');
            if (closeParen < 0) return names;

            var paramsSpan = lineSpan.Slice(openParen + 1, closeParen - openParen - 1);

            // Split by ';' at paren depth 0
            int segStart = 0;
            int parenDepth = 0;

            void ProcessSegment(ReadOnlySpan<char> seg)
            {
                seg = seg.Trim();
                if (seg.IsEmpty) return;
                names.Add(seg.ToString());
            }

            for (int i = 0; i < paramsSpan.Length; i++)
            {
                var c = paramsSpan[i];
                if (c == '(') parenDepth++;
                else if (c == ')') parenDepth--;
                else if (c == ';' && parenDepth == 0)
                {
                    ProcessSegment(paramsSpan[segStart..i]);
                    segStart = i + 1;
                }
            }
            ProcessSegment(paramsSpan[segStart..]);

            return names;
        }

        /// <summary>
        /// Finds which parameters appear in comment sections of a line.
        /// Comments start with ' or " and end with the same character.
        /// </summary>
        public static HashSet<string> FindCommentParamsInLine(string line, List<string> parameters)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            if (parameters.Count == 0)
                return result;

            var lineSpan = line.AsSpan();

            // Find all comment sections in the line
            var inComment = false;
            char commentChar = '\0';
            var commentStart = -1;

            for (int i = 0; i < lineSpan.Length; i++)
            {
                var c = lineSpan[i];

                if (!inComment && (c == '\'' || c == '"'))
                {
                    inComment = true;
                    commentChar = c;
                    commentStart = i;
                }
                else if (inComment && c == commentChar)
                {
                    // End of comment - check for parameters in this section
                    var commentSpan = lineSpan.Slice(commentStart, i - commentStart + 1);
                    foreach (var param in parameters)
                    {
                        if (commentSpan.IndexOf(param.AsSpan(), StringComparison.Ordinal) >= 0)
                        {
                            result.Add(param);
                        }
                    }
                    inComment = false;
                    commentChar = '\0';
                    commentStart = -1;
                }
            }

            // Handle unclosed comment (continues to end of line)
            if (inComment && commentStart >= 0)
            {
                var commentSpan = lineSpan[commentStart..];
                foreach (var param in parameters)
                {
                    if (commentSpan.IndexOf(param.AsSpan(), StringComparison.Ordinal) >= 0)
                    {
                        result.Add(param);
                    }
                }
            }

            return result;
        }
    }
}
