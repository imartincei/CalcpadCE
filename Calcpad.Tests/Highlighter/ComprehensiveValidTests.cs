using System.Collections.Generic;
using System.IO;
using System.Linq;
using Calcpad.Highlighter.Linter.Models;

namespace Calcpad.Tests.Highlighter
{
    public class ComprehensiveValidTests : IClassFixture<HighlighterLinterFixture>
    {
        private readonly HighlighterLinterFixture _fixture;

        public ComprehensiveValidTests(HighlighterLinterFixture fixture)
        {
            _fixture = fixture;
        }

        public static IEnumerable<object[]> ValidFiles()
        {
            var fixture = new HighlighterLinterFixture();
            foreach (var path in Directory.GetFiles(fixture.ValidDir, "*.cpd", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(fixture.ValidDir, path);
                yield return new object[] { relative };
            }
        }

        [Theory]
        [MemberData(nameof(ValidFiles))]
        public void ValidExample_HasNoErrors(string relativePath)
        {
            var fullPath = Path.Combine(_fixture.ValidDir, relativePath);
            var result = _fixture.LintFile(fullPath);
            var errors = result.Diagnostics
                .Where(d => d.Severity == LinterSeverity.Error)
                .ToList();

            Assert.True(
                errors.Count == 0,
                $"Expected no Error-level diagnostics in '{relativePath}' but found {errors.Count}: " +
                string.Join("; ", errors.Select(e => $"[{e.Code}] Line {e.Line + 1}: {e.Message}")));
        }
    }
}
