using System.Collections.Generic;
using System.Linq;
using Calcpad.Highlighter.ContentResolution;
using Calcpad.Highlighter.Linter;
using Calcpad.Highlighter.Linter.Models;

namespace Calcpad.Tests.Highlighter
{
    public class KeywordArgumentLinterTests
    {
        private static (LinterResult Result, StagedResolvedContent Staged) Lint(string content)
        {
            var staged = new ContentResolver().GetStagedContent(content, new Dictionary<string, string>());
            var regions = new LintIgnoreRegionParser().ExtractRegions(content);
            return (new CalcpadLinter().Lint(staged, regions), staged);
        }

        private static string[] Codes(LinterResult result) =>
            result.Diagnostics.Select(d => d.Code).ToArray();

        [Fact]
        public void FunctionDefaults_NoParameterCountError()
        {
            var (result, _) = Lint("f(x; y = 2; z = 3) = x + y + z\nr = f(1)\ns = f(1; z = 9)\nt = f(z = 9; x = 1)\nu = r + s + t\n");
            Assert.DoesNotContain("CPD-3302", Codes(result));
        }

        [Fact]
        public void FunctionCall_TooFewArguments_StillReported()
        {
            var (result, _) = Lint("f(x; y; z = 3) = x + y + z\nr = f(1)\n");
            Assert.Contains("CPD-3302", Codes(result));
        }

        [Fact]
        public void FunctionCall_UnknownKeywordArgument_IsReported()
        {
            var (result, _) = Lint("f(x; y = 2) = x + y\nr = f(1; q = 3)\n");
            Assert.Contains("CPD-3315", Codes(result));
        }

        [Fact]
        public void FunctionDefinition_RequiredAfterOptional_IsReported()
        {
            var (result, _) = Lint("f(x = 1; y) = x + y\n");
            Assert.Contains("CPD-3207", Codes(result));
        }

        [Fact]
        public void MacroDefaults_ExpandUsingDefaults()
        {
            var (result, staged) = Lint("#def m$(a$; b$ = 10; c$ = 5) = a$ + b$ + c$\nr = m$(1)\ns = m$(1; c$ = 7)\n");
            Assert.Contains("r = 1 + 10 + 5", staged.Stage3.Lines);
            Assert.Contains("s = 1 + 10 + 7", staged.Stage3.Lines);
            Assert.DoesNotContain("CPD-3304", Codes(result));
            Assert.DoesNotContain("CPD-2211", Codes(result));
        }

        [Fact]
        public void MacroCall_UnknownKeywordArgument_IsReported()
        {
            var (result, _) = Lint("#def m$(a$; b$ = 1) = a$ + b$\nr = m$(1; z$ = 2)\n");
            Assert.Contains("CPD-3314", Codes(result));
            Assert.DoesNotContain("CPD-3303", Codes(result));
        }

        [Fact]
        public void MacroArgumentNamingAnExistingMacro_StaysPositional()
        {
            var (result, staged) = Lint("#def y$ = q\n#def emit$(lhs$) = [lhs$]\nr = emit$(y$ = 5)\n");
            Assert.Contains("r = [q = 5]", staged.Stage3.Lines);
            Assert.DoesNotContain("CPD-3314", Codes(result));
        }

        [Fact]
        public void MacroDefinition_RequiredAfterOptional_IsReported()
        {
            var (result, _) = Lint("#def m$(a$ = 1; b$) = a$ + b$\nr = m$(1; 2)\n");
            Assert.Contains("CPD-2213", Codes(result));
        }

        [Fact]
        public void MacroCall_TooFewArguments_StillReported()
        {
            var (result, _) = Lint("#def m$(a$; b$; c$ = 1) = a$ + b$ + c$\nr = m$(1)\n");
            Assert.Contains("CPD-3304", Codes(result));
        }

        [Fact]
        public void FunctionDefaults_AreTrackedForHover()
        {
            var (_, staged) = Lint("f(x; y = 2kg) = x + y\nr = f(1kg)\n");
            var def = staged.Stage3.FunctionsWithParams.Single(f => f.Name == "f");
            Assert.Equal(new List<string> { "x", "y" }, def.Params);
            Assert.Equal(new List<string> { null, "2kg" }, def.Defaults);
            Assert.Equal(1, staged.Stage3.UserDefinedFunctions["f"].RequiredParamCount);
        }

        [Fact]
        public void FunctionCall_EqualityComparison_IsNotKeywordArgument()
        {
            var (result, _) = Lint("f(x; y = 2) = x + y\na = 1\nb = 1\nr = f(a == b)\n");
            Assert.DoesNotContain("CPD-3315", Codes(result));
        }

        [Theory]
        [InlineData("r = f(1)")]
        [InlineData("r = f(1; z = 9)")]
        [InlineData("r = f(z = 9; x = 1)")]
        [InlineData("r = f(1; 2; 3)")]
        public void CommandBlockFunction_CallUsingDefaults_ValidatesBody(string call)
        {
            var (result, _) = Lint("f(x; y = 2; z = 3) = $Inline{a = x + y + z + q}\n" + call + "\n");
            Assert.Single(result.Diagnostics, d => d.Code == "CPD-3301" && d.Line == 1);
        }

        [Fact]
        public void CommandBlockFunction_TooFewArguments_SkipsBodyValidation()
        {
            var (result, _) = Lint("f(x; y; z = 3) = $Block{a = x + y + z + q}\nr = f(1)\n");
            Assert.Contains("CPD-3302", Codes(result));
            Assert.DoesNotContain("CPD-3301", Codes(result));
        }

        [Theory]
        [InlineData("b = 1\nf(x; y = b) = x + y\nr = f(1)\n")]
        [InlineData("f(x; y = b) = x + y\nb = 7\nr = f(1)\n")]
        [InlineData("b = 1\nf(x; y = sqr(b)) = x + y\nr = f(1)\n")]
        [InlineData("g(t) = t\nb = 1\nf(x; y = g(b)) = x + y\nr = f(1)\n")]
        [InlineData("f(b; h = b) = b*h\nr = f(3)\n")]
        public void DefaultReferencingVariable_KeepsFunctionDefined(string content)
        {
            var (result, _) = Lint(content);
            Assert.DoesNotContain("CPD-3301", Codes(result));
            Assert.DoesNotContain("CPD-3305", Codes(result));
        }
    }
}
