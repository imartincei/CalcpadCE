using System.IO;
using System.Linq;
using Calcpad.Highlighter.ContentResolution;

namespace Calcpad.Tests.Highlighter
{
    /// <summary>Includes and macros resolved through Core's MacroParser.</summary>
    public class CoreResolutionTests : IClassFixture<HighlighterLinterFixture>
    {
        private readonly HighlighterLinterFixture _fixture;

        public CoreResolutionTests(HighlighterLinterFixture fixture)
        {
            _fixture = fixture;
        }

        private StagedResolvedContent Resolve(string file)
        {
            var path = Path.Combine(_fixture.ValidDir, file);
            return new ContentResolver().GetStagedContent(File.ReadAllText(path), path);
        }

        [Fact]
        public void Include_IsInlinedWithFileLines()
        {
            var staged = Resolve("modules.cpd");
            var stage2 = staged.Stage2;
            Assert.DoesNotContain(stage2.Lines, l => l.Contains("Include file not provided"));

            var constLine = stage2.Lines.FindIndex(l => l == "IMPORTED_CONST = 42");
            Assert.Equal(1, stage2.SourceMap[constLine]);
            var info = stage2.IncludeMap[constLine];
            Assert.Equal("include", info.Source);
            Assert.Equal("import.cpd", info.SourceFile);
            Assert.Equal(2, info.OriginalLine);

            // #local content of the include never reaches the includer.
            Assert.DoesNotContain(stage2.Lines, l => l.Contains("localVar"));
            Assert.Contains(stage2.Lines, l => l.Contains("localOnly"));
            Assert.Single(staged.Stage2.IncludedFileHashes);
        }

        [Fact]
        public void IncludedMacros_AreCollectedAndExpanded()
        {
            var staged = Resolve("modules.cpd");
            var check = staged.Stage2.MacroDefinitions.Single(m => m.Name == "check$");
            Assert.Equal(["Rn$", "Ru$"], check.Params);
            Assert.Equal(5, check.Content.Count);
            Assert.Equal("include", check.Source);

            var stage3 = staged.Stage3;
            var expanded = stage3.Lines.FindIndex(l => l.Trim() == "#if 100 ≥ 80");
            Assert.True(expanded >= 0);
            var expansion = stage3.MacroExpansions[expanded];
            Assert.Equal(["check$"], expansion.MacroNames);
            Assert.Equal("check$(100; 80)", expansion.CallSiteLine);
            Assert.Equal(0, expansion.ContentLineIndex);
            Assert.Equal(5, expansion.TotalContentLines);
        }

        [Fact]
        public void MacroNames_AreCaseSensitive()
        {
            var staged = new ContentResolver().GetStagedContent("#def m$ = 1\nx = M$\ny = m$");
            Assert.Equal(["x = M$", "y = 1"], staged.Stage3.Lines);
        }
    }
}
