using System.IO;
using System.Linq;
using Calcpad.Highlighter.Linter.Models;
using Calcpad.Highlighter.Tokenizer;
using Calcpad.Highlighter.Tokenizer.Models;

namespace Calcpad.Tests.HighlighterTests
{
    public class UiDirectiveHighlighterTests : IClassFixture<HighlighterLinterFixture>
    {
        private readonly HighlighterLinterFixture _fixture;

        public UiDirectiveHighlighterTests(HighlighterLinterFixture fixture)
        {
            _fixture = fixture;
        }

        private LinterResult Lint(string source)
        {
            var path = Path.Combine(Path.GetTempPath(), $"ui_lint_{System.Guid.NewGuid():N}.cpd");
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

        private string[] Messages(string source, string code = "CPD-3415") =>
            Lint(source).Diagnostics.Where(d => d.Code == code).Select(d => d.Message).ToArray();

        [Fact]
        public void AnnotatedAssignment_DefinesTheVariable()
        {
            // The #UI prefix and its JSON block must not hide the definition from the linter.
            var diagnostics = Lint("#UI {\"type\": \"entry\"} L = 10m\nA = L * 2m\nA");
            Assert.DoesNotContain(diagnostics.Diagnostics, d => d.Severity == LinterSeverity.Error);
        }

        [Fact]
        public void UndefinedVariable_IsStillReported() =>
            Assert.Contains(Lint("#UI L = zzzMissing * 2").Diagnostics,
                d => d.Code == "CPD-3301" && d.Message.Contains("zzzMissing"));

        [Fact]
        public void UndefinedVariable_IsReportedBehindAJsonBlock() =>
            Assert.Contains(Lint("#UI {\"type\": \"entry\"} L = zzzMissing * 2").Diagnostics,
                d => d.Code == "CPD-3301" && d.Message.Contains("zzzMissing"));

        [Fact]
        public void TheKeywordItselfIsNotTreatedAsAnIdentifier() =>
            Assert.DoesNotContain(Lint("#UI L = 10m\nL").Diagnostics, d => d.Code == "CPD-3301");

        [Theory]
        [InlineData("#UI L = 10m")]
        [InlineData("#UI {\"type\": \"checkbox\"} f = 1")]
        [InlineData("#UI {\"type\": \"datagrid\", \"rows\": 2, \"columns\": 2, \"columnHeaders\": [\"a\", \"b\"]} T = [1; 2 | 3; 4]")]
        [InlineData("#UI {\"type\": \"dropdown\", \"keys\": [\"a\", \"b\"], \"values\": [\"1\", \"2\"]} g = 1")]
        [InlineData("#UI {\"type\": \"radio\", \"keys\": [\"a\"], \"values\": [\"1\"]} r = 1")]
        [InlineData("#UI {\"style\": \"s\", \"reportStyle\": \"b\", \"mode\": \"number\"} v = 1")]
        public void ValidDirective_ProducesNoDiagnostic(string source) =>
            Assert.Empty(Messages(source));

        [Fact]
        public void UnclosedBrace_IsReported() =>
            Assert.Contains("Missing closing brace", Assert.Single(Messages("#UI {\"type\": \"entry\" a = 1")));

        [Fact]
        public void MalformedJson_IsReported() =>
            Assert.Contains("malformed", Assert.Single(Messages("#UI {not json} a = 1")));

        [Fact]
        public void UnknownProperty_IsReported() =>
            Assert.Contains("'colour' is not a recognized", Assert.Single(Messages("#UI {\"colour\": \"red\"} a = 1")));

        [Fact]
        public void UnknownType_IsReported() =>
            Assert.Contains("'slider' is not a recognized", Assert.Single(Messages("#UI {\"type\": \"slider\"} a = 1")));

        [Theory]
        [InlineData("#UI {\"type\": \"dropdown\"} a = 1")]
        [InlineData("#UI {\"type\": \"radio\", \"keys\": [\"x\"]} a = 1")]
        [InlineData("#UI {\"type\": \"dropdown\", \"values\": [\"1\"]} a = 1")]
        public void MissingOptionArrays_AreReported(string source) =>
            Assert.Contains("requires both 'keys' and 'values'", Assert.Single(Messages(source)));

        [Fact]
        public void MismatchedOptionArrays_AreReported() =>
            Assert.Contains("2 keys but 1 values",
                Assert.Single(Messages("#UI {\"type\": \"dropdown\", \"keys\": [\"a\", \"b\"], \"values\": [\"1\"]} a = 1")));

        [Theory]
        [InlineData("#UI {\"mode\": \"string\"} a = 1")]
        [InlineData("#UI a$ = 1")]
        public void StringMode_IsReported(string source) =>
            Assert.Contains(Messages(source), m => m.Contains("String mode is not supported"));

        [Fact]
        public void MissingAssignment_IsReported() =>
            Assert.Contains("requires a variable assignment", Assert.Single(Messages("#UI {\"type\": \"entry\"}")));

        [Theory]
        [InlineData("#UI {\"rows\": -1} a = 1", "'rows' must not be negative")]
        [InlineData("#UI {\"columns\": -2} a = 1", "'columns' must not be negative")]
        public void NegativeGridSize_IsReported(string source, string expected) =>
            Assert.Contains(expected, Assert.Single(Messages(source)));

        [Theory]
        [InlineData("#UI {\"columns\": 2, \"columnHeaders\": [\"a\", \"b\", \"c\"]} T = [1; 2]", "3 entries but the grid has 2 columns")]
        [InlineData("#UI {\"rows\": 1, \"rowHeaders\": [\"r1\", \"r2\"]} T = [1; 2]", "2 entries but the grid has 1 rows")]
        public void MoreHeadersThanCells_IsReported(string source, string expected) =>
            Assert.Contains(expected, Assert.Single(Messages(source)));

        /// <summary>
        /// An omitted size is auto-detected from the right hand side, which the JSON block
        /// cannot see, so headers are only counted against a size the payload declared.
        /// </summary>
        [Theory]
        [InlineData("#UI {\"columnHeaders\": [\"a\", \"b\", \"c\"]} T = [1; 2]")]
        [InlineData("#UI {\"columns\": 3, \"columnHeaders\": [\"a\"]} T = [1; 2; 3]")]
        public void HeaderCount_IsNotReportedWithoutADeclaredSize(string source) =>
            Assert.Empty(Messages(source));

        [Theory]
        [InlineData("#UI {\"rows\": \"two\"} a = 1")]
        [InlineData("#UI {\"keys\": \"a\"} a = 1")]
        [InlineData("#UI {\"style\": 5} a = 1")]
        public void WrongValueType_IsReported(string source) =>
            Assert.Contains("wrong type", Assert.Single(Messages(source)));

        /// <summary>
        /// The first identifier on a line is a definition even when its name happens to be a
        /// unit — 'a' is Are, 'L' is litre. A directive keyword sits before that position and
        /// must not consume it, or the definition never registers and every later use of the
        /// name cascades to Units as well.
        /// </summary>
        [Theory]
        [InlineData("#UI a = 5", "a")]
        [InlineData("#UI L = 10m", "L")]
        [InlineData("#UI {\"type\": \"entry\"} a = 5", "a")]
        [InlineData("#UI 'label ='a = 5", "a")]
        [InlineData("#const h = 6", "h")]
        [InlineData("a = 5", "a")]
        public void UnitNamedVariable_IsDefinedNotAUnit(string source, string name)
        {
            var tokens = new CalcpadTokenizer().Tokenize(source).Tokens;
            Assert.Contains(tokens, t => t.Type == TokenType.Variable && t.Text == name);
            Assert.DoesNotContain(tokens, t => t.Type == TokenType.Units && t.Text == name);
        }

        [Theory]
        [InlineData("#UI x = 5*ft", "ft")]
        [InlineData("#const y = 5*ft", "ft")]
        [InlineData("#UI L = 10m", "m")]
        [InlineData("A = 5*a", "a")]
        public void UnitsAfterTheFirstIdentifier_StayUnits(string source, string unit)
        {
            var tokens = new CalcpadTokenizer().Tokenize(source).Tokens;
            Assert.Contains(tokens, t => t.Type == TokenType.Units && t.Text == unit);
        }

        [Fact]
        public void UnitNamedVariable_DoesNotCascadeToLaterUses()
        {
            var diagnostics = Lint("#UI a = 5\nb = a * 2\nb").Diagnostics;
            Assert.DoesNotContain(diagnostics, d => d.Severity == LinterSeverity.Error);
        }

        [Fact]
        public void UiIsARecognizedKeyword() =>
            Assert.DoesNotContain(Lint("#UI L = 10m\nL").Diagnostics, d => d.Code == "CPD-3403");
    }
}
