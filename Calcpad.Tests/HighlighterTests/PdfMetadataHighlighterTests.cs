using System.Collections.Generic;
using System.Linq;
using Calcpad.Highlighter.ContentResolution;
using Calcpad.Highlighter.Linter;
using Calcpad.Highlighter.Linter.Models;

namespace Calcpad.Tests.HighlighterTests
{
    /// <summary>
    /// The <c>pdf</c> object of a metadata comment, and the metadata-comment checks that
    /// share its code path: recognized keys, region markers, and multi-line payloads.
    /// </summary>
    public class PdfMetadataHighlighterTests
    {
        private static LinterResult Lint(string content)
        {
            var staged = new ContentResolver().GetStagedContent(content, new Dictionary<string, string>());
            var ignore = new LintIgnoreRegionParser().ExtractRegions(content);
            return new CalcpadLinter().Lint(staged, ignore);
        }

        [Fact]
        public void PdfSettings_Valid_ReportsNothing()
        {
            var result = Lint(
                "'<!--{\"pdf\":{\"format\":\"A4\",\"orientation\":\"landscape\",\"marginTop\":\"2cm\",\"showDate\":false}}-->\nx = 1");

            Assert.DoesNotContain(result.Diagnostics, d => d.Code == "CPD-3414");
            Assert.DoesNotContain(result.Diagnostics, d => d.Code == "CPD-3412");
        }

        [Fact]
        public void PdfSettings_UnknownKey_WarnsCpd3414()
        {
            // 'author' is a real PdfOptions field but is deliberately not offered yet,
            // so a document naming it should be told rather than silently ignored.
            var result = Lint("'<!--{\"pdf\":{\"author\":\"Me\"}}-->\nx = 1");

            Assert.Contains(result.Diagnostics, d => d.Code == "CPD-3414");
        }

        [Fact]
        public void PdfSettings_UnknownPaperFormat_WarnsCpd3414()
        {
            var result = Lint("'<!--{\"pdf\":{\"format\":\"A9\"}}-->\nx = 1");

            Assert.Contains(result.Diagnostics, d => d.Code == "CPD-3414" && d.Message.Contains("format"));
        }

        [Fact]
        public void PdfSettings_UnitlessMargin_WarnsCpd3414()
        {
            var result = Lint("'<!--{\"pdf\":{\"marginTop\":\"2 centimetres\"}}-->\nx = 1");

            Assert.Contains(result.Diagnostics, d => d.Code == "CPD-3414" && d.Message.Contains("marginTop"));
        }

        [Fact]
        public void PdfSettings_BareNumberMargin_WarnsCpd3414()
        {
            // Puppeteer reads a unitless margin as pixels, which is never the intent.
            var result = Lint("'<!--{\"pdf\":{\"marginLeft\":\"2\"}}-->\nx = 1");

            Assert.Contains(result.Diagnostics, d => d.Code == "CPD-3414" && d.Message.Contains("marginLeft"));
        }

        [Fact]
        public void PdfSettings_WrongValueType_WarnsCpd3414()
        {
            var result = Lint("'<!--{\"pdf\":{\"showDate\":\"yes\"}}-->\nx = 1");

            Assert.Contains(result.Diagnostics, d => d.Code == "CPD-3414");
        }

        [Fact]
        public void PdfSettings_NotAnObject_WarnsCpd3414()
        {
            var result = Lint("'<!--{\"pdf\":5}-->\nx = 1");

            Assert.Contains(result.Diagnostics, d => d.Code == "CPD-3414");
        }

        [Fact]
        public void MetadataComment_MalformedJson_WarnsCpd3412()
        {
            var result = Lint("'<!--{\"pdf\":{bad}}-->\nx = 1");

            Assert.Contains(result.Diagnostics, d => d.Code == "CPD-3412");
        }

        [Fact]
        public void MetadataComment_UnknownTopLevelKey_WarnsCpd3412()
        {
            var result = Lint("'<!--{\"nonsense\":1}-->\nx = 1");

            Assert.Contains(result.Diagnostics, d => d.Code == "CPD-3412" && d.Message.Contains("nonsense"));
        }

