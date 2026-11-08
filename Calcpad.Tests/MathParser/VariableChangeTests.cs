namespace Calcpad.Tests
{
    public class VariableChangeTests
    {
        [Fact]
        [Trait("Category", "VariableChange")]
        public void CacheClearsWhenNewObjectIsMutatedThroughAlias()
        {
            var calc = new TestCalc(new());
            Assert.Equal(5d, calc.Run(["v = [1; 2]", "f(x) = x*v.1", "f(1)", "w = [5; 6]", "v = w", "f(1)"]));
            Assert.Equal(10d, calc.Run(["fill(w; 10)", "f(1)"]));
        }

        [Fact]
        [Trait("Category", "VariableChange")]
        public void CacheClearsAfterSameObjectIsAssignedTwice()
        {
            var calc = new TestCalc(new());
            Assert.Equal(5d, calc.Run(["v = [1; 2]", "f(x) = x*v.1", "f(1)", "w = [5; 6]", "v = w", "v = w", "f(1)"]));
            Assert.Equal(10d, calc.Run(["fill(w; 10)", "f(1)"]));
        }

        [Fact]
        [Trait("Category", "VariableChange")]
        public void CacheClearsForMatrixMutatedThroughAlias()
        {
            var calc = new TestCalc(new());
            Assert.Equal(3d, calc.Run(["M = [1; 2 | 3; 4]", "f(x) = x*M.(2; 1)", "N = M", "M = N", "f(1)"]));
            Assert.Equal(30d, calc.Run(["mfill(N; 30)", "f(1)"]));
        }

        [Fact]
        [Trait("Category", "VariableChange")]
        public void ParameterReassignedToItself()
        {
            var calc = new TestCalc(new());
            Assert.Equal(10d, calc.Run(["B = [1; 2]", "g(m) = $Inline{m = m; m.2 = 9; m.1 + m.2}", "g(B)"]));
            Assert.Equal(9d, calc.Run("B.2"));
        }

        [Fact]
        [Trait("Category", "VariableChange")]
        public void HelperFillsMatrix()
        {
            var calc = new TestCalc(new());
            calc.Run([
                "msetH(i; j; m; val) = $Inline{helperM = m; helperM.(i; j) = val; m = helperM}",
                "A = matrix(3; 4)",
                "$Repeat{$Repeat{msetH(i; j; A; 10*i + j) @ j = 1 : 4} @ i = 1 : 3}"
            ]);
            for (var i = 1; i <= 3; ++i)
                for (var j = 1; j <= 4; ++j)
                    Assert.Equal(10d * i + j, calc.Run($"A.({i}; {j})"));
        }
    }
}
