using System.Collections.Generic;
using Calcpad.Core;
using Calcpad.Highlighter.ContentResolution;
using Calcpad.Highlighter.Linter.Models;

namespace Calcpad.Highlighter.Linter.Validators.Stage3
{
    /// <summary>
    /// Reports the last run's errors. Core numbers them by root document line, and its columns
    /// fit the source only on lines that no macro, include or continuation rewrote.
    /// </summary>
    public class RunErrorValidator
    {
        public void Validate(StagedResolvedContent staged, Stage3Context stage3, IReadOnlyList<CalcpadError> errors, LinterResult result)
        {
            if (errors.Count == 0)
                return;

            var stage3Lines = new Dictionary<int, int>();
            for (var i = 0; i < stage3.Lines.Count; i++)
                foreach (var line in RuntimeOverlay.GetTraceLines(staged, i))
                    stage3Lines.TryAdd(line, i);

            foreach (var error in errors)
            {
                if (!stage3Lines.TryGetValue(error.SourceLine, out var line))
                    continue;

                var e = error;
                if (e.StartColumn >= 0 && !IsPlain(staged, stage3, line))
                    e = new CalcpadError { SourceLine = e.SourceLine, Message = e.Message, Source = e.Source, Code = e.Code, Kind = e.Kind };

                result.AppendDiagnostic(CoreCheckValidator.ToDiagnostic(e, line, stage3, result, DiagnosticOrigin.Run));
            }
        }

        private static bool IsPlain(StagedResolvedContent staged, Stage3Context stage3, int line)
        {
            if (stage3.MacroExpansions.ContainsKey(line) ||
                stage3.IncludeMap.TryGetValue(line, out var source) && source.Source == "include")
                return false;

            var stage1Line = stage3.SourceMap.GetValueOrDefault(line, line);
            return !(staged.Stage1.LineContinuationSegments?.TryGetValue(stage1Line, out var segments) == true && segments.Count > 1);
        }
    }
}
