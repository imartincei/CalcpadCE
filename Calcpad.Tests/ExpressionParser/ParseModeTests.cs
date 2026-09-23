using System.Text.RegularExpressions;

namespace Calcpad.Tests
{
    public class ParseModeTests
    {
        private const string ModeError = "Only #html, #cpd, #markdown";

        private static string Render(string source, bool debug = false)
        {
            var parser = new ExpressionParser { Settings = new Settings(), Debug = debug };
            parser.Parse(source, true, false);
            return parser.HtmlResult.Replace("\r\n", "\n");
        }

        private static string ExpandAndRender(string source)
        {
            var macroParser = new MacroParser();
            Assert.False(macroParser.Parse(source, out var expanded, null, 0, false), expanded);
            return Render(expanded);
        }

        private static int Count(string html, string value) => Regex.Count(html, Regex.Escape(value));

        [Theory]
        [InlineData("#zeta = 1")]
        [InlineData("#{a}")]
        public void UnknownHashLine_DoesNotCrashKeywordLookup(string line)
        {
            Assert.DoesNotContain("Unexpected error", Render(line));
        }

        [Fact]
        public void Markdown_IsNotMistakenForMd()
        {
            var html = Render("#markdown\n**bold**\n#end markdown");
            Assert.Contains("<strong>bold</strong>", html);
            Assert.DoesNotContain("**bold**", html);
        }

        [Fact]
        public void MdOn_StillRendersComments()
        {
            Assert.Contains("<strong>b</strong>", Render("#md on\n'**b**'"));
        }

        [Fact]
        public void Html_PassesMultilineScriptThroughUnwrapped()
        {
            var html = Render("#html\n<script>\nlet a = 1;\nlet b = a < 2;\n</script>\n#end html");
            Assert.Contains("<script>\nlet a = 1;\nlet b = a < 2;\n</script>", html);
            Assert.DoesNotContain("<p>let", html);
            Assert.DoesNotContain("class=\"err\"", html);
        }

        [Fact]
        public void Html_KeepsIndentation()
        {
            Assert.Contains("<pre>\n    indented\n</pre>", Render("#html\n<pre>\n    indented\n</pre>\n#end html"));
        }

        [Fact]
        public void Markdown_RendersTableAsOneBlock()
        {
            var html = Render("#markdown\n| a | b |\n|---|---|\n| 1 | 2 |\n| 3 | 4 |\n#end markdown");
            Assert.Equal(1, Count(html, "<table"));
            Assert.Contains("<td>4</td>", html);
        }

        [Fact]
        public void Markdown_KeepsIndentationForNestedLists()
        {
            var html = Render("#markdown\n- one\n  - two\n- three\n#end markdown");
            Assert.Matches(@"<li>one\s*<ul>\s*<li>two</li>\s*</ul>\s*</li>", html);
            Assert.Equal(2, Count(html, "<ul>"));
        }

        [Fact]
        public void Markdown_BlankLineSeparatesParagraphs()
        {
            Assert.Equal(2, Count(Render("#markdown\nfirst\n\nsecond\n#end markdown"), "<p>"));
        }

        [Fact]
        public void Markdown_SupportsEmphasisExtras()
        {
            var html = Render("#markdown\n++ins++ and H~2~O and x^2^\n#end markdown");
            Assert.Contains("<ins>ins</ins>", html);
            Assert.Contains("<sub>2</sub>", html);
            Assert.Contains("<sup>2</sup>", html);
        }

        [Fact]
        public void Markdown_TagsEachBlockWithItsSourceLine()
        {
            var html = Render("#markdown\n# Title\n\nparagraph\n#end markdown", debug: true);
            Assert.Matches("<h1[^>]*data-source-line=\"2\"[^>]*>Title</h1>", html);
            Assert.Matches("<p[^>]*data-source-line=\"4\"[^>]*>paragraph</p>", html);
        }

        [Fact]
        public void Html_TagsOpeningTagsWithSourceLine()
        {
            Assert.Matches("<div[^>]*data-source-line=\"2\"", Render("#html\n<div>x</div>\n#end html", debug: true));
        }

        [Fact]
        public void EndForms_RestorePreviousMode()
        {
            var html = Render("#html\n<i>a</i>\n#cpd\nccc = 3\n#end cpd\n<i>back</i>\n#end html\nddd = 4");
            Assert.Contains("<i>back</i>", html);
            Assert.Contains("ccc", html);
            Assert.Contains("ddd", html);
            Assert.DoesNotContain("class=\"err\"", html);
        }

