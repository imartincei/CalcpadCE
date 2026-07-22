using System;
using System.Collections.Generic;
using Calcpad.Highlighter.ContentResolution;
using Calcpad.Highlighter.Linter.Constants;
using Calcpad.Highlighter.Linter.Helpers;
using Calcpad.Highlighter.Linter.Models;
using Calcpad.Highlighter.Tokenizer;
using Calcpad.Highlighter.Tokenizer.Models;

namespace Calcpad.Highlighter.Linter.Validators.Stage3
{
    /// <summary>
    /// Validates calls to functions that use command blocks ($Inline, $Block, $While).
    /// When a command block function is called, this validator tokenizes the block
    /// statements and validates them for undefined variables.
    /// </summary>
    public class CommandBlockValidator
    {
        public void Validate(Stage3Context stage3, LinterResult result, TokenizedLineProvider tokenProvider)
        {
            if (stage3.CommandBlockFunctions.Count == 0)
                return;

            // Validate that command block definitions don't contain # directives
            ValidateNoHashDirectivesInBlocks(stage3, result);

            ValidateCommandBlockCalls(stage3, result, tokenProvider);
        }

        /// <summary>
        /// Validates that command block statements don't contain # symbol.
        /// Inside $Inline, $Block, $While blocks, # directives are not allowed.
        /// Use if(), $Repeat, etc. instead.
        /// </summary>
        private void ValidateNoHashDirectivesInBlocks(Stage3Context stage3, LinterResult result)
        {
            foreach (var kvp in stage3.CommandBlockFunctions)
            {
                var blockInfo = kvp.Value;

                // Check each statement in the block for # symbol
                foreach (var statement in blockInfo.Statements)
                {
                    var hashIdx = statement.AsSpan().IndexOf('#');
                    if (hashIdx >= 0)
                    {
                        // Find the position of this statement in the full line
                        var fullLine = blockInfo.FullLine;
                        var statementIdx = fullLine.AsSpan().IndexOf(statement.AsSpan(), StringComparison.Ordinal);
                        var col = statementIdx >= 0 ? statementIdx + hashIdx : hashIdx;

                        result.AddError(blockInfo.LineNumber, col, col + 1, "CPD-3405",
                            "'#' directives are not allowed inside command blocks. Use if(), $Repeat, etc. instead");
                    }
                }
            }
        }

        private void ValidateCommandBlockCalls(Stage3Context stage3, LinterResult result, TokenizedLineProvider tokenProvider)
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

                // Skip the function definition line itself
                var trimmed = trimmedSpan.ToString();
                if (CalcpadPatterns.FunctionWithCommandBlock.IsMatch(trimmed))
                    continue;

                var tokens = tokenProvider.GetTokensForLine(i);

