using System;
using System.Collections.Generic;
using Calcpad.Highlighter.Linter.Constants;
using Calcpad.Highlighter.Linter.Helpers;
using Calcpad.Highlighter.Linter.Models;

namespace Calcpad.Highlighter.Linter.Validators.Stage3
{
    public class NamingValidator
    {
        public void Validate(Stage3Context stage3, LinterResult result, TokenizedLineProvider tokenProvider)
        {
            ValidateVariableNaming(stage3, result, tokenProvider);
            ValidateFunctionNaming(stage3, result, tokenProvider);
        }

        private void ValidateVariableNaming(Stage3Context stage3, LinterResult result, TokenizedLineProvider tokenProvider)
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

                // Check for variable assignments
                var varMatch = CalcpadPatterns.VariableAssignment.Match(trimmed);
                if (varMatch.Success)
                {
                    var varName = varMatch.Groups[1].Value;
                    // Pass Stage3 line index - diagnostic extensions handle mapping
                    ValidateIdentifierName(varName, line, i, true, result);
                }
            }
        }

        private void ValidateFunctionNaming(Stage3Context stage3, LinterResult result, TokenizedLineProvider tokenProvider)
        {
            var definedNames = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < stage3.Lines.Count; i++)
            {
                if (!tokenProvider.IsCpdMode(i)) continue;

                var line = stage3.Lines[i];

                if (LineParser.ShouldSkipLine(line))
                    continue;

                var trimmed = line.Trim();

                if (LineParser.IsDirectiveLine(trimmed))
                    continue;

                // Check for function definitions
                var funcMatch = CalcpadPatterns.FunctionDefinition.Match(trimmed);
                if (funcMatch.Success)
                {
                    var funcName = funcMatch.Groups[1].Value;
                    var paramsStr = funcMatch.Groups[2].Value.Trim();

                    // Redefining a user-defined function is allowed - inform rather than error.
                    // Built-in conflicts (CPD-3203) and macros ($) are handled separately.
                    if (!funcName.EndsWith("$") && !CalcpadBuiltIns.Functions.Contains(funcName)
                        && !definedNames.Add(funcName))
                    {
                        var startPos = line.IndexOf(funcName, StringComparison.Ordinal);
                        var col = startPos >= 0 ? startPos : 0;
                        result.AddInformation(i, col, col + funcName.Length, "CPD-3313",
                            "Function '" + funcName + "' redefines an existing function");
                    }

                    // Functions must have at least one parameter
                    if (string.IsNullOrWhiteSpace(paramsStr))
                    {
                        var startPos = line.IndexOf(funcName, StringComparison.Ordinal);
                        var parenPos = line.IndexOf('(', startPos);
                        var endPos = line.IndexOf(')', parenPos);
                        if (endPos < 0) endPos = parenPos + 1;
                        result.AddError(i, startPos >= 0 ? startPos : 0, endPos + 1, "CPD-3206",
                            "Function '" + funcName + "' must have at least one parameter");
                    }

                    // Pass Stage3 line index - diagnostic extensions handle mapping
                    ValidateIdentifierName(funcName, line, i, false, result);
                }
            }
        }

        private void ValidateIdentifierName(string identifier, string line, int stage3Line, bool isVariable, LinterResult result)
        {
            // Skip macros (they end with $)
            if (identifier.EndsWith("$"))
                return;

            var startPos = line.IndexOf(identifier, StringComparison.Ordinal);
            var endPos = startPos >= 0 ? startPos + identifier.Length : line.Length;
            var col = startPos >= 0 ? startPos : 0;

            // Check if name starts with valid character
            // Variables, functions and units must begin with a letter or ∡
            // (underscore is NOT valid as a starting character)
            if (identifier.Length > 0)
            {
                var firstChar = identifier[0];
                if (!char.IsLetter(firstChar) && !CalcpadCharacterHelpers.IsGreekLetter(firstChar) && !CalcpadCharacterHelpers.IsSpecialMathChar(firstChar))
                {
                    var code = isVariable ? "CPD-3201" : "CPD-3202";
                    result.AddError(stage3Line, col, endPos, code,
                        "Invalid character: '" + firstChar + "'. Variables, functions and units must begin with a letter or ∡");
                    return;
                }
            }

            // Variable names CAN overlap with built-in function names (e.g., vector = [1;2;3])
            // But user-defined function names cannot - they would shadow the built-in
            if (!isVariable && CalcpadBuiltIns.Functions.Contains(identifier))
            {
                result.AddError(stage3Line, col, endPos, "CPD-3203",
                    "Function name '" + identifier + "' conflicts with built-in function");
                return;
            }

            // Check for conflict with built-in constants (WARNING - redefinition is allowed)
            if (CalcpadBuiltIns.CommonConstants.Contains(identifier))
            {
                result.AddWarning(stage3Line, col, endPos, "CPD-3205",
                    "Variable name '" + identifier + "' conflicts with built-in constant");
                return; // Don't report additional conflicts
            }

            // Check for conflict with keywords (ERROR, only for variables)
            if (isVariable && IsKeywordName(identifier))
            {
                result.AddError(stage3Line, col, endPos, "CPD-3204",
                    "Variable name '" + identifier + "' conflicts with keyword");
                return; // Don't report additional conflicts
            }

            // Note: Variable names shadowing built-in units is common and usually intentional
            // (e.g., d for depth, m for mass, etc.) - not worth warning about
        }

        private static bool IsKeywordName(string name)
        {
            // Check if the name matches a control keyword (without #)
            var lower = name.ToLowerInvariant();
            return lower == "if" || lower == "else" || lower == "for" ||
                   lower == "while" || lower == "repeat" || lower == "loop" ||
                   lower == "break" || lower == "continue" || lower == "def" ||
                   lower == "end" || lower == "and" || lower == "or" ||
                   lower == "not" || lower == "xor";
        }
    }
}
