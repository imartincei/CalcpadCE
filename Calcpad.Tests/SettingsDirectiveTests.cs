namespace Calcpad.Tests
{
    public class SettingsDirectiveTests
    {
        private static string Render(string source)
        {
            var parser = new ExpressionParser { Settings = new Settings() };
            parser.Parse(source, true, false);
            return parser.HtmlResult;
        }

        [Fact]
        public void SettingsDirective_AppliesDecimals()
        {
            var html = Render("#settings {\"decimals\": 4}\nx = 6.12345");
            Assert.Contains("6.1235", html);
        }

        [Fact]
        public void SettingsDirective_ChangesSettingsMidFile()
        {
            // The directive applies to every line after it, so a second directive
            // must change the precision of subsequent output.
            var html = Render("#settings {\"decimals\": 4}\nx = 6.12345\n#settings {\"decimals\": 2}\ny = 8.7654");
            Assert.Contains("6.1235", html);       // x, four decimals
            Assert.Contains("8.77", html);         // y, two decimals
            Assert.DoesNotContain("8.7654", html); // y is not rendered at four decimals
        }

        [Fact]
        public void SettingsDirective_MalformedJson_ReportsError()
        {
            var parser = new ExpressionParser { Settings = new Settings(), Debug = true };
            parser.Parse("#settings {not valid}\nx = 1", true, false);
            Assert.Contains("Invalid settings", parser.HtmlResult);
        }

        [Fact]
        public void SettingsDirective_UnknownKey_IsIgnored()
        {
            // Unrecognized keys are silently ignored by the engine (the linter warns).
            var parser = new ExpressionParser { Settings = new Settings(), Debug = true };
            parser.Parse("#settings {\"nonsense\": 4}\nx = 1", true, false);
            Assert.DoesNotContain("Invalid settings", parser.HtmlResult);
        }
    }
}
