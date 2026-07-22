using System.Linq;
using Calcpad.Highlighter.ContentResolution;
using Calcpad.Highlighter.Linter;
using Calcpad.Highlighter.Linter.Models;

namespace Calcpad.Tests.HighlighterTests
{
    public class MetadataReturnTypeTests
    {
        private static Stage3Result Stage3(string content)
        {
            var resolver = new ContentResolver();
            return resolver.GetStagedContent(content).Stage3;
        }

        [Fact]
        public void ReturnType_Metadata_OverridesInferredFunctionType()
        {
            // f(x) = x infers a scalar Value; the metadata comment declares matrix.
            var src = "'<!--{\"returnType\":\"matrix\"}-->\nf(x) = x\n";
            var stage3 = Stage3(src);

            var func = stage3.TypeTracker.Functions.GetValueOrDefault("f");
            Assert.NotNull(func);
            Assert.Equal(CalcpadType.Matrix, func.ReturnType);
        }

        [Fact]
        public void ReturnType_Metadata_Any_MapsToVarious()
        {
            var src = "'<!--{\"returnType\":\"any\"}-->\nf(x) = x\n";
            var stage3 = Stage3(src);

            var func = stage3.TypeTracker.Functions.GetValueOrDefault("f");
            Assert.NotNull(func);
            Assert.Equal(CalcpadType.Various, func.ReturnType);
        }

        [Fact]
        public void CustomUnit_Metadata_Description_Captured()
        {
            var src = "'<!--{\"desc\":\"Euro currency\"}-->\n.EUR = 1\n";
            var stage3 = Stage3(src);

            var unit = stage3.CustomUnits.FirstOrDefault(u => u.Name == "EUR");
            Assert.NotNull(unit);
            Assert.Equal("Euro currency", unit.Description);
        }

        [Fact]
        public void TryParse_ReadsReturnType()
        {
            var line = "'<!--{\"returnType\":\"vector\"}-->";
            Assert.True(DefinitionMetadata.TryParse(line, out var metadata));
            Assert.Equal("vector", metadata.ReturnType);
        }

        [Fact]
        public void InvalidReturnType_ReportsCpd3416()
        {
            var src = "'<!--{\"returnType\":\"scalar\"}-->\nf(x) = x\n";
            var resolver = new ContentResolver();
            var staged = resolver.GetStagedContent(src);
            var result = new CalcpadLinter().Lint(staged, new LintIgnoreRegionParser().ExtractRegions(src));

            Assert.Contains(result.Diagnostics, d => d.Code == "CPD-3411");
        }
    }
}
