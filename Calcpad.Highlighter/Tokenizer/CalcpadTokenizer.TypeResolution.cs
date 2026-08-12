using System;
using System.Collections.Generic;
using Calcpad.Highlighter.Linter.Constants;
using Calcpad.Highlighter.Linter.Helpers;
using Calcpad.Highlighter.Tokenizer.Models;

namespace Calcpad.Highlighter.Tokenizer
{
    public partial class CalcpadTokenizer
    {
        // ── Definition tracking state ──────────────────────────────

        private readonly HashSet<string> _definedVariables = new(StringComparer.Ordinal);
        private readonly HashSet<string> _definedFunctions = new(StringComparer.Ordinal);
        private readonly HashSet<string> _definedMacros = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _definedUnits = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _localVariables = new(StringComparer.Ordinal);

        // Tracks #read keyword in all modes (not just Lint) so the next variable can be
        // recorded as a definition.
        private bool _expectingReadVariable;

        // True before the first non-comment code token on a line.
        // Suppresses Variable→Units fallback at definition sites (e.g., "a = 42"
        // where "a" is also a built-in unit like Are). Without this, the definition
        // never enters _definedVariables, causing all subsequent uses to cascade as Units.
        private bool _beforeFirstCodeToken;

        // For tracking potential definitions during tokenization
        private string _pendingVariableName = null;
        private int _pendingVariableLine = -1;
        private string _pendingFunctionName = null;
        private int _pendingFunctionLine = -1;
        private int _pendingFunctionParenDepth = 0;  // Track paren depth within function definition

        /// <summary>
        /// Resets all definition tracking state for a new tokenization pass.
        /// </summary>
        private void ResetTypeResolutionState()
        {
            _definedVariables.Clear();
            _definedFunctions.Clear();
            _definedMacros.Clear();
            _definedUnits.Clear();
            _expectingReadVariable = false;
        }

        // ── Type resolution ────────────────────────────────────────

