using System;
using Calcpad.Highlighter.Linter.Constants;
using Calcpad.Highlighter.Linter.Helpers;
using Calcpad.Highlighter.Tokenizer.Models;

namespace Calcpad.Highlighter.Tokenizer
{
    public partial class CalcpadTokenizer
    {
        private void ParseSpace(char c, int position)
        {
            if (_state.IsLeading)
            {
                _builder.Append(c);
            }
            else if (IsContinuedConditionBuilder(_builder))
            {
                _builder.Append(' ');
            }
            else
            {
                // Flush any pending content first
                if (_builder.Length > 0)
                {
                    var len = _builder.Length;
                    if (len == 4 &&
                        _builder[1] == 'd' &&
                        _builder[2] == 'e' &&
                        _builder[3] == 'f')
                    {
                        _state.IsMacro = true;
                    }

                    var isInclude = false;
                    var isFormat = false;
                    var isSettings = false;
                    var isPathRoot = false;
                    if (_state.CurrentType == TokenType.Keyword)
                    {
                        isInclude = len == 8 &&
                            _builder[1] == 'i' &&
                            _builder[2] == 'n' &&
                            _builder[3] == 'c';
                        isFormat = len == 7 &&
                            _builder[1] == 'f' &&
                            _builder[2] == 'o' &&
                            _builder[3] == 'r';
                        isSettings = len == 9 &&
                            _builder[1] == 's' &&
                            _builder[2] == 'e' &&
                            _builder[3] == 't';
                        // Case-insensitive, unlike the checks above: #ProjectPath/#LibraryPath are
                        // suggested to authors in PascalCase, so a case-sensitive check here would
                        // silently miss the exact spelling this feature itself recommends.
                        isPathRoot = len == 12 &&
                            (_builder.ToString().Equals("#projectpath", StringComparison.OrdinalIgnoreCase) ||
                             _builder.ToString().Equals("#librarypath", StringComparison.OrdinalIgnoreCase));
                    }

                    Append(_state.CurrentType);

                    // Check if we should start parsing a file path
                    if (_state.ExpectingFilePath && _state.IsDataExchangeKeyword)
                    {
                        _state.CurrentType = TokenType.FilePath;
                        // Set start column to AFTER this space for the file path token
                        _state.TokenStartColumn = position + 1;
                    }
                    else if (isInclude)
                    {
                        _state.CurrentType = TokenType.Include;
                        // Set start column to AFTER this space for the include path token
                        _state.TokenStartColumn = position + 1;
                    }
                    else if (isFormat)
                    {
                        _state.CurrentType = TokenType.Format;
                        // Set start column to AFTER this space for the format specifier token
                        _state.TokenStartColumn = position + 1;
                    }
                    else if (isSettings)
                    {
                        _state.CurrentType = TokenType.SettingsJson;
                        // Set start column to AFTER this space for the JSON payload token
                        _state.TokenStartColumn = position + 1;
                    }
                    else if (isPathRoot)
                    {
                        _state.CurrentType = TokenType.FilePath;
                        // Set start column to AFTER this space for the path value token
                        _state.TokenStartColumn = position + 1;
                    }
                    else
                    {
                        _state.CurrentType = TokenType.None;
                    }
                }

                // Emit whitespace as None type to preserve column positions
                // But preserve FilePath, Include, Format, and SettingsJson modes
                var inFilePath = _state.CurrentType == TokenType.FilePath;
                var inIncludeMode = _state.CurrentType == TokenType.Include;
                var inFormatMode = _state.CurrentType == TokenType.Format;
                var inSettingsMode = _state.CurrentType == TokenType.SettingsJson;
                if (inFilePath || inIncludeMode || inSettingsMode)
                {
                    // For filepath/include, spaces within are part of the token
                    // But if this is the FIRST char (builder is empty), skip leading space
                    if (_builder.Length > 0)
                    {
                        _builder.Append(c);
                    }
                    // If empty, this is leading space after keyword - skip it, column already set
                }
                else if (inFormatMode && _builder.Length == 0)
                {
                    // Leading space after #format keyword or between format content and comment
                    // Skip it and advance start column so the format token starts at the right position
                    _state.TokenStartColumn = position + 1;
                }
                else
                {
                    _state.TokenStartColumn = position;
                    _builder.Append(c);
                    Append(TokenType.None);
                    _state.CurrentType = TokenType.None;
                }
            }
        }

