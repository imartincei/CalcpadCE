using System;
using System.Text.Json;
using Calcpad.Highlighter.Linter.Constants;
using Calcpad.Highlighter.Linter.Helpers;
using Calcpad.Highlighter.Linter.Models;
using Calcpad.Highlighter.Tokenizer.Models;

namespace Calcpad.Highlighter.Linter.Validators.Stage3
{
    public class SemanticValidator
    {
        public void Validate(Stage3Context stage3, LinterResult result, TokenizedLineProvider tokenProvider)
        {
            ValidateOperators(stage3, result, tokenProvider);
            ValidateCommands(stage3, result, tokenProvider);
            ValidateDirectives(stage3, result, tokenProvider);
            ValidateAssignments(stage3, result, tokenProvider);
            ValidateIncompleteExpressions(stage3, result, tokenProvider);
            ValidateConstReassignment(stage3, result);
        }

        private void ValidateOperators(Stage3Context stage3, LinterResult result, TokenizedLineProvider tokenProvider)
        {
            for (int i = 0; i < stage3.Lines.Count; i++)
            {
                if (!tokenProvider.IsCpdMode(i)) continue;

                var line = stage3.Lines[i];

                if (LineParser.ShouldSkipLine(line))
                    continue;

                var tokens = tokenProvider.GetTokensForLine(i);

                // Check operator tokens for invalid sequences
                for (int t = 0; t < tokens.Count - 1; t++)
                {
                    var token = tokens[t];
                    var nextToken = tokens[t + 1];

                    if (token.Type != TokenType.Operator)
                        continue;

                    // Check for consecutive operators that form invalid sequences
                    if (nextToken.Type == TokenType.Operator && token.Column + token.Length == nextToken.Column)
                    {
                        var combined = token.Text + nextToken.Text;
                        if (IsInvalidOperatorSequence(combined))
                        {
                            // Pass Stage3 line index - diagnostic extensions handle mapping
                            result.AddError(i, token.Column, nextToken.Column + nextToken.Length, "CPD-3401",
                                "'" + combined + "' is not valid in Calcpad");
                        }
                    }
                }
            }
        }

        private static bool IsInvalidOperatorSequence(string seq)
        {
            return seq == "++" || seq == "--" || seq == "**" || seq == "//" || seq == "&&" || seq == "||";
        }

        private void ValidateCommands(Stage3Context stage3, LinterResult result, TokenizedLineProvider tokenProvider)
        {
            for (int i = 0; i < stage3.Lines.Count; i++)
            {
                if (!tokenProvider.IsCpdMode(i)) continue;

                var line = stage3.Lines[i];

                if (LineParser.ShouldSkipLine(line))
                    continue;

                var tokens = tokenProvider.GetTokensForLine(i);

                foreach (var token in tokens)
                {
                    // Only check Command tokens
                    if (token.Type != TokenType.Command)
                        continue;

                    var command = token.Text;

                    // Check if it's a valid command
                    if (!CalcpadBuiltIns.Commands.Contains(command))
                    {
                        // Pass Stage3 line index - diagnostic extensions handle mapping
                        result.AddError(i, token.Column, token.Column + token.Length, "CPD-3402",
                            "'" + command + "' is not a recognized command");
                    }
                }
            }
        }

