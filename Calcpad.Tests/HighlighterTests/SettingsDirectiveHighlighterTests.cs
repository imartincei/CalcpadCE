using System.Collections.Generic;
using System.Linq;
using Calcpad.Highlighter.ContentResolution;
using Calcpad.Highlighter.Linter;
using Calcpad.Highlighter.Linter.Models;
using Calcpad.Highlighter.Tokenizer;
using Calcpad.Highlighter.Tokenizer.Models;

namespace Calcpad.Tests.HighlighterTests
{
    public class SettingsDirectiveHighlighterTests
    {
        private static LinterResult Lint(string content)
        {
            var staged = new ContentResolver().GetStagedContent(content, new Dictionary<string, string>());
            var ignore = new LintIgnoreRegionParser().ExtractRegions(content);
            return new CalcpadLinter().Lint(staged, ignore);
        }

        [Fact]
        public void SettingsDirective_TokenizesJsonPayloadAsSingleToken()
        {
            var result = new CalcpadTokenizer().Tokenize("#settings {\"decimals\": 4}");
            var jsonTokens = result.Tokens.Where(t => t.Type == TokenType.SettingsJson).ToList();
            var token = Assert.Single(jsonTokens);
            Assert.Equal("{\"decimals\": 4}", token.Text);
        }

        [Fact]
        public void SettingsDirective_IsRecognizedDirective()
        {
            var result = Lint("#settings {\"decimals\": 4}\nx = 1");
            Assert.DoesNotContain(result.Diagnostics, d => d.Code == "CPD-3406");
            Assert.DoesNotContain(result.Diagnostics, d => d.Code == "CPD-3413");
        }

        [Fact]
        public void SettingsDirective_MalformedJson_WarnsCpd3413()
        {
            var result = Lint("#settings {bad}\nx = 1");
            Assert.Contains(result.Diagnostics, d => d.Code == "CPD-3413");
        }

        [Fact]
        public void SettingsDirective_UnknownKey_WarnsCpd3413()
        {
            var result = Lint("#settings {\"nonsense\": 4}\nx = 1");
            Assert.Contains(result.Diagnostics, d => d.Code == "CPD-3413");
        }
    }
}
