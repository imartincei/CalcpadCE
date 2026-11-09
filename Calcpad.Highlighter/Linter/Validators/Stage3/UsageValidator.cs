using System;
using System.Collections.Generic;
using System.Linq;
using Calcpad.Core;
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
            ValidateUnusedDefinitions(stage3, result, tokenProvider);
        }

        /// <summary>
        /// CPD-3408: a command's expression must use its declared variable. $Plot, $Map and
        /// $Repeat are exempt. Core reports malformed counters.
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

                // $Plot, $Map and $Repeat counters needn't appear in the expression
                if (commandName.Equals("$Plot", StringComparison.OrdinalIgnoreCase) ||
                    commandName.Equals("$Map", StringComparison.OrdinalIgnoreCase) ||
                    commandName.Equals("$Repeat", StringComparison.OrdinalIgnoreCase))
                    continue;

                var tokens = tokenProvider.GetTokensForLine(i);
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

                if (equalsIndex < 0)
                    continue;

                var atAbsolutePos = atIndex;
                var equalsAbsolutePos = atIndex + 1 + equalsIndex;

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
                    !CalcpadBuiltIns.SettingsVariables.Contains(varName) &&
                    !stage3.CustomUnits.Contains(varName))
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
