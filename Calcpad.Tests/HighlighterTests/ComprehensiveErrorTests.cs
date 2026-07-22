using System.IO;
using System.Linq;

namespace Calcpad.Tests.HighlighterTests
{
    public class ComprehensiveErrorTests : IClassFixture<HighlighterLinterFixture>
    {
        private readonly HighlighterLinterFixture _fixture;

        public ComprehensiveErrorTests(HighlighterLinterFixture fixture)
        {
            _fixture = fixture;
        }

        public static TheoryData<string> ErrorFiles()
        {
            var data = new TheoryData<string>();
            var fixture = new HighlighterLinterFixture();
            foreach (var path in Directory.GetFiles(fixture.ErrorsDir, "*.cpd", SearchOption.AllDirectories))
                data.Add(Path.GetRelativePath(fixture.ErrorsDir, path));
            return data;
        }

        [Theory]
        [MemberData(nameof(ErrorFiles))]
        public void ErrorExample_ProducesAtLeastOneError(string relativePath)
        {
            var fullPath = Path.Combine(_fixture.ErrorsDir, relativePath);
            var result = _fixture.LintFile(fullPath);
            var errors = result.Diagnostics
                .Where(d => d.Severity == Calcpad.Highlighter.Linter.Models.LinterSeverity.Error)
                .ToList();

            Assert.True(
                errors.Count > 0,
                $"Expected at least one Error-level diagnostic in '{relativePath}' but found none.");
        }

        public static TheoryData<string, string> ExpectedErrorCodes()
        {
            var data = new TheoryData<string, string>
            {
                // Balance errors (CPD-31xx)
                { "balance_errors.cpd", "CPD-3101" },
                { "balance_errors.cpd", "CPD-3102" },
                { "balance_errors.cpd", "CPD-3103" },
                { "balance_errors.cpd", "CPD-3104" },
                { "balance_errors.cpd", "CPD-3105" },
                { "balance_errors.cpd", "CPD-3106" },

                // Include errors (CPD-11xx)
                { "include_errors.cpd", "CPD-1102" },

                // Macro errors (CPD-22xx)
                { "macro_errors.cpd", "CPD-2201" },
                { "macro_errors.cpd", "CPD-2202" },
                { "macro_errors.cpd", "CPD-2206" },
                { "macro_errors.cpd", "CPD-2207" },
                { "macro_errors.cpd", "CPD-2209" },
                { "macro_errors.cpd", "CPD-2210" },
                { "macro_errors.cpd", "CPD-2212" },

                // Naming errors (CPD-32xx)
                { "naming_errors.cpd", "CPD-3204" },
                { "naming_errors.cpd", "CPD-3205" }, // Warning: conflict with built-in constant is allowed
                { "naming_errors.cpd", "CPD-3206" },

                // Usage errors (CPD-33xx)
                { "usage_errors.cpd", "CPD-3301" },
                { "usage_errors.cpd", "CPD-3302" },
                { "usage_errors.cpd", "CPD-3303" },
                { "usage_errors.cpd", "CPD-3307" },
                { "usage_errors.cpd", "CPD-3308" },
                { "usage_errors.cpd", "CPD-3310" },
                { "usage_errors.cpd", "CPD-3311" },

                // Semantic errors (CPD-34xx)
                { "semantic_errors.cpd", "CPD-3401" },
                { "semantic_errors.cpd", "CPD-3403" },
                { "semantic_errors.cpd", "CPD-3405" },
                { "semantic_errors.cpd", "CPD-3407" },
                { "semantic_errors.cpd", "CPD-3408" },
                { "semantic_errors.cpd", "CPD-3410" },

                // Reassignment
                { "reassignment_errors.cpd", "CPD-3409" },
                { "reassignment_errors.cpd", "CPD-3410" },
            };
            return data;
        }

        [Theory]
        [MemberData(nameof(ExpectedErrorCodes))]
        public void ErrorExample_ProducesExpectedErrorCode(string filename, string expectedCode)
        {
            var fullPath = Path.Combine(_fixture.ErrorsDir, filename);
            var result = _fixture.LintFile(fullPath);
            var matching = result.Diagnostics.Where(d => d.Code == expectedCode).ToList();

            Assert.True(
                matching.Count > 0,
                $"Expected diagnostic '{expectedCode}' in '{filename}' but it was not produced. " +
                $"Diagnostics found: " +
                string.Join("; ", result.Diagnostics.Select(d => $"[{d.Code}] {d.Severity} Line {d.Line + 1}")));
        }
    }
}
