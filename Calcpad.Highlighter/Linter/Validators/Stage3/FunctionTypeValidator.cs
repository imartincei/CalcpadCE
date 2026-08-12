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
    /// Validates function calls against their signatures, checking parameter counts and types.
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
            // Get function parameters from this line - these should be treated as Various type
            var functionParams = ParsingHelpers.GetFunctionParamsFromLine(line);

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
                        continue;
                    }

                    // Validate parameter types if we have a TypeTracker
                    if (stage3.TypeTracker != null)
                    {
                        ValidateParameterTypesAgainstOverloads(parameters, paramTokenGroups, matchingOverloads, funcName, token, stage3Line, line, stage3, functionParams, result);
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

        /// <summary>
        /// Validates parameter types against all matching overloads.
        /// If any overload matches all parameters, no error is reported.
        /// Only reports an error if NO overload matches the parameter types.
        /// </summary>
        private void ValidateParameterTypesAgainstOverloads(
            List<string> parameters,
            List<List<Token>> paramTokenGroups,
            List<FunctionSignature> matchingOverloads,
            string funcName,
            Token token,
            int stage3Line,
            string line,
            Stage3Context stage3,
            HashSet<string> functionParams,
            LinterResult result)
        {
            // Infer types for all parameters once, using tokens when available
            var actualTypes = new CalcpadType[parameters.Count];
            for (int i = 0; i < parameters.Count; i++)
            {
                var param = parameters[i].Trim();
                if (string.IsNullOrEmpty(param))
                {
                    actualTypes[i] = CalcpadType.Unknown;
                }
                else if (i < paramTokenGroups.Count && paramTokenGroups[i].Count > 0)
                {
                    actualTypes[i] = InferTypeFromTokens(paramTokenGroups[i], stage3, functionParams);
                }
                else
                {
                    actualTypes[i] = InferParameterType(param, stage3, functionParams);
                }
            }

            // Check if any overload fully matches
            FunctionSignature bestMatch = matchingOverloads[0];
            int bestMatchScore = -1;

            foreach (var sig in matchingOverloads)
            {
                int matchScore = 0;
                bool allMatch = true;

                for (int i = 0; i < parameters.Count; i++)
                {
                    if (string.IsNullOrEmpty(parameters[i].Trim()))
                        continue;

                    var expectedType = sig.GetParameterType(i);

                    // Any and Various always match
                    if (expectedType == ParameterType.Any || expectedType == ParameterType.Various)
                    {
                        matchScore++;
                        continue;
                    }

                    if (sig.IsArgumentCompatible(expectedType, actualTypes[i]))
                    {
                        matchScore++;
                    }
                    else
                    {
                        allMatch = false;
                    }
                }

                // If this overload matches all parameters, we're done - no error
                if (allMatch)
                    return;

                // Track best partial match for error reporting
                if (matchScore > bestMatchScore)
                {
                    bestMatchScore = matchScore;
                    bestMatch = sig;
                }
            }

            // No overload fully matched - report errors against the best matching signature
            for (int i = 0; i < parameters.Count; i++)
            {
                var param = parameters[i].Trim();
                if (string.IsNullOrEmpty(param))
                    continue;

                var expectedType = bestMatch.GetParameterType(i);
                if (expectedType == ParameterType.Any || expectedType == ParameterType.Various)
                    continue;

                if (!bestMatch.IsArgumentCompatible(expectedType, actualTypes[i]))
                {
                    var endCol = ParsingHelpers.FindClosingParen(line, token.Column + token.Length);
                    var expectedName = FunctionSignature.GetTypeName(expectedType);
                    var actualName = GetCalcpadTypeName(actualTypes[i]);
                    result.AddWarning(stage3Line, token.Column, endCol, "CPD-3309",
                        "'" + funcName + "' parameter " + (i + 1) + " expects " + expectedName + " but got " + actualName);
                }
            }
        }

        /// <summary>
        /// Infers the CalcpadType of a parameter expression.
        /// </summary>
        private CalcpadType InferParameterType(string expression, Stage3Context stage3, HashSet<string> functionParams)
        {
            var trimmed = expression.Trim();

            // If the expression is a function parameter, treat it as Various (unknown type at definition)
            if (functionParams.Contains(trimmed))
                return CalcpadType.Various;

            // Check if it's a known variable
            if (stage3.TypeTracker != null)
            {
                var varInfo = stage3.TypeTracker.GetVariableInfo(trimmed);
                if (varInfo != null)
                {
                    // For functions, return the return type, not the type (which is Function)
                    if (varInfo.Type == CalcpadType.Function)
                        return varInfo.ReturnType;
                    return varInfo.Type;
                }

                // Try to infer from expression (this handles function calls)
                return stage3.TypeTracker.InferTypeFromExpression(trimmed);
            }

            return CalcpadType.Unknown;
        }

        private static string GetCalcpadTypeName(CalcpadType type)
        {
            return type switch
            {
                CalcpadType.Value => "scalar",
                CalcpadType.Vector => "vector",
                CalcpadType.Matrix => "matrix",
                CalcpadType.Various => "various",
                CalcpadType.Unknown => "unknown",
                _ => type.ToString().ToLower()
            };
        }

        /// <summary>
        /// Infers the type of a parameter expression from its actual tokens.
        /// Uses the tokenizer's correct variable name boundaries (handling commas,
        /// dots, Greek letters, etc.) instead of re-scanning the raw string.
        /// </summary>
        private static CalcpadType InferTypeFromTokens(
            List<Token> paramTokens,
            Stage3Context stage3,
            HashSet<string> functionParams)
        {
            // Single token: direct lookup
            if (paramTokens.Count == 1)
            {
                var t = paramTokens[0];
                if (t.Type == TokenType.Variable || t.Type == TokenType.LocalVariable)
                {
                    if (functionParams.Contains(t.Text))
                        return CalcpadType.Various;
                    if (stage3.TypeTracker != null)
                    {
                        var info = stage3.TypeTracker.GetVariableInfo(t.Text);
                        if (info != null)
                            return info.Type == CalcpadType.Function ? info.ReturnType : info.Type;
                    }
                }
                if (t.Type == TokenType.Const)
                    return CalcpadType.Value;
                return CalcpadType.Unknown;
            }

            // Check for vector/matrix literal: starts with [
            if (paramTokens[0].Type == TokenType.Bracket && paramTokens[0].Text == "[")
            {
                bool hasPipe = false;
                int d = 0;
                foreach (var t in paramTokens)
                {
                    if (t.Type == TokenType.Bracket && t.Text == "[") d++;
                    else if (t.Type == TokenType.Bracket && t.Text == "]") d--;
                    else if (d == 1 && t.Text == "|") hasPipe = true;
                }
                return hasPipe ? CalcpadType.Matrix : CalcpadType.Vector;
            }

            // Complex expression: scan top-level operands using actual tokens.
            // Bracket depth tracking naturally skips function arguments so
            // e.g. len(vec) uses len's return type, not vec's type.
            var highestType = CalcpadType.Unknown;
            int depth = 0;

            for (int i = 0; i < paramTokens.Count; i++)
            {
                var t = paramTokens[i];

                if (t.Type == TokenType.Bracket)
                {
                    if (t.Text == "(" || t.Text == "[" || t.Text == "{") depth++;
                    else if (t.Text == ")" || t.Text == "]" || t.Text == "}") depth--;
                    continue;
                }

                if (depth > 0)
                    continue;

                if (t.Type == TokenType.Function)
                {
                    if (stage3.TypeTracker != null)
                    {
                        var retType = stage3.TypeTracker.GetFunctionReturnType(t.Text);
                        if (retType == CalcpadType.Matrix) return CalcpadType.Matrix;
                        if (retType == CalcpadType.Vector && highestType != CalcpadType.Matrix)
                            highestType = CalcpadType.Vector;
                    }
                    continue;
                }

                if (t.Type == TokenType.Variable || t.Type == TokenType.LocalVariable)
                {
                    // Element access (v.1, v.i) yields a scalar — skip the base variable's
                    // type and consume the index token. Bracket-form indices like v.(expr)
                    // and M.(1;2) are handled by the depth tracking above.
                    if (i + 1 < paramTokens.Count &&
                        paramTokens[i + 1].Type == TokenType.Operator &&
                        paramTokens[i + 1].Text == ".")
                    {
                        i++;
                        if (i + 1 < paramTokens.Count)
                        {
                            var idx = paramTokens[i + 1];
                            if (idx.Type == TokenType.Variable ||
                                idx.Type == TokenType.LocalVariable ||
                                idx.Type == TokenType.Const)
                            {
                                i++;
                            }
                        }
                        continue;
                    }

                    if (functionParams.Contains(t.Text))
                    {
                        if (highestType == CalcpadType.Unknown) highestType = CalcpadType.Various;
                        continue;
                    }
                    if (stage3.TypeTracker != null)
                    {
                        var info = stage3.TypeTracker.GetVariableInfo(t.Text);
                        if (info != null)
                        {
                            var vt = info.Type == CalcpadType.Function ? info.ReturnType : info.Type;
                            if (vt == CalcpadType.Matrix) return CalcpadType.Matrix;
                            if (vt == CalcpadType.Vector && highestType != CalcpadType.Matrix)
                                highestType = CalcpadType.Vector;
                        }
                    }
                }
            }

            return highestType;
        }
    }
}
