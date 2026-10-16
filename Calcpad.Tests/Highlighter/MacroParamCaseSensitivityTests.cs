using System.Linq;
using Calcpad.Highlighter.ContentResolution;
using Calcpad.Highlighter.Linter;
using Calcpad.Highlighter.Linter.Models;

namespace Calcpad.Tests.Highlighter
{
    /// <summary>
    /// Core substitutes macro parameters with an ordinal, case-sensitive replace, so 'A$' and
    /// 'a$' are distinct parameters of the same macro and must not be reported as duplicates.
    /// </summary>
    public class MacroParamCaseSensitivityTests
    {
        private static LinterResult Lint(string content)
        {
            var staged = new ContentResolver().GetStagedContent(content);
            var ignoreRegions = new LintIgnoreRegionParser().ExtractRegions(content);
            return new CalcpadLinter().Lint(staged, ignoreRegions);
        }

        private static int DuplicateParamErrors(LinterResult result) =>
            result.Diagnostics.Count(d => d.Code == "CPD-2212");

        [Fact]
        public void ParamsDifferingOnlyByCase_AreNotDuplicates()
        {
            var result = Lint("#def sum$(A$; a$) = A$ + a$\n");

            Assert.Equal(0, DuplicateParamErrors(result));
        }

        [Fact]
        public void MultilineParamsDifferingOnlyByCase_AreNotDuplicates()
        {
            var src =
                "#def report$(X$; x$)\n" +
                "X$ = x$\n" +
                "#end def\n";

            var result = Lint(src);

            Assert.Equal(0, DuplicateParamErrors(result));
        }

        [Fact]
        public void IdenticalParams_AreStillDuplicates()
        {
            var result = Lint("#def sum$(a$; a$) = a$\n");

            Assert.Equal(1, DuplicateParamErrors(result));
        }
    }
}
