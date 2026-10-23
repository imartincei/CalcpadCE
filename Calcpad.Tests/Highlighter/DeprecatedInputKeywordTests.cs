using System.IO;
using System.Linq;
using Calcpad.Highlighter.Linter.Models;

namespace Calcpad.Tests.Highlighter
{
    public class DeprecatedInputKeywordTests : IClassFixture<HighlighterLinterFixture>
    {
        private readonly HighlighterLinterFixture _fixture;

        public DeprecatedInputKeywordTests(HighlighterLinterFixture fixture)
        {
            _fixture = fixture;
        }

        private LinterDiagnostic[] Lint(string source)
        {
            var path = Path.Combine(Path.GetTempPath(), $"input_keyword_{System.Guid.NewGuid():N}.cpd");
            File.WriteAllText(path, source);
            try
            {
                return _fixture.LintFile(path).Diagnostics.ToArray();
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Theory]
        [InlineData("a = 1\n#input\nb = 2")]
        [InlineData("a = 1\n#Input\nb = 2")]
        [InlineData("#if 1\n    #input\n#end if")]
        public void Input_IsWarnedOnly(string source)
        {
            var diagnostics = Lint(source);
            var diagnostic = Assert.Single(diagnostics, d => d.Code == "CPD-3420");
            Assert.Equal(LinterSeverity.Warning, diagnostic.Severity);
            Assert.DoesNotContain(diagnostics, d => d.Severity == LinterSeverity.Error);
        }

        [Fact]
        public void Pause_IsNotWarned() =>
            Assert.DoesNotContain(Lint("a = 1\n#pause\nb = 2"), d => d.Code == "CPD-3420");
    }
}
