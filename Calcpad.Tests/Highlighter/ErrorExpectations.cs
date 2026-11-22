using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Calcpad.Highlighter.Linter.Models;

namespace Calcpad.Tests.Highlighter
{
    /// <summary>
    /// A diagnostic an error sample expects: its 0-based line, code and, optionally, the underlined text,
    /// and the line and text it links to.
    /// </summary>
    public sealed record ErrorExpectation(int Line, string Code, string Span, int MarkerLine, int? LinkLine = null, string LinkSpan = null);

    /// <summary>
    /// Reads <c>'expect: CPD-3301[name], CPD-3305</c> markers from error samples. A marker applies
    /// to the next line that isn't a marker, and <c>'expect+N:</c> to N lines below that one.
    /// <c>CPD-3301[f(1)]-&gt;-2[qq]</c> expects a link to the text qq two lines up; without one, the diagnostic must have no links.
    /// Write $ as \$ in spans, since macro names expand in comments too.
    /// </summary>
    public static partial class ErrorExpectations
    {
        [GeneratedRegex(@"^\s*'expect(?:\+(\d+))?:(.*)$")]
        private static partial Regex MarkerRegex();

        // A span ends at the ] before a comma, a link or the line end, so it may hold brackets
        [GeneratedRegex(@"(CPD-\d{4})(?:\[(.*?)\](?=\s*(?:,|->|$)))?(?:\s*->\s*([+-]\d+)(?:\[(.*?)\](?=\s*(?:,|$)))?)?")]
        private static partial Regex ItemRegex();

        public static bool IsMarker(string line) => MarkerRegex().IsMatch(line);

        public static List<ErrorExpectation> Parse(IReadOnlyList<string> lines)
        {
            var expectations = new List<ErrorExpectation>();
            for (var i = 0; i < lines.Count; i++)
            {
                var match = MarkerRegex().Match(lines[i]);
                if (!match.Success)
                    continue;

                var target = i + 1;
                while (target < lines.Count && IsMarker(lines[target]))
                    target++;
                if (match.Groups[1].Success)
                    target += int.Parse(match.Groups[1].Value);

                foreach (Match item in ItemRegex().Matches(match.Groups[2].Value))
                    expectations.Add(new ErrorExpectation(target, item.Groups[1].Value,
                        Unescape(item.Groups[2]), i,
                        item.Groups[3].Success ? target + int.Parse(item.Groups[3].Value) : null,
                        Unescape(item.Groups[4])));
            }
            return expectations;
        }

        private static string Unescape(Group span) => span.Success ? span.Value.Replace("\\$", "$") : null;

        /// <summary>
        /// Each expectation needs its own diagnostic, and every Error or Warning needs an expectation.
        /// Information diagnostics may be expected but are otherwise ignored. Every link must also lead
        /// to a diagnostic with the same code, as <see cref="CheckLinks"/> describes.
        /// </summary>
        public static List<string> Check(IReadOnlyList<string> lines, IReadOnlyList<LinterDiagnostic> diagnostics)
        {
            var problems = new List<string>();
            var expectations = Parse(lines);
            var unmatched = diagnostics.ToList();

            // Expectations with a span claim their diagnostic first
            foreach (var e in expectations.OrderBy(e => e.Span is null).ThenBy(e => e.LinkSpan is null))
            {
                var found = unmatched.FirstOrDefault(d => d.Line == e.Line && d.Code == e.Code &&
                    (e.Span is null || SpanText(lines, d) == e.Span) && LinkMatches(lines, e, d));
                if (found is not null)
                {
                    unmatched.Remove(found);
                    continue;
                }

                var sameCode = diagnostics.Where(d => d.Line == e.Line && d.Code == e.Code).ToList();
                var detail = sameCode.Count > 0
                    ? $" (it underlines {string.Join(", ", sameCode.Select(d => $"[{SpanText(lines, d)}]{Links(lines, d)}"))})"
                    : "";
                problems.Add($"Line {e.Line + 1}: expected {e.Code}{(e.Span is null ? "" : $"[{e.Span}]")}" +
                    (e.LinkLine is { } link ? $" linked to line {link + 1}{(e.LinkSpan is null ? "" : $"[{e.LinkSpan}]")}" : "") +
                    $" from the marker on line {e.MarkerLine + 1}{detail}");
            }

            foreach (var d in unmatched.Where(d => d.Severity != LinterSeverity.Information))
                problems.Add($"Line {d.Line + 1}: unexpected {d.Code} {d.Severity} [{SpanText(lines, d)}]{Links(lines, d)} {d.Message}");

            problems.AddRange(CheckLinks(lines, diagnostics));
            return problems;
        }

