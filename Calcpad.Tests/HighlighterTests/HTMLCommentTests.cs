using System.IO;
using System.Linq;
using Calcpad.Highlighter.HtmlComment;
using Calcpad.Highlighter.Linter.Models;
using Calcpad.Highlighter.Tokenizer;

namespace Calcpad.Tests.HighlighterTests
{
    public class HTMLCommentTests : IClassFixture<HighlighterLinterFixture>
    {
        private const string TestFile = "HTML Comment Data.cpd";
        private readonly HighlighterLinterFixture _fixture;

        public HTMLCommentTests(HighlighterLinterFixture fixture) => _fixture = fixture;

        [Fact]
        public void LintIgnoreRegions_SuppressOnlyCodesNotEndedSelectively()
        {
            var fullPath = Path.Combine(_fixture.ValidDir, TestFile);
            var result = _fixture.LintFile(fullPath);

            var errors = result.Diagnostics
                .Where(d => d.Severity == LinterSeverity.Error)
                .ToList();

            // Line 6 (`x = = 2`) is inside a LintIgnore for CPD-3407 and CPD-3312.
            // Line 8 (`x = 3`) has EndLintIgnore for CPD-3407 above it but CPD-3312
            // remains suppressed, so any reassignment-style diagnostic on x is hidden.
            // EndLintIgnore on line 9 names CPD-3412 which was never opened — a no-op.
            // Result: file should be error-free.
            Assert.True(
                errors.Count == 0,
                "Expected no error diagnostics, got: " +
                string.Join("; ", errors.Select(e => $"[{e.Code}] line {e.Line + 1}: {e.Message}")));
        }

        [Fact]
        public void SettingsDirective_AppliesFourDecimalsToOutput()
        {
            // The file opens with `#settings {"decimals": 4}`; the engine must read it
            // during parsing and render subsequent output with four decimals.
            var fullPath = Path.Combine(_fixture.ValidDir, TestFile);
            var source = File.ReadAllText(fullPath);

            var macroParser = new MacroParser();
            var hasMacroErrors = macroParser.Parse(source, out var expanded, null, 0, false);
            Assert.False(hasMacroErrors);

            var parser = new ExpressionParser { Settings = new Settings() };
            parser.Parse(expanded, true, false);
            var html = parser.HtmlResult;

            // macro$(param; 6.12345) expands to `param = 6.12345`. With decimals=4
            // the rendered value is 6.1235; with the default (2) it would be 6.12.
            Assert.Contains("6.1235", html);
            Assert.DoesNotContain("6.12345", html);
        }

        [Fact]
        public void LineContinuation_MetadataCommentParsesAsSingleJsonBlock()
        {
            // A metadata comment split across lines with an explicit " _" continuation
            // must be recognized as one HtmlComment block with intact JSON. Regression:
            // the opener line was typed as plain Comment and the continuation line's
            // leading JSON quote was stripped as if it were a comment delimiter.
            var source =
                "'<!--{\"desc\":\"k\",\"paramTypes\":[\"value\"], _\n" +
                "\"paramDesc\":[\"ndfdf\"]}-->";

            var tokens = new CalcpadTokenizer().Tokenize(source);
            var blocks = new HtmlCommentParser().Parse(tokens);

            var block = Assert.Single(blocks);
            Assert.Equal(HtmlCommentParseStatus.Success, block.Status);
            Assert.True(block.Data.HasValue);

            var data = block.Data.Value;
            Assert.Equal("k", data.GetProperty("desc").GetString());
            Assert.Equal("value", data.GetProperty("paramTypes")[0].GetString());
            Assert.Equal("ndfdf", data.GetProperty("paramDesc")[0].GetString());
        }

        [Fact]
        public void NonObjectJson_InHtmlComment_DoesNotThrow()
        {
            // '<!--[]--> parses as a valid JSON array, not an object. The region parser
            // must not call TryGetProperty on it (which throws for non-objects).
            var content = "'<!--[]-->'\nx = 1\n";
            var regions = new Calcpad.Highlighter.Linter.LintIgnoreRegionParser().ExtractRegions(content);
            Assert.Empty(regions);
        }
    }
}
