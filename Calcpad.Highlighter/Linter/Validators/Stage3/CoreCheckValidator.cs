using System;
using System.Collections.Generic;
using Calcpad.Core;
using Calcpad.Highlighter.ContentResolution;
using Calcpad.Highlighter.Linter.Helpers;
using Calcpad.Highlighter.Linter.Models;

namespace Calcpad.Highlighter.Linter.Validators.Stage3
{
    /// <summary>
    /// Reports Core's check pass, minus errors the last run reported on the same line. Names and
    /// Values errors on lines the run executed without errors are left to it too, since it knows the order and values.
    /// For a body error the run reported at its call, a function's is left to the run, which marks the call
    /// and body, and a macro's keeps only its body diagnostic, as the run cannot point into a macro body.
    /// </summary>
    public class CoreCheckValidator
    {
        public void Validate(Stage3Context stage3, IReadOnlyList<CalcpadError> errors, LinterResult result, Stage2Result stage2 = null)
        {
            foreach (var error in errors)
            {
                var line = error.SourceLine - 1;
                if (line < 0 || line >= stage3.Lines.Count)
                    continue;

                var callLine = error.CallSite?.SourceLine - 1 ?? -1;
                if (callLine >= stage3.Lines.Count || IsIncluded(stage3, callLine))
                    callLine = -1;

                var diagnostic = ToDiagnostic(error, line, stage3, result);
                var isFunctionBody = callLine >= 0 || error.IsFunctionBody;
                var isMacroBody = !isFunctionBody && diagnostic.Stage == LineStage.Stage3 &&
                    BodyDiagnostics.FindBodyText(stage3, stage2, line, diagnostic.Column, diagnostic.EndColumn).Line >= 0;
                var isIncludedMacroBody = !isFunctionBody && !isMacroBody && diagnostic.Stage == LineStage.Stage3 &&
                    BodyDiagnostics.IsIncludedBodyText(stage3, stage2, line, diagnostic.Column, diagnostic.EndColumn);
                if (error.Severity == CalcpadErrorSeverity.Error && stage3.Runtime is { } run)
                {
                    if (isFunctionBody)
                    {
                        if (callLine >= 0 && run.ReportedError(callLine, error.Code))
                            continue;
                    }
                    else if (isMacroBody)
                        diagnostic.IsBodyOnly = run.ReportedError(line, error.Code);
                    else if (isIncludedMacroBody && run.ReportedError(line, error.Code))
                        diagnostic.IsIncludeOnly = true;
                    else if (run.ReportedError(line, error.Code) ||
                        error.Kind is CalcpadErrorKind.Names or CalcpadErrorKind.Values &&
                        run.GetExecution(line) == LineExecution.Executed && !run.ReportedAnyError(line))
                        continue;
                }

                if (callLine >= 0 && diagnostic.Stage is LineStage.Stage3 or LineStage.Stage1)
                    diagnostic = AtCall(diagnostic, error.CallSite, callLine, stage3, result);
                result.AppendDiagnostic(diagnostic);
            }
        }

        private static bool IsIncluded(Stage3Context stage3, int line) =>
            stage3.IncludeMap.TryGetValue(line, out var source) && source.Source == "include";

        // Reports at the call, and returns the diagnostic for the body
        private static LinterDiagnostic AtCall(LinterDiagnostic body, CalcpadCallSite call, int callLine, Stage3Context stage3, LinterResult result)
        {
            var text = stage3.Lines[callLine];
            var (start, end) = call.StartColumn >= 0
                ? (Math.Min(call.StartColumn, text.Length), Math.Min(Math.Max(call.EndColumn, call.StartColumn + 1), text.Length))
                : WholeLine(text);
            var callPosition = SourceMapper.MapPositionToOriginal(callLine, start, end, stage3, result.Stage2Context, result.Stage1Context);
            // An included body is on its #include line
            var bodyPosition = body.Stage == LineStage.Stage1
                ? new MappedPosition { Line = SourceMapper.MapStage1ToOriginal(body.StageLine, result.Stage1Context), Column = body.Column, EndColumn = body.EndColumn }
                : SourceMapper.MapPositionToOriginal(body.StageLine, body.Column, body.EndColumn, stage3, result.Stage2Context, result.Stage1Context);
            var callDiagnostic = new LinterDiagnostic
            {
                Code = body.Code,
                Message = body.Message,
                Severity = body.Severity
            };
            var bodyDiagnostic = BodyDiagnostics.Link(callDiagnostic, callPosition, bodyPosition, call.Function,
                $"At the first call to {call.Function} on line {{0}}.", true);
            if (body.IncludeOrigins is [var origin])
            {
                bodyDiagnostic.IncludeOrigins = [new IncludeOrigin { FilePath = origin.FilePath, Line = origin.Line, Column = origin.Column, EndColumn = origin.EndColumn }];
                bodyDiagnostic.IncludeMessageStart = body.IncludeMessageStart;
            }
            result.AppendDiagnostic(callDiagnostic);
            return bodyDiagnostic;
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
                var included = Diagnostic(LineStage.Stage1, stage1Line, s, e, error.Message);
                var hasColumns = stage3.MacroExpansions?.ContainsKey(line) != true;
                SourceMapper.MarkIncluded(included, source, hasColumns ? error.StartColumn : -1, error.EndColumn);
                return included;
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
                Severity = error.Severity == CalcpadErrorSeverity.Warning ? LinterSeverity.Warning : LinterSeverity.Error
            };
        }

        private static (int, int) WholeLine(string text) =>
            (text.Length - text.TrimStart().Length, text.TrimEnd().Length);
    }
}
