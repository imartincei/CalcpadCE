using System.Linq;
using Calcpad.Highlighter.ContentResolution;
using Calcpad.Highlighter.Linter;
using Calcpad.Highlighter.Linter.Models;

namespace Calcpad.Tests.Highlighter
{
    /// <summary>
    /// MacroParser's own errors surface as diagnostics at the source columns they came from.
    /// </summary>
    public class MacroParserDiagnosticsTests
    {
        private static LinterDiagnostic Single(string content, string code) =>
            Assert.Single(new CalcpadLinter().Lint(new ContentResolver().GetStagedContent(content)).Diagnostics,
                d => d.Code == code);

        [Fact]
        public void UndefinedMacro_CoversItsName()
        {
            var d = Single("#def m$ = 1\nx = 2 + n$(3)", "CPD-3303");
            Assert.Equal((1, 8, 10), (d.Line, d.Column, d.EndColumn));
            Assert.Equal(LinterSeverity.Error, d.Severity);
        }

        [Fact]
        public void ArgumentCount_CoversTheCall()
        {
            var d = Single("#def m$(a$) = a$\nx = 1 + m$(1; 2)", "CPD-3304");
            Assert.Equal((1, 8, 16), (d.Line, d.Column, d.EndColumn));
        }

        [Fact]
        public void ErrorInMacroBody_CoversTheCallLine()
        {
            const string line = "x = m$";
            var d = Single("#def m$ = n$\n" + line, "CPD-3303");
            Assert.Equal((1, 0, line.Length), (d.Line, d.Column, d.EndColumn));
        }

        [Fact]
        public void ContinuedLine_MapsToTheOriginalLine()
        {
            var d = Single("#def m$(a$) = a$\nx = 1 + _\n  m$(1; 2)", "CPD-3304");
            Assert.Equal((2, 2, 10), (d.Line, d.Column, d.EndColumn));
        }

        [Fact]
        public void DuplicateMacro_IsReportedOnTheDuplicate()
        {
            var d = Single("#def m$ = 1\n#def m$ = 2", "CPD-2201");
            Assert.Equal((1, 5, 7), (d.Line, d.Column, d.EndColumn));
        }

        [Fact]
        public void UndefinedMacro_WithoutAnyMacros_IsStillReported()
        {
            var d = Single("x = n$(3)", "CPD-3303");
            Assert.Equal((0, 4, 6), (d.Line, d.Column, d.EndColumn));
        }
    }
}
