using System.IO;
using System.Linq;
using Calcpad.Highlighter.Linter.Models;

namespace Calcpad.Tests.Highlighter
{
    /// <summary>
    /// Columns reported on a macro-expanded line map back through the expansion: text from a call
    /// argument keeps its range, and text from the macro body marks the call and, for the first call, the body.
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

        private LinterDiagnostic LintUndefinedFunction(string source, string name, int line = -1)
        {
            var diagnostic = Lint(source).Diagnostics
                .FirstOrDefault(d => d.Code == "CPD-3305" && d.Message.Contains(name) && (line < 0 || d.Line == line));
            Assert.NotNull(diagnostic);
            return diagnostic;
        }

        [Fact]
        public void InlineMacroCall_HighlightsCallAndBody()
        {
            var lines = new[]
            {
                "#def area$(w$; h$) = w$*h$*zzzMissing(1)",
                "a = 1",
                "b = 2",
                "result = area$(a; b)"
            };

            var source = string.Join('\n', lines);
            var call = LintUndefinedFunction(source, "zzzMissing", 3);
            Assert.Equal("area$(a; b)", lines[3][call.Column..call.EndColumn]);
            Assert.Null(call.AdditionalRanges);
            var related = Assert.Single(call.Related);
            Assert.Equal(0, related.Line);

            var body = LintUndefinedFunction(source, "zzzMissing", 0);
            Assert.Equal("zzzMissing", lines[0][body.Column..body.EndColumn]);
            Assert.Contains("line 4", body.Message);
            Assert.Equal(3, Assert.Single(body.Related).Line);
        }

        [Fact]
        public void LaterMacroCalls_DoNotRepeatTheBodyDiagnostic()
        {
            var source = "#def m$(x$) = x$ + zzzMissing(1)\na = m$(1)\nb = m$(2)";
            var diagnostics = Lint(source).Diagnostics.Where(d => d.Code == "CPD-3305").ToList();
            Assert.Equal([0, 1, 2], diagnostics.Select(d => d.Line).Order());
        }

        [Fact]
        public void MultilineMacroCall_HighlightsCallAndBodyLine()
        {
            var lines = new[]
            {
                "#def block$(x$)",
                "\ty = x$ + zzzMissing(1)",
                "#end def",
                "q = 3",
                "block$(q)"
            };

            var source = string.Join('\n', lines);
            var diagnostic = LintUndefinedFunction(source, "zzzMissing", 4);
            Assert.Equal(0, diagnostic.Column);
            Assert.Equal(lines[4].Length, diagnostic.EndColumn);

            var body = LintUndefinedFunction(source, "zzzMissing", 1);
            Assert.Equal("zzzMissing", lines[1][body.Column..body.EndColumn]);
        }

        [Fact]
        public void MacroCallAcrossLineContinuation_HighlightsTheCallOnEveryLine()
        {
            var lines = new[]
            {
                "#def sum3$(a$; b$; c$) = a$ + b$ + c$ + zzzMissing(1)",
                "p = 1",
                "r = sum3$(p; _",
                "    p; p)"
            };

            var diagnostic = LintUndefinedFunction(string.Join('\n', lines), "zzzMissing", 2);

            Assert.Equal(2, diagnostic.Line);
            Assert.Equal(4, diagnostic.Column);
            // The continuation marker " _" is not part of the merged content
            Assert.Equal(lines[2].Length - 2, diagnostic.EndColumn);

            var additional = Assert.Single(diagnostic.AdditionalRanges);
            Assert.Equal(3, additional.Line);
            Assert.Equal(0, additional.Column);
            Assert.Equal(lines[3].Length, additional.EndColumn);
        }

        [Fact]
        public void UndefinedNameInArgument_HighlightsArgument()
        {
            var lines = new[]
            {
                "#def area$(w$; h$) = w$*h$",
                "result = area$(2; zzzMissing(1))"
            };

            var diagnostic = LintUndefinedFunction(string.Join('\n', lines), "zzzMissing");

            Assert.Equal(1, diagnostic.Line);
            Assert.Equal(18, diagnostic.Column);
            Assert.Equal(28, diagnostic.EndColumn);
        }

        [Theory]
        [InlineData("#def call$(x$) = x$", "z = call$(f(1))", "f(1)")]
        [InlineData("#def call$(x$) = x$", "z = call$(2 + f(1; 2; 3))", "f(1; 2; 3)")]
        public void WrongArgumentCount_HighlightsArgument(string def, string call, string highlighted)
        {
            var source = string.Join('\n', "f(a; b) = a + b", def, call);
            var diagnostic = Assert.Single(Lint(source).Diagnostics, d => d.Code == "CPD-3302");
            Assert.Equal(2, diagnostic.Line);
            Assert.Equal(highlighted, call[diagnostic.Column..diagnostic.EndColumn]);
        }

        [Fact]
        public void WrongArgumentCountInBody_HighlightsCallAndBody()
        {
            var lines = new[] { "f(a; b) = a + b", "#def call$(x$) = f(x$)", "z = call$(1)" };
            var diagnostics = Lint(string.Join('\n', lines)).Diagnostics.Where(d => d.Code == "CPD-3302").ToList();

            var call = Assert.Single(diagnostics, d => d.Line == 2);
            Assert.Equal("call$(1)", lines[2][call.Column..call.EndColumn]);
            var body = Assert.Single(diagnostics, d => d.Line == 1);
            Assert.Equal("f(x$)", lines[1][body.Column..body.EndColumn]);
        }

        [Fact]
        public void UndefinedNameInContinuedArgument_HighlightsArgument()
        {
            var lines = new[]
            {
                "#def sum3$(a$; b$; c$) = a$ + b$ + c$",
                "p = 1",
                "r = sum3$(p; _",
                "    zzzMissing(1); p)"
            };

            var diagnostic = LintUndefinedFunction(string.Join('\n', lines), "zzzMissing");

            Assert.Equal(3, diagnostic.Line);
            Assert.Equal(4, diagnostic.Column);
            Assert.Equal(14, diagnostic.EndColumn);
            Assert.Null(diagnostic.AdditionalRanges);
        }

        [Fact]
        public void LineWithoutMacroExpansion_KeepsTokenRange()
        {
            var source = "a = 1\nb = a + zzzMissing(1)";

            var diagnostic = LintUndefinedFunction(source, "zzzMissing");

            Assert.Equal(1, diagnostic.Line);
            Assert.Equal(8, diagnostic.Column);
            Assert.Equal(18, diagnostic.EndColumn);
        }
    }
}
