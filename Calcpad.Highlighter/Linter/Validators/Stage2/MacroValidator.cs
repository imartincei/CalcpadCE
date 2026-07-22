using System;
using System.Collections.Generic;
using Calcpad.Highlighter.Linter.Constants;
using Calcpad.Highlighter.Linter.Helpers;
using Calcpad.Highlighter.Linter.Models;

namespace Calcpad.Highlighter.Linter.Validators.Stage2
{
    public class MacroValidator
    {
        public void Validate(Stage2Context stage2, LinterResult result)
        {
            ValidateDuplicateMacros(stage2, result);
            ValidateMacroDefinitions(stage2, result);
        }

        private void ValidateDuplicateMacros(Stage2Context stage2, LinterResult result)
        {
            foreach (var duplicate in stage2.DuplicateMacros)
            {
                result.AddError(duplicate.DuplicateLineNumber, 0, stage2.Lines[duplicate.DuplicateLineNumber].Length, "CPD-2201",
                    "'" + duplicate.Name + "' was already defined at line " + (duplicate.OriginalLineNumber + 1), LineStage.Stage2);
            }
        }

        private void ValidateMacroDefinitions(Stage2Context stage2, LinterResult result)
        {
            bool inMacroDef = false;
            int macroDefStartLine = -1;
            var controlBlockStack = new Stack<(ControlBlockType type, int lineNumber)>();

            for (int i = 0; i < stage2.Lines.Count; i++)
            {
                var line = stage2.Lines[i];
                var trimmed = line.Trim();

                // Track control blocks (for CPD-2209 warning)
                var blockType = CalcpadBuiltIns.GetBlockType(line);
                if (CalcpadBuiltIns.IsBlockStarter(blockType) && blockType != ControlBlockType.Def)
                {
                    controlBlockStack.Push((blockType, i));
                }
                else if (blockType == ControlBlockType.EndIf || blockType == ControlBlockType.Loop)
                {
                    if (controlBlockStack.Count > 0 && CalcpadBuiltIns.MatchesEnder(controlBlockStack.Peek().type, blockType))
                    {
                        controlBlockStack.Pop();
                    }
                }

                if (blockType == ControlBlockType.Def)
                {
                    // Check for nested macros
                    if (inMacroDef)
                    {
                        result.AddError(i, 0, line.Length, "CPD-2207",
                            "(outer macro started at line " + (macroDefStartLine + 1) + ")", LineStage.Stage2);
                    }

                    // Check if inside control block
                    if (controlBlockStack.Count > 0)
                    {
                        var (outerBlockType, _) = controlBlockStack.Peek();
                        var keyword = CalcpadBuiltIns.GetKeywordString(outerBlockType);
                        result.AddWarning(i, 0, line.Length, "CPD-2209",
                            "(inside " + keyword + " block)", LineStage.Stage2);
                    }

                    // Validate macro syntax
                    ValidateMacroSyntax(line, trimmed, i, result);

                    // Check if inline or multiline
                    var isInline = CalcpadPatterns.InlineMacroDef.IsMatch(trimmed);
                    if (!isInline)
                    {
                        var multilineMatch = CalcpadPatterns.MultilineMacroDef.Match(trimmed);
                        if (multilineMatch.Success)
                        {
                            inMacroDef = true;
                            macroDefStartLine = i;
                        }
                    }
                }
                else if (LineParser.IsEndDefStatement(trimmed))
                {
                    if (!inMacroDef)
                    {
                        result.AddError(i, 0, line.Length, "CPD-2206",
                            "#end def without matching #def", LineStage.Stage2);
                    }
                    inMacroDef = false;
                    macroDefStartLine = -1;
                }
            }

            // Check for unclosed macro at end of file
            if (inMacroDef)
            {
                result.AddError(macroDefStartLine, 0, 999, "CPD-2206",
                    "#def without matching #end def", LineStage.Stage2);
            }
        }

