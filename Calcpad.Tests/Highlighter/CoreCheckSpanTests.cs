using System.Linq;
using Calcpad.Highlighter.ContentResolution;
using Calcpad.Highlighter.Linter;
using Calcpad.Highlighter.Linter.Models;

namespace Calcpad.Tests.Highlighter
{
    public class CoreCheckSpanTests
    {
        private static RuntimeTrace RunCore(string source)
        {
            new MacroParser().Parse(source, out var code, null, 0, true);
            var parser = new ExpressionParser { Settings = new Settings(), Debug = true };
            parser.Parse(code, true, false);
            return parser.Trace;
        }

        private static LinterResult Lint(string source, bool withTrace = false)
        {
            var staged = new ContentResolver().GetStagedContent(source);
            if (withTrace)
                staged = ContentResolver.ApplyRuntimeTrace(staged, RunCore(source));
            return new CalcpadLinter().Lint(staged);
        }

        private static string Text(string source, LinterDiagnostic d) =>
            source.Split('\n')[d.Line][d.Column..d.EndColumn];

        [Theory]
        [InlineData("x = 1 + _\n  qq*2", "CPD-3301", 1, "qq")]
        [InlineData("y = 1 + * 2 + _\n  3", "CPD-3401", 0, "+ *")]
        [InlineData("y = 1 + _\n  2 + * 3", "CPD-3401", 1, "+ *")]
        [InlineData("x = 1\n$Plot{zz(x) @ x = 0 : 1}", "CPD-3305", 1, "zz")]
        [InlineData("x = 1\n$Map{zz(x; y) @ x = 0 : 1 & y = 0 : 1}", "CPD-5110", 1, "zz(x; y)")]
        public void CoreErrors_PointAtTheirTokens(string source, string code, int line, string expected)
        {
            var d = Lint(source).Diagnostics.Single(d => d.Code == code);
            Assert.Equal(line, d.Line);
            Assert.Equal(expected, Text(source, d));
        }

        [Theory]
        [InlineData("a = sin(1; 2)", "CPD-3308", "sin(1; 2)")]
        [InlineData("a = atan2(1)", "CPD-3307", "atan2(1)")]
        [InlineData("a = sin()", "CPD-3307", "sin()")]
        [InlineData("a = max()", "CPD-3307", "max()")]
        [InlineData("a = 1 + _\n  sin(1; 2)", "CPD-3308", "sin(1; 2)")]
        [InlineData("a = max(1;; 2)", "CPD-3311", ";;")]
        [InlineData("a = sin(; 1)", "CPD-3311", "(;")]
        [InlineData("f(x) = x\na = f(1; )", "CPD-3311", "; )")]
        [InlineData("g(x) = $Inline{a = 1; #if x}", "CPD-3405", "#")]
        [InlineData("a = $Block{b = 2; c = #b}", "CPD-3405", "#")]
        [InlineData("x = 1\nsin(x) = x", "CPD-3203", "sin")]
        public void FormerStaticCodes_ComeOnlyFromCore(string source, string code, string expected)
        {
            var line = source.Split('\n').Length - 1;
            var errors = Lint(source).Diagnostics.Where(d => d.Severity == LinterSeverity.Error).ToList();
            var d = Assert.Single(errors);
            Assert.Equal(code, d.Code);
            Assert.Equal(line, d.Line);
            Assert.Equal(expected, Text(source, d));
        }

        [Theory]
        [InlineData("a = max(1)")]
        [InlineData("v = [1; 2]\na = take(v)")]
        [InlineData("a = round(1.5)")]
        [InlineData("a = [1;; 2]")]
        [InlineData("#noc\nsin(2*x) = 2*sin(x)*cos(x)")]
        public void ValidCalls_GiveNoArgumentErrors(string source) =>
            Assert.DoesNotContain(Lint(source).Diagnostics, d => d.Code is "CPD-3307" or "CPD-3308" or "CPD-3311" or "CPD-3203");

