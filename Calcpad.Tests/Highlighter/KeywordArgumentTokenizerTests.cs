using System.Linq;
using Calcpad.Highlighter.Tokenizer;
using Calcpad.Highlighter.Tokenizer.Models;

namespace Calcpad.Tests.Highlighter
{
    /// <summary>
    /// A keyword argument names a parameter of the called function, so it must tokenize as a
    /// local variable - not as a unit when the name collides with one (y, m, s, ...).
    /// </summary>
    public class KeywordArgumentTokenizerTests
    {
        private static TokenType TypeOf(string source, string text, int line) =>
            new CalcpadTokenizer().Tokenize(source).Tokens
                .First(t => t.Line == line && t.Text == text).Type;

        [Theory]
        [InlineData("y")]
        [InlineData("x")]
        [InlineData("z")]
        public void KeywordArgumentName_IsLocalVariable(string name)
        {
            var source = "f(x; y = 2; z = 3) = x + y + z\nr = f(" + name + " = 1)";

            Assert.Equal(TokenType.LocalVariable, TypeOf(source, name, 1));
        }

        [Fact]
        public void KeywordArgumentName_InNestedCall_IsLocalVariable()
        {
            const string source = "f(x; y = 2; z = 3) = x + y + z\nn = f(x = f(1; z = 0); y = 0)";

            var tokens = new CalcpadTokenizer().Tokenize(source).Tokens
                .Where(t => t.Line == 1 && t.Type == TokenType.LocalVariable)
                .Select(t => t.Text);

            Assert.Equal(new[] { "x", "z", "y" }, tokens);
        }

        [Fact]
        public void KeywordArgumentName_WithUnitValuedDefault_IsLocalVariable()
        {
            const string source = "w(L; q = 5kN/m) = q*L^2/8\nM = w(6m; q = 12kN/m)";

            Assert.Equal(TokenType.LocalVariable, TypeOf(source, "q", 1));
        }

        [Fact]
        public void EqualsInCallOfFunctionWithoutDefaults_IsNotKeywordArgument()
        {
            const string source = "g(x; y) = x + y\nr = g(1; y = 3)";

            Assert.Equal(TokenType.Units, TypeOf(source, "y", 1));
        }

        [Fact]
        public void EqualsInBuiltInFunctionCall_StaysComparison()
        {
            const string source = "y = 7\nr = if(y = 2; 1; 0)";

            Assert.Equal(TokenType.Variable, TypeOf(source, "y", 1));
        }

        [Fact]
        public void MacroParameterAfterDefault_IsMacroParameter()
        {
            const string source = "#def calc$(a$; b$ = 10; c$ = 5) = a$ + b$ + c$";

            var tokens = new CalcpadTokenizer().Tokenize(source).Tokens
                .Where(t => t.Text == "c$")
                .Select(t => t.Type);

            Assert.Equal(new[] { TokenType.MacroParameter, TokenType.MacroParameter }, tokens);
        }

        [Fact]
        public void MultilineMacroBody_KeepsParametersAfterDefault()
        {
            const string source = "#def convert$(v$; u$ = mm)\n\tv$|u$\n#end def";

            var tokens = new CalcpadTokenizer().Tokenize(source).Tokens
                .Where(t => t.Line == 1)
                .Select(t => t.Type);

            Assert.Equal(
                new[] { TokenType.MacroParameter, TokenType.Bracket, TokenType.MacroParameter },
                tokens);
        }

        [Fact]
        public void MacroKeywordArgumentName_IsMacroParameter()
        {
            const string source = "#def calc$(a$; b$ = 10; c$ = 5) = a$ + b$ + c$\nm = calc$(1; c$ = 7)";

            Assert.Equal(TokenType.MacroParameter, TypeOf(source, "c$", 1));
        }

        [Fact]
        public void MacroCall_TokenizesEveryArgument()
        {
            const string source = "#def calc$(a$; b$ = 10; c$ = 5) = a$ + b$ + c$\nm = calc$(1; 2; 3)";

            var tokens = new CalcpadTokenizer().Tokenize(source).Tokens
                .Where(t => t.Line == 1 && t.Type == TokenType.Const)
                .Select(t => t.Text);

            Assert.Equal(new[] { "1", "2", "3" }, tokens);
        }

        [Fact]
        public void NameAfterCall_ResolvesNormally()
        {
            const string source = "f(x; y = 2) = x + y\ny = 7\nr = f(1; y = 2) + y";

            var tokens = new CalcpadTokenizer().Tokenize(source).Tokens
                .Where(t => t.Line == 2 && t.Text == "y")
                .Select(t => t.Type);

            Assert.Equal(new[] { TokenType.LocalVariable, TokenType.Variable }, tokens);
        }
    }
}
