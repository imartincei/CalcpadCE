using System.Collections.Generic;
using System.IO;
using System.Linq;
using Calcpad.Highlighter.Linter.Constants;
using Calcpad.Highlighter.Linter.Models;

namespace Calcpad.Tests.Highlighter
{
    /// <summary>
    /// Every errors/*.cpd sample declares its diagnostics with <c>'expect:</c> markers
    /// (see <see cref="ErrorExpectations"/>). Files under errors/includes are only include targets.
    /// </summary>
    public class ComprehensiveErrorTests : IClassFixture<HighlighterLinterFixture>
    {
        private const string IncludesFolder = "includes";
        private const string UntakenFolder = "untaken";
        private const int MinCasesPerCode = 2;

        // Only a run reports these, or the check pass has no line that reaches them
        private static readonly HashSet<string> UncoveredCodes =
        [
            "CPD-2200", // Fallback for an uncoded MacroParser exception
            "CPD-5101", "CPD-5103", "CPD-5104", "CPD-5105", "CPD-5106", "CPD-5107",
            "CPD-5111", "CPD-5117", "CPD-5118",
            "CPD-5301", "CPD-5302", "CPD-5303", "CPD-5304",
        ];

        private static readonly string[] RunOnlyPrefixes = ["CPD-54", "CPD-55", "CPD-56", "CPD-57", "CPD-59"];

        private readonly HighlighterLinterFixture _fixture;

        public ComprehensiveErrorTests(HighlighterLinterFixture fixture)
        {
            _fixture = fixture;
        }

        public static TheoryData<string> ErrorFiles()
        {
            var data = new TheoryData<string>();
            foreach (var path in SamplePaths(new HighlighterLinterFixture().ErrorsDir))
                data.Add(path);
            return data;
        }

        private static IEnumerable<string> SamplePaths(string errorsDir) =>
            Directory.GetFiles(errorsDir, "*.cpd", SearchOption.AllDirectories)
                .Select(p => Path.GetRelativePath(errorsDir, p).Replace('\\', '/'))
                .Where(p => !p.StartsWith(IncludesFolder + "/"))
                .Order();

        private static string[] ReadLines(string fullPath) =>
            File.ReadAllText(fullPath).Split('\n').Select(l => l.TrimEnd('\r')).ToArray();

        [Theory]
        [MemberData(nameof(ErrorFiles))]
        public void ErrorSample_ReportsExactlyItsExpectedDiagnostics(string relativePath)
        {
            var fullPath = Path.Combine(_fixture.ErrorsDir, relativePath);
            var lines = ReadLines(fullPath);
            var result = _fixture.LintFile(fullPath);

            Assert.True(ErrorExpectations.Parse(lines).Count > 0,
                $"'{relativePath}' has no 'expect: markers.");
            Assert.True(result.Diagnostics.Any(d => d.Severity == LinterSeverity.Error),
                $"'{relativePath}' should report at least one Error-level diagnostic.");

            var problems = ErrorExpectations.Check(lines, result.Diagnostics);
            Assert.True(problems.Count == 0,
                $"'{relativePath}' does not match its markers:\n  " + string.Join("\n  ", problems));
        }

        public static TheoryData<string> UntakenFiles()
        {
            var data = new TheoryData<string>();
            foreach (var path in SamplePaths(new HighlighterLinterFixture().ErrorsDir).Where(p => p.StartsWith(UntakenFolder + "/")))
                data.Add(path);
            return data;
        }

        // The check pass is the only source for lines the run skipped, so each marker must stay visible after a run
        [Theory]
        [MemberData(nameof(UntakenFiles))]
        public void UntakenSample_KeepsItsErrorsAfterARun(string relativePath)
        {
            var fullPath = Path.Combine(_fixture.ErrorsDir, relativePath);
            var lines = ReadLines(fullPath);
            var (result, runErrors, notRunLines) = _fixture.LintFileAfterRun(fullPath);

            var shown = ErrorExpectations.WithRunErrors(lines, result.Diagnostics, runErrors);
            var problems = ErrorExpectations.Check(lines, shown);
            // A body marker is untaken through the call it links to
            problems.AddRange(ErrorExpectations.Parse(lines)
                .Where(e => !notRunLines.Contains(e.Line) && !(e.LinkLine is { } link && notRunLines.Contains(link)))
                .Select(e => $"Line {e.Line + 1}: {e.Code} is marked on a line the run executed"));

            Assert.True(problems.Count == 0,
                $"'{relativePath}' after a run:\n  " + string.Join("\n  ", problems));
        }

