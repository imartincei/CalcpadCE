using System.Text.RegularExpressions;

namespace Calcpad.Tests
{
    public class PlotUndefinedTests
    {
        private static string Render(string source)
        {
            var parser = new ExpressionParser { Settings = new Settings() };
            parser.Parse(source, true, false);
            return parser.HtmlResult;
        }

        private static int Count(string html, string tag) => Regex.Matches(html, $"<{tag}[ >]").Count;

        [Theory]
        [InlineData("f(x) = $Repeat{y = 0/0 @ k = 1 : 1}")]
        [InlineData("f(x) = $Repeat{y = if(x < 0.5; 0/0; x) @ k = 1 : 2}")]
        [InlineData("f(x) = $Repeat{y = if(k ≡ 1; 0/0; x*1m) @ k = 1 : 2}")]
        [InlineData("f(x) = $Repeat{y = if(x < 0.5; x*1m; 0/0) @ k = 1 : 2}")]
        [InlineData("f(x) = if(x < 0.5; x*1m; 0/0)")]
        [InlineData("f(x) = $Repeat{y = x/(k - 1) @ k = 1 : 3}")]
        [InlineData("f(x) = $Repeat{y = if(x < 0.5; x*1m; 1/0) @ k = 1 : 2}")]
        [InlineData("f(x) = if(x < 0.5; x*1m; -1/0)")]
        public void Plot_FunctionWithUndefinedValues_Renders(string definition)
        {
            var html = Render($"PlotSVG = 1\n{definition}\n$Plot{{f(x) @ x = 0 : 1}}");
            Assert.DoesNotContain("class=\"err\"", html);
            Assert.Contains("<svg", html);
        }

        [Theory]
        [InlineData(0, "0/0")]
        [InlineData(1, "0/0")]
        [InlineData(0, "1/0")]
        [InlineData(1, "1/0")]
        public void Plot_UndefinedInterval_LeavesGap(int adaptive, string gapValue)
        {
            var html = Render($"PlotSVG = 1\nPlotAdaptive = {adaptive}\nf(x) = $Repeat{{y = if(abs(x - 0.5) < 0.2; {gapValue}; x*k*1m) @ k = 1 : 2}}\n$Plot{{f(x) @ x = 0 : 1}}");
            Assert.DoesNotContain("class=\"err\"", html);
            Assert.Equal(2, Count(html, "polyline"));
            Assert.Equal(2, Count(html, "polygon"));
        }

        [Fact]
        public void Plot_DefinedFunction_SingleSegment()
        {
            var html = Render("PlotSVG = 1\nf(x) = x^2\n$Plot{f(x) @ x = 0 : 1}");
            Assert.Equal(1, Count(html, "polyline"));
        }
    }
}
