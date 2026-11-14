using System.Linq;
using Calcpad.Highlighter.ContentResolution;
using Calcpad.Highlighter.Linter;

namespace Calcpad.Tests.Highlighter
{
    /// <summary>The check pass starts in the editor's complex mode and follows #settings from there.</summary>
    public class CoreCheckModeTests
    {
        private static int[] ErrorLines(string source, bool isComplex) =>
            [.. new CalcpadLinter().Lint(new ContentResolver().GetStagedContent(source), null, isComplex)
                .Diagnostics.Where(d => d.Severity == Calcpad.Highlighter.Linter.Models.LinterSeverity.Error)
                .Select(d => d.Line).Distinct().Order()];

        [Theory]
        [InlineData("a = hp([1; 2])", false, new int[0])]
        [InlineData("a = hp([1; 2])", true, new[] { 0 })]
        [InlineData("z = 1i", false, new[] { 0 })]
        [InlineData("z = 1i", true, new int[0])]
        public void StartsInTheEditorsMode(string source, bool isComplex, int[] expected) =>
            Assert.Equal(expected, ErrorLines(source, isComplex));

        [Theory]
        [InlineData(false, new[] { 1, 4 })]
        [InlineData(true, new[] { 1, 4 })]
        public void FollowsSettingsMidFile(bool isComplex, int[] expected) =>
            Assert.Equal(expected, ErrorLines(
                "#settings {\"complex\": true}\na = hp([1; 2])\n#end settings\n#settings {\"complex\": false}\nz = 1i\n#end settings",
                isComplex));

        [Fact]
        public void EachModeIsCheckedOnce()
        {
            var staged = new ContentResolver().GetStagedContent("z = 1i");
            Assert.Same(staged.CoreCheck.Get(true), staged.CoreCheck.Get(true));
            Assert.NotSame(staged.CoreCheck.Get(true), staged.CoreCheck.Get(false));
        }
    }
}
