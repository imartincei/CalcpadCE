using System.Linq;
using Calcpad.Highlighter.ContentResolution;
using Xunit;

namespace Calcpad.Tests.Highlighter
{
    /// <summary>
    /// Guards the definition line-number contract: FunctionsWithParams /
    /// VariablesWithDefinitions / CustomUnits must report ORIGINAL editor line
    /// numbers, not Stage3-resolved ones. The metadata panel keys definitions by
    /// cursor line, so a mismatch (introduced whenever content resolution changes
    /// line counts — macro expansion, includes, hidden regions) makes definitions
    /// resolve to the wrong line or none.
    /// </summary>
    public class DefinitionClassificationTests
    {
        private static Stage3Result Stage3(string content)
            => new ContentResolver().GetStagedContent(content).Stage3;

        [Fact]
        public void MultipleFunctionsPerLine_ClassifyAsFunctions()
        {
            var src =
                "am(u; k) = $Root{F(φ; k) = u @ φ = 0 : 10*π}\n" +
                "sn(u; k) = sin(am(u; k))','cn(u; k) = cos(am(u; k))\n" +
                "dn(u; k) = sqrt(1 - k*sn(u; k)^2)','cd(u; k) = cn(u; k)/dn(u; k)\n";

            var funcs = Stage3(src).FunctionsWithParams.Select(f => f.Name).ToList();
            Assert.Contains("am", funcs);
            Assert.Contains("sn", funcs);
            Assert.Contains("cn", funcs);
            Assert.Contains("dn", funcs);
            Assert.Contains("cd", funcs);
        }

        [Fact]
        public void MacroCallSites_ProduceNoVariableDefinitions()
        {
            var src =
                "#def circle$(x$; y$; style$) = '<circle cx=\"'x$'\" cy=\"'y$'\" r=\"12\" style$/>\n" +
                "R = 10\n" +
                "circle$(0; 0; R)\n" +
                "circle$(1; 1; R)\n";

            var vars = Stage3(src).VariablesWithDefinitions.Select(v => v.Name).ToList();
            Assert.Equal(new[] { "R" }, vars);
        }

        [Fact]
        public void DefinitionLineNumbers_AreOriginalEditorLines_AfterMacroExpansion()
        {
            // A multiline-macro CALL expands to two body lines, shifting every
            // definition after it in Stage3. The reported LineNumber must still
            // point at the original editor line, not the shifted Stage3 line.
            var src =
                "#def emit$(v$)\n" +   // 0
                "\tv$\n" +             // 1
                "\tv$\n" +             // 2
                "#end def\n" +         // 3
                "p = 1\n" +            // 4
                "emit$(p)\n" +         // 5  (expands -> shifts lines below in Stage3)
                "f(x) = x + p','g(x) = x - p\n" + // 6  two functions
                "q = 2\n";             // 7

            var lines = src.Replace("\r\n", "\n").Split('\n');
            var s3 = Stage3(src);

            foreach (var f in s3.FunctionsWithParams)
                Assert.Contains(f.Name + "(", lines[f.LineNumber]);
            foreach (var v in s3.VariablesWithDefinitions)
                Assert.StartsWith(v.Name + " ", lines[v.LineNumber].TrimStart());

            Assert.Equal(6, s3.FunctionsWithParams.Single(f => f.Name == "f").LineNumber);
            Assert.Equal(6, s3.FunctionsWithParams.Single(f => f.Name == "g").LineNumber);
            Assert.Equal(7, s3.VariablesWithDefinitions.Single(v => v.Name == "q").LineNumber);
        }
    }
}
