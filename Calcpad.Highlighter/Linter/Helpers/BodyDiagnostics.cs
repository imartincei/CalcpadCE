using System;
using System.Collections.Generic;
using Calcpad.Highlighter.ContentResolution;
using Calcpad.Highlighter.Linter.Models;

namespace Calcpad.Highlighter.Linter.Helpers
{
    /// <summary>
    /// Splits an error found in a macro or custom function body into one at the call and,
    /// for the first call, one at the body text, each linked to the other.
    /// </summary>
    internal static class BodyDiagnostics
    {
        /// <summary>Moves Stage 3 diagnostics on macro body text to the macro call, adding one in the body for the first call.</summary>
        internal static void MapMacroBodies(LinterResult result, Stage2Result stage2)
        {
            var stage3 = result.Stage3Context;
            if (stage3?.MacroExpansions is not { Count: > 0 } || result.Stage2Context is null || result.Stage1Context is null)
                return;

            var firstCalls = FirstCalls(stage3);
            var bodies = new List<LinterDiagnostic>();
            foreach (var d in result.Diagnostics)
            {
                if (d.Stage != LineStage.Stage3)
                    continue;

                var (bodyLine, bodyStart, bodyEnd) = FindBodyText(stage3, stage2, d.StageLine, d.Column, d.EndColumn);
                if (bodyLine < 0)
                    continue;

                var expansion = stage3.MacroExpansions[d.StageLine];
                var callLine = expansion.CallSiteStage2Line;
                var (from, to) = Clamp(expansion.Origins, d.Column, d.EndColumn);
                var (callStart, callEnd) = FindCallSpan(expansion, expansion.Origins, from, to);
                var call = SourceMapper.MapStage2PositionToOriginal(callLine, callStart, callEnd, result.Stage2Context, result.Stage1Context);
                var body = SourceMapper.MapStage2PositionToOriginal(bodyLine, bodyStart, bodyEnd, result.Stage2Context, result.Stage1Context);
                var name = FindMacroName(stage2, bodyLine) ?? expansion.MacroNames[0];
                var bodyDiagnostic = Link(d, call, body, name, "With the arguments of the call on line {0}.",
                    firstCalls.GetValueOrDefault(bodyLine, -1) == callLine);
                if (bodyDiagnostic is not null)
                    bodies.Add(bodyDiagnostic);
            }

            var seen = new HashSet<(int, int, int, string)>();
            foreach (var d in bodies)
                if (seen.Add((d.Line, d.Column, d.EndColumn, d.Code)))
                    result.AppendDiagnostic(d);
        }

        /// <summary>Stage 2 line and columns of the local macro body text in a range of an expanded line, or line -1.</summary>
        internal static (int Line, int Start, int End) FindBodyText(Stage3Context stage3, Stage2Result stage2, int stage3Line, int column, int endColumn)
        {
            if (stage2 is null ||
                !stage3.MacroExpansions.TryGetValue(stage3Line, out var expansion) ||
                expansion.Origins is not { Count: > 0 } origins ||
                stage3.IncludeMap.GetValueOrDefault(stage3Line) is { Source: not "local" })
                return (-1, -1, -1);

            var (from, to) = Clamp(origins, column, endColumn);
            int bodyLine = -1, bodyStart = int.MaxValue, bodyEnd = -1;
            for (var i = from; i < to; i++)
            {
                var (line, c) = origins[i];
                if (line < 0 || line == expansion.CallSiteStage2Line || bodyLine >= 0 && line != bodyLine)
                    continue;

                bodyLine = line;
                bodyStart = Math.Min(bodyStart, c);
                bodyEnd = Math.Max(bodyEnd, c + 1);
            }
            if (bodyLine < 0 || stage2.IncludeMap.GetValueOrDefault(bodyLine) is { Source: not "local" })
                return (-1, -1, -1);

            return (bodyLine, bodyStart, bodyEnd);
        }

        private static (int, int) Clamp(IReadOnlyList<(int Line, int Column)> origins, int column, int endColumn)
        {
            var from = Math.Clamp(column, 0, origins.Count);
            return (from, Math.Clamp(Math.Max(endColumn, column + 1), from, origins.Count));
        }