                foreach (var token in tokens)
                {
                    // Only check Function tokens
                    if (token.Type != TokenType.Function)
                        continue;

                    var funcName = token.Text;

                    // Check if this is a command block function
                    if (!stage3.CommandBlockFunctions.TryGetValue(funcName, out var blockInfo))
                        continue;

                    // Extract the actual parameters from the call
                    var callParams = ExtractCallParameters(line, token.Column + token.Length);
                    if (callParams == null)
                        continue;

                    // Check parameter count matches
                    if (blockInfo.Parameters.Count != callParams.Count)
                        continue;

                    // Validate each statement using tokenizer-based parsing
                    // Actual argument expressions are already validated at the call site
                    // by UsageValidator, so no parameter substitution is needed here.
                    var errors = ValidateBlockStatements(blockInfo, stage3);

                    // Report errors at the call site
                    if (errors.Count > 0)
                    {
                        var callEnd = FindCallEnd(line, token.Column);
                        foreach (var error in errors)
                        {
                            result.AddError(i, token.Column, callEnd, error.Code, error.Message);
                        }
                    }
                }
            }
        }

        private static List<string> ExtractCallParameters(string line, int afterFuncName)
        {
            var lineSpan = line.AsSpan();

            // Skip whitespace to find opening paren
            var pos = afterFuncName;
            ParsingHelpers.SkipWhitespace(lineSpan, ref pos);

            if (pos >= lineSpan.Length || lineSpan[pos] != '(')
                return null;

            var startPos = pos + 1;
            var closePos = ParsingHelpers.FindMatchingClose(lineSpan, pos, '(', ')');

            if (closePos < 0)
                return null;

            var paramsStr = lineSpan.Slice(startPos, closePos - startPos).ToString();
            return ParameterParser.ParseParameters(paramsStr);
        }

        private static int FindCallEnd(string line, int funcStart)
        {
            var lineSpan = line.AsSpan();

            // Find opening paren
            var pos = funcStart;
            while (pos < lineSpan.Length && lineSpan[pos] != '(')
                pos++;

            if (pos >= lineSpan.Length)
                return funcStart + 1;

            var closePos = ParsingHelpers.FindMatchingClose(lineSpan, pos, '(', ')');
            return closePos >= 0 ? closePos + 1 : lineSpan.Length;
        }

        private static List<ValidationError> ValidateBlockStatements(
            CommandBlockInfo blockInfo,
            Stage3Context stage3)
        {
            var errors = new List<ValidationError>();

            // Build local variables set - variables assigned within the block
            var localVariables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var localAssignments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var tokenizer = new CalcpadTokenizer();

            // First pass: collect all variables that are assigned in the block and their RHS
            foreach (var statement in blockInfo.Statements)
            {
                CollectAssignedVariables(tokenizer, statement, localVariables, localAssignments);
                CollectCommandLoopVariables(tokenizer, statement, localVariables);
            }

            // Second pass: validate each statement
            foreach (var statement in blockInfo.Statements)
            {
                ValidateStatement(tokenizer, statement, stage3, localVariables, blockInfo.Parameters, errors);
            }

            return errors;
        }

        /// <summary>
        /// Tokenizes the statement and finds Variable tokens at the start that are
        /// followed by an "=" operator, indicating a local variable assignment.
        /// Also captures the RHS expression for type inference.
        /// </summary>
        private static void CollectAssignedVariables(
            CalcpadTokenizer tokenizer, string statement,
            HashSet<string> localVariables,
            Dictionary<string, string> localAssignments = null)
        {
            var result = tokenizer.Tokenize(statement);
            var tokens = result.Tokens;

            // Look for assignment pattern: first Variable token followed by Operator "="
            bool foundVariable = false;
            string variableName = null;
            bool isElementAccess = false;
            int equalsEndColumn = -1;

            foreach (var token in tokens)
            {
                // Skip whitespace/none tokens
                if (token.Type == TokenType.None)
                    continue;

                if (!foundVariable)
                {
                    // First meaningful token must be a Variable for an assignment
                    if (token.Type == TokenType.Variable)
                    {
                        foundVariable = true;
                        variableName = token.Text;
                        isElementAccess = token.Text[^1] == '.';
                    }
                    else
                    {
                        break; // Not an assignment pattern
                    }
                }
                else
                {
                    // After the variable, check for "." (element access) or "=" (assignment)
                    if (token.Type == TokenType.Operator && token.Text == ".")
                    {
                        isElementAccess = true;
                        continue; // Keep looking for =
                    }

                    if (token.Type == TokenType.Operator && token.Text == "=")
                    {
                        var name = variableName;
                        if (isElementAccess && name[^1] == '.')
                            name = name[..^1];
                        localVariables.Add(name);
                        equalsEndColumn = token.Column + token.Length;

                        // Capture the RHS expression for type inference
                        if (localAssignments != null && !isElementAccess && equalsEndColumn < statement.Length)
                        {
                            var rhs = statement.AsSpan(equalsEndColumn).Trim().ToString();
                            if (!string.IsNullOrEmpty(rhs))
                                localAssignments[name] = rhs;
                        }
                    }
                    break; // Done checking this statement
                }
            }
        }

        /// <summary>
        /// Collects the loop variables an embedded command declares inside a block statement,
        /// e.g. 'k' in "$Repeat{s = s + k @ k = 1 : n}". These are scoped to the command but,
        /// like assigned locals, must not be flagged when used in the block body.
        /// </summary>
        private static void CollectCommandLoopVariables(
            CalcpadTokenizer tokenizer, string statement, HashSet<string> localVariables)
        {
            var tokens = tokenizer.Tokenize(statement).Tokens;
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
                    localVariables.Add(token.Text);
                    afterAt = false;
                }
            }
        }

        /// <summary>
        /// Tokenizes the statement and checks all Variable tokens against known definitions.
        /// Uses the tokenizer to correctly handle commas and special characters in variable names.
        /// </summary>
        private static void ValidateStatement(
            CalcpadTokenizer tokenizer,
            string statement,
            Stage3Context stage3,
            HashSet<string> localVariables,
            List<string> paramNames,
            List<ValidationError> errors)
        {
            var result = tokenizer.Tokenize(statement);

            foreach (var token in result.Tokens)
            {
                // Only check Variable tokens (unknown functions also appear as Variable)
                if (token.Type != TokenType.Variable)
                    continue;

                var identifier = token.Text;

                // Handle element access syntax: v.(index)
                var isElementAccess = identifier[^1] == '.';
                var baseName = isElementAccess ? identifier[..^1] : identifier;

                // Skip if it's a parameter name
                if (paramNames.Contains(baseName))
                    continue;

                // Skip if it's a local variable assigned in this block
                if (localVariables.Contains(baseName))
                    continue;

                // Skip if it's a keyword or command
                if (IsKeywordOrCommand(baseName))
                    continue;

                // Check if it's defined globally
                var isDefined = stage3.DefinedVariables.Contains(baseName) ||
                               stage3.DefinedFunctions.ContainsKey(baseName) ||
                               CalcpadBuiltIns.Functions.Contains(baseName) ||
                               CalcpadBuiltIns.CommonConstants.Contains(baseName) ||
                               CalcpadBuiltIns.Units.Contains(baseName) ||
                               stage3.CustomUnits.Contains(baseName) ||
                               stage3.DefinedMacros.ContainsKey(baseName);

                if (!isDefined)
                {
                    // Check if this looks like param.x where param is a function parameter.
                    // Function params don't support .i syntax - must use .(i).
                    var dotIdx = baseName.AsSpan().IndexOf('.');
                    if (dotIdx > 0)
                    {
                        var beforeDot = baseName[..dotIdx];
                        if (paramNames.Contains(beforeDot))
                        {
                            var afterDot = baseName[(dotIdx + 1)..];
                            errors.Add(new ValidationError
                            {
                                Code = "CPD-3301",
                                Message = "'" + baseName + "' in command block. Function parameters require .()" +
                                    " syntax for element access: " + beforeDot + ".(" + afterDot + ")"
                            });
                            continue;
                        }
                    }
                    errors.Add(new ValidationError
                    {
                        Code = "CPD-3301",
                        Message = "'" + baseName + "' in command block"
                    });
                }
            }
        }

        private static bool IsKeywordOrCommand(string identifier)
        {
            // Check if it's a valid hash keyword (without #)
            if (CalcpadBuiltIns.ValidHashKeywords.Contains(identifier))
                return true;

            // Check if it's a command (without $)
            if (CalcpadBuiltIns.Commands.Contains("$" + identifier))
                return true;

            return false;
        }

        private class ValidationError
        {
            public string Code { get; set; }
            public string Message { get; set; }
        }
    }
}
