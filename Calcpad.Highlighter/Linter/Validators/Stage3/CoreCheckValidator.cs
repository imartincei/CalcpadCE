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

                result.AppendDiagnostic(ToDiagnostic(error, line, stage3, result, DiagnosticOrigin.Check));
            }
        }

        /// <summary>A Stage 3 diagnostic for a Core error, covering the whole line when Core gave no columns.</summary>
        internal static LinterDiagnostic ToDiagnostic(CalcpadError error, int line, Stage3Context stage3, LinterResult result, DiagnosticOrigin origin)
        {
            var text = stage3.Lines[line];
            int start, end;
            var message = error.Message;
            if (stage3.IncludeMap.TryGetValue(line, out var source) && source.Source == "include")
            {
                // An #include line has none of the included text, so the whole line is marked
                var stage1Line = stage3.SourceMap.GetValueOrDefault(line, line);
                var stage1Lines = result.Stage1Context?.Lines;
                text = stage1Lines != null && stage1Line < stage1Lines.Count ? stage1Lines[stage1Line] : text;
                (start, end) = WholeLine(text);
                message = $"{System.IO.Path.GetFileName(source.SourceFile)} line {source.OriginalLine + 1}: {message}";
                return Diagnostic(LineStage.Stage1, stage1Line);
            }
            if (error.StartColumn >= 0)
            {
                start = Math.Min(error.StartColumn, text.Length);
                end = Math.Min(Math.Max(error.EndColumn, error.StartColumn + 1), text.Length);
                return Diagnostic(LineStage.Stage3, line);
            }
            // Columns of a macro call's whole expansion would project onto its arguments only
            var callLine = stage3.SourceMap.GetValueOrDefault(line, line);
            if (stage3.MacroExpansions.ContainsKey(line) && result.Stage1Context is { } stage1 &&
                callLine < stage1.Lines.Count &&
                !(stage1.LineContinuationSegments.TryGetValue(callLine, out var segments) && segments.Count > 1))
            {
                (start, end) = WholeLine(stage1.Lines[callLine]);
                return Diagnostic(LineStage.Stage1, callLine);
            }
            (start, end) = WholeLine(text);
            return Diagnostic(LineStage.Stage3, line);

            LinterDiagnostic Diagnostic(LineStage stage, int stageLine) => new()
            {
                StageLine = stageLine,
                Stage = stage,
                Line = stageLine,
                Column = start,
                EndColumn = end,
                Code = error.Code,
                Message = message,
                Severity = LinterSeverity.Error,
                Origin = origin,
                Source = origin == DiagnosticOrigin.Run ? "Calcpad Run" : "Calcpad Linter"
            };
        }

        private static (int, int) WholeLine(string text) =>
            (text.Length - text.TrimStart().Length, text.TrimEnd().Length);
    }
}