        private static bool LinkMatches(IReadOnlyList<string> lines, ErrorExpectation e, LinterDiagnostic d) =>
            e.LinkLine is not { } line
                ? d.Related is not { Count: > 0 }
                : d.Related?.Exists(r => r.Line == line && (e.LinkSpan is null || SpanText(lines, r) == e.LinkSpan)) == true;

        private static string Links(IReadOnlyList<string> lines, LinterDiagnostic d) =>
            d.Related is { Count: > 0 } related
                ? string.Concat(related.Select(r => $" linked to line {r.Line + 1}[{SpanText(lines, r)}]"))
                : "";

        /// <summary>
        /// Each link leads to a diagnostic with the same code on exactly the linked text, and the
        /// message names the linked line. The call a body links to must link back to it.
        /// </summary>
        public static List<string> CheckLinks(IReadOnlyList<string> lines, IReadOnlyList<LinterDiagnostic> diagnostics)
        {
            var problems = new List<string>();
            foreach (var d in diagnostics)
                foreach (var r in d.Related ?? [])
                {
                    var where = $"Line {d.Line + 1}: {d.Code} [{SpanText(lines, d)}] links to line {r.Line + 1}[{SpanText(lines, r)}]";
                    var target = diagnostics.FirstOrDefault(t => t != d && t.Code == d.Code &&
                        t.Line == r.Line && t.Column == r.Column && t.EndColumn == r.EndColumn);
                    if (target is null)
                        problems.Add($"{where}, where no {d.Code} is reported");
                    else if (r.Message.StartsWith("Call to") && target.Related?.Exists(b =>
                        b.Line == d.Line && b.Column == d.Column && b.EndColumn == d.EndColumn) != true)
                        problems.Add($"{where}, which does not link back to it");
                    if (!d.Message.Contains($"line {r.Line + 1}"))
                        problems.Add($"{where}, but its message does not name that line: {d.Message}");
                }
            return problems;
        }

        /// <summary>Adds the run's errors the lint doesn't already report, since the editor shows both.</summary>
        public static List<LinterDiagnostic> WithRunErrors(IReadOnlyList<string> lines,
            IReadOnlyList<LinterDiagnostic> diagnostics, IEnumerable<Calcpad.Core.CalcpadError> runErrors)
        {
            var merged = diagnostics.ToList();
            foreach (var e in runErrors)
            {
                var line = e.SourceLine - 1;
                if (line < 0 || line >= lines.Count || merged.Any(d => d.Line == line && d.Code == e.Code))
                    continue;

                var text = lines[line];
                var (start, end) = e.StartColumn >= 0
                    ? (e.StartColumn, e.EndColumn)
                    : (text.Length - text.TrimStart().Length, text.TrimEnd().Length);
                merged.Add(new LinterDiagnostic
                {
                    Line = line,
                    Column = start,
                    EndColumn = end,
                    Code = e.Code,
                    Message = "(run) " + e.Message,
                    Severity = e.Severity == Calcpad.Core.CalcpadErrorSeverity.Warning
                        ? LinterSeverity.Warning : LinterSeverity.Error
                });
            }
            return merged;
        }

        /// <summary>Source lines (0-based) that some stage 3 line the run didn't execute maps to.</summary>
        public static HashSet<int> NotRunLines(Calcpad.Highlighter.ContentResolution.StagedResolvedContent staged, int lineCount)
        {
            var stage1Map = staged.Stage1.SourceMap;
            var lines = new HashSet<int>();
            for (var s3 = 0; s3 < staged.Stage3.Lines.Count; s3++)
            {
                if (staged.Runtime.GetExecution(s3) == Calcpad.Highlighter.ContentResolution.LineExecution.Executed)
                    continue;

                var s2 = staged.Stage3.SourceMap.GetValueOrDefault(s3, s3);
                var s1 = staged.Stage2.SourceMap.GetValueOrDefault(s2, s2);
                // A stage 1 line spans its continued source lines
                var first = stage1Map.GetValueOrDefault(s1, s1);
                var next = stage1Map.TryGetValue(s1 + 1, out var n) ? n : lineCount;
                for (var line = first; line < System.Math.Max(next, first + 1); line++)
                    lines.Add(line);
            }
            return lines;
        }

        public static string SpanText(IReadOnlyList<string> lines, LinterDiagnostic d) =>
            SpanText(lines, d.Line, d.Column, d.EndColumn);

        public static string SpanText(IReadOnlyList<string> lines, DiagnosticRange r) =>
            SpanText(lines, r.Line, r.Column, r.EndColumn);

        private static string SpanText(IReadOnlyList<string> lines, int line, int column, int endColumn)
        {
            if (line < 0 || line >= lines.Count)
                return "";
            var text = lines[line];
            var start = System.Math.Clamp(column, 0, text.Length);
            var end = System.Math.Clamp(endColumn, start, text.Length);
            return text[start..end];
        }
    }
}
