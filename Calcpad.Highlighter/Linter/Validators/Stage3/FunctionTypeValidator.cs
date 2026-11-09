using System;
using System.Collections.Generic;
using Calcpad.Highlighter.Linter.Constants;
using Calcpad.Highlighter.Linter.Helpers;
using Calcpad.Highlighter.Linter.Models;
using Calcpad.Highlighter.Snippets.Models;
using Calcpad.Highlighter.Tokenizer.Models;

namespace Calcpad.Highlighter.Linter.Validators.Stage3
{
    /// <summary>
    /// Validates built-in function calls against their signatures' parameter counts.
    /// </summary>
    public class FunctionTypeValidator
    {
        public void Validate(Stage3Context stage3, LinterResult result, TokenizedLineProvider tokenProvider)
        {
            for (int i = 0; i < stage3.Lines.Count; i++)
            {
                if (!tokenProvider.IsCpdMode(i)) continue;

                var line = stage3.Lines[i];

                if (LineParser.ShouldSkipLine(line))
                    continue;

                ReadOnlySpan<char> trimmedSpan = line.AsSpan().Trim();

                if (LineParser.IsDirectiveLine(trimmedSpan))
                    continue;

                // Skip command block function definitions - type checking doesn't apply
                // since parameters can accept various types at runtime
                var trimmed = trimmedSpan.ToString();
                if (CalcpadPatterns.FunctionWithCommandBlock.IsMatch(trimmed))
                    continue;

                var tokens = tokenProvider.GetTokensForLine(i);

                // Pass Stage3 line index - diagnostic extensions handle mapping
                ValidateFunctionCallsOnLine(line, tokens, i, stage3, result);
            }
        }

        private void ValidateFunctionCallsOnLine(string line, List<Token> tokens, int stage3Line, Stage3Context stage3, LinterResult result)
        {
            foreach (var token in tokens)
            {
                if (token.Type != TokenType.Function)
                    continue;

                var funcName = token.Text;

                // Extract parameters from the call
                var (found, paramsStr) = ParsingHelpers.ExtractParamsString(line, token.Column + token.Length);
                if (!found)
                    continue;

                // Compute column of first character inside the parentheses
                var parenCol = token.Column + token.Length;
                ParsingHelpers.SkipWhitespace(line, ref parenCol);
                var paramStartCol = parenCol + 1; // skip the '('

                var paramTokenGroups = new List<List<Token>>();
                var parameters = ParameterParser.ParseParameters(paramsStr, tokens, paramStartCol, paramTokenGroups);
                var paramCount = parameters.Count;

                // Check for empty parameters in function calls (not allowed)
                for (int paramIdx = 0; paramIdx < parameters.Count; paramIdx++)
                {
                    if (string.IsNullOrWhiteSpace(parameters[paramIdx]))
                    {
                        var endCol = ParsingHelpers.FindClosingParen(line, token.Column + token.Length);
                        result.AddError(stage3Line, token.Column, endCol, "CPD-3311",
                            "Empty parameter " + (paramIdx + 1) + " in call to '" + funcName + "'");
                        break; // Report only the first empty parameter
                    }
                }

                // Check built-in functions with known signatures
                if (FunctionSignatures.HasSignature(funcName))
                {
                    // Get all overloads for this function
                    var overloads = FunctionSignatures.GetAllOverloads(funcName);

                    // Find overloads that match the parameter count
                    var matchingOverloads = FindMatchingOverloads(overloads, paramCount);

                    if (matchingOverloads.Count == 0)
                    {
                        // No overload matches the parameter count - report error
                        var signature = FunctionSignatures.GetSignature(funcName);
                        var endCol = ParsingHelpers.FindClosingParen(line, token.Column + token.Length);

                        if (paramCount < signature.MinParams)
                        {
                            result.AddError(stage3Line, token.Column, endCol, "CPD-3307",
                                "'" + funcName + "' requires at least " + signature.MinParams + " parameter(s), got " + paramCount);
                        }
                        else if (signature.MaxParams >= 0 && paramCount > signature.MaxParams)
                        {
                            result.AddError(stage3Line, token.Column, endCol, "CPD-3308",
                                "'" + funcName + "' accepts at most " + signature.MaxParams + " parameter(s), got " + paramCount);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Finds all overloads that accept the given parameter count.
        /// </summary>
        private static List<FunctionSignature> FindMatchingOverloads(FunctionSignature[] overloads, int paramCount)
        {
            var matching = new List<FunctionSignature>();

            foreach (var sig in overloads)
            {
                // AcceptsAnyCount means any parameter count is valid
                if (sig.AcceptsAnyCount)
                {
                    matching.Add(sig);
                    continue;
                }

                // Check if parameter count is within bounds
                if (paramCount >= sig.MinParams && (sig.MaxParams < 0 || paramCount <= sig.MaxParams))
                {
                    matching.Add(sig);
                }
            }

            return matching;
        }
    }
}
