using System.Collections.Generic;
using System.Linq;
using Calcpad.Highlighter.ContentResolution;
using Calcpad.Highlighter.Linter;
using Calcpad.Highlighter.Linter.Models;

namespace Calcpad.Tests.Highlighter
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
        public void UiOverrides_NotOnFirstLine_WarnsCpd3416()
        {
            // The host only ever reads a 'uiOverrides' comment off the file's first line,
            // so one lower down - e.g. above a #UI line it means to target - is inert.
            var result = Lint("#UI L = 4\n'<!--{\"uiOverrides\":{\"L:1\":\"8\"}}-->\n#UI q = 1");

            Assert.Contains(result.Diagnostics, d => d.Code == "CPD-3416");
        }

        [Fact]
        public void UiOverrides_OnFirstLine_DoesNotWarnCpd3416()
        {
            var result = Lint("'<!--{\"uiOverrides\":{\"L:1\":\"8\"}}-->\n#UI L = 4");

            Assert.DoesNotContain(result.Diagnostics, d => d.Code == "CPD-3416");
        }

        [Fact]
        public void UiOverrides_Duplicate_WarnsCpd3417ForTheSecondOne()
        {
            // Only the first 'uiOverrides' comment in a file is ever read back, so a second
            // one - e.g. from a merge or a stray copy-paste - would otherwise fail silently.
            var result = Lint(
                "'<!--{\"uiOverrides\":{\"L:1\":\"8\"}}-->\n#UI L = 4\n'<!--{\"uiOverrides\":{\"q:1\":\"2\"}}-->\n#UI q = 1");

            var duplicateWarnings = result.Diagnostics.Where(d => d.Code == "CPD-3417").ToList();
            Assert.Single(duplicateWarnings);
            Assert.Equal(2, duplicateWarnings[0].Line);
        }

        [Fact]
        public void UiOverrides_SharingCommentWithAnotherKey_WarnsCpd3418()
        {
            // The Properties tab and the host's save/restore both rewrite this comment by
            // position, so mixing 'uiOverrides' with another key makes the result unpredictable.
            var result = Lint("'<!--{\"desc\":\"Beam span\",\"uiOverrides\":{\"L:1\":\"8\"}}-->\n#UI L = 4");

            Assert.Contains(result.Diagnostics, d => d.Code == "CPD-3418");
        }

        [Fact]
        public void UiOverrides_Alone_DoesNotWarnCpd3418()
        {
            var result = Lint("'<!--{\"uiOverrides\":{\"L:1\":\"8\"}}-->\n#UI L = 4");

            Assert.DoesNotContain(result.Diagnostics, d => d.Code == "CPD-3418");
        }

        [Fact]
        public void UiOverrides_Single_DoesNotWarnCpd3417()
        {
            var result = Lint("'<!--{\"uiOverrides\":{\"L:1\":\"8\"}}-->\n#UI L = 4");

            Assert.DoesNotContain(result.Diagnostics, d => d.Code == "CPD-3417");
        }

        [Fact]
        public void UiOverrides_FromInclude_ReportsNothing()
        {
            // CalcpadService strips 'uiOverrides' out of included content unconditionally
            // (UiOverridesIncludeTests), so a comment only reached through #include is dead
            // weight regardless of where it sits - not a misplaced comment worth flagging.
            var mainPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "uioverrides-include", "main.cpd");
            var subPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "uioverrides-include", "sub.cpd");
            var content = "#UI L = 4\n#include sub.cpd\n";
            var includeFiles = new Dictionary<string, string>
            {
                [subPath] = "'<!--{\"uiOverrides\":{\"L:1\":\"8\"}}-->\n#UI q = 1"
            };

            var staged = new ContentResolver().GetStagedContent(content, includeFiles, sourceFilePath: mainPath);
            var ignore = new LintIgnoreRegionParser().ExtractRegions(content);
            var result = new CalcpadLinter().Lint(staged, ignore);

            Assert.DoesNotContain(result.Diagnostics, d =>
                d.Code == "CPD-3416" || d.Code == "CPD-3417" || d.Code == "CPD-3418" || d.Code == "CPD-3412");
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
