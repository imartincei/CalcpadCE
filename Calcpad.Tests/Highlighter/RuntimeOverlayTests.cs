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

        private static LinterDiagnostic[] TypeWarnings(string source, bool withTrace) =>
            new CalcpadLinter().Lint(Resolve(source, withTrace)).Diagnostics
                .Where(d => d.Code == "CPD-3309").ToArray();

        [Fact]
        public void OnlyTheTakenBranch_IsTypeChecked()
        {
            const string source = "a = 5\n#if a > 10\nn = len(a)\n#else\nm = len(a)\n#end if";
            // Static inference can't type a literal, so it has nothing to check.
            Assert.Empty(TypeWarnings(source, withTrace: false));

            var warning = Assert.Single(TypeWarnings(source, withTrace: true));
            Assert.Equal(4, warning.Line);
        }

        [Fact]
        public void Redefinition_IsTypedPerLine()
        {
            const string source = "x = 5\nn = len(x)\nx = [1; 2; 3]\nm = len(x)";
            Assert.Empty(TypeWarnings(source, withTrace: false));

            var warning = Assert.Single(TypeWarnings(source, withTrace: true));
            Assert.Equal(1, warning.Line);
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
