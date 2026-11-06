namespace Calcpad.Tests
{
    public class AbsoluteTemperatureTests
    {
        private const double Tol = 1e-12;

        #region Scalars
        [Theory]
        [Trait("Category", "AbsoluteTemperature")]
        [InlineData("20°C*2", "586.3K")]
        [InlineData("2*20°C", "586.3K")]
        [InlineData("-20°C", "-20°C")]
        [InlineData("30°C - 20°C", "10Δ°C")]
        [InlineData("20°C + 5Δ°C", "25°C")]
        [InlineData("20°C + 5K", "25°C")]
        [InlineData("5K + 20°C", "25°C")]
        [InlineData("5Δ°C + 20°C", "25°C")]
        [InlineData("20°C - 5K", "15°C")]
        [InlineData("20°C - 5Δ°C", "15°C")]
        [InlineData("300K - 20°C", "6.85K")]
        [InlineData("(20°C + 30°C)/2|°C", "25°C")]
        [InlineData("20°C*2|°C", "313.15°C")]
        [InlineData("10Δ°C*2", "20Δ°C")]
        [InlineData("212°F - 10°C|°C", "90°C")]
        [InlineData("68°F - 32°F", "36Δ°F")]
        [InlineData("68°F + 9Δ°F", "77°F")]
        [InlineData("68°F*2", "1055.34°R")]
        [InlineData("180°F - 10°C|Δ°C", "72.22Δ°C")]
        [InlineData("30°C - 18°F", "37.78Δ°C")]
        [InlineData("30°C + 18°F", "568.52K")]
        [InlineData("20°C + 30°C", "596.3K")]
        [InlineData("(30°C - 20°C)|K", "10K")]
        public void Expression(string expression, string expected) => Test(expression, expected);

        [Theory]
        [Trait("Category", "AbsoluteTemperature")]
        [InlineData("20°C|K", 293.15)]
        [InlineData("8.314J/(mol*K)*20°C", 8.314 * 293.15)]
        [InlineData("101325Pa*1m^3/(8.314J/(mol*K)*20°C)", 101325d / (8.314 * 293.15))]
        [InlineData("30°C/20°C", 303.15 / 293.15)]
        [InlineData("(20°C)^2", 293.15 * 293.15)]
        [InlineData("20°C^2", 20d)]
        [InlineData("sqrt(20°C)", 17.12162375477279)]
        [InlineData("root(20°C; 3)", 6.642985423858836)]
        [InlineData("20°C\\(2K)", 146d)]
        [InlineData("4.186kJ/(kg*Δ°C)*2kg*(30°C - 20°C)", 83.72)]
        [InlineData("4.186kJ/(kg*°C)*2kg*(30°C - 20°C)", 83.72)]
        [InlineData("1.2*10^-5/°C*(30°C - 20°C)", 1.2e-4)]
        [InlineData("20°C ≡ 68°F", 1d)]
        [InlineData("20°C ≡ 293.15K", 1d)]
        [InlineData("20°C < 300K", 1d)]
        [InlineData("20°C > 50°F", 1d)]
        [InlineData("$Sum{k*°C*2 @ k = 20 : 20}", 586.3)]
        [InlineData("$Sum{(k*°C - 10°C) @ k = 20 : 20}", 10d)]
        public void Value(string expression, double expected) => Test(expression, expected);

        [Fact]
        [Trait("Category", "AbsoluteTemperature")]
        public void Variables()
        {
            var calc = new TestCalc(Absolute());
            calc.Run("T_1 = 20°C");
            calc.Run("T_2 = 30°C");
            calc.Run("R = 8.314J/(mol*K)");
            calc.Run("ΔT = T_2 - T_1");
            Assert.Equal("10Δ°C", calc.ToString());
            calc.Run("R*T_1");
            Assert.Equal(8.314 * 293.15, calc.Real, 9);
            calc.Run("T_1 + ΔT");
            Assert.Equal("30°C", calc.ToString());
        }

        [Fact]
        [Trait("Category", "AbsoluteTemperature")]
        public void UserFunction()
        {
            var calc = new TestCalc(Absolute());
            calc.Run("f(T) = 8.314J/(mol*K)*T");
            calc.Run("f(20°C)");
            Assert.Equal(8.314 * 293.15, calc.Real, 9);
        }

        [Fact]
        [Trait("Category", "AbsoluteTemperature")]
        public void Complex()
        {
            var calc = new TestCalc(new() { IsComplex = true, AbsoluteTemperature = true });
            calc.Run("20°C*2");
            Assert.Equal("586.3K", calc.ToString());
            calc.Run("30°C - 20°C");
            Assert.Equal("10Δ°C", calc.ToString());
            calc.Run("20°C + 5K");
            Assert.Equal("25°C", calc.ToString());
            calc.Run("sqrt(20°C)");
            Assert.Equal(17.12162375477279, calc.Real, 9);
        }
        #endregion

        #region Vectors and matrices
        [Theory]
        [Trait("Category", "AbsoluteTemperature")]
        [InlineData("[20°C; 30°C]*2", "[586.3K 606.3K]")]
        [InlineData("2*[20°C; 30°C]", "[586.3K 606.3K]")]
        [InlineData("[20; 30]*°C*2", "[586.3K 606.3K]")]
        [InlineData("[30°C; 40°C] - [20°C; 20°C]", "[10Δ°C 20Δ°C]")]
        [InlineData("[30°C; 40°C] - 20°C", "[10Δ°C 20Δ°C]")]
        [InlineData("[20°C; 30°C] + 5K", "[25°C 35°C]")]
        [InlineData("5Δ°C + [20°C; 30°C]", "[25°C 35°C]")]
        [InlineData("[5Δ°C; 10Δ°C] + 20°C", "[25°C 30°C]")]
        [InlineData("hp([20°C; 30°C])*2", "[586.3K 606.3K]")]
        [InlineData("2*hp([20°C; 30°C])", "[586.3K 606.3K]")]
        [InlineData("hp([30°C; 40°C]) - hp([20°C; 20°C])", "[10Δ°C 20Δ°C]")]
        [InlineData("hp([30°C; 40°C]) - 20°C", "[10Δ°C 20Δ°C]")]
        [InlineData("hp([20°C; 30°C]) + 5K", "[25°C 35°C]")]
        [InlineData("hp([5Δ°C; 10Δ°C]) + 20°C", "[25°C 30°C]")]
        [InlineData("hp([5Δ°C; 10Δ°C])*2", "[10Δ°C 20Δ°C]")]
        [InlineData("[20°C; 30°C]^2|K^2", "[85936.9K^2 91899.9K^2]")]
        [InlineData("sqrt([20°C; 30°C])|K^0.5", "[17.12K^0.5 17.41K^0.5]")]
        [InlineData("hp([20°C; 30°C])^2|K^2", "[85936.9K^2 91899.9K^2]")]
        public void Vector(string expression, string expected) => Test(expression, expected);

        [Theory]
        [Trait("Category", "AbsoluteTemperature")]
        [InlineData("[20°C; 30°C|40°C; 50°C]*2", "[586.3K 606.3K|626.3K 646.3K]")]
        [InlineData("[30°C; 40°C|50°C; 60°C] - [20°C; 20°C|20°C; 20°C]", "[10Δ°C 20Δ°C|30Δ°C 40Δ°C]")]
        [InlineData("[20°C; 30°C|40°C; 50°C] + 5K", "[25°C 35°C|45°C 55°C]")]
        [InlineData("hp([20°C; 30°C|40°C; 50°C])*2", "[586.3K 606.3K|626.3K 646.3K]")]
        [InlineData("hp([30°C; 40°C|50°C; 60°C]) - hp([20°C; 20°C|20°C; 20°C])", "[10Δ°C 20Δ°C|30Δ°C 40Δ°C]")]
        [InlineData("hp([20°C; 30°C|40°C; 50°C]) + 5K", "[25°C 35°C|45°C 55°C]")]
        public void Matrix(string expression, string expected) => Test(expression, expected);
        #endregion

        #region Setting off (default)
        [Fact]
        [Trait("Category", "AbsoluteTemperature")]
        public void OffByDefault() => Assert.False(new MathSettings().AbsoluteTemperature);

        [Theory]
        [Trait("Category", "AbsoluteTemperature")]
        [InlineData("20°C*2", "40°C")]
        [InlineData("30°C - 20°C", "10°C")]
        [InlineData("180°F - 18°F", "162°F")]
        [InlineData("30°C + 18°F", "40°C")]
        [InlineData("[20°C; 30°C]*2", "[40°C 60°C]")]
        [InlineData("hp([20°C; 30°C])*2", "[40°C 60°C]")]
        public void Disabled(string expression, string expected)
        {
            var calc = new TestCalc(new());
            calc.Run(expression);
            Assert.Equal(expected, calc.ToString());
        }

        [Fact]
        [Trait("Category", "AbsoluteTemperature")]
        public void SettingsDirective()
        {
            var parser = new ExpressionParser { Settings = new Settings() };
            parser.Parse("a = 20°C*2\n#settings {\"absoluteTemperature\": true}\nb = 20°C*2", true, false);
            var html = parser.HtmlResult;
            var off = html.IndexOf("40", StringComparison.Ordinal);
            var on = html.IndexOf("586.3", StringComparison.Ordinal);
            Assert.True(off >= 0 && on > off, html);
        }
        #endregion

        private static MathSettings Absolute() => new() { AbsoluteTemperature = true };

        private static void Test(string expression, double expected)
        {
            var result = new TestCalc(Absolute()).Run(expression);
            Assert.True(Math.Abs(expected - result) < Tol * Math.Abs(expected), $"{expression}: expected {expected}, got {result}");
        }

        private static void Test(string expression, string expected)
        {
            var calc = new TestCalc(Absolute());
            calc.Run(expression);
            Assert.Equal(expected, calc.ToString());
        }
    }
}
