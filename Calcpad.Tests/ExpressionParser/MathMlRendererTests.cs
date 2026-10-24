using System.Text.RegularExpressions;

namespace Calcpad.Tests
{
    public class MathMlRendererTests
    {
        private const string MathMl = "#settings {\"mathRenderer\": \"mathml\"}\n";

        private static string Render(string source)
        {
            var parser = new ExpressionParser { Settings = new Settings() };
            parser.Parse(source, true, false);
            return parser.HtmlResult;
        }

        private static string[] MathElements(string html) =>
            Regex.Matches(html, "<math>(.*?)</math>", RegexOptions.Singleline)
                .Select(m => m.Groups[1].Value)
                .ToArray();

        private static List<string> StrayText(string math)
        {
            var stray = new List<string>();
            var depth = 0;
            foreach (var part in Regex.Split(math, "(<[^>]+>)"))
            {
                if (part.StartsWith('<'))
                {
                    var name = Regex.Match(part, "^</?([a-zA-Z]+)").Groups[1].Value;
                    if (name is "mi" or "mn" or "mo" or "mtext" or "ms" && !part.EndsWith("/>"))
                        depth += part.StartsWith("</") ? -1 : 1;
                }
                else if (depth == 0 && part.Trim().Length > 0)
                    stray.Add(part);
            }
            return stray;
        }

        [Fact]
        public void Default_RendersClassicHtml()
        {
            var html = Render("x = 1/1^(5/4)");
            Assert.DoesNotContain("<math>", html);
            Assert.Contains("class=\"dvc\"", html);
        }

        [Fact]
        public void MathMl_RendersFractionExponentInDenominator()
        {
            var html = Render(MathMl + "x = 1/1^(5/4)");
            var math = Assert.Single(MathElements(html));
            Assert.Contains("<mfrac><mrow><mn>1</mn></mrow><mrow><msup><mrow><mn>1</mn></mrow><mrow><mfrac>", math);
            Assert.DoesNotContain("dvc", html);
        }

        [Fact]
        public void MathMl_SwitchesMidFile()
        {
            var html = Render("a = 1/2\n" + MathMl + "b = 1/2\n#settings {\"mathRenderer\": \"html\"}\nc = 1/2");
            var equations = Regex.Matches(html, "<span class=\"eq\">(.*?)</span></p>", RegexOptions.Singleline)
                .Select(m => m.Groups[1].Value.StartsWith("<math>"))
                .ToArray();
            Assert.Equal([false, true, false], equations);
        }

        [Theory]
        [InlineData("latex")]
        [InlineData("1")]
        public void MathMl_InvalidValueReportsError(string value)
        {
            var html = Render($"#settings {{\"mathRenderer\": \"{value}\"}}\nx = 1");
            Assert.Contains("'mathRenderer' must be one of: html, mathml", html);
            Assert.DoesNotContain("<math>", html);
        }

        [Fact]
        public void MathMl_SolverBlocksUseUnderOver()
        {
            var html = Render(MathMl + "s = $Sum{1/k^2 @ k = 1 : 10}\nr = $Root{x^2 - 2 @ x = 0 : 2}");
            Assert.Contains("<munderover><mo class=\"m-nary\"", html);
            Assert.Contains("<mtext class=\"cond\">", html);
            Assert.DoesNotContain("class=\"dvr\"", html);
        }

        [Fact]
        public void MathMl_InputFieldSitsInsideMtext()
        {
            var parser = new ExpressionParser { Settings = new Settings() };
            parser.Parse(MathMl + "a = ?", false, false);
            Assert.Contains("<mtext><input type=\"text\"", parser.HtmlResult);
        }

        [Theory]
        [InlineData("x = -(-3)^2")]
        [InlineData("L = 2.5m\nA = L^2/(3kN/m^2)")]
        [InlineData("y = sqrt(2)^(3/2) + root(8; 3)")]
        [InlineData("f(x; y) = x^2/y\nf(2; 3)")]
        [InlineData("v = [1; 2; 3]\nw = v/norm(v)")]
        [InlineData("M = [1; 2|3; 4]\nN = inverse(M)/det(M)")]
        [InlineData("t = if(1 < 2; 1/2; 2/1)\nu = 1/(1 + switch(t < 1; t/2; t < 2; t^2/3; 1))")]
        [InlineData("h = $Block{p = 2; q = p^2/3; q + 1}\ng = $Repeat{k = k + 1 @ j = 1 : 5}")]
        [InlineData("θ = 30°\nT = 20°C\ns = 1/(0.5*10^(-8))")]
        public void MathMl_HasNoTextOutsideTokens(string source)
        {
            var html = Render(MathMl + source);
            Assert.DoesNotContain("class=\"err\"", html);
            var maths = MathElements(html);
            Assert.NotEmpty(maths);
            foreach (var math in maths)
            {
                Assert.Empty(StrayText(math));
                Assert.DoesNotMatch("<(span|b|i|sup|sub|var|em|small)\\b", math);
            }
        }
    }
}