        [Fact]
        public void TwoUndefinedNamesOnALine_AreBothReported()
        {
            const string source = "x = 1\n$Plot{x^2 + qq & 2*bb @ x = 0 : 1}";
            Assert.Equal(["qq", "bb"], Lint(source).Diagnostics.Where(d => d.Code == "CPD-3301").Select(d => Text(source, d)));
        }

        [Fact]
        public void BareUiKeyword_IsNotAnInvalidKeyword() =>
            Assert.DoesNotContain(Lint("#UI").Diagnostics, d => d.Code == "CPD-3403");

        [Theory]
        [InlineData("#UI {\"type\": \"nope\"} x = 1\nx", "CPD-3415")]
        [InlineData("#UI {\"type\": 3} x = 1\nx", "CPD-3415")]
        [InlineData("#UI x = 1 + 2\nx", "CPD-3415")]
        [InlineData("#UI {\"type\": \"entry\"} 2", "CPD-3415")]
        [InlineData("#settings {\"decimals\": -3}", "CPD-3413")]
        [InlineData("#settings {bad}", "CPD-3413")]
        public void DirectiveErrors_ComeFromTheCheckUntilARun_ThenOnlyFromTheRun(string source, string code)
        {
            var before = Lint(source).Diagnostics.Where(d => d.Code == code).ToList();
            Assert.Equal(LinterSeverity.Error, Assert.Single(before).Severity);

            Assert.DoesNotContain(Lint(source, withTrace: true).Diagnostics, d => d.Code == code);
        }

        [Theory]
        [InlineData("a = 1)", "CPD-3102")]
        [InlineData("#if 1\nz = 1", "CPD-3105")]
        [InlineData("a = sin(1; 2)", "CPD-3308")]
        [InlineData("#def m$(x$; x$) = x$\na = 1", "CPD-2212")]
        public void ErrorsTheRunReports_AreNotRepeatedByTheCheck(string source, string code)
        {
            Assert.Single(Lint(source).Diagnostics, d => d.Code == code);
            Assert.DoesNotContain(Lint(source, withTrace: true).Diagnostics, d => d.Code == code);
        }

        [Theory]
        [InlineData("#settings {\"decimals\": \"x\"}", "A #settings value has the wrong type.")]
        [InlineData("#settings {bad}", "Invalid JSON in #settings.")]
        public void SettingsErrors_PointAtTheJson(string source, string message)
        {
            var d = Assert.Single(Lint(source).Diagnostics, d => d.Code == "CPD-3413");
            Assert.Equal(source[10..], Text(source, d));
            Assert.Equal(message, d.Message);
        }

        [Theory]
        [InlineData("#UI {\"zzz\": 1, \"type\": \"nope\"} x = 1\nx", "CPD-3415")]
        [InlineData("#settings {\"zzz\": 1, \"decimals\": -3}", "CPD-3413")]
        public void UnknownKey_AndCoreError_AreBothReported(string source, string code)
        {
            var messages = Lint(source).Diagnostics.Where(d => d.Code == code).Select(d => d.Message).ToList();
            Assert.Equal(2, messages.Count);
            Assert.Single(messages, m => m.Contains("'zzz'"));
        }

        [Theory]
        [InlineData("#UI {\"zzz\": 1} x = 1\nx", "CPD-3415")]
        [InlineData("#settings {\"zzz\": 1}", "CPD-3413")]
        public void UnknownKeys_StayAfterARun(string source, string code)
        {
            var d = Assert.Single(Lint(source, withTrace: true).Diagnostics, d => d.Code == code);
            Assert.Contains("'zzz'", d.Message);
            Assert.Equal(LinterSeverity.Warning, d.Severity);
        }

        [Fact]
        public void UiErrorsInAnUntakenBranch_ComeOnlyFromTheCheck()
        {
            const string source = "#if 0\n#UI x = 1 + 2\n#end if";
            Assert.Single(Lint(source, withTrace: true).Diagnostics, d => d.Code == "CPD-3415");

            var parser = new ExpressionParser { Settings = new Settings(), Debug = true };
            parser.Parse(source, true, false);
            Assert.Empty(parser.Errors);
        }
    }
}
