using System.Linq;
using Calcpad.Highlighter.ContentResolution;
using Calcpad.Highlighter.Linter;
using Calcpad.Highlighter.Linter.Models;

namespace Calcpad.Tests.Highlighter
{
    /// <summary>
    /// Core stores user-defined functions/variables case-sensitively (StringComparer.Ordinal),
    /// so 'F' and 'f' are distinct functions. The linter must not conflate them — e.g. calling
    /// 'f' (1 param) must not be checked against 'F' (2 params) and raise CPD-3302.
    /// </summary>
    public class FunctionCaseSensitivityTests
    {
        private static LinterResult Lint(string content)
        {
            var staged = new ContentResolver().GetStagedContent(content);
            var ignoreRegions = new LintIgnoreRegionParser().ExtractRegions(content);
            return new CalcpadLinter().Lint(staged, ignoreRegions);
        }

        private static int ParamCountErrors(LinterResult result) =>
            result.Diagnostics.Count(d => d.Code == "CPD-3302");

        private static int UndefinedErrors(LinterResult result) =>
            result.Diagnostics.Count(d => d.Code == "CPD-3301" || d.Code == "CPD-3305");

        [Fact]
        public void UpperAndLowerCaseFunctions_AreDistinct_NoParamCountError()
        {
            var src =
                "F(a; b) = a + b\n" +
                "f(x) = x^2\n" +
                "r = f(3)\n";

            var result = Lint(src);
            Assert.Equal(0, ParamCountErrors(result));
        }

        [Fact]
        public void CommandBlockFunction_DoesNotShadowLowerCaseFunction()
        {
            // Mirrors the reported pendulum example: F is a command-block function (2 params),
            // f is an ordinary function (1 param). Calling f(θ) must resolve to f, not F.
            var src =
                "F(φ; k) = $Integral{1/sqrt(1 - k^2*sin(θ)^2) @ θ = 0 : φ}\n" +
                "f(θ) = sin(-θ) - θ\n" +
                "r = f(1)\n";

            var result = Lint(src);
            Assert.Equal(0, ParamCountErrors(result));
        }

        [Fact]
        public void CallingWrongCaseFunction_IsFlaggedUndefined()
        {
            // Only 'F' is defined; 'f' is a different, undefined name in Core
            // (Core: "Invalid function: f"). It must not be silently matched to 'F'.
            var src =
                "F(a; b) = a + b\n" +
                "r = f(1)\n";

            var result = Lint(src);
            Assert.True(UndefinedErrors(result) > 0);
            Assert.Equal(0, ParamCountErrors(result));
        }
    }
}
