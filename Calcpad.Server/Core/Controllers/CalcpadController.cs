using Calcpad.Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Calcpad.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CalcpadController : ControllerBase
    {
        private readonly CalcpadService _calcpadService;
        private readonly PdfGeneratorService _pdfGeneratorService;

        public CalcpadController(CalcpadService calcpadService, PdfGeneratorService pdfGeneratorService)
        {
            _calcpadService = calcpadService;
            _pdfGeneratorService = pdfGeneratorService;
        }

        [HttpPost("convert")]
        [EnableRateLimiting("convert")]
        public async Task<IActionResult> ConvertToHtml([FromBody] CalcpadRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.Content))
                {
                    return BadRequest("Content is required");
                }

                var validationError = ValidateSettings(request);
                if (validationError != null) return BadRequest(validationError);

                // Get HTML result first
                var htmlResult = await _calcpadService.ConvertAsync(request.Content, request.Settings, request.ForceUnwrappedCode, request.Theme);
                
                // Return appropriate content type based on format
                var format = request.Settings?.Output?.Format?.ToLower() ?? "html";
                return format switch
                {
                    "html" => Content(htmlResult, "text/html"),
                    "pdf" => await GeneratePdfResponse(htmlResult, request.PdfSettings),
                    _ => Content(htmlResult, "text/html")
                };
            }
            catch (Exception ex)
            {
                return StatusCode(500, CalcpadApiService.IsPublicMode
                    ? "An internal error occurred while processing the request."
                    : $"Error processing Calcpad content: {ex.Message}");
            }
        }

        private async Task<IActionResult> GeneratePdfResponse(string htmlContent, PdfSettings? pdfSettings)
        {
            try
            {
                var pdfBytes = await _pdfGeneratorService.GeneratePdfAsync(htmlContent, ConvertToPdfGeneratorSettings(pdfSettings));
                var fileName = $"calcpad-{DateTime.Now:yyyy-MM-dd-HHmm}.pdf";
                return File(pdfBytes, "application/pdf", fileName);
            }
            catch (Exception ex)
            {
                return StatusCode(500, CalcpadApiService.IsPublicMode
                    ? "An internal error occurred during PDF generation."
                    : $"PDF generation failed: {ex.Message}");
            }
        }

        private PdfGeneratorService.PdfSettings? ConvertToPdfGeneratorSettings(PdfSettings? settings)
        {
            if (settings == null) return null;

            return new PdfGeneratorService.PdfSettings
            {
                Format = settings.Format ?? "Letter",
                Orientation = settings.Orientation ?? "portrait",
                PrintBackground = settings.PrintBackground ?? true,
                Scale = settings.Scale ?? 1.0,
                MarginTop = settings.MarginTop ?? "2cm",
                MarginRight = settings.MarginRight ?? "1.5cm",
                MarginBottom = settings.MarginBottom ?? "2cm",
                MarginLeft = settings.MarginLeft ?? "1.5cm",
                EnableHeader = settings.EnableHeader ?? false,
                EnableFooter = settings.EnableFooter ?? false,
                DocumentTitle = settings.DocumentTitle ?? "Calcpad Document",
                DocumentSubtitle = settings.DocumentSubtitle ?? "",
                HeaderCenter = settings.HeaderCenter ?? "",
                FooterCenter = settings.FooterCenter ?? "",
                Author = settings.Author ?? "",
                Company = settings.Company ?? "",
                Project = settings.Project ?? ""
            };
        }

        [HttpPost("convert-unwrapped")]
        [EnableRateLimiting("convert")]
        public async Task<IActionResult> ConvertToUnwrappedHtml([FromBody] CalcpadRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.Content))
                {
                    return BadRequest("Content is required");
                }

                var validationError = ValidateSettings(request);
                if (validationError != null) return BadRequest(validationError);

                var result = await _calcpadService.ConvertAsync(request.Content, request.Settings, forceUnwrappedCode: true, request.Theme);
                return Content(result, "text/html");
            }
            catch (Exception ex)
            {
                return StatusCode(500, CalcpadApiService.IsPublicMode
                    ? "An internal error occurred while processing the request."
                    : $"Error processing Calcpad content: {ex.Message}");
            }
        }

        [HttpGet("sample")]
        [EnableRateLimiting("general")]
        public IActionResult GetSample()
        {
            var sampleContent = _calcpadService.GetSampleContent();
            return Ok(new CalcpadRequest { Content = sampleContent });
        }

        private static string? ValidateSettings(CalcpadRequest request)
        {
            if (request.Settings?.Math != null)
            {
                var m = request.Settings.Math;
                if (m.Decimals is < 0 or > 15) return "Decimals must be between 0 and 15.";
                if (m.Degrees is not null and not (0 or 1)) return "Degrees must be 0 or 1.";
            }
            if (request.Settings?.Plot != null)
            {
                var p = request.Settings.Plot;
                if (p.ScreenScaleFactor is < 0.1 or > 10) return "ScreenScaleFactor must be between 0.1 and 10.";
                if (p.ImagePath?.Length > 200) return "ImagePath is too long.";
                if (p.ImageUri?.Length > 500) return "ImageUri is too long.";
            }
            if (request.PdfSettings != null)
            {
                var pdf = request.PdfSettings;
                if (pdf.Scale is < 0.1 or > 3.0) return "PDF scale must be between 0.1 and 3.0.";
                if (pdf.DocumentTitle?.Length > 200) return "DocumentTitle is too long.";
                if (pdf.DocumentSubtitle?.Length > 200) return "DocumentSubtitle is too long.";
                if (pdf.Author?.Length > 100) return "Author is too long.";
                if (pdf.Company?.Length > 100) return "Company is too long.";
                if (pdf.Project?.Length > 100) return "Project is too long.";
            }
            if (request.Theme is not ("light" or "dark")) return "Theme must be 'light' or 'dark'.";
            return null;
        }
    }

    public class CalcpadRequest
    {
        public string Content { get; set; } = string.Empty;
        public CalcpadSettings? Settings { get; set; }
        public bool ForceUnwrappedCode { get; set; } = false;
        public string Theme { get; set; } = "light"; // "light" or "dark"
        public PdfSettings? PdfSettings { get; set; }
    }

    public class CalcpadSettings
    {
        public MathSettings? Math { get; set; }
        public PlotSettings? Plot { get; set; }
        public AuthSettings? Auth { get; set; }
        public string? Units { get; set; }
        public OutputSettings? Output { get; set; }
    }

    public class MathSettings
    {
        public int? Decimals { get; set; }
        public int? Degrees { get; set; }
        public bool? IsComplex { get; set; }
        public bool? Substitute { get; set; }
        public bool? FormatEquations { get; set; }
    }

    public class PlotSettings
    {
        public bool? IsAdaptive { get; set; }
        public double? ScreenScaleFactor { get; set; }
        public string? ImagePath { get; set; }
        public string? ImageUri { get; set; }
        public bool? VectorGraphics { get; set; }
        public string? ColorScale { get; set; }
        public bool? SmoothScale { get; set; }
        public bool? Shadows { get; set; }
        public string? LightDirection { get; set; }
    }

    public class OutputSettings
    {
        public string? Format { get; set; }
        public bool? Silent { get; set; }
    }

    public class PdfSettings
    {
        public string? Format { get; set; } = "Letter"; // A3, A4, A5, Legal, Letter, Tabloid
        public string? Orientation { get; set; } = "portrait"; // portrait, landscape
        public bool? PrintBackground { get; set; } = true;
        public double? Scale { get; set; } = 1.0;
        public string? MarginTop { get; set; } = "2cm";
        public string? MarginRight { get; set; } = "1.5cm";
        public string? MarginBottom { get; set; } = "2cm";
        public string? MarginLeft { get; set; } = "1.5cm";
        public bool? EnableHeader { get; set; } = false;
        public bool? EnableFooter { get; set; } = false;
        public string? DocumentTitle { get; set; } = "Calcpad Document";
        public string? DocumentSubtitle { get; set; } = "";
        public string? HeaderCenter { get; set; } = "";
        public string? FooterCenter { get; set; } = "";
        public string? Author { get; set; } = "";
        public string? Company { get; set; } = "";
        public string? Project { get; set; } = "";
    }

    public class AuthSettings
    {
        public string? Url { get; set; }
        public string? JWT { get; set; }
    }
}