        [Fact]
        public void MetadataComment_PlainProseComment_ReportsNothing()
        {
            // A non-JSON HTML comment is not metadata; flagging it would light up
            // every ordinary commented-out block of markup.
            var result = Lint("'<!-- a note to self -->\nx = 1");

            Assert.DoesNotContain(result.Diagnostics, d =>
                d.Code == "CPD-3412" || d.Code == "CPD-3414" || d.Code == "CPD-3411");
        }

        [Fact]
        public void LintIgnore_UnknownCode_WarnsCpd3412()
        {
            var result = Lint("'<!--{\"LintIgnore\":[\"CPD-9999\"]}-->\nx = 1\n'<!--{\"EndLintIgnore\":[]}-->");

            Assert.Contains(result.Diagnostics, d => d.Code == "CPD-3412" && d.Message.Contains("CPD-9999"));
        }

        [Fact]
        public void LintIgnore_KnownCode_ReportsNothing()
        {
            var result = Lint("'<!--{\"LintIgnore\":[\"CPD-3301\"]}-->\nx = 1\n'<!--{\"EndLintIgnore\":[]}-->");

            Assert.DoesNotContain(result.Diagnostics, d => d.Code == "CPD-3412");
        }

        [Fact]
        public void LintIgnore_NotAnArray_WarnsCpd3412()
        {
            var result = Lint("'<!--{\"LintIgnore\":\"CPD-3301\"}-->\nx = 1");

            Assert.Contains(result.Diagnostics, d => d.Code == "CPD-3412" && d.Message.Contains("LintIgnore"));
        }

        [Fact]
        public void UiOverrides_SavedValues_ReportNothing()
        {
            var result = Lint("'<!--{\"uiOverrides\":{\"L:1\":\"8\",\"v:1\":\"[4; 5; 6]\",\"y:1:2\":\"5\"}}-->\n#UI L = 4");

            Assert.DoesNotContain(result.Diagnostics, d => d.Code == "CPD-3412");
        }

        [Fact]
        public void UiOverrides_NotAnObject_WarnsCpd3412()
        {
            var result = Lint("'<!--{\"uiOverrides\":[\"L:1\"]}-->\nx = 1");

            Assert.Contains(result.Diagnostics, d => d.Code == "CPD-3412" && d.Message.Contains("uiOverrides"));
        }

        [Fact]
        public void UiOverrides_NonStringValue_WarnsCpd3412()
        {
            // The host writes every entered value as a string; a bare number here would
            // come back as a value the form can't round-trip.
            var result = Lint("'<!--{\"uiOverrides\":{\"L:1\":8}}-->\nx = 1");

            Assert.Contains(result.Diagnostics, d => d.Code == "CPD-3412" && d.Message.Contains("L:1"));
        }

        [Fact]
        public void PdfSettings_SpanningLines_IsValidatedNotSkipped()
        {
            // A `_`-continued comment used to be invisible to this validator, which read
            // one token at a time; the payload below has to be judged as a whole.
            var result = Lint(
                "'<!--{\"pdf\":{\"format\":\"A9\"}, _\n\"documentTitle\":\"Beam\"}-->\nx = 1");

            Assert.Contains(result.Diagnostics, d => d.Code == "CPD-3414");
        }

        [Fact]
        public void PdfSettings_SpanningLines_ValidPayloadReportsNothing()
        {
            // The companion to the case above: the halves must be rejoined into valid
            // JSON, not reported as malformed for ending mid-object.
            var result = Lint(
                "'<!--{\"pdf\":{\"format\":\"A4\"}, _\n\"desc\":\"Beam check\"}-->\nx = 1");

            Assert.DoesNotContain(result.Diagnostics, d =>
                d.Code == "CPD-3412" || d.Code == "CPD-3414");
        }

        [Fact]
        public void MetadataComment_AboveDefinition_KeepsDescAndPdfTogether()
        {
            var result = Lint(
                "'<!--{\"desc\":\"Area\",\"returnType\":\"value\",\"pdf\":{\"format\":\"A4\"}}-->\nA(b; h) = b*h");

            Assert.DoesNotContain(result.Diagnostics, d =>
                d.Code == "CPD-3411" || d.Code == "CPD-3412" || d.Code == "CPD-3414");
        }
    }
}