        /// <summary>
        /// Resolve token type based on context. Does not detect errors - that's the linter's job.
        /// </summary>
        private TokenType ResolveType(TokenType type, string text)
        {
            switch (type)
            {
                case TokenType.Function:
                    // Keep as function if it's known or being defined here (f(x) = ...)
                    // Unknown function calls become Variable so the linter can flag them
                    if (!IsKnownFunction(text) && !_state.IsFunctionDefinition)
                        return TokenType.Variable;
                    break;

                case TokenType.Variable:
                    // Check for "to" or "from" keywords in data exchange context
                    if (_state.IsDataExchangeKeyword)
                    {
                        if (text.Equals("to", StringComparison.OrdinalIgnoreCase) ||
                            text.Equals("from", StringComparison.OrdinalIgnoreCase))
                        {
                            _state.ExpectingFilePath = true;
                            return TokenType.DataExchangeKeyword;
                        }
                        // Check for option keywords
                        if (text.Equals("sep", StringComparison.OrdinalIgnoreCase) ||
                            text.Equals("type", StringComparison.OrdinalIgnoreCase))
                        {
                            return TokenType.DataExchangeKeyword;
                        }
                    }
                    // Check if this is a macro parameter (inside #def macro$(...) parentheses)
                    if (_state.IsInFunctionParams && _state.HasMacro)
                    {
                        _macroParameters.Add(text);
                        return TokenType.MacroParameter;
                    }
                    // Check if this is a known macro parameter (in macro body)
                    if (_inMacroDefinition && _macroParameters.Contains(text))
                    {
                        return TokenType.MacroParameter;
                    }
                    // Check if this is a local variable (function param, command scope var, etc.)
                    if (_state.IsInFunctionParams || _state.IsAfterAtOrAmp)
                    {
                        _localVariables.Add(text);
                        _state.IsAfterAtOrAmp = false; // Reset after capturing the variable
                        return TokenType.LocalVariable;
                    }
                    // Check if it's a known local variable (defined earlier in this line)
                    if (_localVariables.Contains(text))
                        return TokenType.LocalVariable;
                    // Check if it's a known defined variable
                    if (IsKnownVariable(text))
                        return TokenType.Variable;
                    // If not a known variable, fall back to unit if it's a valid unit name
                    // This handles cases like "5*ft" where ft should be a unit (not a variable)
                    // Exception: at the first identifier position on a line the identifier is
                    // a variable definition (e.g. "a = 42", or "#const h = 6" and "#UI L = 10m"
                    // where the directive keyword precedes it). Falling back to Units there
                    // would keep it out of _definedVariables, cascading every later use to
                    // Units as well. Positions after the first still resolve units normally,
                    // so the "ft" in "x = 5*ft" stays a unit.
                    if (IsKnownUnit(text) &&
                        !_beforeFirstCodeToken &&
                        !_state.IsDataExchangeKeyword)
                        return TokenType.Units;
                    // Otherwise keep as variable - linter will detect if undefined
                    break;

                case TokenType.Keyword:
                    var trimmed = text.TrimEnd();
                    if (CalcpadBuiltIns.Keywords.Contains(trimmed))
                    {
                        _state.Keyword = trimmed;
                        // Check for data exchange keywords (#read, #write, #append)
                        // Don't set ExpectingFilePath here - file path comes after "from" or "to"
                        _state.IsDataExchangeKeyword = IsDataExchangeKeyword(trimmed);
                        // Note: #for loop variables are global in Calcpad, not local
                        // They persist after the loop ends, so don't mark as LocalVariable
                    }
                    else if (CalcpadBuiltIns.ControlBlockKeywords.Contains(trimmed) ||
                             CalcpadBuiltIns.EndKeywords.Contains(trimmed))
                    {
                        // Set Keyword for control block keywords (#for, #while, etc.) and
                        // end keywords (#end if, #loop, etc.) so that ':' in range expressions
                        // like "#for i = 1 : 5" is treated as Operator, not as a format delimiter.
                        _state.Keyword = trimmed;
                    }
                    // Check for #end def to exit macro definition
                    if (trimmed.Equals("#end def", StringComparison.OrdinalIgnoreCase))
                    {
                        _inMacroDefinition = false;
                        _macroParameters.Clear();
                    }
                    // Keep as keyword even if invalid - linter will detect
                    break;

                case TokenType.Command:
                    // Track that we're entering a command (will become command block when { is seen)
                    break;

                case TokenType.Macro:
                    // Keep as macro - linter will detect if undefined
                    break;

                case TokenType.Units:
                    // If this is classified as Units but is actually a known local variable,
                    // reclassify it. This handles cases like "f(g) = 4*g" where g after the
                    // operator might be initially classified as Units due to PreviousType tracking.
                    if (_localVariables.Contains(text))
                        return TokenType.LocalVariable;
                    // If it's a known defined variable, treat as variable
                    if (IsKnownVariable(text))
                        return TokenType.Variable;
                    // Keep as units - linter will validate if it's a valid unit
                    break;
            }

            return type;
        }

        private TokenType InitType(char c, TokenType current)
        {
            if (c == '$')
                return TokenType.Command;

            if (c == '#')
                return _state.IsLeading ? TokenType.Keyword : TokenType.Bracket;

            if (char.IsAsciiDigit(c))
                return TokenType.Const;

            if (_state.IsMacro && IsMacroIdentChar(c, 0))
                return TokenType.Macro;

            if (CalcpadCharacterHelpers.IsLetterForTokenizer(c))
            {
                // Check if this is a valid identifier start character
                // Note: IsUnitStart chars (μ, °, etc.) are NOT used here because unit classification
                // is already handled by _state.PreviousType == TokenType.Const (for "5 μm") and
                // _state.IsUnits (for "|μm|"). Direct number-adjacent units (e.g., "5μm") go through
                // ParseUnits, not InitType. Using IsUnitStart here would misclassify standalone
                // Greek letters like "μ = 5" as units instead of variables.
                if (CalcpadCharacterHelpers.IsIdentifierStartChar(c))
                {
                    if (_state.IsUnits || _state.PreviousType == TokenType.Const)
                        return TokenType.Units;
                    return TokenType.Variable;
                }
                // Character is valid inside identifier but cannot start one (e.g., _, digits, subscripts)
                // Tokenize as Variable so linter can flag it as undefined
                return TokenType.Variable;
            }

            if (c == '?')
                return TokenType.Input;

            return current;
        }