        [Fact]
        public void ErrorSamples_CoverEveryStaticCodeAtLeastTwice()
        {
            var counts = SamplePaths(_fixture.ErrorsDir)
                .SelectMany(p => ErrorExpectations.Parse(ReadLines(Path.Combine(_fixture.ErrorsDir, p))))
                .GroupBy(e => e.Code)
                .ToDictionary(g => g.Key, g => g.Count());

            var required = ErrorCodes.Descriptions.Keys
                .Where(c => !UncoveredCodes.Contains(c) && !RunOnlyPrefixes.Any(c.StartsWith))
                .Order()
                .ToList();

            var missing = required.Where(c => counts.GetValueOrDefault(c) < MinCasesPerCode)
                .Select(c => $"{c} ({counts.GetValueOrDefault(c)})")
                .ToList();
            Assert.True(missing.Count == 0,
                $"Codes with fewer than {MinCasesPerCode} cases in errors/: {string.Join(", ", missing)}");

            var nowCovered = UncoveredCodes.Where(counts.ContainsKey).Order().ToList();
            Assert.True(nowCovered.Count == 0,
                $"Remove these from UncoveredCodes, samples now cover them: {string.Join(", ", nowCovered)}");
        }

        [Fact]
        public void ErrorExpectations_ParsesMarkerTargetsAndSpans()
        {
            string[] lines =
            [
                "'expect: CPD-3301[a], CPD-3305",
                "'expect+2: CPD-3104[]]",
                "x = a",
                "y = 1 + _",
                "2]",
            ];

            var expectations = ErrorExpectations.Parse(lines);

            Assert.Equal(
                [
                    new ErrorExpectation(2, "CPD-3301", "a", 0),
                    new ErrorExpectation(2, "CPD-3305", null, 0),
                    new ErrorExpectation(4, "CPD-3104", "]", 1),
                ],
                expectations);
        }

        [Fact]
        public void ErrorExpectations_ParsesLinksAndEscapedDollars()
        {
            string[] lines =
            [
                @"'expect: CPD-3301[f(1)]->-2[qq], CPD-3302->+1, CPD-3303[m\$(1)]->+3[a\$]",
                "x = f(1)",
            ];

            Assert.Equal(
                [
                    new ErrorExpectation(1, "CPD-3301", "f(1)", 0, -1, "qq"),
                    new ErrorExpectation(1, "CPD-3302", null, 0, 2),
                    new ErrorExpectation(1, "CPD-3303", "m$(1)", 0, 4, "a$"),
                ],
                ErrorExpectations.Parse(lines));
        }

        private static LinterDiagnostic Linked(int line, int column, int end, string message, params DiagnosticRelated[] related) => new()
        {
            Line = line, Column = column, EndColumn = end, Code = "CPD-3301",
            Severity = LinterSeverity.Error, Message = message, Related = [.. related]
        };

        private static DiagnosticRelated To(int line, int column, int end, string message) =>
            new() { Line = line, Column = column, EndColumn = end, Message = message };

        [Fact]
        public void ErrorExpectations_RequiresTheLinkAMarkerNames()
        {
            string[] lines = ["f(x) = x + qq", "'expect: CPD-3301[f(1)]", "a = f(1)"];
            var call = Linked(2, 4, 8, "In the body of f on line 1.", To(0, 11, 13, "Error in the body of f"));
            var body = Linked(0, 11, 13, "At the first call to f on line 3.", To(2, 4, 8, "Call to f"));

            var problems = ErrorExpectations.Check(lines, [call, body]);

            Assert.Contains(problems, p => p.StartsWith("Line 3: expected CPD-3301[f(1)] from"));
            Assert.Contains(problems, p => p.StartsWith("Line 1: unexpected CPD-3301"));
        }

        [Fact]
        public void ErrorExpectations_RejectsBrokenLinks()
        {
            string[] lines = ["f(x) = x + qq", "a = f(1)", "b = f(2)"];
            var dangling = Linked(1, 4, 8, "In the body of f on line 1.", To(0, 7, 13, "Error in the body of f"));
            var oneWay = Linked(0, 11, 13, "At the first call to f on line 3.", To(2, 4, 8, "Call to f"));
            var unlinkedCall = Linked(2, 4, 8, "Undefined.");
            var wrongLine = Linked(0, 11, 13, "At the first call to f on line 9.", To(1, 4, 8, "Call to f"));

            var problems = ErrorExpectations.CheckLinks(lines, [dangling, oneWay, unlinkedCall, wrongLine]);

            Assert.Contains(problems, p => p.StartsWith("Line 2: CPD-3301 [f(1)] links to line 1[x + qq], where no CPD-3301"));
            Assert.Contains(problems, p => p.StartsWith("Line 1: CPD-3301 [qq] links to line 3[f(2)], which does not link back"));
            Assert.Contains(problems, p => p.StartsWith("Line 1: CPD-3301 [qq] links to line 2[f(1)], but its message does not name that line"));
        }
    }
}
