using System.Linq;

namespace Calcpad.Tests
{
    /// <summary>A run error raised inside custom functions names them and their definition lines.</summary>
    public class RunFunctionFrameTests
    {
        private static CalcpadError Run(string code)
        {
            var parser = new ExpressionParser { Debug = true };
            parser.Parse(code, true, false);
            return parser.Errors.Single();
        }

        [Fact]
        public void ErrorInBody_NamesTheFunction()
        {
            var error = Run("f(x) = x + 1m\na = f(2s)");
            Assert.Equal(2, error.SourceLine);
            Assert.Equal([new CalcpadFunctionFrame("f", 1)], error.Functions);
        }

        [Fact]
        public void NestedCall_ListsInnermostFirst()
        {
            var error = Run("f(x) = x + 1m\ng(x) = 2*f(x)\nb = g(3s)");
            Assert.Equal([new CalcpadFunctionFrame("f", 1), new CalcpadFunctionFrame("g", 2)], error.Functions);
        }

        [Fact]
        public void ContinuedDefinition_UsesItsFirstLine()
        {
            var error = Run("h(x) = $Inline{ _\nq = x; \nq + 1m}\nc = h(2s)");
            Assert.Equal([new CalcpadFunctionFrame("h", 1)], error.Functions);
        }

        [Fact]
        public void ErrorOutsideFunctions_HasNoFrames()
        {
            Assert.Null(Run("a = 2s + 1m").Functions);
        }
    }
}
