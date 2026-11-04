using System.Linq;

namespace Calcpad.Tests.Highlighter
{
    /// <summary>
    /// Core substitutes macro parameters with an ordinal, case-sensitive replace, so 'A$' and
    /// 'a$' are distinct parameters of the same macro and must not be reported as duplicates.
    /// </summary>
    public class MacroParamCaseSensitivityTests
    {
        private static MacroParser Lint(string content)
        {
            var parser = new MacroParser();
            parser.Parse(content, out _, null, 0, true);
            return parser;
        }

        private static int DuplicateParamErrors(MacroParser parser) =>
            parser.Errors.Count(e => e.Code == "CPD-2212");

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
