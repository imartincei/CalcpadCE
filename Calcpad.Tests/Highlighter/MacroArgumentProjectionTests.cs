using System.Collections.Generic;
using System.Linq;
using Calcpad.Highlighter.ContentResolution;
using Calcpad.Highlighter.Tokenizer;
using Calcpad.Highlighter.Tokenizer.Models;

namespace Calcpad.Tests.Highlighter
{
    /// <summary>
    /// Macro call arguments take their token types from MacroParser's real expansion, projected
    /// back to the call site.
    /// </summary>
    public class MacroArgumentProjectionTests
    {
        private static List<Token> Tokenize(string content, RuntimeVariableNames names = null)
        {
            var tokenizer = new CalcpadTokenizer();
            tokenizer.SetResolvedContent(new ContentResolver().GetStagedContent(content), names);
            return tokenizer.Tokenize(content).Tokens;
        }

        private static Token Find(List<Token> tokens, int line, string text) =>
            tokens.Single(t => t.Line == line && t.Text == text);

        [Fact]
        public void CommentArgument_IsComment()
        {
            var tokens = Tokenize("#def note$(t$) = 'Note: t$'\nnote$(Beam span)");
            var token = Find(tokens, 1, "Beam span");
            Assert.Equal(TokenType.Comment, token.Type);
            Assert.Equal(6, token.Column);
        }

        [Fact]
        public void ArgumentInExpressionAndComment_IsExpression()
        {
            var tokens = Tokenize("#def show$(x$) = 'x$ = 'x$\nq = 1\nshow$(q)");
            Assert.Equal(TokenType.Variable, Find(tokens, 2, "q").Type);
        }

        [Fact]
        public void ArgumentClosingTheComment_IsExpression()
        {
            var tokens = Tokenize("#def lbl$(s$) = '<b>s$</b>'\nF_x = 1\nlbl$(F = 'F_x'kN)");
            Assert.Equal(TokenType.Variable, Find(tokens, 2, "F_x").Type);
            Assert.Equal(TokenType.Comment, Find(tokens, 2, "'kN").Type);
        }

        [Fact]
        public void NestedCall_KeepsCallSyntax()
        {
            var tokens = Tokenize("#def in$(p$) = p$*2\n#def out$(q$) = r = in$(q$) + 1\na = 3\nout$(in$(a))");
            Assert.Equal(TokenType.Macro, Find(tokens, 3, "in$").Type);
            Assert.Equal(TokenType.Variable, Find(tokens, 3, "a").Type);
            Assert.Equal(4, tokens.Count(t => t.Line == 3 && t.Type == TokenType.Bracket));
        }

        [Fact]
        public void KeywordArgumentName_IsMacroParameter()
        {
            var tokens = Tokenize("#def calc$(a$; b$ = 10) = a$ + b$\nm = calc$(1; b$ = 7)");
            Assert.Equal(TokenType.MacroParameter, Find(tokens, 1, "b$").Type);
            Assert.Equal(TokenType.Const, Find(tokens, 1, "7").Type);
        }

        [Fact]
        public void FunctionName_IsFunction()
        {
            var tokens = Tokenize("f(x) = x^2\n#def apply$(fn$) = y = fn$(2)\napply$(f)");
            Assert.Equal(TokenType.Function, Find(tokens, 2, "f").Type);
        }

        [Fact]
        public void CallInsideDefBody_UsesFirstExpansion()
        {
            const string content = "#def lbl$(s$) = '<b>s$</b>'\n#def frame$(n$)\nlbl$(n$ = 'F_x'kN)\n#end def\nF_x = 1\nframe$(Fx)";
            var tokens = Tokenize(content);
            Assert.Equal(TokenType.MacroParameter, Find(tokens, 2, "n$").Type);
            Assert.Equal(TokenType.Variable, Find(tokens, 2, "F_x").Type);
        }

        [Fact]
        public void CallInsideUncalledDefBody_IsLiteral()
        {
            var tokens = Tokenize("#def lbl$(s$) = '<b>s$</b>'\n#def frame$\nlbl$(F = 'F_x'kN)\n#end def");
            Assert.Equal(TokenType.Comment, Find(tokens, 2, "'F_x'").Type);
        }

        [Fact]
        public void UnitNamedArgument_FollowsLastRun()
        {
            const string content = "#def set$(x$) = c = x$\nx = 1\n#if x > 5\na = 5\n#end if\nset$(a)";
            var names = new RuntimeVariableNames(["x", "c"], ["x", "a", "c"], true);
            Assert.Equal(TokenType.Variable, Find(Tokenize(content), 5, "a").Type);
            Assert.Equal(TokenType.Units, Find(Tokenize(content, names), 5, "a").Type);
        }
    }
}