        private void ValidateDirectives(Stage3Context stage3, LinterResult result, TokenizedLineProvider tokenProvider)
        {
            for (int i = 0; i < stage3.Lines.Count; i++)
            {
                if (!tokenProvider.IsCpdMode(i)) continue;

                var line = stage3.Lines[i];

                if (LineParser.ShouldSkipLine(line))
                    continue;

                var tokens = tokenProvider.GetTokensForLine(i);

                // Find Keyword tokens
                foreach (var token in tokens)
                {
                    if (token.Type != TokenType.Keyword)
                        continue;

                    var keyword = token.Text.ToLowerInvariant();

                    // Check if it's a valid keyword (includes regular keywords, control block keywords, and end keywords)
                    if (!IsValidKeyword(keyword))
                    {
                        // Check if it might be a multi-word keyword (e.g., "#end if", "#else if")
                        // The tokenizer may emit "#end" or "#else" as one token, need to check following tokens
                        var trimmed = keyword.TrimStart('#');
                        if (CalcpadBuiltIns.MultiWordFirstWords.Contains(trimmed))
                        {
                            // Look for the next non-whitespace content
                            var remaining = line.Substring(token.Column + token.Length).TrimStart();
                            if (remaining.Length > 0)
                            {
                                var spaceIndex = remaining.IndexOf(' ');
                                var secondWord = spaceIndex >= 0 ? remaining.Substring(0, spaceIndex) : remaining;
                                var twoWordKeyword = keyword + " " + secondWord.ToLowerInvariant();
                                if (IsValidKeyword(twoWordKeyword))
                                {
                                    continue; // Valid two-word keyword
                                }
                            }
                        }

                        // Pass Stage3 line index - diagnostic extensions handle mapping
                        result.AddError(i, token.Column, token.Column + token.Length, "CPD-3403",
                            "'" + keyword + "' is not a recognized directive");
                    }
                }
            }
        }


        private void ValidateAssignments(Stage3Context stage3, LinterResult result, TokenizedLineProvider tokenProvider)
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

                // Check for multiple assignments within an expression segment (not in commands)
                // Strings (Comment/HtmlComment tokens) act as expression boundaries,
                // so h=5', 'g=6 is valid (two separate expression segments)
                var assignmentCount = 0;
                var parenDepth = 0;
                var hasCommand = false;
                var hasMultipleInSegment = false;

                for (int t = 0; t < tokens.Count; t++)
                {
                    var token = tokens[t];

                    // String tokens are expression boundaries - reset count
                    if (token.Type == TokenType.Comment || token.Type == TokenType.HtmlComment)
                    {
                        assignmentCount = 0;
                        continue;
                    }

                    // Track parenthesis depth - = inside parens is a default value, not assignment
                    if (token.Type == TokenType.Bracket)
                    {
                        if (token.Text == "(") parenDepth++;
                        else if (token.Text == ")") parenDepth--;
                    }

                    if (token.Type == TokenType.Command)
                    {
                        hasCommand = true;
                    }
                    else if (token.Type == TokenType.Operator && (token.Text == "=" || token.Text == "←") && parenDepth == 0)
                    {
                        var isComparison = false;

                        // Only = can form compound operators (==, <=, >=, !=), not ←
                        if (token.Text == "=")
                        {
                            // Check if it's not part of ==, <=, >=, !=
                            // Check previous token
                            if (t > 0)
                            {
                                var prev = tokens[t - 1];
                                if (prev.Type == TokenType.Operator &&
                                    prev.Column + prev.Length == token.Column &&
                                    (prev.Text == "<" || prev.Text == ">" || prev.Text == "!" || prev.Text == "="))
                                {
                                    isComparison = true;
                                }
                            }

                            // Check next token
                            if (t < tokens.Count - 1)
                            {
                                var next = tokens[t + 1];
                                if (next.Type == TokenType.Operator &&
                                    token.Column + token.Length == next.Column &&
                                    next.Text == "=")
                                {
                                    isComparison = true;
                                }
                            }
                        }

                        if (!isComparison)
                        {
                            assignmentCount++;
                            if (assignmentCount > 1)
                                hasMultipleInSegment = true;
                        }
                    }
                }