        /// <summary>
        /// Moves <paramref name="call"/> to <paramref name="callPosition"/> and links it to <paramref name="bodyPosition"/>.
        /// Returns the diagnostic for the body when <paramref name="addBody"/> is set.
        /// </summary>
        internal static LinterDiagnostic Link(LinterDiagnostic call, MappedPosition callPosition, MappedPosition bodyPosition,
            string name, string bodyNote, bool addBody)
        {
            var message = call.Message;
            call.Stage = LineStage.Original;
            call.Line = callPosition.Line;
            call.Column = callPosition.Column;
            call.EndColumn = callPosition.EndColumn;
            call.AdditionalRanges = ToRanges(callPosition);
            call.Message = $"{message} In the body of {name} on line {bodyPosition.Line + 1}.";
            (call.Related ??= []).Add(Related(bodyPosition, $"Error in the body of {name}"));
            if (!addBody)
                return null;

            return new LinterDiagnostic
            {
                Stage = LineStage.Original,
                Line = bodyPosition.Line,
                Column = bodyPosition.Column,
                EndColumn = bodyPosition.EndColumn,
                AdditionalRanges = ToRanges(bodyPosition),
                Code = call.Code,
                Message = $"{message} {string.Format(bodyNote, callPosition.Line + 1)}",
                Severity = call.Severity,
                Source = call.Source,
                Related = [Related(callPosition, $"Call to {name}")]
            };
        }

        // Per body line, the call site of the first expansion that reaches it
        private static Dictionary<int, int> FirstCalls(Stage3Context stage3)
        {
            var firstCalls = new Dictionary<int, int>();
            var lines = new List<int>(stage3.MacroExpansions.Keys);
            lines.Sort();
            foreach (var line in lines)
            {
                var expansion = stage3.MacroExpansions[line];
                if (expansion.Origins is null)
                    continue;

                foreach (var (origin, _) in expansion.Origins)
                    if (origin >= 0 && origin != expansion.CallSiteStage2Line)
                        firstCalls.TryAdd(origin, expansion.CallSiteStage2Line);
            }
            return firstCalls;
        }

        private static string FindMacroName(Stage2Result stage2, int bodyLine)
        {
            ContentResolution.MacroDefinition found = null;
            foreach (var macro in stage2.MacroDefinitions)
                if (macro.LineNumber <= bodyLine && (found is null || macro.LineNumber > found.LineNumber))
                    found = macro;

            return found?.Name;
        }

        // The innermost macro call around the call site text next to the error, or the whole line
        private static (int, int) FindCallSpan(MacroExpansionInfo expansion, IReadOnlyList<(int Line, int Column)> origins, int from, int to)
        {
            var callLine = expansion.CallSiteStage2Line;
            int before = -1, after = -1;
            for (var i = from - 1; i >= 0 && before < 0; i--)
                if (origins[i].Line == callLine)
                    before = origins[i].Column;
            for (var i = to; i < origins.Count && after < 0; i++)
                if (origins[i].Line == callLine)
                    after = origins[i].Column;

            var spans = new List<(int Start, int End)>();
            foreach (var name in expansion.MacroNames)
                AddCallSpans(expansion.CallSiteLine, name, spans);
            if (spans.Count == 0)
                return (-1, -1);

            return Innermost(spans, before) ?? Innermost(spans, after) ?? spans[0];
        }

        private static (int, int)? Innermost(List<(int Start, int End)> spans, int column)
        {
            (int Start, int End)? best = null;
            foreach (var span in spans)
                if (span.Start <= column && column < span.End &&
                    (best is null || span.End - span.Start < best.Value.End - best.Value.Start))
                    best = span;

            return best;
        }

        private static void AddCallSpans(string line, string name, List<(int, int)> spans)
        {
            for (var i = line.IndexOf(name, StringComparison.Ordinal); i >= 0; i = line.IndexOf(name, i + name.Length, StringComparison.Ordinal))
            {
                if (i > 0 && (char.IsLetterOrDigit(line[i - 1]) || line[i - 1] == '_'))
                    continue;

                var end = i + name.Length;
                var j = end;
                while (j < line.Length && line[j] == ' ')
                    j++;
                if (j < line.Length && line[j] == '(')
                {
                    var depth = 0;
                    for (; j < line.Length; j++)
                    {
                        if (line[j] == '(')
                            depth++;
                        else if (line[j] == ')' && --depth == 0)
                        {
                            end = j + 1;
                            break;
                        }
                    }
                }
                spans.Add((i, end));
            }
        }

        private static DiagnosticRelated Related(MappedPosition position, string message) => new()
        {
            Line = position.Line,
            Column = position.Column,
            EndColumn = position.EndColumn,
            Message = message
        };

        private static List<DiagnosticRange> ToRanges(MappedPosition position)
        {
            if (position.AdditionalRanges is not { Count: > 0 })
                return null;

            return position.AdditionalRanges.ConvertAll(r => new DiagnosticRange { Line = r.Line, Column = r.Column, EndColumn = r.EndColumn });
        }
    }
}
