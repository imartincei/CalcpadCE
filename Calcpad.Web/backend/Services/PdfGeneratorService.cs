using System.Reflection;
using Calcpad.Highlighter.HtmlComment;
using PuppeteerSharp;
using PuppeteerSharp.Media;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Calcpad.Server.Services
{
    /// <summary>
    /// Font resolver that maps any font request to embedded Segoe WP fonts from PdfSharp.WPFonts.
    /// Required for PDFsharp on Linux where system fonts aren't auto-discovered.
    /// </summary>
    internal class EmbeddedFontResolver : IFontResolver
    {
        private const string Regular = "SegoeWP";
        private const string Bold = "SegoeWP-Bold";

        private static readonly Assembly _wpFontsAssembly =
            Assembly.Load("PdfSharp.WPFonts");

        public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
        {
            return new FontResolverInfo(isBold ? Bold : Regular);
        }

        public byte[]? GetFont(string faceName)
        {
            var resourceName = $"PdfSharp.WPFonts.Fonts.{faceName}.ttf";
            using var stream = _wpFontsAssembly.GetManifestResourceStream(resourceName);
            if (stream == null) return null;
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return ms.ToArray();
        }
    }

    public class PdfGeneratorService : IAsyncDisposable
    {
        private IBrowser? _browser;
        private readonly SemaphoreSlim _browserLock = new(1, 1);
        // Caps concurrent Chromium pages against the one shared browser, independent of
        // _browserLock (which only guards lazy browser creation) — a burst of PDF/DOCX
        // requests would otherwise open one page per request with no upper bound.
        private readonly SemaphoreSlim _pageLock = new(4, 4);
        private readonly IConfiguration _config;
        private static string? _cachedChromiumPath;

        static PdfGeneratorService()
        {
            if (GlobalFontSettings.FontResolver == null)
                GlobalFontSettings.FontResolver = new EmbeddedFontResolver();
        }

        public PdfGeneratorService(IConfiguration config)
        {
            _config = config;
        }

        public async Task<byte[]> GeneratePdfAsync(string html, PdfSettingsDto? options = null, string? browserPath = null)
        {
            options ??= new PdfSettingsDto();

            // Step 1: Generate basic PDF with PuppeteerSharp
            var basicPdf = await GenerateBasicPdfAsync(html, options, browserPath).ConfigureAwait(false);

            // Step 2: Enhance with PDFsharp (headers, footers, backgrounds)
            return EnhancePdf(basicPdf, options);
        }

        private async Task<byte[]> GenerateBasicPdfAsync(string html, PdfSettingsDto options, string? browserPath)
        {
            var browser = await GetOrCreateBrowserAsync(browserPath).ConfigureAwait(false);

            await _pageLock.WaitAsync().ConfigureAwait(false);
            IPage page;
            try
            {
                page = await browser.NewPageAsync().ConfigureAwait(false);
            }
            catch
            {
                _pageLock.Release();
                throw;
            }

            try
            {
                // PuppeteerSharp doesn't reliably switch to print media before
                // PdfStreamAsync, so do it explicitly — otherwise @media print
                // rules in the page CSS won't apply (e.g. the body max-width
                // override that lets content fill the print area).
                await page.EmulateMediaTypeAsync(MediaType.Print).ConfigureAwait(false);

                // Bumped from the 30 s default because large documents (multi-MB
                // HTML with many script tags) can need more time to reach
                // networkidle0.
                await page.SetContentAsync(html, new NavigationOptions
                {
                    WaitUntil = [WaitUntilNavigation.Networkidle0],
                    Timeout = 120_000
                }).ConfigureAwait(false);

                // Inject PDF-specific styles
                await page.AddStyleTagAsync(new AddTagOptions
                {
                    Content = @"
                        body {
                            -webkit-print-color-adjust: exact;
                            print-color-adjust: exact;
                        }
                        .calcpad-ui-datagrid td {
                            background-color: LightYellow !important;
                        }
                    "
                }).ConfigureAwait(false);

                // Replace <input> elements with underlined text (matches WPF PDF output),
                // then fit datagrid tables to page width with text wrapping
                await page.EvaluateFunctionAsync(@"() => {
                    // 1. Replace inputs with static underlined text
                    document.querySelectorAll('input[type=""text""]').forEach(input => {
                        const u = document.createElement('u');
                        u.textContent = input.value || '\u00A0\u00A0\u00A0';
                        u.style.backgroundColor = 'LightYellow';
                        input.parentNode.replaceChild(u, input);
                    });

                    // 2. Fit datagrid tables to page width
                    document.querySelectorAll('.calcpad-ui-datagrid').forEach(container => {
                        // Remove jspreadsheet overflow constraints so full table is visible
                        var content = container.querySelector('.jss_content');
                        if (content) {
                            content.style.overflow = 'visible';
                            content.style.maxWidth = 'none';
                            content.style.maxHeight = 'none';
                        }
                        var wrapper = container.querySelector('.jss');
                        if (wrapper) {
                            wrapper.style.overflow = 'visible';
                            wrapper.style.maxWidth = 'none';
                        }

                        var table = container.querySelector('table');
                        if (!table) return;

                        // Distribute column widths evenly across available page width
                        var pageWidth = document.body.clientWidth || 700;
                        table.style.tableLayout = 'fixed';
                        table.style.width = pageWidth + 'px';

                        // Allow text wrapping within cells
                        var cells = table.querySelectorAll('td, th');
                        cells.forEach(function(cell) {
                            cell.style.width = '';
                            cell.style.minWidth = '0';
                            cell.style.padding = '2px 6px';
                            cell.style.whiteSpace = 'normal';
                            cell.style.wordWrap = 'break-word';
                            cell.style.overflowWrap = 'break-word';
                            cell.style.fontSize = '10pt';
                        });
                    });
                }").ConfigureAwait(false);

                await WaitForAsyncContentAsync(page).ConfigureAwait(false);

                var pdfOptions = new PuppeteerSharp.PdfOptions
                {
                    Format = ParsePaperFormat(options.Format),
                    Landscape = options.Orientation == "landscape",
                    PrintBackground = true,
                    MarginOptions = new MarginOptions
                    {
                        Top = options.MarginTop ?? PdfSettingsDefaults.MarginTop,
                        Right = options.MarginRight ?? PdfSettingsDefaults.MarginRight,
                        Bottom = options.MarginBottom ?? PdfSettingsDefaults.MarginBottom,
                        Left = options.MarginLeft ?? PdfSettingsDefaults.MarginLeft
                    },
                    DisplayHeaderFooter = false
                };

                // Stream the PDF in chunks instead of returning the whole blob in
                // one CDP message. The single-message path hits a buffer cap in the
                // DevTools transport (see puppeteer/puppeteer#11720) which produces
                // a silent blank PDF on large documents.
                var pdfStream = await page.PdfStreamAsync(pdfOptions).ConfigureAwait(false);
                try
                {
                    using var ms = new MemoryStream();
                    await pdfStream.CopyToAsync(ms).ConfigureAwait(false);
                    return ms.ToArray();
                }
                finally
                {
                    await pdfStream.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                try
                {
                    await page.CloseAsync().ConfigureAwait(false);
                }
                finally
                {
                    _pageLock.Release();
                }
            }
        }

        /// <summary>
        /// Waits for all client-side rendered images (DXF plots, charts, anything
        /// async that ends up as an <c>&lt;img&gt;</c>) to finish before capturing
        /// the PDF. <c>Networkidle0</c> isn't enough on its own because the work
        /// that fills <c>img.src</c> happens after the last network request returns
        /// (canvas-to-dataURL, font parsing, WebGL rendering, etc.).
        ///
        /// Strategy: poll every <c>&lt;img&gt;</c> in the document — any that have
        /// a <c>src</c> set must report <c>complete</c> with a non-zero
        /// <c>naturalWidth</c>. Then do a double <c>requestAnimationFrame</c>
        /// flush so layout/paint commit before serialization. A 30 s timeout
        /// prevents one stuck render from blocking the whole PDF.
        /// </summary>
        private static async Task WaitForAsyncContentAsync(IPage page)
        {
            try
            {
                await page.WaitForFunctionAsync(@"() => {
                    const imgs = document.querySelectorAll('img');
                    return Array.from(imgs).every(img => {
                        if (!img.src && !img.currentSrc) return false;
                        return img.complete && img.naturalWidth > 0;
                    });
                }", new WaitForFunctionOptions { Timeout = 30000, PollingInterval = 100 }).ConfigureAwait(false);
            }
            catch (WaitTaskTimeoutException)
            {
                // One stuck render shouldn't block the whole PDF — log and proceed
                // with whatever did finish.
                FileLogger.LogWarning("PDF render-wait timed out; proceeding with partial result", null);
            }

            try
            {
                await page.EvaluateFunctionAsync(@"() => new Promise(resolve =>
                    requestAnimationFrame(() => requestAnimationFrame(resolve)))").ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                FileLogger.LogWarning("PDF paint-frame flush failed before capture", ex.Message);
            }
        }

        private async Task<IBrowser> GetOrCreateBrowserAsync(string? browserPath)
        {
            if (_browser is { IsClosed: false })
                return _browser;

            await _browserLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_browser is { IsClosed: false })
                    return _browser;

                // Clean up stale browser
                if (_browser != null)
                {
                    try { await _browser.CloseAsync().ConfigureAwait(false); } catch { }
                    _browser = null;
                }

                var executablePath = await ResolveBrowserPathAsync(browserPath).ConfigureAwait(false);
                FileLogger.LogInfo("Launching browser", executablePath);

                try
                {
                    _browser = await Puppeteer.LaunchAsync(new LaunchOptions
                    {
                        Headless = true,
                        ExecutablePath = executablePath,
                        Args = ["--no-sandbox", "--disable-setuid-sandbox", "--disable-gpu", "--disable-dev-shm-usage"]
                    }).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    FileLogger.LogWarning("Browser launch failed, falling back to ChromeHeadlessShell download", ex.Message);
                    var fallbackPath = await DownloadChromiumAsync().ConfigureAwait(false);
                    _browser = await Puppeteer.LaunchAsync(new LaunchOptions
                    {
                        Headless = true,
                        ExecutablePath = fallbackPath,
                        Args = ["--no-sandbox", "--disable-setuid-sandbox", "--disable-gpu", "--disable-dev-shm-usage"]
                    }).ConfigureAwait(false);
                }

                return _browser;
            }
            finally
            {
                _browserLock.Release();
            }
        }

        /// <summary>
        /// Resolves a Chromium-based browser executable. Checks (in order):
        /// 1. Explicitly configured path (parameter, appsettings, env var)
        /// 2. Well-known system browser locations (Edge, Chrome)
        /// 3. BrowserFetcher download of ChromeHeadlessShell
        /// </summary>
        private async Task<string> ResolveBrowserPathAsync(string? browserPath)
        {
            // 1. Try configured/detected browser path
            var path = NullIfEmpty(browserPath)
                ?? NullIfEmpty(_config["BrowserPath"])
                ?? Environment.GetEnvironmentVariable("BROWSER_PATH");

            if (!string.IsNullOrEmpty(path))
                return path;

            // 2. Try well-known system browser locations
            var systemBrowser = FindSystemBrowser();
            if (systemBrowser != null)
            {
                FileLogger.LogInfo("Auto-detected system browser", systemBrowser);
                return systemBrowser;
            }

            // 3. Fallback: download ChromeHeadlessShell
            return await DownloadChromiumAsync().ConfigureAwait(false);
        }

        private static string? FindSystemBrowser()
        {
            string[] candidates;

            if (OperatingSystem.IsWindows())
            {
                var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

                candidates =
                [
                    Path.Combine(programFiles, "Microsoft", "Edge", "Application", "msedge.exe"),
                    Path.Combine(programFilesX86, "Microsoft", "Edge", "Application", "msedge.exe"),
                    Path.Combine(localAppData, "Microsoft", "Edge", "Application", "msedge.exe"),
                    Path.Combine(programFiles, "Google", "Chrome", "Application", "chrome.exe"),
                    Path.Combine(programFilesX86, "Google", "Chrome", "Application", "chrome.exe"),
                    Path.Combine(localAppData, "Google", "Chrome", "Application", "chrome.exe"),
                ];
            }
            else if (OperatingSystem.IsLinux())
            {
                candidates =
                [
                    "/usr/bin/chromium",
                    "/usr/bin/chromium-browser",
                    "/usr/bin/google-chrome",
                    "/usr/bin/google-chrome-stable",
                    "/snap/bin/chromium",
                ];
            }
            else if (OperatingSystem.IsMacOS())
            {
                candidates =
                [
                    "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
                    "/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge",
                    "/Applications/Chromium.app/Contents/MacOS/Chromium",
                ];
            }
            else
            {
                return null;
            }

            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                    return candidate;
            }

            return null;
        }

        private async Task<string> DownloadChromiumAsync()
        {
            if (_cachedChromiumPath != null && File.Exists(_cachedChromiumPath))
                return _cachedChromiumPath;

            var baseDir = AppContext.BaseDirectory;
            var downloadDir = Path.Combine(baseDir, "chromium");

            FileLogger.LogInfo("Downloading ChromeHeadlessShell", downloadDir);

            var fetcher = new BrowserFetcher(new BrowserFetcherOptions
            {
                Path = downloadDir,
                Browser = SupportedBrowser.ChromeHeadlessShell
            });

            var installed = fetcher.GetInstalledBrowsers().FirstOrDefault();
            if (installed == null)
            {
                installed = await fetcher.DownloadAsync().ConfigureAwait(false);
                FileLogger.LogInfo("ChromeHeadlessShell download complete", installed.GetExecutablePath());
            }

            _cachedChromiumPath = installed.GetExecutablePath();
            return _cachedChromiumPath;
        }

        private static string? NullIfEmpty(string? value) =>
            string.IsNullOrEmpty(value) ? null : value;

        /// <summary>Recognized names mirror <see cref="PdfPaperFormat"/> in Calcpad.Highlighter,
        /// which is what validates a document's <c>pdf</c> metadata comment.</summary>
        private static PaperFormat ParsePaperFormat(string? format) =>
            Enum.TryParse<PdfPaperFormat>(format, true, out var parsed)
                ? MapPaperFormat(parsed)
                : MapPaperFormat(Enum.Parse<PdfPaperFormat>(PdfSettingsDefaults.Format, true));

        private static PaperFormat MapPaperFormat(PdfPaperFormat format) => format switch
        {
            PdfPaperFormat.Letter => PaperFormat.Letter,
            PdfPaperFormat.Legal => PaperFormat.Legal,
            PdfPaperFormat.Tabloid => PaperFormat.Tabloid,
            PdfPaperFormat.Ledger => PaperFormat.Ledger,
            PdfPaperFormat.A0 => PaperFormat.A0,
            PdfPaperFormat.A1 => PaperFormat.A1,
            PdfPaperFormat.A2 => PaperFormat.A2,
            PdfPaperFormat.A3 => PaperFormat.A3,
            PdfPaperFormat.A4 => PaperFormat.A4,
            PdfPaperFormat.A5 => PaperFormat.A5,
            PdfPaperFormat.A6 => PaperFormat.A6,
            _ => PaperFormat.Letter,
        };

        #region PDFsharp Enhancement

        private byte[] EnhancePdf(byte[] pdfBytes, PdfSettingsDto options)
        {
            using var inputStream = new MemoryStream(pdfBytes);
            var document = PdfReader.Open(inputStream, PdfDocumentOpenMode.Modify);

            for (int i = 0; i < document.PageCount; i++)
            {
                var page = document.Pages[i];
                using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);

                double width = page.Width.Point;
                double height = page.Height.Point;

                DrawHeader(gfx, width, height, options);
                DrawFooter(gfx, width, height, i + 1, document.PageCount, options);
            }

            using var outputStream = new MemoryStream();
            document.Save(outputStream, false);
            return outputStream.ToArray();
        }

        private readonly record struct HeaderFooterContext(double Margin, XPen LinePen, XSolidBrush GrayBrush);

        private static HeaderFooterContext SetupHeaderFooterContext() => new(
            Margin: 20,
            LinePen: new XPen(XColor.FromArgb(179, 179, 179), 0.5),
            GrayBrush: new XSolidBrush(XColor.FromArgb(77, 77, 77)));

        private void DrawHeader(XGraphics gfx, double width, double height, PdfSettingsDto options)
        {
            var ctx = SetupHeaderFooterContext();
            var font = new XFont("Helvetica", 10);
            var boldFont = new XFont("Helvetica", 12, XFontStyleEx.Bold);
            var smallFont = new XFont("Helvetica", 8);
            var darkBrush = XBrushes.Black;
            var lightGrayBrush = new XSolidBrush(XColor.FromArgb(128, 128, 128));

            double margin = ctx.Margin;
            double headerY = margin;

            // Separator line below header text
            double lineY = headerY + 25;
            gfx.DrawLine(ctx.LinePen, margin, lineY, width - margin, lineY);

            // Document title (top-left, bold)
            if (!string.IsNullOrEmpty(options.DocumentTitle))
            {
                gfx.DrawString(options.DocumentTitle, boldFont, darkBrush,
                    new XPoint(margin, headerY + 12));
            }

            // Timestamp (top-right)
            if (options.ShowDate ?? PdfSettingsDefaults.ShowDate)
            {
                var timestampFormat = string.IsNullOrEmpty(options.DateTimeFormat) ? "g" : options.DateTimeFormat;
                var timestamp = DateTime.Now.ToString(timestampFormat);
                var timestampSize = gfx.MeasureString(timestamp, smallFont);
                gfx.DrawString(timestamp, smallFont, lightGrayBrush,
                    new XPoint(width - margin - timestampSize.Width, headerY + 12));
            }
        }

        private void DrawFooter(XGraphics gfx, double width, double height,
            int pageNumber, int totalPages, PdfSettingsDto options)
        {
            var ctx = SetupHeaderFooterContext();
            var font = new XFont("Helvetica", 8);
            var centerFont = new XFont("Helvetica", 10);

            double margin = ctx.Margin;
            double footerY = height - margin - 20;

            // Separator line above footer
            double lineY = footerY - 5;
            gfx.DrawLine(ctx.LinePen, margin, lineY, width - margin, lineY);

            // Right side: Page numbers
            if (options.ShowPageNumbers ?? PdfSettingsDefaults.ShowPageNumbers)
            {
                var pageText = $"Page {pageNumber} of {totalPages}";
                var pageSize = gfx.MeasureString(pageText, font);
                gfx.DrawString(pageText, font, ctx.GrayBrush,
                    new XPoint(width - margin - pageSize.Width, footerY + 8));
            }
        }

        #endregion

        public async ValueTask DisposeAsync()
        {
            if (_browser != null)
            {
                try
                {
                    var process = _browser.Process;
                    await _browser.CloseAsync().ConfigureAwait(false);
                    // Ensure the browser process is fully terminated so it doesn't hold file locks
                    if (process is { HasExited: false })
                    {
                        process.Kill(entireProcessTree: true);
                        process.WaitForExit(3000);
                    }
                }
                catch { }
                _browser = null;
            }
        }
    }
}
