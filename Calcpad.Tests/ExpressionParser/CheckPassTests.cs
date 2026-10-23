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

        [Fact]
        public void UndefinedVariables_AreLeftToTheRun()
        {
            Assert.Empty(Check("c = undefinedvar + 1"));
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

        [Fact]
        public void LoopErrors_AreNotCapped()
        {
            var code = string.Join('\n', Enumerable.Range(0, 60).Select(_ => "x = 1 + * 2"));
            Assert.Equal(60, Check(code).Length);
        }
    }
}
