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
    /// Paper names <c>PdfGeneratorService.ParsePaperFormat</c> maps to a
    /// <c>PuppeteerSharp.Media.PaperFormat</c> instance. A dedicated enum rather than
    /// <c>PuppeteerSharp.Media.PaperFormat</c> itself, since that type is a class
    /// (static properties, not enum members) and Calcpad.Highlighter must not
    /// depend on PuppeteerSharp.
    /// </summary>
    public enum PdfPaperFormat { Letter, Legal, Tabloid, Ledger, A0, A1, A2, A3, A4, A5, A6 }

    /// <summary>
    /// Canonical values <c>PdfGeneratorService</c> resolves a null <see cref="PdfSettingsDto"/>
    /// field to. Mirrors the frontend's <c>DEFAULT_PDF_SETTINGS</c> (pdf-settings.ts).
    /// </summary>
    public static class PdfSettingsDefaults
    {
        public const string Format = "Letter";
        public const string Orientation = "portrait";
        public const string MarginTop = "0.75in";
        public const string MarginRight = "0.5in";
        public const string MarginBottom = "0.75in";
        public const string MarginLeft = "0.5in";
        public const bool ShowPageNumbers = true;
        public const bool ShowDate = true;
    }

    /// <summary>
    /// JSON payload of the <c>pdf</c> key of a metadata comment
    /// (<c>'&lt;!--{"pdf":{...}}--&gt;</c>) - the PDF export settings a document pins for itself.
    /// Calcpad.Core never sees these: the comment is HTML, so only the export path reads it, and
    /// <c>PdfGeneratorService</c> deserializes a <c>/pdf</c> request's <c>options</c> into this
    /// same type, where a null field means "use the default".
    /// <para>
    /// The recognized keys are curated, not the full set the PDF endpoint accepts. Keep in step
    /// with <c>PDF_SETTING_KEYS</c> in <c>metadata-comment.ts</c>, which validates the same payload.
    /// </para>
    /// </summary>
    public sealed class PdfSettingsDto : DirectiveDto<PdfSettingsDto, PdfSettingKey>
    {
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
            if (Format is not null && !Enum.TryParse<PdfPaperFormat>(Format, true, out _))
                errors.Add(new(PdfSettingKey.Format,
                    $"'format' must be one of: {string.Join(", ", Enum.GetNames<PdfPaperFormat>())}"));
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
