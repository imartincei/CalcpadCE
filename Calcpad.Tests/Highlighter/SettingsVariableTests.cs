using System.Linq;
using Calcpad.Highlighter.ContentResolution;
using Calcpad.Highlighter.Linter;
using Calcpad.Highlighter.Linter.Models;

namespace Calcpad.Tests.Highlighter
{
    /// <summary>
    /// Special variable names (PlotHeight, Precision, ...) configure the engine when assigned,
    /// so assigning one without referencing it later must NOT raise CPD-3312 ('unused variable').
    /// </summary>
    public class SettingsVariableTests
    {
        private static LinterResult Lint(string content)
        {
            var staged = new ContentResolver().GetStagedContent(content);
            var ignoreRegions = new LintIgnoreRegionParser().ExtractRegions(content);
            return new CalcpadLinter().Lint(staged, ignoreRegions);
        }

        private static int UnusedWarnings(LinterResult result, string name) =>
            result.Diagnostics.Count(d => d.Code == "CPD-3312" && d.Message.Contains("'" + name + "'"));

        [Theory]
        [InlineData("PlotHeight")]
        [InlineData("PlotWidth")]
        [InlineData("PlotStep")]
        [InlineData("Precision")]
        [InlineData("Tol")]
        [InlineData("ReturnAngleUnits")]
        public void SettingsVariable_Unused_NoWarning(string name)
        {
            var result = Lint($"{name} = 100\n");
            Assert.Equal(0, UnusedWarnings(result, name));
        }

        [Fact]
        public void OrdinaryVariable_Unused_StillWarns()
        {
            var result = Lint("myVar = 100\n");
            Assert.True(UnusedWarnings(result, "myVar") > 0);
        }

        [Fact]
        public void SettingsVariable_WrongCase_StillWarns()
        {
            // Core's settings lookup is case-sensitive, so a lowercase name is a plain variable.
            var result = Lint("precision = 100\n");
            Assert.True(UnusedWarnings(result, "precision") > 0);
        }
    }
}