        private void ValidateMacroSyntax(string line, string trimmed, int stage2Line, LinterResult result)
        {
            // Try inline pattern first
            var inlineMatch = CalcpadPatterns.InlineMacroDef.Match(trimmed);
            if (inlineMatch.Success)
            {
                ValidateMacroNameAndParams(line, inlineMatch.Groups[1].Value, inlineMatch.Groups[2].Value, stage2Line, result);
                return;
            }

            // Try multiline pattern
            var multilineMatch = CalcpadPatterns.MultilineMacroDef.Match(trimmed);
            if (multilineMatch.Success)
            {
                ValidateMacroNameAndParams(line, multilineMatch.Groups[1].Value, multilineMatch.Groups[2].Value, stage2Line, result);
                return;
            }

            // Neither matched - check if it's due to invalid characters in macro name
            var looseMatch = CalcpadPatterns.LooseMacroNameExtract.Match(trimmed);
            if (looseMatch.Success)
            {
                var attemptedName = looseMatch.Groups[1].Value;
                if (!CalcpadPatterns.ValidMacroName.IsMatch(attemptedName))
                {
                    result.AddError(stage2Line, 0, line.Length, "CPD-2210",
                        "'" + attemptedName + "'. Macro names can only contain ASCII letters (a-z, A-Z), digits (0-9), and underscores (_).", LineStage.Stage2);
                    return;
                }
            }

            // General malformed syntax
            result.AddError(stage2Line, 0, line.Length, "CPD-2205", "'" + trimmed + "'", LineStage.Stage2);
        }

        private void ValidateMacroNameAndParams(string line, string macroName, string paramsStr, int stage2Line, LinterResult result)
        {
            var startPos = line.AsSpan().IndexOf(macroName.AsSpan(), StringComparison.OrdinalIgnoreCase);
            var col = startPos >= 0 ? startPos : 0;
            var endCol = startPos >= 0 ? startPos + macroName.Length : line.Length;

            // Check macro name ends with $
            if (macroName[^1] != '$')
            {
                result.AddError(stage2Line, col, endCol, "CPD-2202",
                    "'" + macroName + "' should be '" + macroName + "$'", LineStage.Stage2);
            }

            // Check macro name starts with letter
            if (macroName.Length > 0 && !char.IsLetter(macroName[0]) && macroName[0] != '_')
            {
                result.AddError(stage2Line, col, endCol, "CPD-2204", "'" + macroName + "'", LineStage.Stage2);
            }

            // Validate parameters
            if (!string.IsNullOrWhiteSpace(paramsStr))
            {
                var parameters = ParameterParser.ParseParameters(paramsStr); // splits by ';'
                var seenParams = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var param in parameters)
                {
                    // Skip empty params (will be caught elsewhere)
                    if (string.IsNullOrWhiteSpace(param))
                        continue;

                    var paramName = param;

                    // Check for duplicate parameter names
                    if (!seenParams.Add(paramName))
                    {
                        result.AddError(stage2Line, 0, line.Length, "CPD-2212",
                            "'" + paramName + "'", LineStage.Stage2);
                        continue;
                    }

                    // Check for invalid characters in parameter name
                    if (!CalcpadPatterns.ValidMacroParam.IsMatch(paramName))
                    {
                        result.AddError(stage2Line, 0, line.Length, "CPD-2211",
                            "'" + paramName + "'. Macro parameters can only contain ASCII letters (a-z, A-Z), digits (0-9), and underscores (_).", LineStage.Stage2);
                        continue;
                    }

                    // Check param ends with $
                    if (!paramName.EndsWith('$'))
                    {
                        result.AddError(stage2Line, 0, line.Length, "CPD-2203",
                            "'" + paramName + "'", LineStage.Stage2);
                        continue;
                    }

                    // Check param starts with letter
                    if (paramName.Length > 0 && !char.IsLetter(paramName[0]) && paramName[0] != '_')
                    {
                        result.AddError(stage2Line, 0, line.Length, "CPD-2208", "'" + paramName + "'", LineStage.Stage2);
                    }
                }
            }
        }
    }
}
