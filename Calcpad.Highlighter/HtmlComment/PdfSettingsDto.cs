using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Calcpad.Core;

namespace Calcpad.Highlighter.HtmlComment
{
    public enum PdfSettingKey
    {
        Format,
        Orientation,
        MarginTop,
        MarginRight,
        MarginBottom,
        MarginLeft,
        ShowPageNumbers,
        ShowDate,
        DocumentTitle,
        DateTimeFormat
    }

    /// <summary>
    /// JSON payload of the <c>pdf</c> key of a metadata comment
    /// (<c>'&lt;!--{"pdf":{...}}--&gt;</c>) - the PDF export settings a document pins
    /// for itself. Calcpad.Core never sees these: the comment is HTML, so the engine
    /// renders past it and only the export path reads it.
    /// <para>
    /// The recognized keys are curated, not the full set the PDF endpoint accepts -
    /// only options that demonstrably affect the output are offered. Keep this in step
    /// with <c>PDF_SETTING_KEYS</c> in <c>metadata-comment.ts</c>, which validates the
    /// same payload in the editor panel, and with the server's <c>PdfOptions</c>.
    /// </para>
    /// </summary>
    public sealed class PdfSettingsDto : DirectiveDto<PdfSettingsDto, PdfSettingKey>
    {
        /// <summary>Paper names <c>PdfGeneratorService.ParsePaperFormat</c> accepts.</summary>
        private static readonly string[] Formats =
            { "Letter", "Legal", "Tabloid", "Ledger", "A0", "A1", "A2", "A3", "A4", "A5", "A6" };

        private static readonly string[] Orientations = { "portrait", "landscape" };

        /// <summary>
        /// A CSS length as the headless browser accepts it for a page margin. A bare
        /// number is rejected: the browser reads it as pixels, which is never what
        /// someone setting a print margin means.
        /// </summary>
        private static readonly Regex CssLength =
            new(@"^\d*\.?\d+(cm|mm|in|pt|pc|px)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public string Format { get; set; }
        public string Orientation { get; set; }
        public string MarginTop { get; set; }
        public string MarginRight { get; set; }
        public string MarginBottom { get; set; }
        public string MarginLeft { get; set; }
        public bool? ShowPageNumbers { get; set; }
        public bool? ShowDate { get; set; }
        public string DocumentTitle { get; set; }
        public string DateTimeFormat { get; set; }

        protected override void Validate(List<DirectiveError<PdfSettingKey>> errors)
        {
            CheckOneOf(errors, PdfSettingKey.Format, "format", Format, Formats);
            CheckOneOf(errors, PdfSettingKey.Orientation, "orientation", Orientation, Orientations);
            CheckMargin(errors, PdfSettingKey.MarginTop, "marginTop", MarginTop);
            CheckMargin(errors, PdfSettingKey.MarginRight, "marginRight", MarginRight);
            CheckMargin(errors, PdfSettingKey.MarginBottom, "marginBottom", MarginBottom);
            CheckMargin(errors, PdfSettingKey.MarginLeft, "marginLeft", MarginLeft);
        }

        private static void CheckOneOf(List<DirectiveError<PdfSettingKey>> errors,
            PdfSettingKey key, string name, string value, string[] allowed)
        {
            if (value is null)
                return;

            if (Array.FindIndex(allowed, a => a.Equals(value, StringComparison.OrdinalIgnoreCase)) < 0)
                errors.Add(new(key, $"'{name}' must be one of: {string.Join(", ", allowed)}"));
        }

        private static void CheckMargin(List<DirectiveError<PdfSettingKey>> errors,
            PdfSettingKey key, string name, string value)
        {
            if (value is null)
                return;

            if (!CssLength.IsMatch(value))
                errors.Add(new(key, $"'{name}' must be a length with a unit, e.g. 2cm, 0.5in, or 12mm"));
        }
    }
}
