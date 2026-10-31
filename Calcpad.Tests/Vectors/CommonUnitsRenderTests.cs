using System.Text.RegularExpressions;

namespace Calcpad.Tests
{
    // Standard vectors and matrices whose elements all have the same units
    // show those units once after the closing bracket, like hp arrays.
    public class CommonUnitsRenderTests
    {
        private const string VectorStart = "<b class=\"b0\">[</b>";
        private const string VectorEnd = "<b class=\"b0\">]</b>";
        private const string MatrixStart = "<span class=\"matrix\">";

        private static string Render(string source)
        {
            var parser = new ExpressionParser { Settings = new Settings() };
            parser.Parse(source, true, false);
            return parser.HtmlResult.Replace("\r\n", "\n");
        }

        // The result is the last array in the line, after the input expression.
        private static string Result(string html, string start) =>
            html[html.LastIndexOf(start, StringComparison.Ordinal)..];

        private static int Count(string html, string value) => Regex.Count(html, Regex.Escape(value));

        [Fact]
        public void Vector_WithSameUnits_ShowsUnitsOnceAfterBracket()
        {
            var result = Result(Render("v = [1; 2; 3]*mm"), VectorStart);
            Assert.Equal(1, Count(result, "<i>mm</i>"));
            Assert.Contains(VectorEnd + " <i>mm</i>", result);
        }

        [Fact]
        public void Vector_WithConvertedTargetUnits_ShowsUnitsOnce()
        {
            var result = Result(Render("v = [1000; 2000]*mm|m"), VectorStart);
            Assert.Equal(1, Count(result, "<i>m</i>"));
            Assert.Contains(VectorEnd + " <i>m</i>", result);
        }

        [Fact]
        public void Vector_WithDifferentUnits_KeepsUnitsPerElement()
        {
            var result = Result(Render("v = [1kg; 2s]"), VectorStart);
            Assert.Equal(1, Count(result, "<i>kg</i>"));
            Assert.Equal(1, Count(result, "<i>s</i>"));
            Assert.DoesNotContain(VectorEnd + " ", result);
        }

        [Fact]
        public void Vector_WithUnitlessZero_StillSharesUnits()
        {
            var result = Result(Render("v = [0; 2mm; 3mm]"), VectorStart);
            Assert.Equal(1, Count(result, "<i>mm</i>"));
        }

        [Fact]
        public void Vector_WithoutUnits_IsUnchanged()
        {
            var result = Result(Render("v = [1; 2; 3]"), VectorStart);
            Assert.DoesNotContain(VectorEnd + " ", result);
        }

        [Fact]
        public void HpVector_IsUnchanged()
        {
            var result = Result(Render("v = hp([1; 2; 3]*mm)"), VectorStart);
            Assert.Equal(1, Count(result, "<i>mm</i>"));
        }

        [Fact]
        public void Matrix_WithSameUnits_ShowsUnitsOnce()
        {
            var result = Result(Render("M = [1; 2|3; 4]*mm"), MatrixStart);
            Assert.Equal(1, Count(result, "<i>mm</i>"));
        }

        [Fact]
        public void Matrix_WithDifferentUnits_KeepsUnitsPerElement()
        {
            var result = Result(Render("M = [1kg; 2s|3kg; 4s]"), MatrixStart);
            Assert.Equal(2, Count(result, "<i>kg</i>"));
            Assert.Equal(2, Count(result, "<i>s</i>"));
        }
    }
}
