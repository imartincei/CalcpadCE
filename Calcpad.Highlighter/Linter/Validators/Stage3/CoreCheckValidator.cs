using System;
using System.Collections.Generic;
using Calcpad.Core;
using Calcpad.Highlighter.ContentResolution;
using Calcpad.Highlighter.Linter.Helpers;
using Calcpad.Highlighter.Linter.Models;

namespace Calcpad.Highlighter.Linter.Validators.Stage3
{
    /// <summary>
    /// Reports Core's check pass. Names and Values errors on lines the last run executed are
    /// left to the run, which knows the order and values.
    /// </summary>
    public class CoreCheckValidator
    {
        public void Validate(Stage3Context stage3, IReadOnlyList<CalcpadError> errors, LinterResult result)
        {
            foreach (var error in errors)
            {
                var line = error.SourceLine - 1;
                if (line < 0 || line >= stage3.Lines.Count)
                    continue;

                if (error.Kind is CalcpadErrorKind.Names or CalcpadErrorKind.Values &&
                    stage3.Runtime?.GetExecution(line) == LineExecution.Executed)
                    continue;

                result.AppendDiagnostic(ToDiagnostic(error, line, stage3, result));
            }
        }

        private static LinterDiagnostic ToDiagnostic(CalcpadError error, int line, Stage3Context stage3, LinterResult result)
        {
            var text = stage3.Lines[line];
            if (stage3.IncludeMap.TryGetValue(line, out var source) && source.Source == "include")
            {
                // The #include line has none of the included text, so all of it is marked
                var stage2Line = stage3.Stage3ToStage2Map.GetValueOrDefault(line, line);
                var stage1Line = result.Stage2Context?.Stage2ToStage1Map.GetValueOrDefault(stage2Line, stage2Line) ?? stage2Line;
                var stage1Lines = result.Stage1Context?.Lines;
                if (stage1Lines != null && stage1Line < stage1Lines.Count)
                    text = stage1Lines[stage1Line];

                var (s, e) = WholeLine(text);
                var message = $"{System.IO.Path.GetFileName(source.SourceFile)} line {source.OriginalLine + 1}: {error.Message}";
                return Diagnostic(LineStage.Stage1, stage1Line, s, e, message);
            }
            if (error.StartColumn >= 0)
            {
                var start = Math.Min(error.StartColumn, text.Length);
                var end = Math.Min(Math.Max(error.EndColumn, error.StartColumn + 1), text.Length);
                return Diagnostic(LineStage.Stage3, line, start, end, error.Message);
            }
            var (ws, we) = WholeLine(text);
            return Diagnostic(LineStage.Stage3, line, ws, we, error.Message);

            LinterDiagnostic Diagnostic(LineStage stage, int stageLine, int start, int end, string message) => new()
            {
                StageLine = stageLine,
                Stage = stage,
                Line = stageLine,
                Column = start,
                EndColumn = end,
                Code = error.Code,
                Message = message,
                Severity = LinterSeverity.Error,
                Origin = DiagnosticOrigin.Check
            };
        }

        private static (int, int) WholeLine(string text) =>
            (text.Length - text.TrimStart().Length, text.TrimEnd().Length);
    }
}
