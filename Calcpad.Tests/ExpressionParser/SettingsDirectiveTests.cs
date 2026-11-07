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

        private static string PlotWidthStyle(string source)
        {
            var m = System.Text.RegularExpressions.Regex.Match(Render(source), @"width:(\d+)pt");
            return m.Success ? m.Groups[1].Value : null;
        }

        [Fact]
        public void SettingsDirective_LaterDirective_OverridesEarlierVariable()
        {
            const string plot = "\n$Plot{ x^2 @ x = 0 : 2 }";
            var variableOnly = PlotWidthStyle("PlotWidth = 800" + plot);
            var directiveOnly = PlotWidthStyle("#settings {\"plotWidth\": 400}" + plot);
            // #settings after the variable assignment must win (latest value).
            var directiveAfterVariable = PlotWidthStyle("PlotWidth = 800\n#settings {\"plotWidth\": 400}" + plot);

            Assert.NotNull(variableOnly);
            Assert.NotEqual(variableOnly, directiveOnly);
            Assert.Equal(directiveOnly, directiveAfterVariable);
        }

        [Fact]
        public void SettingsDirective_LaterVariable_OverridesEarlierDirective()
        {
            const string plot = "\n$Plot{ x^2 @ x = 0 : 2 }";
            var variableOnly = PlotWidthStyle("PlotWidth = 800" + plot);
            // A variable assignment after the directive must win (latest value).
            var variableAfterDirective = PlotWidthStyle("#settings {\"plotWidth\": 400}\nPlotWidth = 800" + plot);

            Assert.NotNull(variableOnly);
            Assert.Equal(variableOnly, variableAfterDirective);
        }

        [Fact]
        public void SettingsDirective_SettingControllingVariable_StaysReadable()
        {
            // Applying a #settings key writes through to its special variable, so
            // the variable resolves afterwards even without an explicit assignment.
            var html = Render("#settings {\"plotWidth\": 400}\nw = PlotWidth");
            Assert.Contains("400", html);
        }

        [Fact]
        public void SettingsDirective_ReturnAngleUnits()
        {
            Assert.Contains("°", Render("x = atan(1)"));
            Assert.DoesNotContain("°", Render("#settings {\"returnAngleUnits\": false}\nx = atan(1)"));
            Assert.Contains("°", Render("#settings {\"returnAngleUnits\": false}\nReturnAngleUnits = 1\nx = atan(1)"));
        }

        [Fact]
        public void EndSettings_RestoresPriorValues()
        {
            var html = Render("#settings {\"decimals\": 4}\nx = 6.12345\n#end settings\ny = 8.7654");
            Assert.Contains("6.1235", html);
            Assert.Contains("8.77", html);
            Assert.DoesNotContain("8.7654", html);
        }

        [Fact]
        public void EndSettings_Nested_PopsOneLevel()
        {
            var html = Render("#settings {\"decimals\": 4}\n#settings {\"decimals\": 1}\nx = 6.12345\n#end settings\ny = 8.7654321\n#end settings\nz = 3.14159");
            Assert.Contains("6.1", html);
            Assert.DoesNotContain("6.12", html);
            Assert.Contains("8.7654", html);
            Assert.DoesNotContain("8.76543", html);
            Assert.Contains("3.14", html);
            Assert.DoesNotContain("3.141", html);
        }

        [Fact]
        public void EndSettings_LeavesUntouchedKeys()
        {
            // #round isn't a #settings key, so #end settings must not undo it.
            var html = Render("#round 4\n#settings {\"returnAngleUnits\": false}\n#end settings\nx = 6.12345");
            Assert.Contains("6.1235", html);
        }

        [Fact]
        public void EndSettings_RestoresSettingsVariable()
        {
            const string plot = "\n$Plot{ x^2 @ x = 0 : 2 }";
            var variableOnly = PlotWidthStyle("PlotWidth = 800" + plot);
            var restored = PlotWidthStyle("PlotWidth = 800\n#settings {\"plotWidth\": 400}\n#end settings" + plot);
            Assert.Equal(variableOnly, restored);
        }

        [Fact]
        public void EndSettings_RestoresReturnAngleUnits()
        {
            Assert.Contains("°", Render("#settings {\"returnAngleUnits\": false}\n#end settings\nx = atan(1)"));
        }

        [Fact]
        public void EndSettings_WithoutOpener_IsNoOp()
        {
            var html = Render("#end settings\nx = 6.12345");
            Assert.Contains("6.12", html);
            Assert.DoesNotContain("Invalid", html);
        }

        [Fact]
        public void EndSettings_AfterInvalidSettings_DoesNotPopOuter()
        {
            var html = Render("#settings {\"decimals\": 4}\n#settings {not valid}\n#end settings\nx = 6.12345");
            Assert.Contains("6.1235", html);
        }

        [Fact]
        public void SettingsDirective_MalformedJson_ReportsError()
        {
            var parser = new ExpressionParser { Settings = new Settings(), Debug = true };
            parser.Parse("#settings {not valid}\nx = 1", true, false);
            Assert.Contains("Invalid JSON in #settings", parser.HtmlResult);
        }

        [Fact]
        public void SettingsDirective_UnknownKey_IsIgnored()
        {
            // Unrecognized keys are silently ignored by the engine (the linter warns).
            var parser = new ExpressionParser { Settings = new Settings(), Debug = true };
            parser.Parse("#settings {\"nonsense\": 4}\nx = 1", true, false);
            Assert.DoesNotContain("Invalid JSON in #settings", parser.HtmlResult);
        }
    }
}
