using System.Linq;
using Calcpad.Highlighter.ContentResolution;
using Calcpad.Highlighter.Linter;
using Calcpad.Highlighter.Linter.Models;
using Calcpad.Highlighter.Prettifier;
using Calcpad.Highlighter.Tokenizer;
using Calcpad.Highlighter.Tokenizer.Models;

namespace Calcpad.Tests.Highlighter
{
    public class ParseModeHighlighterTests : IClassFixture<HighlighterLinterFixture>
    {
        private readonly HighlighterLinterFixture _fixture;

        public ParseModeHighlighterTests(HighlighterLinterFixture fixture)
        {
            _fixture = fixture;
        }

        private LinterResult Lint(string source)
        {
            var staged = new ContentResolver().GetStagedContent(source);
            return new CalcpadLinter().Lint(staged, new LintIgnoreRegionParser().ExtractRegions(source));
        }

        private static TokenizerResult Tokenize(string source) => new CalcpadTokenizer().Tokenize(source);

        [Fact]
        public void ContentLines_GetNoTokens_AndModesFollowTheStack()
        {
            var result = Tokenize("#html\n<b>x = 1</b>\n#cpd\ny = 2\n#end cpd\n<i>z</i>\n#end html\nw = 3");
            Assert.Empty(result.GetTokensForLine(1));
            Assert.NotEmpty(result.GetTokensForLine(3));
            Assert.Empty(result.GetTokensForLine(5));
            Assert.Equal(new[] { 1, 5 }, result.RawLines.OrderBy(l => l));
            Assert.Equal(ParseMode.Html, result.LineModes[1]);
            Assert.False(result.LineModes.ContainsKey(3));
            Assert.Equal(ParseMode.Html, result.LineModes[6]);
            Assert.False(result.LineModes.ContainsKey(7));
        }

        [Fact]
        public void ContentLines_HighlightOnlyDefinedMacros()
        {
            var result = Tokenize("#def greet$ = hi\n#html\n<b>greet$</b> costs 5$\n#end html");
            var token = Assert.Single(result.GetTokensForLine(2));
            Assert.Equal(TokenType.Macro, token.Type);
            Assert.Equal("greet$", token.Text);
            Assert.Equal(3, token.Column);
        }

        [Theory]
        [InlineData("# Title")]
        [InlineData("#hashtag text")]
        public void UnknownHashLines_KeepMarkdownColouring(string line)
        {
            Assert.Contains(1, Tokenize($"#markdown\n{line}\n#end markdown").RawLines);
        }

        [Theory]
        [InlineData("markdown", "#tag")]
        [InlineData("markdown", "# Title")]
        [InlineData("markdown", "#end html")]
        [InlineData("markdown", "#round 2")]
        [InlineData("html", "#test")]
        [InlineData("html", "#header { color: red; }")]
        [InlineData("html", "#end markdown")]
        public void UnrecognizedKeywords_AreContent(string mode, string line)
        {
            var source = $"#{mode}\n{line}\n#end {mode}";
            Assert.Empty(Lint(source).Diagnostics);
            Assert.Contains(1, Tokenize(source).RawLines);
        }

        [Fact]
        public void DirectiveWithCondition_IsTokenized()
        {
            var result = Tokenize("x = 1\n#html\n#html x > 1\n<b>a</b>\n#end html\n#end html");
            Assert.DoesNotContain(2, result.RawLines);
            Assert.Contains(3, result.RawLines);
        }

        [Fact]
        public void MarkdownProse_ProducesNoDiagnostics()
        {
            var result = Lint("#markdown\na = b + undefinedThing\n\n| x | y |\n|---|---|\n#end markdown");
            Assert.Empty(result.Diagnostics);
        }

        [Fact]
        public void ModeDirectives_AreRecognized()
        {
            const string source = "#html\n<b>x</b>\n#cpd\nx = 1\n#end cpd\n#markdown\n**y**\n#end markdown\n#end html\n#html x > 0\n<i>z</i>\n#end html";
            Assert.DoesNotContain(Lint(source).Diagnostics, d => d.Severity == LinterSeverity.Error);
        }

        [Theory]
        [InlineData("#if 1")]
        [InlineData("#end if")]
        [InlineData("#md on")]
        public void CalcpadDirectives_InHtml_AreContent(string line)
        {
            var source = $"#html\n{line}\n#end html";
            Assert.Empty(Lint(source).Diagnostics);
            Assert.Contains(1, Tokenize(source).RawLines);
        }

        [Fact]
        public void MacroDirectives_InHtml_AreStillDirectives()
        {
            var source = "#html\n#def tag$ = <b>t</b>\ntag$\n#end html";
            Assert.Empty(Lint(source).Diagnostics);
            Assert.DoesNotContain(1, Tokenize(source).RawLines);
        }

        [Fact]
        public void ControlBlocks_WrapModeBlocks()
        {
            const string source = "#if x > 3\n#html\n<b>big</b>\n#end html\n#else\n#markdown\n*small*\n#end markdown\n#end if";
            Assert.Empty(Lint($"x = 1\n{source}").Diagnostics);
        }
        [Fact]
        public void HtmlContent_DoesNotJoinContinuationLines()
        {
            const string source = "#html\n<style>\np { color: red;\n}\n</style>\n#end html";
            var staged = new ContentResolver().GetStagedContent(source);
            Assert.Equal(6, staged.Stage1.Lines.Count);
            Assert.Empty(Lint(source).Diagnostics);
        }

        private ParseModeRange[] SourceModes(string source)
        {
            var staged = new ContentResolver().GetStagedContent(source);
            return SourceParseModes.Build(staged, source.Split('\n').Length).ToArray();
        }

        [Fact]
        public void SourceModes_FollowDirectives()
        {
            var ranges = SourceModes("a = 1\n#html\n<b>x</b>\n#cpd\nb = 2\n#end cpd\n<i>y</i>\n#end html\nc = 3");
            Assert.Equal(new[]
            {
                new ParseModeRange(2, 3, ParseMode.Html),
                new ParseModeRange(6, 7, ParseMode.Html),
            }, ranges);
        }

        [Fact]
        public void SourceModes_FollowMacrosThatSwitchMode()
        {
            const string source = "#def open$\n#html\n#end def\n#def close$\n#end html\n#end def\nopen$\n<b>x</b>\n\nclose$\nd = 4";
            Assert.Equal(new[] { new ParseModeRange(7, 9, ParseMode.Html) }, SourceModes(source));
        }

        [Fact]
        public void SourceModes_IgnoreDirectivesInsideMacroBodies()
        {
            Assert.Empty(SourceModes("#def block$\n#markdown\n**b**\n#end markdown\n#end def\ne = 5"));
        }

        [Fact]
        public void Prettifier_LeavesContentLinesUntouched()
        {
            const string source = "#if 1\n#markdown\n- one\n  - two\n#end markdown\nx = 1\n#end if";
            var expected = "#if 1\n\t#markdown\n- one\n  - two\n\t#end markdown\n\tx = 1\n#end if";
            Assert.Equal(expected, CalcpadPrettifier.Prettify(source));
        }
    }
}
