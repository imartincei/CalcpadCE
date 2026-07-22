using System;
using System.Collections.Generic;
using System.Linq;
using Calcpad.Highlighter.ContentResolution;
using Calcpad.Highlighter.Linter.Constants;
using Calcpad.Highlighter.Linter.Helpers;
using Calcpad.Highlighter.Linter.Models;
using Calcpad.Highlighter.Tokenizer.Models;

namespace Calcpad.Highlighter.Linter.Validators.Stage3
{
    public class UsageValidator
    {
        public void Validate(Stage3Context stage3, LinterResult result, TokenizedLineProvider tokenProvider)
        {
            ValidateCommands(stage3, result, tokenProvider);
            ValidateUndefinedIdentifiers(stage3, result, tokenProvider);
            ValidateUndefinedUnits(stage3, result, tokenProvider);
            ValidateFunctionCalls(stage3, result, tokenProvider);
            ValidateMacroCalls(stage3, result, tokenProvider);
            ValidateUnusedDefinitions(stage3, result, tokenProvider);
        }

        /// <summary>
        /// Validates the @ separator pattern in commands like $Repeat{expr @ variable = start : end}.
        /// Two passes folded into one (formerly ValidateCommandSyntax + ValidateCommandVariables):
        ///   - CPD-3406: command syntax — exactly one variable token between @ and =, no numbers
        ///   - CPD-3408: expression uses the declared loop variable
        /// $Plot is exempt from both checks when the expression contains | or & (multi-function /
        /// parametric forms). $Map is exempt from the loop-variable check (allows multiple counters).
        /// </summary>
        private void ValidateCommands(Stage3Context stage3, LinterResult result, TokenizedLineProvider tokenProvider)
        {
            for (int i = 0; i < stage3.Lines.Count; i++)
            {
                if (!tokenProvider.IsCpdMode(i)) continue;

                var line = stage3.Lines[i];
                if (LineParser.ShouldSkipLine(line)) continue;

                var trimmed = line.Trim();
                if (LineParser.IsDirectiveLine(trimmed)) continue;

                var atIndex = line.IndexOf('@');
                if (atIndex < 0) continue;

                // Locate which $Command{ precedes the @
                var hasCommand = false;
                var commandName = string.Empty;
                var cmdStartIndex = -1;
                foreach (var (cmd, cmdWithBrace) in CalcpadBuiltIns.CommandsWithBrace)
                {
                    var cmdIndex = line.IndexOf(cmdWithBrace, StringComparison.OrdinalIgnoreCase);
                    if (cmdIndex >= 0 && cmdIndex < atIndex)
                    {
                        hasCommand = true;
                        commandName = cmd;
                        cmdStartIndex = cmdIndex + cmd.Length + 1; // position just after "{"
                        break;
                    }
                }
                if (!hasCommand) continue;

                // Tokens consumed by both phases — fetch once.
                var tokens = tokenProvider.GetTokensForLine(i);

                // $Plot with | or & is fully exempt (parametric / multi-function form).
                bool skipSyntaxPhase = false;
                if (commandName.Equals("$Plot", StringComparison.OrdinalIgnoreCase))
                {
                    var beforeAt = line.AsSpan(0, atIndex);
                    if (beforeAt.Contains('|') || beforeAt.Contains('&'))
                        skipSyntaxPhase = true;
                }

                // Find the non-comparison = sign after @. Used by both phases when present.
                var afterAt = line.AsSpan(atIndex + 1);
                var equalsIndex = -1;
                for (int j = 0; j < afterAt.Length; j++)
                {
                    if (afterAt[j] == '=')
                    {
                        var isComparison = (j > 0 && (afterAt[j - 1] == '<' || afterAt[j - 1] == '>' || afterAt[j - 1] == '!' || afterAt[j - 1] == '=')) ||
                                         (j + 1 < afterAt.Length && afterAt[j + 1] == '=');
                        if (!isComparison)
                        {
                            equalsIndex = j;
                            break;
                        }
                    }
                }

                var atAbsolutePos = atIndex;
                var equalsAbsolutePos = equalsIndex >= 0 ? atIndex + 1 + equalsIndex : -1;

                // --- Phase 1: command syntax (CPD-3406) ---
                if (!skipSyntaxPhase)
                {
                    if (equalsIndex < 0)
                    {
                        result.AddError(i, atIndex, atIndex + 1, "CPD-3406",
                            "Invalid command syntax: expected 'variable = start : end' after '@'");
                        continue;
                    }

                    var variableTokens = new List<Token>();
                    var numberTokens = new List<Token>();
                    foreach (var token in tokens)
                    {
                        if (token.Column > atAbsolutePos && token.Column < equalsAbsolutePos)
                        {
                            if (token.Type == TokenType.LocalVariable || token.Type == TokenType.Variable)
                                variableTokens.Add(token);
                            else if (token.Type == TokenType.Const)
                                numberTokens.Add(token);
                        }
                    }

                    if (variableTokens.Count == 0)
                    {
                        result.AddError(i, atAbsolutePos + 1, equalsAbsolutePos, "CPD-3406",
                            "Invalid command syntax: expected variable name after '@'");
                    }
                    else if (variableTokens.Count > 1)
                    {
                        result.AddError(i, atAbsolutePos + 1, equalsAbsolutePos, "CPD-3406",
                            "Invalid command syntax: expected single variable name after '@'");
                    }
                    else if (numberTokens.Count > 0)
                    {
                        var firstNumber = numberTokens[0];
                        result.AddError(i, firstNumber.Column, firstNumber.Column + firstNumber.Length, "CPD-3406",
                            "Invalid command syntax: unexpected number '" + firstNumber.Text + "' between '@' and variable name");
                    }
                }

                // --- Phase 2: command variable matching (CPD-3408) ---
                // $Plot and $Map are exempt (different counter semantics). $Repeat is exempt
                // too: its counter only controls the iteration count, so the body (which may be
                // pure side-effect assignments) is not required to reference the loop variable.
                if (commandName.Equals("$Plot", StringComparison.OrdinalIgnoreCase) ||
                    commandName.Equals("$Map", StringComparison.OrdinalIgnoreCase) ||
                    commandName.Equals("$Repeat", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (equalsIndex < 0) continue; // already reported above

                string declaredVar = null;
                foreach (var token in tokens)
                {
                    if (token.Column > atAbsolutePos && token.Column < equalsAbsolutePos &&
                        (token.Type == TokenType.LocalVariable || token.Type == TokenType.Variable))
                    {
                        declaredVar = token.Text;
                        break;
                    }
                }
                if (string.IsNullOrEmpty(declaredVar)) continue;

                var usedVariables = new HashSet<string>(StringComparer.Ordinal);
                foreach (var token in tokens)
                {
                    if (token.Column >= cmdStartIndex && token.Column < atAbsolutePos &&
                        (token.Type == TokenType.Variable || token.Type == TokenType.LocalVariable))
                    {
                        usedVariables.Add(token.Text);
                    }
                }

                // Loop variable may appear directly or as element-access suffix (e.g., v1_ind.i)
                // when the tokenizer doesn't split because v1_ind isn't known as a vector.
                var usesLoopVar = usedVariables.Contains(declaredVar);
                if (!usesLoopVar)
                {
                    var dotSuffix = "." + declaredVar;
                    foreach (var uv in usedVariables)
                    {
                        if (uv.EndsWith(dotSuffix, StringComparison.Ordinal))
                        {
                            usesLoopVar = true;
                            break;
                        }
                    }
                }

                if (usedVariables.Count > 0 && !usesLoopVar)
                {
                    var usedVarsList = string.Join(", ", usedVariables.Select(v => "'" + v + "'"));
                    result.AddError(i, cmdStartIndex, atAbsolutePos, "CPD-3408",
                        "Expression uses " + usedVarsList + " but loop variable is '" + declaredVar + "'");
                }
            }
        }

        private void ValidateUndefinedIdentifiers(Stage3Context stage3, LinterResult result, TokenizedLineProvider tokenProvider)
        {
            // #noc renders equations without evaluating them, so referencing an undefined
            // identifier is legitimate. Track the output mode as we go and skip the check
            // while it stays in the #noc region (until the next #val/#equ).
            var directives = new DirectiveState();

            for (int i = 0; i < stage3.Lines.Count; i++)
            {
                if (!tokenProvider.IsCpdMode(i)) continue;

                var line = stage3.Lines[i];

                if (LineParser.ShouldSkipLine(line))
                    continue;

                var trimmed = line.Trim();

                if (LineParser.IsDirectiveLine(trimmed))
                {
                    directives.Apply(trimmed);
                    continue;
                }

                if (directives.Output == OutputMode.NoCalculation)
                    continue;

                // Skip undefined variable checks for function definitions with command blocks
                // These have their own local scope where any variable can be assigned and used
                // Errors will be caught when the function is actually called
                if (IsCommandBlockFunctionDefinition(trimmed))
                    continue;

                var tokens = tokenProvider.GetTokensForLine(i);

                // Check if this line is a function definition and extract its parameters
                var functionParams = ParsingHelpers.GetFunctionParamsFromLine(line);

                // Check for command-scope variables (e.g., x in $Root{f(x) @ x = 0 : 5})
                var commandScopeVars = GetCommandScopeVariables(line, tokens);

                foreach (var token in tokens)
                {
                    // Skip LocalVariable tokens - these are function params, loop vars, command scope vars
                    // The tokenizer has already identified them as locally scoped
                    if (token.Type == TokenType.LocalVariable)
                        continue;

                    // Skip FilePath tokens - these are file paths in data exchange keywords
                    if (token.Type == TokenType.FilePath)
                        continue;

                    // Only check Variable tokens (Function tokens are checked in ValidateFunctionCalls)
                    if (token.Type != TokenType.Variable)
                        continue;

                    var identifier = token.Text;

                    // Handle element access syntax: v.(index) - the identifier ends with .
                    var isElementAccess = identifier.EndsWith(".");
                    var baseName = isElementAccess ? identifier.Substring(0, identifier.Length - 1) : identifier;

                    // Check if it's on the left side of an assignment (definition, not usage)
                    if (IsBeingDefined(line, token.Column, baseName))
                        continue;

                    // Check if it's a function parameter on this line (fallback for edge cases)
                    if (functionParams.Contains(baseName))
                        continue;

                    // Check if it's a command-scope variable (fallback for edge cases)
                    if (commandScopeVars.Contains(baseName))
                        continue;

                    // Check if defined
                    var isDefined = stage3.DefinedVariables.Contains(baseName) ||
                                   stage3.DefinedFunctions.ContainsKey(baseName) ||
                                   CalcpadBuiltIns.Functions.Contains(baseName) ||
                                   CalcpadBuiltIns.CommonConstants.Contains(baseName) ||
                                   CalcpadBuiltIns.Units.Contains(baseName) ||
                                   stage3.CustomUnits.Contains(baseName);

                    if (!isDefined)
                    {
                        var dotIdx = baseName.IndexOf('.');
                        if (dotIdx > 0)
                        {
                            var beforeDot = baseName.Substring(0, dotIdx);

                            // Check if this looks like param.x where param is a function parameter.
                            // Function params don't support .i syntax - must use .(i).
                            if (functionParams.Contains(beforeDot) || commandScopeVars.Contains(beforeDot))
                            {
                                var afterDot = baseName.Substring(dotIdx + 1);
                                result.AddError(i, token.Column, token.Column + token.Length, "CPD-3301",
                                    "'" + baseName + "'. Function parameters require .()" +
                                    " syntax for element access: " + beforeDot + ".(" + afterDot + ")");
                                continue;
                            }

                            // If the base variable before the dot is defined and its type
                            // supports element access (Unknown, Vector, Matrix, Various),
                            // suppress the false positive. The tokenizer didn't split the dot
                            // because it didn't know the variable was a vector/matrix during
                            // tokenization, but element access is still plausible.
                            if (stage3.DefinedVariables.Contains(beforeDot) && stage3.TypeTracker != null)
                            {
                                var varInfo = stage3.TypeTracker.GetVariableInfo(beforeDot);
                                if (varInfo == null || varInfo.SupportsElementAccess)
                                    continue;
                            }
                        }
                        // Line continuation mapping is handled automatically by LinterResult
                        result.AddError(i, token.Column, token.Column + token.Length, "CPD-3301",
                            "'" + baseName + "'");
                        continue;
                    }

                    // If using element access, validate that the variable supports it
                    if (isElementAccess && stage3.TypeTracker != null)
                    {
                        var varInfo = stage3.TypeTracker.GetVariableInfo(baseName);
                        if (varInfo != null && !varInfo.SupportsElementAccess)
                        {
                            result.AddWarning(i, token.Column, token.Column + token.Length, "CPD-3306",
                                "'" + baseName + "' is a " + varInfo.Type.ToString().ToLower() + ", element access .() is only valid for vectors and matrices");
                        }
                    }
                }
            }
        }

        private void ValidateUndefinedUnits(Stage3Context stage3, LinterResult result, TokenizedLineProvider tokenProvider)
        {
            for (int i = 0; i < stage3.Lines.Count; i++)
            {
                if (!tokenProvider.IsCpdMode(i)) continue;

                var line = stage3.Lines[i];

                if (LineParser.ShouldSkipLine(line))
                    continue;

                var trimmed = line.Trim();

                if (LineParser.IsDirectiveLine(trimmed))
                    continue;

                var tokens = tokenProvider.GetTokensForLine(i);

                foreach (var token in tokens)
                {
                    // Only check Units tokens
                    if (token.Type != TokenType.Units)
                        continue;

                    var unitName = token.Text;

                    // Check if it's a valid built-in unit or custom unit
                    var isValidUnit = CalcpadBuiltIns.Units.Contains(unitName) ||
                                     stage3.CustomUnits.Contains(unitName);

                    if (!isValidUnit)
                    {
                        result.AddError(i, token.Column, token.Column + token.Length, "CPD-3310",
                            "'" + unitName + "'");
                    }
                }
            }
        }

        /// <summary>
        /// Extracts command-scope variables from numerical method commands.
        /// For example: "$Root{f(x) @ x = 0 : 5}" returns {"x"}
        /// Also handles: "$Sum{k^2 @ k = 1 : 5}", "$Plot{sin(x) @ x = 0 : 2*π}"
        /// Also handles command block local variables ($Inline, $Block, $While).
        /// Uses tokens for correct variable name extraction (handles commas, dots, etc.).
        /// </summary>
        private static HashSet<string> GetCommandScopeVariables(string line, List<Token> tokens)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);

            // Check for command blocks - variables assigned inside are local to the block
            foreach (var cmdBlock in CalcpadBuiltIns.CommandBlocks)
            {
                var cmdIndex = line.IndexOf(cmdBlock + "{", StringComparison.OrdinalIgnoreCase);
                if (cmdIndex >= 0)
                {
                    var braceStart = line.IndexOf('{', cmdIndex);
                    if (braceStart >= 0)
                    {
                        var braceEnd = ParsingHelpers.FindClosingBrace(line, braceStart);
                        ExtractBlockLocalVariables(tokens, braceStart, braceEnd, result);
                    }
                    break;
                }
            }

            // Use tokens to find each @ variable: the first Variable/LocalVariable after every
            // '@' is a loop variable. Nested commands have more than one (e.g. the inner 'j' and
            // outer 'i' in $Sum{$Sum{i*j @ j = 1 : 3} @ i = 1 : 3}), so collect them all.
            bool afterAt = false;
            foreach (var token in tokens)
            {
                if (token.Type == TokenType.Operator && token.Text == "@")
                {
                    afterAt = true;
                    continue;
                }

                if (afterAt && (token.Type == TokenType.LocalVariable || token.Type == TokenType.Variable))
                {
                    result.Add(token.Text);
                    afterAt = false;
                }
            }

            return result;
        }


