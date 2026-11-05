using System;
using System.Collections.Generic;
using Calcpad.Highlighter.Linter.Constants;
using Calcpad.Highlighter.Linter.Helpers;
using Calcpad.Highlighter.Linter.Models;

namespace Calcpad.Highlighter.Linter.Validators.Stage2
{
    /// <summary>Macro checks Core's MacroParser doesn't report; its own errors reach Problems from a run.</summary>
    public class MacroValidator
    {
        public void Validate(Stage2Context stage2, LinterResult result)
        {
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
                    // Check if inside control block
                    if (controlBlockStack.Count > 0)
                    {
                        var (outerBlockType, _) = controlBlockStack.Peek();
                        var keyword = CalcpadBuiltIns.GetKeywordString(outerBlockType);
                        result.AddWarning(i, 0, line.Length, "CPD-2209",
                            "(inside " + keyword + " block)", LineStage.Stage2);
                    }

                    ValidateMacroSyntax(line, trimmed, i, result);
                }
            }
        }

        private void ValidateMacroSyntax(string line, string trimmed, int stage2Line, LinterResult result)
        {
            // Try inline pattern first
            var inlineMatch = CalcpadPatterns.InlineMacroDef.Match(trimmed);
            if (inlineMatch.Success)
            {
                ValidateParams(inlineMatch.Groups[2].Value, stage2Line, line, result);
                return;
            }

            // Try multiline pattern
            var multilineMatch = CalcpadPatterns.MultilineMacroDef.Match(trimmed);
            if (multilineMatch.Success)
            {
                ValidateParams(multilineMatch.Groups[2].Value, stage2Line, line, result);
                return;
            }

            // Core reports invalid characters in the name
            var looseMatch = CalcpadPatterns.LooseMacroNameExtract.Match(trimmed);
            if (looseMatch.Success && !CalcpadPatterns.ValidMacroName.IsMatch(looseMatch.Groups[1].Value))
                return;

            // General malformed syntax
            result.AddError(stage2Line, 0, line.Length, "CPD-2205", "'" + trimmed + "'", LineStage.Stage2);
        }

        // Core accepts a parameter without '$', so this is the one parameter check it doesn't repeat
        private void ValidateParams(string paramsStr, int stage2Line, string line, LinterResult result)
        {
            if (string.IsNullOrWhiteSpace(paramsStr))
                return;

            var seenParams = new HashSet<string>(StringComparer.Ordinal);
            foreach (var paramName in ParsingHelpers.ParseParamList(paramsStr).Names)
            {
                if (seenParams.Add(paramName) && CalcpadPatterns.ValidMacroParam.IsMatch(paramName) && !paramName.EndsWith('$'))
                    result.AddError(stage2Line, 0, line.Length, "CPD-2203", "'" + paramName + "'", LineStage.Stage2);
            }
        }
    }
}
