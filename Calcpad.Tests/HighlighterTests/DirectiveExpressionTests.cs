using System.IO;
using System.Linq;
using Calcpad.Highlighter.Linter.Models;

namespace Calcpad.Tests.HighlighterTests
{
    /// <summary>
    /// Directives used to be skipped wholesale by the usage checks, which was fine when none
    /// of them took an expression. Conditions after #hide/#show/#pre/#post, loop bounds and
    /// #UI assignments changed that, so the expression part now has to resolve like any other.
    /// </summary>
    public class DirectiveExpressionTests : IClassFixture<HighlighterLinterFixture>
    {
        private readonly HighlighterLinterFixture _fixture;

        public DirectiveExpressionTests(HighlighterLinterFixture fixture)
        {
            _fixture = fixture;
        }

        private LinterResult Lint(string source)
        {
            var path = Path.Combine(Path.GetTempPath(), $"directive_{System.Guid.NewGuid():N}.cpd");
            File.WriteAllText(path, source);
            try
            {
                return _fixture.LintFile(path);
            }
            finally
            {
                File.Delete(path);
            }
        }

        private bool ReportsUndefined(string source) =>
            Lint(source).Diagnostics.Any(d => d.Code == "CPD-3301" && d.Message.Contains("zzzMissing"));

        [Theory]
        [InlineData("#if zzzMissing > 0\n#end if")]
        [InlineData("#else if", "x = 1\n#if x ≡ 1\n#else if zzzMissing > 0\n#end if")]
        [InlineData("#while zzzMissing > 0\n#loop")]
        [InlineData("#repeat zzzMissing\n#loop")]
        [InlineData("#round zzzMissing")]
        [InlineData("#hide zzzMissing > 0\n#end hide")]
        [InlineData("#show zzzMissing > 0\n#end show")]
        [InlineData("#pre zzzMissing > 0\n#end pre")]
        [InlineData("#post zzzMissing > 0\n#end post")]
        [InlineData("#val zzzMissing > 0\n#end val")]
        [InlineData("#equ zzzMissing > 0\n#end equ")]
        [InlineData("#noc zzzMissing > 0\n#end noc")]
        [InlineData("#for i = 1 : zzzMissing\n#loop")]
        [InlineData("#UI q = zzzMissing")]
        public void ExpressionDirective_ResolvesItsIdentifiers(string source, string overrideSource = null) =>
            Assert.True(ReportsUndefined(overrideSource ?? source),
                $"Expected an undefined-variable error for 'zzzMissing' in: {overrideSource ?? source}");

        [Theory]
        [InlineData("#round default")]
        [InlineData("#format default")]
        [InlineData("#deg")]
        [InlineData("#hide")]
        [InlineData("#end if")]
        [InlineData("#loop")]
        [InlineData("#md off")]
        public void DirectiveWithoutAnExpression_ReportsNothing(string source) =>
            Assert.DoesNotContain(Lint(source).Diagnostics, d => d.Code == "CPD-3301");

        [Fact]
        public void ConditionOnAVisibilityDirective_AcceptsDefinedVariables() =>
            Assert.DoesNotContain(Lint("x = 5\n#hide x ≡ 5\ny = 1\n#end hide\ny").Diagnostics,
                d => d.Severity == LinterSeverity.Error);

        [Fact]
        public void LoopCounter_IsLocalAndNotFlagged() =>
            Assert.DoesNotContain(Lint("#for i = 1 : 3\ni\n#loop").Diagnostics,
                d => d.Code == "CPD-3301");
    }
}
