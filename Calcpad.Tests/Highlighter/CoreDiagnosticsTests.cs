using System.Collections.Generic;
using System.Linq;
using Calcpad.Highlighter.ContentResolution;
using Calcpad.Highlighter.Linter;
using Calcpad.Highlighter.Linter.Models;

namespace Calcpad.Tests.Highlighter
{
    /// <summary>Core's check pass and last run, as the linter reports them.</summary>
    public class CoreDiagnosticsTests
    {
        // Mirrors the backend's debug /convert run.
        private static (RuntimeTrace Trace, IReadOnlyList<CalcpadError> Errors) Run(string source)
        {
            var macroParser = new MacroParser();
            macroParser.Parse(source, out var code, null, 0, true);
            var parser = new ExpressionParser { Settings = new Settings(), Debug = true };
            parser.Parse(code, true, false);
            return (parser.Trace, parser.Errors);
        }

        private static List<LinterDiagnostic> Lint(string content, bool withRun = false, string runContent = null)
        {
            var staged = new ContentResolver().GetStagedContent(content);
            IReadOnlyList<CalcpadError> runErrors = null;
            if (withRun)
            {
                var (trace, errors) = Run(runContent ?? content);
                if (runContent is null)
                    staged = ContentResolver.ApplyRuntimeTrace(staged, trace);
                runErrors = new RunErrors(runContent ?? content, errors).For(content);
            }
            return new CalcpadLinter().Lint(staged, null, runErrors).Diagnostics;
        }

        private static (int Line, string Code)[] Codes(List<LinterDiagnostic> diagnostics) =>
            diagnostics.Where(d => d.Severity == LinterSeverity.Error).Select(d => (d.Line, d.Code)).OrderBy(d => d).ToArray();

        [Fact]
        public void CheckPass_ReportsUntakenBranchesAndStructure()
        {
            Assert.Equal([(0, "CPD-3105"), (2, "CPD-3407"), (4, "CPD-3105")],
                Codes(Lint("#else\n#if 0\n  a = 1 +\n#end if\n#end if")));
        }

        [Fact]
        public void CheckPass_ColumnsPointAtTheExpression()
        {
            var d = Assert.Single(Lint("'Text' a = 1 + * 2 'more'"), d => d.Code == "CPD-3401");
            Assert.Equal((7, 18), (d.Column, d.EndColumn));
        }

        [Fact]
        public void CheckPass_ColumnsMapThroughMacroArguments()
        {
            var d = Assert.Single(Lint("#def m$(x$) = y = x$\nm$(1 + * 2)"), d => d.Code == "CPD-3401");
            Assert.Equal(1, d.Line);
            Assert.True(d.Column >= 3 && d.EndColumn <= 10, $"{d.Column}-{d.EndColumn}");
        }

        [Fact]
        public void MacroErrors_SuppressTheCheckPass()
        {
            var codes = Codes(Lint("#def m$\nm$\nx = 1 + * 2"));
            Assert.DoesNotContain(codes, c => c.Code == "CPD-3401");
        }

        [Fact]
        public void ComplexOnlyErrors_AreLeftToTheRun()
        {
            Assert.Empty(Codes(Lint("z = 3 + 2i")));
        }

        [Fact]
        public void StaticArgumentCheck_HidesCoresGenericCount()
        {
            var codes = Codes(Lint("x = sin(1; 2)"));
            Assert.Contains(codes, c => c.Code == "CPD-3308");
            Assert.DoesNotContain(codes, c => c.Code is "CPD-3302" or "CPD-5110");
        }

        [Fact]
        public void UserFunctionArity_FollowsDefinitionOrder()
        {
            var codes = Codes(Lint("f(x) = x\na = f(1)\nf(x; y) = x + y\nb = f(1; 2)"));
            Assert.Empty(codes);
        }

        [Fact]
        public void RunErrors_AreReportedOnExecutedLines()
        {
            var d = Assert.Single(Lint("a = 1m\nb = a + 1s", withRun: true), d => d.Source == "Calcpad Run");
            Assert.Equal((1, "CPD-5422"), (d.Line, d.Code));
        }

        [Fact]
        public void RunErrors_OnMacroLines_CoverTheCall()
        {
            var d = Assert.Single(Lint("#def m$(x$) = b = x$ + 1s\nm$(1m)", withRun: true), d => d.Source == "Calcpad Run");
            Assert.Equal((1, 0, 6), (d.Line, d.Column, d.EndColumn));
        }

        [Fact]
        public void RunErrors_FollowEditsAboveThem()
        {
            var d = Assert.Single(Lint("x = 1\na = 1m\nb = a + 1s", withRun: true, runContent: "a = 1m\nb = a + 1s"),
                d => d.Source == "Calcpad Run");
            Assert.Equal(2, d.Line);
        }

        [Fact]
        public void RunErrors_BetweenTwoEdits_FollowTheirLine()
        {
            var errors = new RunErrors("a = 1m\nb = a + 1s\nc = 2", Run("a = 1m\nb = a + 1s\nc = 2").Errors)
                .For("x = 0\na = 1m\nb = a + 1s\nc = 3");
            Assert.Equal(3, Assert.Single(errors).SourceLine);
        }

        [Fact]
        public void RunErrors_OnEditedLines_AreDropped()
        {
            Assert.DoesNotContain(Lint("a = 1m\nb = a + 2s", withRun: true, runContent: "a = 1m\nb = a + 1s"),
                d => d.Source == "Calcpad Run");
        }

        [Fact]
        public void UndefinedVariable_BeforeItsDefinition_IsReported()
        {
            var d = Assert.Single(Lint("a = b + 1\nb = 2"), d => d.Code == "CPD-3301");
            Assert.Contains("before its definition on line 2", d.Message);
        }

        [Fact]
        public void UndefinedVariable_WithAnotherCase_SuggestsTheName()
        {
            var d = Assert.Single(Lint("Q = 1\nw = q"), d => d.Code == "CPD-3301");
            Assert.Contains("'Q'", d.Message);
        }

        [Fact]
        public void UndefinedVariable_InAFunctionBody_ResolvesAtTheCall()
        {
            Assert.DoesNotContain(Lint("f(x) = x + k\nk = 2\ny = f(1)"), d => d.Code == "CPD-3301");
        }

        [Fact]
        public void UndefinedVariable_OnExecutedLines_IsLeftToTheRun()
        {
            var diagnostics = Lint("a = b + 1\nb = 2", withRun: true);
            Assert.Equal([(0, "CPD-3301")], Codes(diagnostics));
            Assert.Equal("Calcpad Run", Assert.Single(diagnostics, d => d.Code == "CPD-3301").Source);
        }

        [Fact]
        public void RunErrors_ForOtherContent_KeepOnlyUnchangedValuesErrors()
        {
            var errors = new RunErrors("a = b\nc = 1m + 1s\nd = 1", Run("a = b\nc = 1m + 1s\nd = 1").Errors)
                .For("x = 0\na = b\nc = 1m + 1s\nd = 1");
            var e = Assert.Single(errors);
            Assert.Equal(("CPD-5422", 3, -1), (e.Code, e.SourceLine, e.StartColumn));
        }
    }
}
