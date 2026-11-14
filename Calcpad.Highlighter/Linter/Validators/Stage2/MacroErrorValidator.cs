using System.Collections.Generic;
using Calcpad.Highlighter.ContentResolution;
using Calcpad.Highlighter.Linter.Helpers;
using Calcpad.Highlighter.Linter.Models;

namespace Calcpad.Highlighter.Linter.Validators.Stage2
{
    /// <summary>Reports MacroParser's own include and macro errors.</summary>
    public class MacroErrorValidator
    {
        public void Validate(Stage2Result stage2, Stage1Context stage1, LinterResult result)
        {
            foreach (var error in stage2.MacroErrors)
            {
                if (error.Line < 0 || error.Line >= stage2.Lines.Count)
                    continue;

                var stage1Line = stage2.SourceMap.GetValueOrDefault(error.Line, error.Line);
                if (error.IsIncluded && stage1Line < stage1.Lines.Count &&
                    stage2.IncludeMap.TryGetValue(error.Line, out var source))
                {
                    // The #include line has none of the included text, so all of it is marked
                    var (s, e) = WholeLine(stage1.Lines[stage1Line]);
                    var included = Diagnostic(error, LineStage.Stage1, stage1Line, s, e, error.Message);
                    SourceMapper.MarkIncluded(included, source, error.StartColumn, error.EndColumn);
                    result.AppendDiagnostic(included);
                    continue;
                }

                if (error.StartColumn >= 0 && error.EndColumn <= stage2.Lines[error.Line].Length &&
                    !stage1.LineContinuationMap.ContainsKey(stage1Line))
                    result.AppendDiagnostic(Diagnostic(error, LineStage.Stage2, error.Line, error.StartColumn, error.EndColumn, error.Message));
                else if (stage1Line < stage1.Lines.Count)
                {
                    var (start, end) = WholeLine(stage1.Lines[stage1Line]);
                    result.AppendDiagnostic(Diagnostic(error, LineStage.Stage1, stage1Line, start, end, error.Message));
                }
            }
        }

        private static LinterDiagnostic Diagnostic(MacroError error, LineStage stage, int line, int start, int end, string message) => new()
        {
            StageLine = line,
            Stage = stage,
            Line = line,
            Column = start,
            EndColumn = end,
            Code = error.Code,
            Message = message,
            Severity = LinterSeverity.Error
        };

        private static (int, int) WholeLine(string text) =>
            (text.Length - text.TrimStart().Length, text.TrimEnd().Length);
    }
}
