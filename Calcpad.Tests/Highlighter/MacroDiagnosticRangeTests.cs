using System.IO;
using System.Linq;
using Calcpad.Highlighter.Linter.Models;

namespace Calcpad.Tests.Highlighter
{
    /// <summary>
    /// Columns reported on a macro-expanded line index into the substituted macro body, which
    /// shares no layout with the call site - they used to be applied verbatim to the original
    /// line, underlining unrelated text or running past its end. Such diagnostics now cover the
    /// whole call site line.
    /// </summary>
    public class MacroDiagnosticRangeTests : IClassFixture<HighlighterLinterFixture>
    {
        private readonly HighlighterLinterFixture _fixture;

        public MacroDiagnosticRangeTests(HighlighterLinterFixture fixture)
        {
            _fixture = fixture;
        }

        private LinterResult Lint(string source)
        {
            var path = Path.Combine(Path.GetTempPath(), $"macro_range_{System.Guid.NewGuid():N}.cpd");
            File.WriteAllText(path, source);
            try
            {
                return _fixture.LintFile(path);
            }
            finally
            {
                File.Delete(path);
            }
        }

        private LinterDiagnostic LintUndefined(string source, string name)
        {
            var diagnostic = Lint(source).Diagnostics
                .FirstOrDefault(d => d.Code == "CPD-3301" && d.Message.Contains(name));
            Assert.NotNull(diagnostic);
            return diagnostic;
        }

        [Fact]
        public void InlineMacroCall_HighlightsEntireCallSiteLine()
        {
            var lines = new[]
            {
                "#def area$(w$; h$) = w$*h$*zzzMissing",
                "a = 1",
                "b = 2",
                "result = area$(a; b)"
            };

            var diagnostic = LintUndefined(string.Join('\n', lines), "zzzMissing");

            Assert.Equal(3, diagnostic.Line);
            Assert.Equal(0, diagnostic.Column);
            Assert.Equal(lines[3].Length, diagnostic.EndColumn);
            Assert.Null(diagnostic.AdditionalRanges);
        }

        [Fact]
        public void MultilineMacroCall_HighlightsEntireCallSiteLine()
        {
            var lines = new[]
            {
                "#def block$(x$)",
                "\ty = x$ + zzzMissing",
                "#end def",
                "q = 3",
                "block$(q)"
            };

            var diagnostic = LintUndefined(string.Join('\n', lines), "zzzMissing");

            Assert.Equal(4, diagnostic.Line);
            Assert.Equal(0, diagnostic.Column);
            Assert.Equal(lines[4].Length, diagnostic.EndColumn);
        }

        [Fact]
        public void MacroCallAcrossLineContinuation_HighlightsEveryCallSiteLine()
        {
            var lines = new[]
            {
                "#def sum3$(a$; b$; c$) = a$ + b$ + c$ + zzzMissing",
                "p = 1",
                "r = sum3$(p; _",
                "    p; p)"
            };

            var diagnostic = LintUndefined(string.Join('\n', lines), "zzzMissing");

            Assert.Equal(2, diagnostic.Line);
            Assert.Equal(0, diagnostic.Column);
            // The continuation marker " _" is not part of the merged content
            Assert.Equal(lines[2].Length - 2, diagnostic.EndColumn);

            var additional = Assert.Single(diagnostic.AdditionalRanges);
            Assert.Equal(3, additional.Line);
            Assert.Equal(0, additional.Column);
            Assert.Equal(lines[3].Length, additional.EndColumn);
        }

        [Fact]
        public void LineWithoutMacroExpansion_KeepsTokenRange()
        {
            var source = "a = 1\nb = a + zzzMissing";

            var diagnostic = LintUndefined(source, "zzzMissing");

            Assert.Equal(1, diagnostic.Line);
            Assert.Equal(8, diagnostic.Column);
            Assert.Equal(18, diagnostic.EndColumn);
        }
    }
}
