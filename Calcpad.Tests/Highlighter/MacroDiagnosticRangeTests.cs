using System.IO;
using System.Linq;
using Calcpad.Highlighter.Linter.Models;

namespace Calcpad.Tests.Highlighter
{
    /// <summary>
    /// Columns reported on a macro-expanded line map back through the expansion: text from a call
    /// argument keeps its range, and text from the macro body covers the whole call site line.
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

        private LinterDiagnostic LintUndefinedFunction(string source, string name)
        {
            var diagnostic = Lint(source).Diagnostics
                .FirstOrDefault(d => d.Code == "CPD-3305" && d.Message.Contains(name));
            Assert.NotNull(diagnostic);
            return diagnostic;
        }

        [Fact]
        public void InlineMacroCall_HighlightsEntireCallSiteLine()
        {
            var lines = new[]
            {
                "#def area$(w$; h$) = w$*h$*zzzMissing(1)",
                "a = 1",
                "b = 2",
                "result = area$(a; b)"
            };

            var diagnostic = LintUndefinedFunction(string.Join('\n', lines), "zzzMissing");

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
                "\ty = x$ + zzzMissing(1)",
                "#end def",
                "q = 3",
                "block$(q)"
            };

            var diagnostic = LintUndefinedFunction(string.Join('\n', lines), "zzzMissing");

            Assert.Equal(4, diagnostic.Line);
            Assert.Equal(0, diagnostic.Column);
            Assert.Equal(lines[4].Length, diagnostic.EndColumn);
        }

        [Fact]
        public void MacroCallAcrossLineContinuation_HighlightsEveryCallSiteLine()
        {
            var lines = new[]
            {
                "#def sum3$(a$; b$; c$) = a$ + b$ + c$ + zzzMissing(1)",
                "p = 1",
                "r = sum3$(p; _",
                "    p; p)"
            };

            var diagnostic = LintUndefinedFunction(string.Join('\n', lines), "zzzMissing");

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
        [InlineData("#def call$(x$) = f(x$)", "z = call$(1)", "1")]
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