        /// <summary>
        /// Extracts variable names that are assigned inside a command block by scanning
        /// tokens within the block's column range. Finds assignment patterns (Variable = ...)
        /// using token types instead of manual string parsing.
        /// </summary>
        private static void ExtractBlockLocalVariables(List<Token> lineTokens, int braceStart, int braceEnd, HashSet<string> result)
        {
            for (int i = 0; i < lineTokens.Count; i++)
            {
                var token = lineTokens[i];

                // Only look at tokens inside the block braces
                if (token.Column <= braceStart || token.Column >= braceEnd)
                    continue;

                // Look for Variable token followed by = operator (assignment pattern)
                if (token.Type != TokenType.Variable && token.Type != TokenType.LocalVariable)
                    continue;

                // Check if the next non-whitespace token is =
                for (int j = i + 1; j < lineTokens.Count; j++)
                {
                    var next = lineTokens[j];
                    if (next.Column >= braceEnd)
                        break;

                    if (next.Type == TokenType.Operator)
                    {
                        if (next.Text == "=")
                        {
                            // Found assignment — extract variable name
                            var varName = token.Text;
                            // Element access: token ends with '.' (e.g., "helperV.")
                            if (varName.EndsWith("."))
                                varName = varName.Substring(0, varName.Length - 1);
                            if (!string.IsNullOrEmpty(varName))
                                result.Add(varName);
                        }
                        break; // any operator after variable ends the check
                    }
                    if (next.Type == TokenType.Bracket)
                        break; // e.g., function call, not assignment
                }
            }
        }

