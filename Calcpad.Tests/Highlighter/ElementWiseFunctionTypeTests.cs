using System.Linq;
using Calcpad.Highlighter.ContentResolution;
using Calcpad.Highlighter.Linter;
using Calcpad.Highlighter.Linter.Models;

namespace Calcpad.Tests.Highlighter
{
    /// <summary>
    /// Element-wise built-in functions (sin, sqrt, floor, ...) map over vectors and
    /// matrices in Core, so passing a vector/matrix where the signature declares a
    /// scalar must NOT raise CPD-3309 ('expects scalar but got vector/matrix').
    /// </summary>
    public class ElementWiseFunctionTypeTests
    {
        private static LinterResult Lint(string content)
        {
            var staged = new ContentResolver().GetStagedContent(content);
            var ignoreRegions = new LintIgnoreRegionParser().ExtractRegions(content);
            return new CalcpadLinter().Lint(staged, ignoreRegions);
        }

        private static int TypeWarnings(LinterResult result) =>
            result.Diagnostics.Count(d => d.Code == "CPD-3309");

        [Theory]
        [InlineData("sin")]
        [InlineData("cos")]
        [InlineData("sqrt")]
        [InlineData("ln")]
        [InlineData("exp")]
        [InlineData("floor")]
        [InlineData("abs")]
        [InlineData("sign")]
        public void ElementWiseFunction_AcceptsMatrix_NoTypeWarning(string func)
        {
            var result = Lint($"M = [1; 2; 3 | 4; 5; 6]\nr = {func}(M)\n");
            Assert.Equal(0, TypeWarnings(result));
        }

        [Fact]
        public void ElementWiseFunction_AcceptsVectorLiteral_NoTypeWarning()
        {
            var result = Lint("r = sin([1; 2; 3])\n");
            Assert.Equal(0, TypeWarnings(result));
        }

        [Fact]
        public void NonElementWiseFunction_StillWarnsOnShapeMismatch()
        {
            // n_rows expects a matrix and is NOT element-wise, so passing a scalar is a
            // real mismatch that must still warn.
            var result = Lint("r = n_rows(5)\n");
            Assert.True(TypeWarnings(result) > 0);
        }

        [Theory]
        [InlineData("augment(v; v)")]
        [InlineData("stack(v; v)")]
        [InlineData("hprod(v; v)")]
        [InlineData("transp(v)")]
        [InlineData("row(v; 1)")]
        [InlineData("col(v; 1)")]
        [InlineData("n_rows(v)")]
        public void MatrixFunction_AcceptsVector_NoTypeWarning(string call)
        {
            // Core coerces a vector into an n×1 column matrix (IValue.AsMatrix), so matrix
            // functions accept vectors without a type mismatch.
            var result = Lint($"v = [1; 2; 3]\nr = {call}\n");
            Assert.Equal(0, TypeWarnings(result));
        }

        [Theory]
        [InlineData("take")]
        [InlineData("line")]
        [InlineData("spline")]
        public void Interpolation_AcceptsIndexAndMatrix_NoTypeWarning(string func)
        {
            // With one index and a matrix, Core linearizes the matrix into a vector, so the
            // (index; matrix) overload must not raise a type mismatch.
            var result = Lint($"M = [1; 2; 3 | 4; 5; 6]\nr = {func}(2; M)\n");
            Assert.Equal(0, TypeWarnings(result));
        }

        [Theory]
        [InlineData("gcd")]
        [InlineData("lcm")]
        public void MultiFunction_AcceptsVectorAndMatrix_NoTypeWarning(string func)
        {
            // gcd/lcm flatten vector/matrix arguments element-wise (like sum/min/max), so
            // passing vectors or matrices must not raise a type mismatch.
            var result = Lint($"v = [12; 18]\nM = [24; 30 | 36; 42]\nr = {func}(v; M; 6)\n");
            Assert.Equal(0, TypeWarnings(result));
        }
    }
}
