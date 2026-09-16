using System.Collections.Generic;
using System.Linq;
using Calcpad.Highlighter.ContentResolution;
using Calcpad.Highlighter.Linter;
using Calcpad.Highlighter.Linter.Models;
using Calcpad.Highlighter.Tokenizer;
using Calcpad.Highlighter.Tokenizer.Models;

namespace Calcpad.Tests.Highlighter
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

        /// <summary>
        /// Core matches keywords case-insensitively, so the payload directives must too -- these
        /// spellings used to fall through and tokenize their payload as ordinary code.
        /// </summary>
        [Theory]
        [InlineData("#Settings {\"decimals\": 4}", TokenType.SettingsJson)]
        [InlineData("#SETTINGS {\"decimals\": 4}", TokenType.SettingsJson)]
        [InlineData("#Include lib/beam.cpd", TokenType.Include)]
        [InlineData("#Format 0.##", TokenType.Format)]
        [InlineData("#ProjectPath work/proj", TokenType.FilePath)]
        [InlineData("#LibraryPath work/lib", TokenType.FilePath)]
        public void PayloadDirectives_AreMatchedCaseInsensitively(string source, TokenType payload) =>
            Assert.Contains(new CalcpadTokenizer().Tokenize(source).Tokens, t => t.Type == payload);

        [Fact]
        public void DefDirective_IsMatchedCaseInsensitively() =>
            Assert.Contains(new CalcpadTokenizer().Tokenize("#Def m$(a$)").Tokens,
                t => t.Type == TokenType.Macro && t.Text == "m$");
    }
}