        /// <summary>
        /// Checks if a line is a function definition that uses $Inline, $Block, or $While command blocks.
        /// These command blocks have their own local scope, so we skip undefined variable checks.
        /// Line continuations are already processed by Stage 1 before the linter runs.
        /// </summary>
        private static bool IsCommandBlockFunctionDefinition(string trimmedLine)
        {
            return CalcpadPatterns.FunctionWithCommandBlock.IsMatch(trimmedLine);
        }

        private void ValidateFunctionCalls(Stage3Context stage3, LinterResult result, TokenizedLineProvider tokenProvider)
        {
            // When a function is redefined, the latest definition wins (matching Calcpad.Core),
            // so calls are checked against the last definition's parameter count.
            var lastDefParamCount = BuildLastDefinitionParamCounts(stage3, tokenProvider);

            for (int i = 0; i < stage3.Lines.Count; i++)
            {
                if (!tokenProvider.IsCpdMode(i)) continue;

                var line = stage3.Lines[i];

                if (LineParser.ShouldSkipLine(line))
                    continue;

                var trimmed = line.Trim();

                if (LineParser.IsDirectiveLine(trimmed))
                    continue;

                // The name on the left of a function definition is a (re)definition, not a call.
                // Redefining an existing function is allowed, so don't check it against a param count.
                var defMatch = CalcpadPatterns.FunctionDefinition.Match(line);
                var defName = defMatch.Success ? defMatch.Groups[1].Value : null;
                var defNameCol = defMatch.Success ? defMatch.Groups[1].Index : -1;

                var tokens = tokenProvider.GetTokensForLine(i);

                foreach (var token in tokens)
                {
                    // Only check Function tokens
                    if (token.Type != TokenType.Function)
                        continue;

                    var funcName = token.Text;

                    if (funcName == defName && token.Column == defNameCol)
                        continue;

                    // Skip built-in functions (they have variable param counts)
                    if (CalcpadBuiltIns.Functions.Contains(funcName))
                        continue;

                    // Check user-defined functions
                    if (stage3.DefinedFunctions.TryGetValue(funcName, out FunctionInfo funcInfo))
                    {
                        var (found, paramsStr) = ParsingHelpers.ExtractParamsString(line, token.Column + token.Length);
                        if (found)
                        {
                            var argList = ParameterParser.ParseParameters(paramsStr);
                            // Filter out empty-only result (zero args case)
                            if (argList.Count == 1 && argList[0].Length == 0)
                                argList.Clear();

                            var expected = lastDefParamCount.TryGetValue(funcName, out var pc)
                                ? pc : funcInfo.ParamCount;
                            int totalActual = argList.Count;
                            if (totalActual != expected)
                            {
                                var endCol = ParsingHelpers.FindClosingParen(line, token.Column + token.Length);
                                if (endCol <= token.Column + token.Length) endCol = token.Column + token.Length;
                                result.AddError(i, token.Column, endCol, "CPD-3302",
                                    "'" + funcName + "' expects " + expected + " parameter(s) but got " + totalActual);
                            }
                        }
                    }
                    else
                    {
                        // Function not defined - the content resolver handles included files
                        result.AddError(i, token.Column, token.Column + token.Length, "CPD-3305",
                            "'" + funcName + "' is not defined");
                    }
                }
            }
        }