        [Fact]
        public void SwitchWithoutEnd_LastsUntilNextSwitch()
        {
            var html = Render("#html\n<i>raw</i>\n#cpd\neee = 5");
            Assert.Contains("<i>raw</i>", html);
            Assert.Contains("eee", html);
            Assert.DoesNotContain("class=\"err\"", html);
        }

        [Fact]
        public void InlineCondition_PassesOrSkipsContent()
        {
            var html = Render("x = 5\n#html x > 1\n<b>shown</b>\n#end html\n#html x > 10\n<b>skipped</b>\n#end html");
            Assert.Contains("<b>shown</b>", html);
            Assert.DoesNotContain("<b>skipped</b>", html);
        }

        [Fact]
        public void CpdCondition_SkipsCalculationsPerIteration()
        {
            var html = Render("zzz = 0\n#for i = 1 : 4\n#cpd i > 2\nzzz = zzz + 10\n#end cpd\n#loop\nyyy = zzz");
            Assert.Matches(@"yyy[\s\S]*20", html);
            Assert.DoesNotContain("class=\"err\"", html);
        }

        [Fact]
        public void ModeBlocks_NestInsideIfBranches()
        {
            const string source = "x = 1\n#if x > 0\n#html\n<b>taken</b>\n#end html\n#else\n#markdown\n**skipped**\n#end markdown\n#end if\nwww = 2";
            var html = Render(source);
            Assert.Contains("<b>taken</b>", html);
            Assert.DoesNotContain("skipped", html);
            Assert.Contains("www", html);
            Assert.DoesNotContain("class=\"err\"", html);
        }

        [Fact]
        public void HtmlContent_RepeatsInsideLoop()
        {
            Assert.Equal(3, Count(Render("#for i = 1 : 3\n#html\n<hr/>\n#end html\n#loop"), "<hr/>"));
        }

        [Theory]
        [InlineData("#if 1")]
        [InlineData("#for i = 1 : 2")]
        [InlineData("#hide")]
        [InlineData("#md on")]
        [InlineData("#end if")]
        [InlineData("#val")]
        public void CalcpadKeywords_AreRejectedInHtmlMode(string line)
        {
            Assert.Contains(ModeError, Render($"#html\n{line}\n#end html"));
        }

        [Fact]
        public void CalcpadKeywords_AreRejectedInMarkdownMode()
        {
            Assert.Contains(ModeError, Render("#markdown\n#round 2\n#end markdown"));
        }

        [Fact]
        public void MarkdownHeadings_AreContentNotKeywords()
        {
            var html = Render("#markdown\n# One\n## Two\n#end markdown");
            Assert.Contains(">One</h1>", html);
            Assert.Contains(">Two</h2>", html);
            Assert.DoesNotContain(ModeError, html);
        }

        [Fact]
        public void MacroCalls_ExpandInsideHtml()
        {
            Assert.Contains("<b>hi</b>", ExpandAndRender("#def greet$ = <b>hi</b>\n#html\ngreet$\n#end html\n"));
        }

        [Fact]
        public void MacroWithArguments_DefinedInsideHtml_Expands()
        {
            var html = ExpandAndRender("#html\n#def note$(text$) = <div class=\"note\">text$</div>\nnote$(Check deflection at midspan.)\n#end html\n");
            Assert.Contains("<div class=\"note\">Check deflection at midspan.</div>", html);
            Assert.DoesNotContain("class=\"err\"", html);
        }

        [Fact]
        public void MacrosThatSwitchMode_ApplyAtTheCallSite()
        {
            var html = ExpandAndRender("#def open$\n#html\n#end def\n#def close$\n#end html\n#end def\nopen$\n<b>raw</b>\nclose$\nddd = 4\n");
            Assert.Contains("<b>raw</b>", html);
            Assert.Contains("ddd", html);
            Assert.DoesNotContain("class=\"err\"", html);
        }

        [Fact]
        public void Markdown_SupportsStrikethroughAndAutolinks()
        {
            var html = Render("#markdown\n~~old~~ see https://example.com\n#end markdown");
            Assert.Contains("<del>old</del>", html);
            Assert.Contains("<a href=\"https://example.com\">", html);
        }

        [Fact]
        public void TrailingOperatorCharacters_DoNotJoinHtmlLines()
        {
            var html = Render("#html\n<style>\np { color: red;\n}\n</style>\n#end html");
            Assert.Contains("p { color: red;\n}", html);
        }
    }
}
