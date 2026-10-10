using System.IO;
using System.Linq;
using Calcpad.Highlighter.Linter.Models;

namespace Calcpad.Tests.Highlighter
{
    public class LegacyInputHighlighterTests : IClassFixture<HighlighterLinterFixture>
    {
        private readonly HighlighterLinterFixture _fixture;

        public LegacyInputHighlighterTests(HighlighterLinterFixture fixture)
        {
            _fixture = fixture;
        }

        // Includes resolve from disk, so the samples sit next to the source.
        private LinterResult Lint(string source)
        {
            var dir = Directory.CreateTempSubdirectory("legacy_input_");
            try
            {
                foreach (var file in Directory.GetFiles(_fixture.ValidDir, "*.cpd"))
                    File.Copy(file, Path.Combine(dir.FullName, Path.GetFileName(file)));

                var path = Path.Combine(dir.FullName, "main.cpd");
                File.WriteAllText(path, source);
                return _fixture.LintFile(path);
            }
            finally
            {
                dir.Delete(true);
            }
        }

        private LinterDiagnostic[] Diagnostics(string source, string code) =>
            Lint(source).Diagnostics.Where(d => d.Code == code).ToArray();

        [Theory]
        [InlineData("L = ?{6}m")]
        [InlineData("L = ? {6}m")]
        [InlineData("L = ?{}m")]
        [InlineData("f(x) = x\nA = f(?{2})")]
        public void StoredInputValue_IsWarned(string source)
        {
            var diagnostics = Diagnostics(source, "CPD-3419");
            Assert.Single(diagnostics);
            Assert.Equal(LinterSeverity.Warning, diagnostics[0].Severity);
        }

        [Fact]
        public void StoredInputValue_HighlightsTheFieldOnly()
        {
            var diagnostic = Assert.Single(Diagnostics("L = ?{6}m", "CPD-3419"));
            Assert.Equal(0, diagnostic.Line);
            Assert.Equal(4, diagnostic.Column);
            Assert.Equal(8, diagnostic.EndColumn);
        }

        [Fact]
        public void StoredInputValueAfterAComment_KeepsItsColumns()
        {
            var diagnostic = Assert.Single(Diagnostics("'note' L = ?{6}m", "CPD-3419"));
            Assert.Equal(11, diagnostic.Column);
            Assert.Equal(15, diagnostic.EndColumn);
        }

        [Fact]
        public void EveryStoredInputValueOnALine_IsWarned() =>
            Assert.Equal(2, Diagnostics("f(x; y) = x + y\nA = f(?{2}; ?{3})", "CPD-3419").Length);

        [Theory]
        [InlineData("L = ?m")]
        [InlineData("'What is ?{6}?'")]
        [InlineData("L = 6m 'sets ?{6}'")]
        public void PlainInputAndComments_AreNotWarned(string source) =>
            Assert.Empty(Diagnostics(source, "CPD-3419"));

        [Fact]
        public void IncludeInputValues_AreWarned()
        {
            var diagnostic = Assert.Single(
                Diagnostics("#include import.cpd #{2; 3}", "CPD-1103"));
            Assert.Equal(LinterSeverity.Warning, diagnostic.Severity);
            Assert.Equal(20, diagnostic.Column);
            Assert.Equal(27, diagnostic.EndColumn);
        }

        [Fact]
        public void IncludeWithoutInputValues_IsNotWarned() =>
            Assert.Empty(Diagnostics("#include import.cpd", "CPD-1103"));

        [Fact]
        public void StoredInputValuesInsideAnIncludedFile_AreWarnedOnTheIncludeLine()
        {
            var diagnostic = Assert.Single(Diagnostics("d = 1\n#include legacy_input.cpd", "CPD-3419"));
            Assert.Equal(1, diagnostic.Line);
        }
    }
}
