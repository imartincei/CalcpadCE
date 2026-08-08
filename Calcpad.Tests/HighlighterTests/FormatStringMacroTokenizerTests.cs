using System.Linq;
using Calcpad.Highlighter.Tokenizer;
using Calcpad.Highlighter.Tokenizer.Models;

namespace Calcpad.Tests.HighlighterTests
{
    public class FormatStringMacroTokenizerTests
    {
        [Fact]
        public void MacroReference_ImmediatelyAfterFormatString_TokenizesSeparately()
        {
            const string source = "#def text$ = 'text'\nf = 5ft|in:f2text$";

            var tokens = new CalcpadTokenizer().Tokenize(source).Tokens;

            Assert.Contains(tokens, t => t.Type == TokenType.Format && t.Text == "f2");
            Assert.Contains(tokens, t => t.Type == TokenType.Macro && t.Text == "text$");
        }

        [Theory]
        [InlineData("e3")]
        [InlineData("g3")]
        [InlineData("n3")]
        [InlineData("c3")]
        public void MacroReference_AfterVariousFormatSpecifiers_TokenizesSeparately(string formatSpec)
        {
            var source = "#def note$ = 'text'\nx = 123.456:" + formatSpec + "note$";

            var tokens = new CalcpadTokenizer().Tokenize(source).Tokens;

            Assert.Contains(tokens, t => t.Type == TokenType.Format && t.Text == formatSpec);
            Assert.Contains(tokens, t => t.Type == TokenType.Macro && t.Text == "note$");
        }

        [Fact]
        public void FormatString_WithoutTrailingMacro_StillTokenizesAsSingleFormatToken()
        {
            const string source = "x = 123.456:f3";

            var tokens = new CalcpadTokenizer().Tokenize(source).Tokens;

            Assert.Contains(tokens, t => t.Type == TokenType.Format && t.Text == "f3");
        }
    }
}
