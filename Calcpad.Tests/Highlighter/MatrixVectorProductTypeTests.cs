using System.Linq;
using Calcpad.Highlighter.ContentResolution;
using Calcpad.Highlighter.Linter;
using Calcpad.Highlighter.Linter.Models;

namespace Calcpad.Tests.Highlighter
{
    /// <summary>
    /// Core unwraps a matrix·vector product to a vector (IValue.EvaluateOperator), so the type
    /// tracker must infer 'matrix * vector' as a vector — otherwise downstream functions that
    /// expect a vector (e.g. clsolve) raise a spurious CPD-3309.
    /// </summary>
    public class MatrixVectorProductTypeTests
    {
        private static LinterResult Lint(string content)
        {
            var staged = new ContentResolver().GetStagedContent(content);
            var ignoreRegions = new LintIgnoreRegionParser().ExtractRegions(content);
            return new CalcpadLinter().Lint(staged, ignoreRegions);
        }

        private static int TypeWarnings(LinterResult result) =>
            result.Diagnostics.Count(d => d.Code == "CPD-3309");

        [Fact]
        public void MatrixTimesVector_IntoVectorParam_NoTypeWarning()
        {
            var src =
                "X = [1; 2 | 3; 4]\n" +
                "y = [5; 6]\n" +
                "b = X*y\n" +
                "A = copy(X*transp(X); symmetric(n_rows(X)); 1; 1)\n" +
                "a = clsolve(A; b)\n";
            Assert.Equal(0, TypeWarnings(Lint(src)));
        }

        [Fact]
        public void MatrixTimesMatrix_IntoVectorParam_StillWarns()
        {
            // matrix * matrix is a matrix, so passing it where a vector is expected must warn.
            var src =
                "X = [1; 2 | 3; 4]\n" +
                "A = [1; 2 | 3; 4]\n" +
                "M = X*A\n" +
                "r = clsolve(A; M)\n";
            Assert.True(TypeWarnings(Lint(src)) > 0);
        }
    }
}
