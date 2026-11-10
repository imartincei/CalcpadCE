using System.Linq;
using Calcpad.Highlighter.ContentResolution;
using Calcpad.Highlighter.Linter;
using Calcpad.Highlighter.Linter.Models;

namespace Calcpad.Tests.Highlighter
{
    public class RuntimeOverlayTests
    {
        // Mirrors the backend's debug /convert run.
        private static RuntimeTrace RunCore(string source)
        {
            var macroParser = new MacroParser();
            macroParser.Parse(source, out var code, null, 0, true);
            var parser = new ExpressionParser { Settings = new Settings(), Debug = true };
            parser.Parse(code, true, false);
            return parser.Trace;
        }

        private static StagedResolvedContent Resolve(string source, bool withTrace)
        {
            var staged = new ContentResolver().GetStagedContent(source);
            return withTrace ? ContentResolver.ApplyRuntimeTrace(staged, RunCore(source)) : staged;
        }

        [Theory]
        [InlineData("#def m$(x$) = x$ + zzq\nr = m$(2)", 0, 1)]
        [InlineData("#def m$(x$)\n\ty = x$ + zzq\n#end def\nm$(2)", 1, 3)]
        [InlineData("f(x) = x + zzq\nr = f(2)\nzzq = 1", 0, 1)]
        public void BodyErrorOnExecutedLine_IsKeptWithTheTrace(string source, int bodyLine, int callLine)
        {
            var lines = new CalcpadLinter().Lint(Resolve(source, withTrace: true)).Diagnostics
                .Where(d => d.Code == "CPD-3301").Select(d => d.Line).Order();
            Assert.Equal([bodyLine, callLine], lines);
        }

        [Fact]
        public void UncalledFunctionBodyError_IsKeptWithTheTrace()
        {
            var diagnostics = new CalcpadLinter().Lint(Resolve("f(x) = x + zzq\ny = 1", withTrace: true)).Diagnostics;
            Assert.Single(diagnostics, d => d.Code == "CPD-3301" && d.Line == 0);
        }

        [Fact]
        public void TypeChanges_StartOnTheirAssignmentLines()
        {
            var runtime = Resolve("x = 5\ny = 1\nx = 6\nx = [1; 2]\nn = len(x)", withTrace: true).Runtime;

            Assert.Equal([(0, CalcpadType.Value), (3, CalcpadType.Vector)], runtime.GetTypeChanges("x"));
            Assert.Null(runtime.GetTypeChanges("y"));
        }

        [Fact]
        public void TypeChange_OnContinuedStatement_StartsOnItsFirstLine()
        {
            var runtime = Resolve("x = 5\nx = [1; 2; _\n3; 4]\nn = len(x)", withTrace: true).Runtime;

            Assert.Equal([(0, CalcpadType.Value), (1, CalcpadType.Vector)], runtime.GetTypeChanges("x"));
        }

        [Fact]
        public void FirstDefinition_ComesFromExecutedBranch()
        {
            var staged = Resolve("c = 1\n#if c > 5\ny = 10\n#else\ny = [1; 2]\n#end if", withTrace: true);

            var first = staged.Runtime.GetFirstDefinition("y");
            Assert.Equal(5, first.FirstLine);
            Assert.Equal("[1; 2]", first.FirstExpression);
            Assert.Equal(CalcpadType.Vector, staged.Stage3.TypeTracker.Variables["y"].Type);
            Assert.Equal(LineExecution.NotExecuted, staged.Runtime.GetExecution(2));
            Assert.Equal(LineExecution.Executed, staged.Runtime.GetExecution(4));
        }

        [Fact]
        public void ApplyingATrace_LeavesTheCachedContentUntouched()
        {
            const string source = "c = 1\n#if c > 5\ny = 10\n#else\ny = [1; 2]\n#end if";
            var staged = new ContentResolver().GetStagedContent(source);
            var before = staged.Stage3.TypeTracker.Variables["y"].Type;

            ContentResolver.ApplyRuntimeTrace(staged, RunCore(source));

            Assert.Null(staged.Runtime);
            Assert.Equal(before, staged.Stage3.TypeTracker.Variables["y"].Type);
        }

        [Fact]
        public void ContinuedStatement_MapsToItsLastLine()
        {
            var staged = Resolve("v = [1; _\n2]\nn = len(v)", withTrace: true);
            Assert.Equal(LineExecution.Executed, staged.Runtime.GetExecution(0));
            Assert.Equal(CalcpadType.Vector, staged.Stage3.TypeTracker.GetVariableInfoAt("v", 1).Type);
        }
    }
}
