using System.IO;
using System.Linq;
using Calcpad.Highlighter.ContentResolution;
using Calcpad.Highlighter.Linter;
using Calcpad.Highlighter.Linter.Models;

namespace Calcpad.Tests.Highlighter
{
    public class MacroErrorDiagnosticsTests
    {
        private static LinterDiagnostic[] Lint(string content, string path = null) =>
            [.. new CalcpadLinter().Lint(new ContentResolver().GetStagedContent(content, path)).Diagnostics
                .Where(d => d.Code != "CPD-3312")];

        private static string Marked(string content, LinterDiagnostic d) =>
            content.Split('\n')[d.Line][d.Column..d.EndColumn];

        [Theory]
        [InlineData("#def m$(a$) = a$\nx = 1 + m$(1; 2) + 3", "CPD-3304", "m$(1; 2)")]
        [InlineData("#def m$(a$; b$; c$ = 1) = a$ + b$ + c$\nr = m$(1)", "CPD-3304", "m$(1)")]
        [InlineData("#def m$(a$) = a$\nx = 1 + q$ + 2", "CPD-3303", "q$")]
        [InlineData("#def m$(a$) = a$\nx = m$(b$ = 1)", "CPD-3314", "m$(b$ = 1)")]
        [InlineData("x = q$ + 1", "CPD-3303", "q$")]
        [InlineData("x = 1 +", "CPD-3407", "+")]
        public void CallErrors_PointAtTheCall(string content, string code, string marked)
        {
            var d = Assert.Single(Lint(content));
            Assert.Equal((code, marked), (d.Code, Marked(content, d)));
        }

        [Fact]
        public void DefinitionErrors_AreReportedOnTheirLines()
        {
            var d = Lint("#def m$(a$; a$) = a$\n#def n$(a$ = 1; b$) = b$\n#end def\n#def k$\nx = 1");
            Assert.Equal([(0, "CPD-2212"), (1, "CPD-2213"), (2, "CPD-2206"), (3, "CPD-2206")],
                d.Select(x => (x.Line, x.Code)).OrderBy(x => x));
        }

        [Fact]
        public void IncludedErrors_AreReportedOnTheIncludeLine()
        {
            var dir = Directory.CreateTempSubdirectory();
            try
            {
                var included = Path.Combine(dir.FullName, "inc.cpd");
                File.WriteAllText(included, "a = 1\n#def k$\nb = 2\n");
                var d = Assert.Single(Lint($"y = 2\n#include {included}", Path.Combine(dir.FullName, "main.cpd")));
                Assert.Equal((1, "CPD-2206"), (d.Line, d.Code));
                Assert.StartsWith("inc.cpd line 2: ", d.Message);
            }
            finally
            {
                dir.Delete(true);
            }
        }

        [Fact]
        public void MissingInclude_IsReportedWithoutAFilePrefix()
        {
            var dir = Directory.CreateTempSubdirectory();
            try
            {
                var content = "#include missing.cpd\ny = 2";
                var d = Assert.Single(Lint(content, Path.Combine(dir.FullName, "main.cpd")));
                Assert.Equal(("CPD-1104", "File not found."), (d.Code, d.Message));
                Assert.Equal("#include missing.cpd", Marked(content, d));
            }
            finally
            {
                dir.Delete(true);
            }
        }
    }
}