        private void ParseBrackets(char c, int position = -1)
        {
            var t = _state.CurrentTypeOrPrevious;
            if (c == '(')
            {
                _state.BracketCount++;
                if (t == TokenType.Variable)
                {
                    _state.CurrentType = TokenType.Function;
                    // Check if this is a function DEFINITION by looking ahead for "(...) ="
                    // Only mark params as LocalVariable for definitions, not calls
                    _state.IsFunctionDefinition = IsFunctionDefinitionLine(_state.Text.Span, _state.TokenStartColumn);
                    _state.IsInFunctionParams = _state.IsFunctionDefinition;
                }
                else if (t == TokenType.Function)
                {
                    // This is a call to a known function - NOT a definition
                    // Don't mark params as local variables
                    _state.IsFunctionDefinition = false;
                    _state.IsInFunctionParams = false;
                }
                else if (t == TokenType.Macro && _state.MacroArgs == 0)
                {
                    _state.CurrentType = TokenType.Macro;
                    if (_state.HasMacro)
                    {
                        // Macro definition: #def macro$(param1; param2)
                        // Parameters should be marked as LocalVariable, like function definitions
                        _state.IsInFunctionParams = true;
                    }
                    else
                    {
                        // Macro call: macro$(expr1; expr2)
                        // Use ParseMacroArgs for expansion
                        _state.MacroArgs = _state.BracketCount;

                        // Use captured macro name (builder was already cleared by ParseMacro)
                        var macroName = _lastMacroCallName;
                        _lastMacroCallName = null;

                        if (macroName != null)
                        {
                            _state.CurrentMacroCall = macroName;
                            _state.CurrentMacroArgIndex = 0;

                            // Resolve argument tokens by substituting into the macro body
                            // _state.TokenStartColumn is at the '(' position
                            ResolveMacroCallArgTokens(macroName, _state.TokenStartColumn);
                        }
                    }
                }
            }
            else if (c == ')')
            {
                if (_state.BracketCount == _state.MacroArgs)
                    _state.MacroArgs = 0;

                _state.BracketCount--;
            }

            // Append BEFORE clearing IsInFunctionParams so the last param gets marked as LocalVariable
            Append(_state.CurrentType);

            // End of function params if we're at the matching close paren (after Append so last param is processed)
            if (c == ')' && _state.IsInFunctionParams && _state.BracketCount == 0)
                _state.IsInFunctionParams = false;

            if (c == '{')
            {
                if (_state.CurrentType == TokenType.Command)
                {
                    _state.CommandCount++;
                    _state.IsInCommandBlock = true;
                    if (position >= 0)
                        CollectCommandScopeVariables(_state.Text.Span, position);
                }

                _state.CurrentType = TokenType.Bracket;
            }
            else if (c == '}')
            {
                _state.CommandCount--;
                if (_state.CommandCount <= 0)
                {
                    _state.IsInCommandBlock = false;
                    _state.CommandCount = 0;
                }
                _state.CurrentType = TokenType.Bracket;
            }
            else
            {
                if (c == '[')
                {
                    _state.MatrixCount++;
                }
                else if (c == ']')
                    _state.MatrixCount--;

                _state.CurrentType = TokenType.Bracket;
            }

            _builder.Append(c);
            Append(_state.CurrentType);
        }

        /// <summary>
        /// Registers the scope variables a command block declares after its body, e.g. the s in
        /// $Integral{cos(s^2) @ s = 0 : t}. The body is tokenized before the @ declaration is
        /// reached, so without this a name that is also a unit (s, t, min, ...) resolves to Units.
        /// </summary>
        private void CollectCommandScopeVariables(ReadOnlySpan<char> text, int bracePosition)
        {
            var depth = 0;
            for (int i = bracePosition; i < text.Length; i++)
            {
                var c = text[i];
                if (c == '\'' || c == '"')
                    return;

                if (c == '{')
                    ++depth;
                else if (c == '}')
                {
                    if (--depth == 0)
                        return;
                }
                else if (c == '@' || c == '&')
                {
                    var j = i + 1;
                    while (j < text.Length && char.IsWhiteSpace(text[j]))
                        ++j;

                    var start = j;
                    while (j < text.Length && CalcpadCharacterHelpers.IsIdentifierChar(text[j]))
                        ++j;

                    if (j > start)
                        _localVariables.Add(text[start..j].ToString());

                    i = j - 1;
                }
            }
        }

        private void ParseOperator(char c)
        {
            Append(_state.CurrentTypeOrPrevious);
            _state.TokenStartColumn = _state.TokenStartColumn + _builder.Length;
            _builder.Append(c);
            var isPercent = c == '%' && _state.IsUnits;
            Append(isPercent ? TokenType.Units : TokenType.Operator);
            _state.CurrentType = TokenType.Operator;

            if (c == '=')
            {
                _state.IsFunction = true;
                // Inside parens (BracketCount > 0), = is a default value separator: f(x = 5) = x^2
                // Stop marking tokens as params (so default expr tokens aren't params),
                // but keep IsFunctionDefinition true so the definition is recognized.
                // IsInFunctionParams is restored on ';' for the next parameter.
                if (_state.BracketCount == 0)
                {
                    _state.IsFunctionDefinition = false;
                }
                _state.IsInFunctionParams = false;

                // For inline macro definitions (#def macro$(params) = body), enter macro body
                if (_state.HasMacro && _state.BracketCount == 0)
                {
                    _inMacroDefinition = true;
                }

                // For data exchange keywords, = precedes the file path
                // e.g., #read x = file.csv
                // Keep ExpectingFilePath true - will be handled when we see the file path
            }
        }

