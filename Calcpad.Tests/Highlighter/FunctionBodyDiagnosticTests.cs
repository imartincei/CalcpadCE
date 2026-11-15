using System.IO;
using System.Linq;
using Calcpad.Highlighter.Linter.Models;

namespace Calcpad.Tests.Highlighter
{
    /// <summary>A custom function body is judged at its first call, which is marked along with the body.</summary>
    public class FunctionBodyDiagnosticTests : IClassFixture<HighlighterLinterFixture>
    {
        private readonly HighlighterLinterFixture _fixture;

        public FunctionBodyDiagnosticTests(HighlighterLinterFixture fixture)
        {
            _fixture = fixture;
        }

        private LinterDiagnostic[] Lint(string source)
        {
            var path = Path.Combine(Path.GetTempPath(), $"function_body_{System.Guid.NewGuid():N}.cpd");
            File.WriteAllText(path, source);
            try
            {
                return _fixture.LintFile(path).Diagnostics.Where(d => d.Code == "CPD-3301").ToArray();
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void UndefinedAtFirstCall_MarksCallAndBody()
        {
            var lines = new[] { "f(x) = x + qq", "y = 1 + f(2)", "qq = 3" };
            var diagnostics = Lint(string.Join('\n', lines));

            var call = Assert.Single(diagnostics, d => d.Line == 1);
            Assert.Equal("f(2)", lines[1][call.Column..call.EndColumn]);
            Assert.EndsWith("In the body of f on line 1.", call.Message);
            var toBody = Assert.Single(call.Related);
            Assert.Equal((0, 11, 13), (toBody.Line, toBody.Column, toBody.EndColumn));

            var body = Assert.Single(diagnostics, d => d.Line == 0);
            Assert.Equal("qq", lines[0][body.Column..body.EndColumn]);
            Assert.EndsWith("At the first call to f on line 2.", body.Message);
            var toCall = Assert.Single(body.Related);
            Assert.Equal((1, 8, 12), (toCall.Line, toCall.Column, toCall.EndColumn));
        }

        [Fact]
        public void CalledThroughAnotherFunction_MarksTheOuterCall()
        {
            var lines = new[] { "f(x) = x + qq", "g(x) = 2*f(x)", "y = g(2)", "qq = 3" };
            var diagnostics = Lint(string.Join('\n', lines));

            var call = Assert.Single(diagnostics, d => d.Line == 2);
            Assert.Equal("g(2)", lines[2][call.Column..call.EndColumn]);
            Assert.Single(diagnostics, d => d.Line == 0);
        }

        [Fact]
        public void DefinedBeforeFirstCall_IsClean()
        {
            Assert.Empty(Lint("f(x) = x + qq\nqq = 3\ny = f(2)"));
        }

        [Fact]
        public void TwoUndefinedNamesInOneBody_CallLinksToBoth()
        {
            var lines = new[] { "f(x) = x + qq + rr", "y = f(2)" };
            var diagnostics = Lint(string.Join('\n', lines));

            var call = Assert.Single(diagnostics, d => d.Line == 1);
            Assert.Equal(["qq", "rr"], call.Related.Select(r => lines[r.Line][r.Column..r.EndColumn]).Order());
            Assert.Equal(2, diagnostics.Count(d => d.Line == 0 && d.Related?.Single().Line == 1));
        }

        [Fact]
        public void NeverCalled_MarksOnlyTheBody()
        {
            var diagnostic = Assert.Single(Lint("f(x) = x + qq"));
            Assert.Equal((0, 11, 13), (diagnostic.Line, diagnostic.Column, diagnostic.EndColumn));
            Assert.Null(diagnostic.Related);
        }
    }
}
