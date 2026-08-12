using System.Collections.Generic;
using Calcpad.Highlighter.Linter.Constants;
using Calcpad.Highlighter.Linter.Helpers;
using Calcpad.Highlighter.Linter.Models;
using Calcpad.Highlighter.Tokenizer.Models;

namespace Calcpad.Highlighter.Linter.Validators.Stage3
{
    public class BalanceValidator
    {
        public void Validate(Stage3Context stage3, LinterResult result, TokenizedLineProvider tokenProvider)
        {
            ValidateBrackets(stage3, result, tokenProvider);
            ValidateControlBlocks(stage3, result, tokenProvider);
        }

        private void ValidateBrackets(Stage3Context stage3, LinterResult result, TokenizedLineProvider tokenProvider)
        {
            // Check bracket balance per-line. After Stage 1 resolves explicit line
            // continuations (\) and implicit continuations ({}), each Stage 3 line
            // should have balanced brackets. Unmatched brackets indicate errors.
            for (int i = 0; i < stage3.Lines.Count; i++)
            {
                if (!tokenProvider.IsCpdMode(i)) continue;

                var tokens = tokenProvider.GetTokensForLine(i);

                var parenStack = new Stack<int>();
                var squareStack = new Stack<int>();
                var curlyStack = new Stack<int>();

                foreach (var token in tokens)
                {
                    if (token.Type != TokenType.Bracket)
                        continue;

                    var col = token.Column;
                    var c = token.Text.Length > 0 ? token.Text[0] : '\0';

                    switch (c)
                    {
                        case '(':
                            parenStack.Push(col);
                            break;
                        case ')':
                            if (parenStack.Count == 0)
                                result.AddError(i, col, col + 1, "CPD-3102",
                                    "at column " + (col + 1));
                            else
                                parenStack.Pop();
                            break;
                        case '[':
                            squareStack.Push(col);
                            break;
                        case ']':
                            if (squareStack.Count == 0)
                                result.AddError(i, col, col + 1, "CPD-3104",
                                    "at column " + (col + 1));
                            else
                                squareStack.Pop();
                            break;
                        case '{':
                            curlyStack.Push(col);
                            break;
                        case '}':
                            if (curlyStack.Count == 0)
                                result.AddError(i, col, col + 1, "CPD-3106",
                                    "at column " + (col + 1));
                            else
                                curlyStack.Pop();
                            break;
                    }
                }

                // Report unclosed brackets on this line
                while (parenStack.Count > 0)
                {
                    var col = parenStack.Pop();
                    result.AddError(i, col, col + 1, "CPD-3101",
                        "at column " + (col + 1));
                }

                while (squareStack.Count > 0)
                {
                    var col = squareStack.Pop();
                    result.AddError(i, col, col + 1, "CPD-3103",
                        "at column " + (col + 1));
                }

                while (curlyStack.Count > 0)
                {
                    var col = curlyStack.Pop();
                    result.AddError(i, col, col + 1, "CPD-3105",
                        "at column " + (col + 1));
                }
            }
        }

        private void ValidateControlBlocks(Stage3Context stage3, LinterResult result, TokenizedLineProvider tokenProvider)
        {
            // Store Stage3 line numbers - mapping is done by diagnostic extensions
            var stack = new Stack<(ControlBlockType type, int stage3LineNumber)>();

            for (int i = 0; i < stage3.Lines.Count; i++)
            {
                if (!tokenProvider.IsCpdMode(i)) continue;

                var line = stage3.Lines[i];
                var blockType = CalcpadBuiltIns.GetBlockType(line);

                if (CalcpadBuiltIns.IsBlockStarter(blockType))
                {
                    // For #def, only multiline (without =) goes on the stack
                    if (blockType == ControlBlockType.Def && line.Contains('='))
                        continue;

                    stack.Push((blockType, i));
                }
                else if (blockType == ControlBlockType.EndIf ||
                         blockType == ControlBlockType.Loop ||
                         blockType == ControlBlockType.EndDef)
                {
                    if (stack.Count == 0 || !CalcpadBuiltIns.MatchesEnder(stack.Peek().type, blockType))
                    {
                        // EndDef errors are handled by Stage 2 MacroValidator
                        if (blockType != ControlBlockType.EndDef)
                        {
                            var enderKeyword = CalcpadBuiltIns.GetKeywordString(blockType);
                            var expectedStarters = blockType switch
                            {
                                ControlBlockType.EndIf => "#if",
                                ControlBlockType.Loop => "#repeat, #for, or #while",
                                _ => "matching block"
                            };

                            // Pass Stage3 line index - diagnostic extensions handle mapping
                            result.AddError(i, 0, line.Length, "CPD-3105",
                                enderKeyword + " without matching " + expectedStarters);
                        }
                    }
                    else
                    {
                        stack.Pop();
                    }
                }
            }

            // Report unclosed control blocks - pass Stage3 line index
            while (stack.Count > 0)
            {
                var (blockType, stage3LineNum) = stack.Pop();
                var keyword = CalcpadBuiltIns.GetKeywordString(blockType);
                var expectedEnder = CalcpadBuiltIns.GetExpectedEnderKeyword(blockType);

                result.AddError(stage3LineNum, 0, 999, "CPD-3105",
                    keyword + " without matching " + expectedEnder);
            }
        }
    }
}
