using System.IO;
using System.Linq;

namespace Calcpad.Tests
{
    public class CheckPassTests
    {
        private static (int Line, string Code)[] Check(string code) =>
            new ExpressionParser().Check(code).Select(e => (e.SourceLine, e.Code)).OrderBy(e => e).ToArray();

        [Fact]
        public void StrayElse_DoesNotStopTheCheck()
        {
            Assert.Equal([(1, "CPD-3105"), (2, "CPD-3401"), (3, "CPD-3105")], Check("#else\nx = 1 + * 2\n#end if"));
        }

        [Fact]
        public void UntakenBranch_IsChecked()
        {
            Assert.Equal([(3, "CPD-3407"), (5, "CPD-3101")], Check("x = 1\n#if x > 5\na = 1 +\n#else\nb = (2\n#end if"));
        }

        [Theory]
        [InlineData("c = undefinedvar + 1", 1)]
        [InlineData("Q = 1\nc = q", 2)]
        [InlineData("#if 0\nc = typo + 1\n#end if", 2)]
        [InlineData("f(x) = $Inline{q = 1; q + zz}", 1)]
        [InlineData("#hide zz > 0\n#end hide", 1)]
        [InlineData("a = b + 1\nb = 2", 1)]
        [InlineData("#if zz > 0\n#end if\nzz = 1", 1)]
        [InlineData("#for i = 1 : 2\ny = z\n#loop\nz = 1", 2)]
        public void NameUndefinedWhenRead_IsReported(string code, int line)
        {
            Assert.Equal([(line, "CPD-3301")], Check(code));
        }

        [Theory]
        [InlineData("#for i = 1 : 2\n#if i > 1\ny = z\n#end if\nz = i\n#loop")]
        [InlineData("#while 1\n#if 0\ny = z\n#end if\n#for i = 1 : 2\nz = i\n#loop\n#loop")]
        [InlineData("f(x) = x*k\nk = 2\ny = f(1)")]
        [InlineData("#if 1\nx = 1\n#else\ny = x\n#end if")]
        [InlineData("L = 2m\nA = L*kN/m")]
        [InlineData("f(x; y) = x*y + z\nz = 1")]
        [InlineData("r = $Root{x^2 - 4 @ x = 0 : 5}")]
        [InlineData("s = $Sum{k @ k = 1 : 3}")]
        [InlineData("#for i = 1 : 3\ny = i\n#loop")]
        [InlineData("$Plot{sin(t) @ t = 0 : 1}")]
        [InlineData("$Map{x*y @ x = 0 : 1 & y = 0 : 1}")]
        [InlineData("f(x; z = 1) = x + z\nr = f(1; z = 2)")]
        [InlineData("M = [1; 2|3; 4]\nL = lu(M)\np = ind")]
        [InlineData("#noc\ny = undefinedvar\n#equ")]
        [InlineData("x_min = $Inf{x^2 @ x = -1 : 1}\ny = x_inf")]
        public void NameDefinedWhenRead_IsNotReported(string code)
        {
            Assert.Empty(Check(code));
        }

        [Fact]
        public void ReadTarget_IsDefined()
        {
            Assert.Empty(Check("#read M from data.csv\ny = M"));
        }

        [Fact]
        public void UndefinedName_CoversTheName()
        {
            var error = new ExpressionParser().Check("'Text' c = 2*zz 'more'").Single();
            Assert.Equal((13, 15), (error.StartColumn, error.EndColumn));
        }

        [Fact]
        public void EmptyLoopHeader_DoesNotStopTheCheck()
        {
            Assert.Equal([(1, "CPD-3105"), (1, "CPD-5102"), (3, "CPD-3401")], Check("#while\nw = 1\nq = 1 + * 2"));
        }

        [Fact]
        public void HiddenKeywordLines_AreChecked()
        {
            Assert.Contains((2, "CPD-5121"), Check("#hide\n#for i 1 2\n#loop\n#show"));
        }

        [Theory]
        [InlineData("#loop", 1)]
        [InlineData("#continue", 1)]
        [InlineData("#for i = 1 : 3\n#if i > 1\n#loop\n#end if", 3)]
        [InlineData("#repeat 2\nx = 1", 1)]
        public void LoopStructure_IsChecked(string code, int line)
        {
            Assert.Equal([(line, "CPD-3105")], Check(code));
        }

        [Fact]
        public void TopLevelBreak_IsAllowed()
        {
            Assert.Empty(Check("x = 1\n#break\ny = 2"));
        }

        [Fact]
        public void FunctionBody_IsCheckedOnTheDefinitionLine()
        {
            Assert.Equal([(1, "CPD-3407")], Check("f(x) = x +\ny = 1"));
        }

        [Fact]
        public void PauseAndInput_DoNotStopTheCheck()
        {
            Assert.Equal([(4, "CPD-3401")], Check("x = 1\n#pause\n#input\ny = 1 + * 2"));
        }

        [Fact]
        public void Write_TouchesNoFile()
        {
            var path = Path.Combine(Path.GetTempPath(), $"calcpad-check-{System.Guid.NewGuid():N}.csv");
            Check($"M = [1; 2]\n#write M to {path}");
            Assert.False(File.Exists(path));
        }

        [Theory]
        [InlineData("#for i = 1 : 2\n#if i > 1\nzz ← 2\n#end if\nzz = 1\n#loop")]
        [InlineData("#for i = 1 : 2\n#if i > 1\ny = g(1)\n#end if\ng(x) = x\n#loop")]
        [InlineData("#for i = 1 : 2\ni ← 3\n#loop")]
        public void NameDefinedLaterInLoop_IsNotReported(string code)
        {
            Assert.Empty(Check(code));
        }

        [Theory]
        [InlineData("#for i = 1 : 2\nzz ← 2\n#loop", 2, "CPD-3410")]
        [InlineData("#for i = 1 : 2\ny = nofn(1)\n#loop", 2, "CPD-3305")]
        [InlineData("#if 0\ny = nofn(1)\n#end if", 2, "CPD-3305")]
        [InlineData("#if 0\nπ = 3\n#end if", 2, "CPD-3409")]
        [InlineData("#if 0\ny = 5 qqq\n#end if", 2, "CPD-3310")]
        public void NameErrors_AreReportedOnLinesThatDidNotRun(string code, int line, string expected)
        {
            Assert.Equal([(line, expected)], Check(code));
        }

        [Theory]
        [InlineData("x = (1 + 2", "CPD-3101", 4, 5)]
        [InlineData("x = sin((1 + 2)", "CPD-3101", 7, 8)]
        [InlineData("x = 1 + 2)", "CPD-3102", 9, 10)]
        [InlineData("x = [1; 2", "CPD-3103", 4, 5)]
        [InlineData("x = 1; 2]", "CPD-3104", 8, 9)]
        [InlineData("x = $Root{x - 1 @ x = 0 : 5", "CPD-3105", 4, 10)]
        [InlineData("x = $Root x - 1", "CPD-3106", 4, 9)]
        [InlineData("x = 1*2}", "CPD-3106", 7, 8)]
        [InlineData("x = (1 + [2)]", "CPD-5114", 12, 13)]
        public void BracketErrors_PointAtTheBracket(string code, string expected, int start, int end)
        {
            var error = Assert.Single(new ExpressionParser().Check(code));
            Assert.Equal((expected, start, end), (error.Code, error.StartColumn, error.EndColumn));
        }

        [Fact]
        public void LoopErrors_AreNotCapped()
        {
            var code = string.Join('\n', Enumerable.Range(0, 60).Select(_ => "x = 1 + * 2"));
            Assert.Equal(60, Check(code).Length);
        }
    }
}
