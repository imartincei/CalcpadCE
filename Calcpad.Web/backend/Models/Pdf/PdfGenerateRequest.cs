using Calcpad.Highlighter.HtmlComment;

namespace Calcpad.Server.Models.Pdf
{
    public class PdfGenerateRequest
    {
        public string Html { get; set; } = string.Empty;
        public PdfSettingsDto? Options { get; set; }
    }
}
