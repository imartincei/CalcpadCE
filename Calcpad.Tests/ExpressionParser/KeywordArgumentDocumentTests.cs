namespace Calcpad.Tests
{
    public class KeywordArgumentDocumentTests
    {
        // Mirrors the real pipeline: macro expansion, then expression parsing.
        private static string Render(string source)
        {
            var macroParser = new MacroParser();
            Assert.False(macroParser.Parse(source, out var unwrapped, null, 0, false), unwrapped);
            var parser = new ExpressionParser { Settings = new Settings() };
            parser.Parse(unwrapped, true, false);
            return parser.HtmlResult;
        }

        [Fact]
        public void FunctionDefaults_RenderThroughTheFullPipeline()
        {
            var html = Render("f(x; y = 2; z = 3) = x + y + z\nr = f(1)\ns = f(1; z = 10)\nt = f(z = 10; x = 1; y = 2)");
            Assert.DoesNotContain("Error", html);
            Assert.Contains("6", html);
            Assert.Contains("13", html);
        }

        [Fact]
        public void MacroDefaults_RenderThroughTheFullPipeline()
        {
            var html = Render("#def m$(a$; b$ = 10; c$ = 5) = a$ + b$ + c$\nr = m$(1)\ns = m$(1; c$ = 7)");
            Assert.DoesNotContain("Error", html);
            Assert.Contains("16", html);
            Assert.Contains("18", html);
        }

        [Fact]
        public void MacroDefaultsFeedingAFunctionWithDefaults()
        {
            var html = Render("f(x; k = 2) = x*k\n#def call$(v$; f$ = 3) = f(v$; f$)\nr = call$(4)\ns = call$(4; f$ = 5)");
            Assert.DoesNotContain("Error", html);
            Assert.Contains("12", html);
            Assert.Contains("20", html);
        }

        [Theory]
        [InlineData("$Inline")]
        [InlineData("$Block")]
        public void CommandBlockFunctionDefaults_RenderThroughTheFullPipeline(string block)
        {
            var html = Render($"f(x; y = 2; z = 3) = {block}{{a = x*100 + y*10 + z}}\nr = f(1)\ns = f(1; z = 7)\nt = f(z = 5; x = 4; y = 6)");
            Assert.DoesNotContain("Error", html);
            Assert.Contains("123", html);
            Assert.Contains("127", html);
            Assert.Contains("465", html);
        }

        [Fact]
        public void DefinitionRendersDefaults()
        {
            var html = Render("f(x; y = 2) = x + y");
            Assert.Contains("<var>y</var> = 2", html);
        }

        [Fact]
        public void CallRendersAsWritten()
        {
            var html = Render("f(x; y = 2; z = 3) = x + y + z\nr = f(1; z = 10)");
            Assert.Contains("1; <var>z</var> = 10", html);
            Assert.DoesNotContain("1; 2; 10", html);
        }
    }
}