        /// <summary>
        /// Track variable and function definitions during tokenization.
        /// This handles complex cases like 'text'var = 5'more text' that regex cannot parse.
        /// </summary>
        private void TrackDefinitions(TokenType type, string text)
        {
            // Lint mode: pre-process all tokens except "=" (which needs isFirstDef info)
            if (_mode == TokenizerMode.Lint && !(type == TokenType.Operator && text == "="))
                TrackDefinitionsLint(type, text);

            // Macro mode: capture macro name and metadata
            if (_mode == TokenizerMode.Macro)
                TrackDefinitionsMacro(type, text);

            switch (type)
            {
                case TokenType.Variable:
                    // This could be a variable definition - remember it
                    // If we later see "(" it becomes a function, if we see "=" it's confirmed
                    _pendingVariableName = text;
                    _pendingVariableLine = _state.Line;
                    _pendingFunctionName = null;
                    _pendingFunctionParenDepth = 0;

                    // Track #read variables as defined so subsequent uses resolve correctly.
                    if (_expectingReadVariable)
                    {
                        if (!_definedVariables.Contains(text))
                        {
                            _definedVariables.Add(text);
                            _result.AddVariableDefinition(text, _state.Line);
                        }
                        _expectingReadVariable = false;
                    }
                    break;

                case TokenType.Function:
                    // At definition sites (f(x) = ...), the name stays as Function via ResolveType
                    // so track directly — this avoids it being misclassified as a variable assignment
                    if (_state.IsFunctionDefinition)
                    {
                        _pendingFunctionName = text;
                        _pendingFunctionLine = _state.Line;
                        _pendingVariableName = null;
                        _pendingVariableLine = -1;
                    }
                    // At call sites for unknown functions, promote from pending variable
                    else if (_pendingVariableName != null)
                    {
                        _pendingFunctionName = _pendingVariableName;
                        _pendingFunctionLine = _pendingVariableLine;
                        _pendingVariableName = null;
                        _pendingVariableLine = -1;
                    }
                    break;

                case TokenType.Bracket:
                    if (text == "(")
                    {
                        // If we have a pending variable, it's now a function call/definition
                        if (_pendingVariableName != null)
                        {
                            _pendingFunctionName = _pendingVariableName;
                            _pendingFunctionLine = _pendingVariableLine;
                            _pendingFunctionParenDepth = 1;
                            _pendingVariableName = null;
                            _pendingVariableLine = -1;
                        }
                        else if (_pendingFunctionName != null)
                        {
                            // Nested parens inside function definition params
                            _pendingFunctionParenDepth++;
                        }
                    }
                    else if (text == ")")
                    {
                        // Closing paren - track depth
                        if (_pendingFunctionName != null && _pendingFunctionParenDepth > 0)
                        {
                            _pendingFunctionParenDepth--;
                        }
                    }
                    else
                    {
                        // Other brackets clear pending state
                        ClearPendingDefinitions();
                    }
                    break;

                case TokenType.Keyword:
                    // Track #read keyword for element access detection (all modes)
                    if (text.TrimEnd().Equals("#read", StringComparison.OrdinalIgnoreCase))
                        _expectingReadVariable = true;
                    // Keywords clear pending state (same as previous default behavior)
                    ClearPendingDefinitions();
                    break;

                case TokenType.Operator:
                    if (text == "=")
                    {
                        // Inside function definition parens, = is a default value separator, not assignment
                        if (_pendingFunctionName != null && _pendingFunctionParenDepth > 0)
                            break;

                        // Assignment operator - confirm definition or track reassignment
                        bool isFirstDef = false;

                        if (_pendingFunctionName != null && _pendingFunctionParenDepth == 0)
                        {
                            if (_definedFunctions.Contains(_pendingFunctionName))
                            {
                                // Reassignment of already-defined function
                                _result.VariableReassignments.Add((_pendingFunctionName, _pendingFunctionLine, 0));
                            }
                            else
                            {
                                // First definition: f(x) = ...
                                _definedFunctions.Add(_pendingFunctionName);
                                _result.AddFunctionDefinition(_pendingFunctionName, _pendingFunctionLine);
                                isFirstDef = true;
                            }
                        }
                        else if (_pendingVariableName != null)
                        {
                            if (_definedVariables.Contains(_pendingVariableName))
                            {
                                // Reassignment of already-defined variable
                                _result.VariableReassignments.Add((_pendingVariableName, _pendingVariableLine, 0));
                            }
                            else
                            {
                                // First definition: x = ...
                                _definedVariables.Add(_pendingVariableName);
                                _result.AddVariableDefinition(_pendingVariableName, _pendingVariableLine);
                                isFirstDef = true;
                            }
                        }

                        // Lint mode: start expression capture before clearing pending state
                        if (_mode == TokenizerMode.Lint)
                            OnEqualsSeenLint(isFirstDef);

                        ClearPendingDefinitions();
                    }
                    else if (text == "←")
                    {
                        // Outer scope assignment - always a reassignment, never creates definitions
                        if (_pendingFunctionName != null && _pendingFunctionParenDepth == 0)
                        {
                            _result.OuterScopeAssignments.Add((_pendingFunctionName, _pendingFunctionLine, 0));
                        }
                        else if (_pendingVariableName != null)
                        {
                            _result.OuterScopeAssignments.Add((_pendingVariableName, _pendingVariableLine, 0));
                        }
                        ClearPendingDefinitions();
                    }
                    else if (text != ";" && text != ",")
                    {
                        // Other operators (except separators) - these don't confirm/clear definitions
                        // e.g., in "x*y = 5" we want to clear because it's not a simple assignment
                        // But we need to be careful about function params like f(x; y)
                        if (_pendingFunctionName == null || _pendingFunctionParenDepth == 0)
                        {
                            // Not in a function definition params, so other operators clear the pending
                            _pendingVariableName = null;
                            _pendingVariableLine = -1;
                            if (_pendingFunctionParenDepth == 0)
                            {
                                // Function was already closed, this operator means it's not a definition
                                _pendingFunctionName = null;
                                _pendingFunctionLine = -1;
                            }
                        }
                    }
                    break;

                case TokenType.LocalVariable:
                    // Local variables in function params don't affect pending state
                    break;

                case TokenType.Const:
                case TokenType.Units:
                    // Constants and units don't affect pending definitions
                    break;

                case TokenType.Comment:
                case TokenType.Tag:
                case TokenType.HtmlComment:
                    // Comments don't affect pending definitions - allows for 'text'var = 5
                    break;

                default:
                    // Other token types clear pending state
                    ClearPendingDefinitions();
                    break;
            }
        }