        /// <summary>
        /// Maps each user-defined function name to the parameter count of its last textual
        /// definition. The tokenizer only registers the first definition's arity, but Calcpad
        /// resolves redefinitions to the latest one, so call checks must use the last count.
        /// </summary>
        private static Dictionary<string, int> BuildLastDefinitionParamCounts(Stage3Context stage3, TokenizedLineProvider tokenProvider)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < stage3.Lines.Count; i++)
            {
                if (!tokenProvider.IsCpdMode(i)) continue;

                var line = stage3.Lines[i];
                if (LineParser.ShouldSkipLine(line))
                    continue;

                var defMatch = CalcpadPatterns.FunctionDefinition.Match(line);
                if (!defMatch.Success)
                    continue;

                var paramList = ParameterParser.ParseParameters(defMatch.Groups[2].Value);
                if (paramList.Count == 1 && paramList[0].Trim().Length == 0)
                    paramList.Clear();

                counts[defMatch.Groups[1].Value] = paramList.Count;
            }
            return counts;
        }

        private void ValidateMacroCalls(Stage3Context stage3, LinterResult result, TokenizedLineProvider tokenProvider)
        {
            for (int i = 0; i < stage3.Lines.Count; i++)
            {
                if (!tokenProvider.IsCpdMode(i)) continue;

                var line = stage3.Lines[i];

                if (LineParser.ShouldSkipLine(line))
                    continue;

                var trimmed = line.Trim();

                // Skip #def lines (macro definitions)
                if (LineParser.IsDefStatement(trimmed))
                    continue;

                var tokens = tokenProvider.GetTokensForLine(i);

                foreach (var token in tokens)
                {
                    // Only check Macro tokens
                    if (token.Type != TokenType.Macro)
                        continue;

                    var macroName = token.Text;

                    // Check if macro is defined
                    if (!stage3.DefinedMacros.ContainsKey(macroName))
                    {
                        // Pass Stage3 line index - diagnostic extensions handle mapping
                        result.AddError(i, token.Column, token.Column + token.Length, "CPD-3303",
                            "'" + macroName + "'");
                        continue;
                    }

                    // Check parameter count
                    if (stage3.DefinedMacros.TryGetValue(macroName, out MacroInfo macroInfo))
                    {
                        var rawArgs = ParseMacroCallArgStrings(line, token.Column + token.Length);
                        if (rawArgs == null) rawArgs = new List<string>();

                        int totalActual = rawArgs.Count;
                        if (totalActual != macroInfo.ParamCount)
                        {
                            var endCol = ParsingHelpers.FindClosingParen(line, token.Column + token.Length);
                            if (endCol <= token.Column + token.Length)
                                endCol = token.Column + token.Length;
                            result.AddError(i, token.Column, endCol, "CPD-3304",
                                "'" + macroName + "' expects " + macroInfo.ParamCount + " parameter(s) but got " + totalActual);
                        }
                    }
                }
            }
        }

        private static bool IsBeingDefined(string line, int identifierStart, string identifier)
        {
            // Check if identifier is on the left side of = (being assigned/defined)
            var beforeIdentifier = line.Substring(0, identifierStart).TrimEnd();

            // If there's nothing before, check if = comes after the identifier
            if (string.IsNullOrEmpty(beforeIdentifier))
            {
                var afterPos = identifierStart + identifier.Length;
                if (afterPos < line.Length)
                {
                    var afterIdentifier = line.Substring(afterPos).TrimStart();
                    if (afterIdentifier.StartsWith("=") && !afterIdentifier.StartsWith("=="))
                        return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Count the number of parameters in a function call.
        /// Returns -1 if no opening parenthesis is found.
        /// </summary>
        private static int CountFunctionParams(string line, int afterFuncName)
        {
            var (found, paramsStr) = ParsingHelpers.ExtractParamsString(line, afterFuncName);
            if (!found)
                return -1;

            return ParameterParser.CountParameters(paramsStr);
        }

        /// <summary>
        /// Returns the raw argument strings for a macro call, or null if no opening paren found.
        /// </summary>
        private static List<string> ParseMacroCallArgStrings(string line, int afterMacroName)
        {
            var (found, paramsStr) = ParsingHelpers.ExtractParamsString(line, afterMacroName);
            if (!found) return null;
            return ParameterParser.ParseMacroParameters(paramsStr);
        }

        /// <summary>
        /// Validates that defined variables are actually used after their last assignment.
        /// Reports warnings for unused definitions to help identify dead code.
        ///
        /// Uses pre-collected variable assignment and usage data from the content resolver's
        /// Lint-mode tokenization, which correctly identifies Variable tokens even inside
        /// function call arguments and macro call arguments.
        ///
        /// Examples:
        /// - var = 1 / var / var = 2       -> warns on line 3 (var = 2 is never used after)
        /// - var = 1 / var / var = 2 / var -> no warning (var is used after last assignment)
        /// - var = 1 / var / var = 2 / 'text'var' -> no warning (var used in comment after last assignment)
        ///
        /// Note: Functions are not checked because they may be called from included files.
        /// </summary>
        private void ValidateUnusedDefinitions(Stage3Context stage3, LinterResult result, TokenizedLineProvider tokenProvider)
        {
            // Build last assignment for each variable from pre-collected assignment data
            var lastAssignment = new Dictionary<string, (int Line, int Column, int Length)>(StringComparer.Ordinal);

            foreach (var (name, line, column, length) in stage3.VariableAssignments)
            {
                // Keep the LAST assignment (highest line number)
                if (!lastAssignment.TryGetValue(name, out var existing) || line > existing.Line)
                {
                    lastAssignment[name] = (line, column, length);
                }
            }

            // Check which variables have usage after their last assignment
            var usedAfterLastAssignment = new HashSet<string>(StringComparer.Ordinal);

            foreach (var (name, line, column) in stage3.VariableUsages)
            {
                if (!lastAssignment.TryGetValue(name, out var assignInfo))
                    continue;

                // Usage after the last assignment line
                if (line > assignInfo.Line)
                {
                    usedAfterLastAssignment.Add(name);
                }
                // Usage on the same line as last assignment (right side of =, or in comment interpolation)
                else if (line == assignInfo.Line)
                {
                    usedAfterLastAssignment.Add(name);
                }
            }

            // Report unused variables (those with no usage after their last assignment)
            foreach (var kvp in lastAssignment)
            {
                var varName = kvp.Key;
                var (line, column, length) = kvp.Value;

                if (!usedAfterLastAssignment.Contains(varName) &&
                    !CalcpadBuiltIns.SettingsVariables.Contains(varName))
                {
                    result.AddInformation(line, column, column + length, "CPD-3312",
                        "Variable '" + varName + "' is defined but never used");
                }
            }

            // Note: Unused function warnings are not reported
            // Functions may be called from included files
        }

    }
}