        private void ParseDelimiter(char c)
        {
            Append(_state.CurrentTypeOrPrevious);
            _builder.Append(c);

            // @ or & in a command block introduces a scoped variable so the next identifier is
            // a local variable, e.g. $Sum{x^2 @ x = 1 : 10}. This must be flagged even inside a
            // nested command (CommandCount > 0), which the branch below would otherwise short-circuit.
            if ((c == '@' || c == '&') && _state.IsInCommandBlock)
                _state.IsAfterAtOrAmp = true;

            if (_state.CommandCount > 0 || c == ';')
            {
                Append(TokenType.Operator);
                // Restore IsInFunctionParams after ';' inside function definition parens
                // so the next parameter name is correctly typed as LocalVariable
                if (c == ';' && _state.IsFunctionDefinition && _state.BracketCount > 0)
                    _state.IsInFunctionParams = true;
            }
            else if (c == '|' && (_state.IsUnits || _state.MatrixCount > 0))
            {
                Append(TokenType.Bracket);
            }
            else if (c == ':')
            {
                if (string.IsNullOrEmpty(_state.Keyword))
                {
                    // Emit ':' as Operator, then enter Format mode for the specifier content
                    Append(TokenType.Operator);
                    _state.CurrentType = TokenType.Format;
                    return;
                }
                Append(TokenType.Operator);
            }
            else
            {
                Append(TokenType.Operator);
            }

            _state.CurrentType = TokenType.Bracket;
        }

        private void ParseImaginary(char c, int i, int len, ReadOnlySpan<char> text)
        {
            var j = i + 1;
            if (j < len && text[j] == 'n')
            {
                Append(TokenType.Const);
                _builder.Append(c);
                _state.CurrentType = TokenType.Units;
            }
            else
            {
                _builder.Append('i');
                Append(TokenType.Const);
            }
        }

        private void ParseUnits(char c)
        {
            Append(TokenType.Const);
            _state.TokenStartColumn += _builder.Length;
            _builder.Append(c);
            if (char.IsLetter(c) || CalcpadCharacterHelpers.IsUnitStart(c))
                _state.CurrentType = TokenType.Units;
        }

        private void ParseLineBreak(int underscorePosition)
        {
            // Emit current token if any
            if (_builder.Length > 0)
            {
                var lastChar = _builder[^1];
                // Remove trailing space before line continuation
                if (lastChar == ' ')
                {
                    _builder.Remove(_builder.Length - 1, 1);
                }

                if (_builder.Length > 0)
                {
                    var appendType = _state.CurrentTypeOrPrevious;
                    // Detect HTML comment markers before emitting, so a <!-- opener
                    // on a line ending in " _" is typed as HtmlComment and _inHtmlComment
                    // carries the open block into the continuation line.
                    if (appendType == TokenType.Comment || _inHtmlComment)
                    {
                        _state.CurrentType = appendType;
                        CheckHtmlComment();
                        appendType = _state.CurrentType;
                    }
                    Append(appendType);
                }
            }

            // Emit line continuation token spanning the space and the underscore. The space may
            // already have been emitted as a None token by ParseSpace, so anchor the token on the
            // underscore's position rather than on the running cursor.
            _state.TokenStartColumn = underscorePosition - 1;
            _builder.Append(" _");
            Append(TokenType.LineContinuation);

            // Preserve comment state across the continuation so the next line
            // knows it's still inside a quoted comment (e.g., 'text<br> _\n continued')
            _continueTextComment = _state.TextComment;
            _continueTagComment = _state.TagComment;
            _explicitLineBreak = true;
        }

        private bool IsDoubleOp(char c, char op)
        {
            return c == op && _builder.Length > 0 && _builder[^1] == op;
        }

        private void AppendDoubleOperatorShortcut(char op)
        {
            if (_builder.Length > 1)
            {
                _builder.Remove(_builder.Length - 1, 1);
                Append(_state.CurrentType);
                _builder.Append(op);
            }
            else if (_builder.Length == 1)
            {
                _builder[0] = op;
            }
            else
            {
                _builder.Append(op);
            }

            _state.CurrentType = TokenType.Operator;
            Append(_state.CurrentType);
        }
    }
}