        private void ClearPendingDefinitions()
        {
            _pendingVariableName = null;
            _pendingVariableLine = -1;
            _pendingFunctionName = null;
            _pendingFunctionLine = -1;
            _pendingFunctionParenDepth = 0;
        }

        private bool IsKnownFunction(string name)
        {
            return CalcpadBuiltIns.Functions.Contains(name) || _definedFunctions.Contains(name);
        }

        private bool IsKnownVariable(string name)
        {
            return _localVariables.Contains(name) ||
                   _definedVariables.Contains(name) ||
                   CalcpadBuiltIns.CommonConstants.Contains(name);
        }

        private bool IsKnownUnit(string name)
        {
            return _definedUnits.Contains(name) || IsBuiltInUnit(name);
        }

        private static bool IsBuiltInUnit(string name)
        {
            // Check against the actual built-in units from CalcpadBuiltIns
            return CalcpadBuiltIns.Units.Contains(name);
        }

        private bool IsKnownMacro(string name)
        {
            return _definedMacros.Contains(name);
        }

        private static bool IsDataExchangeKeyword(string keyword)
        {
            return keyword.Equals("#read", StringComparison.OrdinalIgnoreCase) ||
                   keyword.Equals("#write", StringComparison.OrdinalIgnoreCase) ||
                   keyword.Equals("#append", StringComparison.OrdinalIgnoreCase);
        }
    }
}
