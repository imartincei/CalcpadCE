namespace Calcpad.Server.Models.Pdf
{
    public class PdfOptions
    {
        // Page settings
        public string Format { get; set; } = "A4";
        public string Orientation { get; set; } = "portrait";
        public float Scale { get; set; } = 1.0f;

        // Margins
        public string MarginTop { get; set; } = "2cm";
        public string MarginRight { get; set; } = "1.5cm";
        public string MarginBottom { get; set; } = "2cm";
        public string MarginLeft { get; set; } = "1.5cm";

        // Headers and footers
        public bool ShowPageNumbers { get; set; } = true;
        public bool ShowDate { get; set; } = true;

        // Document metadata
        public string? DocumentTitle { get; set; }

        // Custom content
        public string? HeaderCenter { get; set; }
        public string? FooterCenter { get; set; }

        // Timestamp format (null/empty uses system default)
        public string? DateTimeFormat { get; set; }
    }

    public class PdfGenerateRequest
    {
        public string Html { get; set; } = string.Empty;
        public string? BrowserPath { get; set; }
        public PdfOptions? Options { get; set; }
    }
}
