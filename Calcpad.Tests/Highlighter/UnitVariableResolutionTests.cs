using System.IO;
using System.Linq;
using Calcpad.Highlighter.ContentResolution;
using Calcpad.Highlighter.Linter;
using Calcpad.Highlighter.Tokenizer;
using Calcpad.Highlighter.Tokenizer.Models;

namespace Calcpad.Tests.Highlighter
{
    /// <summary>
    /// Unit-named identifiers resolve as Core does: a name right after a number is always a unit,
    /// any other is a variable once one is defined, including through includes and macros.
    /// </summary>
    public class UnitVariableResolutionTests
    {
        private const string MakeMacro = "#def make$(n$) = n$ = 5\n";

        private static TokenType TypeOf(TokenizerResult result, int line, string text) =>
            result.Tokens.Last(t => t.Line == line && t.Text == text).Type;

        private static TokenizerResult TokenizeResolved(string content, string sourceFilePath = null)
        {
            var staged = new ContentResolver().GetStagedContent(content, sourceFilePath: sourceFilePath);
            var tokenizer = new CalcpadTokenizer();
            tokenizer.SetResolvedContent(staged);
            return tokenizer.Tokenize(content);
        }

        [Theory]
        [InlineData("a = 5\nb = 2a", "a")]
        [InlineData("a = 5\ne = 2 a", "a")]
        [InlineData("m = 3\nh = 2m", "m")]
        [InlineData("x = 3\ny = 2x", "x")]
        public void NameAfterNumber_IsUnits(string source, string name)
        {
            Assert.Equal(TokenType.Units, TypeOf(new CalcpadTokenizer().Tokenize(source), 1, name));
        }

        [Fact]
        public void ParameterAfterNumber_IsUnits()
        {
            Assert.Equal(TokenType.Units, TypeOf(new CalcpadTokenizer().Tokenize("f(g) = 2g"), 0, "g"));
        }

        [Fact]
        public void VariableAfterOperator_IsVariable()
        {
            Assert.Equal(TokenType.Variable, TypeOf(new CalcpadTokenizer().Tokenize("a = 5\nb = 2*a"), 1, "a"));
        }

        [Fact]
        public void ParameterAfterOperator_IsLocalVariable()
        {
            Assert.Equal(TokenType.LocalVariable, TypeOf(new CalcpadTokenizer().Tokenize("f(g) = 4*g"), 0, "g"));
        }

        [Fact]
        public void IncludedDefinition_IsVariable()
        {
            var dir = Directory.CreateTempSubdirectory("unit_variable_");
            try
            {
                File.WriteAllText(Path.Combine(dir.FullName, "inc.cpd"), "a = 5");
                var result = TokenizeResolved(
                    "#include inc.cpd\nc = a\nd = 2*a", Path.Combine(dir.FullName, "main.cpd"));

                Assert.Equal(TokenType.Variable, TypeOf(result, 1, "a"));
                Assert.Equal(TokenType.Variable, TypeOf(result, 2, "a"));
            }
            finally
            {
                dir.Delete(true);
            }
        }

        [Fact]
        public void MacroDefinition_IsVariable()
        {
            var result = TokenizeResolved(MakeMacro + "make$(a)\nc = a\nd = 2*a");
            Assert.Equal(TokenType.Variable, TypeOf(result, 2, "a"));
            Assert.Equal(TokenType.Variable, TypeOf(result, 3, "a"));
        }

        [Fact]
        public void UseBeforeMacroDefinition_IsUnits()
        {
            Assert.Equal(TokenType.Units, TypeOf(TokenizeResolved(MakeMacro + "c = a\nmake$(a)"), 1, "a"));
        }

        [Fact]
        public void MacroDefinition_IsCaseSensitive()
        {
            Assert.Equal(TokenType.Units, TypeOf(TokenizeResolved(MakeMacro + "make$(A)\nc = a"), 2, "a"));
        }

        [Fact]
        public void NameAfterNumber_IgnoresMacroDefinition()
        {
            Assert.Equal(TokenType.Units, TypeOf(TokenizeResolved(MakeMacro + "make$(a)\nb = 2a"), 2, "a"));
        }

        [Fact]
        public void CustomUnitUsedAfterNumber_IsNotUnusedVariable()
        {
            var content = ".bit = 1\n.byte = 8bit\nx = 2byte\ny = x";
            var staged = new ContentResolver().GetStagedContent(content);
            var result = new CalcpadLinter().Lint(staged, new LintIgnoreRegionParser().ExtractRegions(content));
            Assert.DoesNotContain(result.Diagnostics, d => d.Code == "CPD-3312" && d.Line < 2);
        }

        [Fact]
        public void VariableFirstLines_MapMacroOutputToCallLine()
        {
            var lines = new ContentResolver().GetStagedContent(MakeMacro + "x = 1\nmake$(a)").Stage3.VariableFirstLines;
            Assert.Equal(1, lines["x"]);
            Assert.Equal(2, lines["a"]);
        }
    }
}