                // Multiple assignments in a single expression segment without command context
                if (hasMultipleInSegment && !hasCommand)
                {
                    // Pass Stage3 line index - diagnostic extensions handle mapping
                    result.AddWarning(i, 0, line.Length, "CPD-3404",
                        "multiple assignments on the same line");
                }
            }
        }

        private void ValidateIncompleteExpressions(Stage3Context stage3, LinterResult result, TokenizedLineProvider tokenProvider)
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

                // Skip empty lines
                if (tokens.Count == 0)
                    continue;

                // Get the last meaningful token (excluding whitespace)
                Token lastToken = tokens[tokens.Count - 1];

                // Check if the last token is a binary operator
                if (lastToken.Type == TokenType.Operator && IsBinaryOperator(lastToken.Text))
                {
                    // This is an incomplete expression
                    result.AddError(i, lastToken.Column, lastToken.Column + lastToken.Length, "CPD-3407",
                        "expression cannot end with operator '" + lastToken.Text + "'");
                }
                // Check if line ends with assignment operator without a right-hand side
                else if (lastToken.Type == TokenType.Operator && (lastToken.Text == "=" || lastToken.Text == "←"))
                {
                    // Check if this is not part of a comparison operator (only applies to =)
                    bool isComparison = false;
                    if (lastToken.Text == "=" && tokens.Count >= 2)
                    {
                        var prevToken = tokens[tokens.Count - 2];
                        if (prevToken.Type == TokenType.Operator &&
                            prevToken.Column + prevToken.Length == lastToken.Column &&
                            (prevToken.Text == "<" || prevToken.Text == ">" || prevToken.Text == "!" || prevToken.Text == "="))
                        {
                            isComparison = true;
                        }
                    }

                    if (!isComparison)
                    {
                        // Assignment without right-hand side
                        result.AddError(i, lastToken.Column, lastToken.Column + lastToken.Length, "CPD-3407",
                            "incomplete assignment: missing right-hand side");
                    }
                }
            }
        }

        private void ValidateConstReassignment(Stage3Context stage3, LinterResult result)
        {
            // Check = reassignments of const variables/functions
            if (stage3.VariableReassignments != null)
            {
                foreach (var (name, line, column) in stage3.VariableReassignments)
                {
                    var info = stage3.TypeTracker?.GetVariableInfo(name);
                    if (info != null && info.IsConst)
                    {
                        result.AddError(line, column, column + name.Length, "CPD-3409",
                            "cannot reassign constant '" + name + "'");
                    }
                }
            }

            // Check ← outer scope assignments
            if (stage3.OuterScopeAssignments != null)
            {
                foreach (var (name, line, column) in stage3.OuterScopeAssignments)
                {
                    var info = stage3.TypeTracker?.GetVariableInfo(name);
                    if (info == null && !stage3.DefinedVariables.Contains(name))
                    {
                        // ← used on a variable that doesn't exist
                        result.AddError(line, column, column + name.Length, "CPD-3410",
                            "outer scope assignment to undefined variable '" + name + "'");
                    }
                    else if (info != null && info.IsConst)
                    {
                        // ← used on a const variable
                        result.AddError(line, column, column + name.Length, "CPD-3409",
                            "cannot reassign constant '" + name + "'");
                    }
                }
            }
        }

        private static bool IsBinaryOperator(string op)
        {
            // Binary operators that require operands on both sides
            return op == "+" || op == "-" || op == "*" || op == "/" || op == "^" ||
                   op == "÷" || op == "·" || op == "∙" || op == "⋅" ||
                   op == "<" || op == ">" || op == "≤" || op == "≥" || op == "≠" ||
                   op == "&" || op == "∨" || op == "⊕" || op == "\\" || op == "%" ||
                   op == "∧" || op == "←";
        }

        /// <summary>
        /// Checks if a keyword is valid. Includes regular keywords, control block keywords, and end keywords.
        /// </summary>
        private static bool IsValidKeyword(string keyword)
        {
            return CalcpadBuiltIns.Keywords.Contains(keyword) ||
                   CalcpadBuiltIns.ControlBlockKeywords.Contains(keyword) ||
                   CalcpadBuiltIns.EndKeywords.Contains(keyword);
        }
    }
}
