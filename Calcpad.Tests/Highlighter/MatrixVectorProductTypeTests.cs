using Calcpad.Highlighter.ContentResolution;
using Calcpad.Highlighter.Linter.Models;

namespace Calcpad.Tests.Highlighter
{
    /// <summary>
    /// Core unwraps a matrix·vector product to a vector (IValue.EvaluateOperator), so the type
    /// tracker must infer 'matrix * vector' as a vector.
    /// </summary>
    public class MatrixVectorProductTypeTests
    {
        private static CalcpadType TypeOf(string content, string name) =>
            new ContentResolver().GetStagedContent(content).Stage3.TypeTracker.GetVariableInfo(name).Type;

        [Fact]
        public void MatrixTimesVector_IsVector()
        {
            Assert.Equal(CalcpadType.Vector, TypeOf("X = [1; 2 | 3; 4]\ny = [5; 6]\nb = X*y\n", "b"));
        }

        [Fact]
        public void MatrixTimesMatrix_IsMatrix()
        {
            Assert.Equal(CalcpadType.Matrix, TypeOf("X = [1; 2 | 3; 4]\nA = [1; 2 | 3; 4]\nM = X*A\n", "M"));
        }
    }
}
