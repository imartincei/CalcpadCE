using System.Linq;

namespace Calcpad.Tests
{
    public class ErrorColumnTests
    {
        private static (int, int) Columns(string code)
        {
            var error = new ExpressionParser().Check(code).Single();
            return (error.StartColumn, error.EndColumn);
        }

        [Theory]
        [InlineData("'Text' a = 1 + * 2 'more' b = 3", 7, 18)]
        [InlineData("    q = 3 foo + 1", 10, 13)]
        [InlineData("u = 5badunit", 5, 12)]
        [InlineData("  w = 1 ¤ 2", 8, 9)]
        [InlineData("#if 1 + * 2\n#end if", 4, 11)]
        public void Columns_PointAtTheError(string code, int start, int end)
        {
            Assert.Equal((start, end), Columns(code));
        }

        [Theory]
        [InlineData("f(x) = $Inline{zz ← 2}", "zz")]
        [InlineData("$Repeat{undefined_outer ← 5 @ k = 1 : 3}", "undefined_outer")]
        [InlineData("#const h = 6\nf(x) = $Inline{x; h ← x + 2}", "h")]
        [InlineData("y = $Block{a = 1; b = nofn(a); a + b}", "nofn")]
        [InlineData("y = $Sum{k*(2 @ k = 1 : 3}", "(")]
        [InlineData("y = $Root{x^2 - 4 = (x @ x = 0 : 5}", "(")]
        [InlineData("y = 1 + $Sum{$Sum{nofn(k) @ k = 1 : 2} @ j = 1 : 3}", "nofn")]
        [InlineData("y = $Find{qq(x) @ x = 0 : 5}", "qq")]
        public void ErrorsInsideSolverBlocks_PointAtTheError(string code, string expected)
        {
            var error = new ExpressionParser().Check(code).Single();
            var line = code.Split('\n')[error.SourceLine - 1];
            Assert.Equal(expected, line[error.StartColumn..error.EndColumn]);
        }

        [Theory]
        [InlineData("f(a; b) = a + b\nz = 2 + f(1) + 3", 8, 12)]
        [InlineData("f(a) = a\nz = 2*f(1; 2)", 6, 13)]
        [InlineData("f(a; b) = a + b\nz = f(1; q = 2)", 4, 15)]
        public void WrongArgumentCount_CoversTheCall(string code, int start, int end)
        {
            var error = new ExpressionParser().Check(code).Single();
            Assert.Equal(code.Contains("q =") ? "CPD-3315" : "CPD-3302", error.Code);
            Assert.Equal((start, end), (error.StartColumn, error.EndColumn));
        }

        [Fact]
        public void Redefinition_ChecksCallsAgainstTheEarlierDefinition()
        {
            Assert.Empty(new ExpressionParser().Check("u(j) = 2*j\na = u(1)\nu(x; y) = x + y\nb = u(1; 2)"));
        }

        [Fact]
        public void ContinuedLine_CoversTheWholeLine()
        {
            Assert.Equal((-1, -1), Columns("x = 1 + _\n* 2"));
        }

        [Fact]
        public void KeywordError_CoversTheWholeLine()
        {
            Assert.Equal((-1, -1), Columns("#for i 1 : 2\n#loop"));
        }
    }
}
