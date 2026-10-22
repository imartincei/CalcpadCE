using System.Collections.Generic;
using Calcpad.Highlighter.Linter.Constants;
using Calcpad.Highlighter.Linter.Helpers;
using Calcpad.Highlighter.Linter.Models;

namespace Calcpad.Highlighter.Linter.Validators.Stage2
{
    /// <summary>
    /// Reports MacroParser's own errors, plus the cases it accepts silently.
    /// </summary>
    public class MacroValidator
    {
        public void Validate(Stage2Context stage2, LinterResult result)
        {
            foreach (var error in stage2.Errors)
            {
                result.AppendDiagnostic(new LinterDiagnostic
                {
                    StageLine = error.Line,
                    Stage = LineStage.Stage2,
                    Line = error.Line,
                    Column = error.Column,
                    EndColumn = error.EndColumn,
                    Code = error.Code,
                    Message = error.Message,
                    Severity = LinterSeverity.Error
                });
            }
            ValidateParameterNames(stage2, result);
            ValidateDefinitionPlacement(stage2, result);
        }

        private static void ValidateParameterNames(Stage2Context stage2, LinterResult result)
        {
            var errorLines = new HashSet<int>();
            foreach (var error in stage2.Errors)
                errorLines.Add(error.Line);

            foreach (var macro in stage2.MacroDefinitions)
            {
                if (errorLines.Contains(macro.LineNumber))
                    continue;

                foreach (var name in macro.Parameters)
                {
                    if (!name.EndsWith('$'))
                        result.AddError(macro.LineNumber, 0, stage2.Lines[macro.LineNumber].Length, "CPD-2203",
                            "'" + name + "'", LineStage.Stage2);
                }
            }
        }

        // CPD-2209: MacroParser ignores control blocks, so a #def inside one always applies.
        private static void ValidateDefinitionPlacement(Stage2Context stage2, LinterResult result)
        {
            var controlBlockStack = new Stack<ControlBlockType>();
            for (int i = 0; i < stage2.Lines.Count; i++)
            {
                var line = stage2.Lines[i];
                var blockType = CalcpadBuiltIns.GetBlockType(line);
                if (blockType == ControlBlockType.Def)
                {
                    if (controlBlockStack.Count > 0)
                        result.AddWarning(i, 0, line.Length, "CPD-2209",
                            "(inside " + CalcpadBuiltIns.GetKeywordString(controlBlockStack.Peek()) + " block)", LineStage.Stage2);
                }
                else if (CalcpadBuiltIns.IsBlockStarter(blockType))
                {
                    controlBlockStack.Push(blockType);
                }
                else if ((blockType == ControlBlockType.EndIf || blockType == ControlBlockType.Loop) &&
                         controlBlockStack.Count > 0 && CalcpadBuiltIns.MatchesEnder(controlBlockStack.Peek(), blockType))
                {
                    controlBlockStack.Pop();
                }
            }
        }
    }
}
