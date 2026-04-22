using PuppeteerSharp;
using PuppeteerSharp.Media;
using System.Runtime.InteropServices;
using System.Text;

namespace Calcpad.Server.Services
{
    public class PdfGeneratorService
    {
        public class PdfSettings
        {
            public string Format { get; set; } = "Letter";
            public string Orientation { get; set; } = "portrait";
            public bool PrintBackground { get; set; } = true;
            public double Scale { get; set; } = 1.0;
            public string MarginTop { get; set; } = "2cm";
            public string MarginRight { get; set; } = "1.5cm";
            public string MarginBottom { get; set; } = "2cm";
            public string MarginLeft { get; set; } = "1.5cm";
            public bool EnableHeader { get; set; } = false;
            public bool EnableFooter { get; set; } = false;
            public string DocumentTitle { get; set; } = "Document";
            public string DocumentSubtitle { get; set; } = "";
            public string HeaderCenter { get; set; } = "";
            public string FooterCenter { get; set; } = "";
            public string Author { get; set; } = "";
            public string Company { get; set; } = "";
            public string Project { get; set; } = "";
        }

        private readonly ILogger<PdfGeneratorService> _logger;

        public PdfGeneratorService(ILogger<PdfGeneratorService> logger)
        {
            _logger = logger;
        }

        public async Task<byte[]> GeneratePdfAsync(string htmlContent, PdfSettings? pdfSettings = null)
        {
            var settings = pdfSettings ?? new PdfSettings();
            
            try
            {
                _logger.LogInformation("Starting PDF generation");
                
                var browserPath = await FindBrowserAsync();
                _logger.LogInformation($"Using browser: {browserPath}");

                // Minimal environment setup
                
                var launchOptions = new LaunchOptions
                {
                    Headless = true,
                    ExecutablePath = browserPath,
                    Args = GetBrowserArgs(),
                    Timeout = 30000,
                    IgnoreDefaultArgs = false,
                    LogProcess = true
                };
                
                // Minimal environment variables - only set DISPLAY for non-Docker Linux
                var isLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
                var isDocker = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true";
                
                if (isLinux && !isDocker)
                {
                    launchOptions.Env["DISPLAY"] = ":99";
                }

                _logger.LogDebug("Attempting to launch browser with arguments: " + string.Join(" ", launchOptions.Args));
                using var browser = await Puppeteer.LaunchAsync(launchOptions);
                _logger.LogDebug("Browser launched successfully");
                using var page = await browser.NewPageAsync();
                _logger.LogDebug("New page created successfully");

                // Inject Content-Security-Policy to block all external resource loading (SSRF prevention)
                const string pdfCsp = "<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'; img-src data:; font-src data:;\">";
                var safeHtml = htmlContent.Replace("<head>", $"<head>\n{pdfCsp}");

                // Set content and wait for DOM to be ready (not Networkidle0, which waits for external fetches)
                await page.SetContentAsync(safeHtml, new NavigationOptions
                {
                    WaitUntil = new[] { WaitUntilNavigation.DOMContentLoaded }
                });

                // Configure PDF options
                var pdfOptions = new PdfOptions
                {
                    Format = settings.Format switch
                    {
                        "A3" => PaperFormat.A3,
                        "A4" => PaperFormat.A4,
                        "A5" => PaperFormat.A5,
                        "Legal" => PaperFormat.Legal,
                        "Letter" => PaperFormat.Letter,
                        "Tabloid" => PaperFormat.Tabloid,
                        _ => PaperFormat.Letter
                    },
                    Landscape = settings.Orientation.Equals("landscape", StringComparison.OrdinalIgnoreCase),
                    PrintBackground = settings.PrintBackground,
                    Scale = (decimal)settings.Scale,
                    MarginOptions = new MarginOptions
                    {
                        Top = settings.MarginTop,
                        Right = settings.MarginRight,
                        Bottom = settings.MarginBottom,
                        Left = settings.MarginLeft
                    },
                    DisplayHeaderFooter = settings.EnableHeader || settings.EnableFooter,
                    HeaderTemplate = settings.EnableHeader ? GenerateHeaderTemplate(settings) : "<span></span>",
                    FooterTemplate = settings.EnableFooter ? GenerateFooterTemplate(settings) : "<span></span>",
                    PreferCSSPageSize = false
                };

                // Generate PDF
                var pdfBytes = await page.PdfDataAsync(pdfOptions);
                
                _logger.LogInformation($"PDF generation completed, size: {pdfBytes.Length} bytes");
                return pdfBytes;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate PDF: {ExceptionType}: {Message}", ex.GetType().Name, ex.Message);
                
                var errorMessage = ex.Message;
                if (ex.InnerException != null)
                {
                    errorMessage += $" Inner: {ex.InnerException.Message}";
                }
                
                throw new InvalidOperationException($"PDF generation failed: {errorMessage}", ex);
            }
        }

        private Task<string> FindBrowserAsync()
        {
            // Check for custom browser path first (from Windows tray app settings)
            var customBrowserPath = Environment.GetEnvironmentVariable("CALCPAD_BROWSER_PATH");
            if (!string.IsNullOrEmpty(customBrowserPath))
            {
                if (File.Exists(customBrowserPath))
                {
                    _logger.LogInformation($"Using custom browser path: {customBrowserPath}");
                    return Task.FromResult(customBrowserPath);
                }
                else
                {
                    _logger.LogWarning($"Custom browser path not found: {customBrowserPath}, falling back to auto-detection");
                }
            }
            
            var browserPaths = GetBrowserPaths();
            
            foreach (var path in browserPaths)
            {
                try
                {
                    if (File.Exists(path))
                    {
                        _logger.LogDebug($"Found browser at: {path}");
                        return Task.FromResult(path);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug($"Error checking browser path {path}: {ex.Message}");
                }
            }

            var installInstructions = GetBrowserInstallInstructions();
            return Task.FromException<string>(new InvalidOperationException($"No browser found for PDF generation. {installInstructions}"));
        }

        private IEnumerable<string> GetBrowserPaths()
        {
            var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            var isLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
            var isMacOS = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
            var isWSL = Environment.GetEnvironmentVariable("WSL_DISTRO_NAME") != null ||
                       Environment.GetEnvironmentVariable("WSLENV") != null;

            var paths = new List<string>();

            if (isWindows)
            {
                // Windows paths
                paths.AddRange(new[]
                {
                    @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
                    @"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
                    @"C:\Program Files\Google\Chrome\Application\chrome.exe",
                    @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe"
                });
            }
            else if (isMacOS)
            {
                // macOS paths
                paths.AddRange(new[]
                {
                    "/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge",
                    "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
                    "/Applications/Chromium.app/Contents/MacOS/Chromium"
                });
            }
            else if (isLinux)
            {
                // Linux paths (prioritize commonly available browsers)
                paths.AddRange(new[]
                {
                    "/usr/bin/chromium",              // Debian/Docker chromium package
                    "/usr/bin/chromium-browser",      // Ubuntu chromium-browser package
                    "/usr/bin/google-chrome-stable",  // Official Google Chrome
                    "/usr/bin/google-chrome",
                    "/snap/bin/chromium",             // Snap package
                    "/usr/bin/chromium-browser-stable", // Alternative naming
                    "/usr/bin/microsoft-edge-stable", // Microsoft Edge for Linux
                    "/usr/bin/microsoft-edge"
                });

                // If running in WSL, also check Windows paths
                if (isWSL)
                {
                    _logger.LogDebug("WSL detected, adding Windows browser paths");
                    paths.AddRange(new[]
                    {
                        "/mnt/c/Program Files (x86)/Microsoft/Edge/Application/msedge.exe",
                        "/mnt/c/Program Files/Microsoft/Edge/Application/msedge.exe",
                        "/mnt/c/Program Files/Google/Chrome/Application/chrome.exe",
                        "/mnt/c/Program Files (x86)/Google/Chrome/Application/chrome.exe"
                    });
                }
            }

            return paths;
        }

        private string[] GetBrowserArgs()
        {
            var isLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
            var isWSL = Environment.GetEnvironmentVariable("WSL_DISTRO_NAME") != null ||
                       Environment.GetEnvironmentVariable("WSLENV") != null;

            // Minimal arguments based on 2024-2025 PuppeteerSharp best practices
            var baseArgs = new List<string>
            {
                "--no-sandbox",
                "--disable-setuid-sandbox",
                "--disable-dev-shm-usage",
                "--disable-gpu",
                "--no-first-run",
                "--disable-extensions",
                "--disable-default-apps",
                "--mute-audio",
                "--hide-scrollbars",
                "--disable-crash-reporter",
                "--no-crash-upload",
                "--disable-crashpad-for-testing",
                "--disable-remote-fonts",
                "--disable-background-networking",
                "--disable-sync",
                "--disable-translate"
            };

            return baseArgs.Distinct().ToArray(); // Remove duplicates
        }

        private string GetBrowserInstallInstructions()
        {
            var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            var isLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
            var isMacOS = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
            var isWSL = Environment.GetEnvironmentVariable("WSL_DISTRO_NAME") != null;

            if (isWindows)
            {
                return "Please install Microsoft Edge or Google Chrome.";
            }
            else if (isMacOS)
            {
                return "Please install Google Chrome, Microsoft Edge, or Chromium.";
            }
            else if (isWSL)
            {
                return "Please install chromium in WSL:\n" +
                       "sudo apt update && sudo apt install chromium-browser\n" +
                       "OR ensure Windows Edge/Chrome is accessible.";
            }
            else if (isLinux)
            {
                return "Please install chromium:\n" +
                       "Ubuntu/WSL: sudo apt update && sudo apt install chromium-browser\n" +
                       "Debian/Docker: sudo apt update && sudo apt install chromium";
            }

            return "Please install Google Chrome, Microsoft Edge, or Chromium.";
        }


        private string GenerateHeaderTemplate(PdfSettings settings)
        {
            var now = DateTime.Now;
            return $@"
                <div style='font-size:10px; padding: 5px; width: 100%; text-align: center; border-bottom: 1px solid #ccc;'>
                    <div style='margin-bottom: 5px;'>
                        <strong>{settings.DocumentTitle}</strong>
                        {(string.IsNullOrEmpty(settings.DocumentSubtitle) ? "" : $" - {settings.DocumentSubtitle}")}
                    </div>
                    <div style='font-size: 8px; color: #666;'>
                        {settings.HeaderCenter} | Generated: {now:yyyy-MM-dd HH:mm}
                    </div>
                </div>";
        }

        private string GenerateFooterTemplate(PdfSettings settings)
        {
            return $@"
                <div style='font-size:10px; padding: 5px; width: 100%; display: flex; justify-content: space-between; align-items: center; border-top: 1px solid #ccc;'>
                    <div style='flex: 1; text-align: left; font-size: 8px; color: #666;'>
                        {(string.IsNullOrEmpty(settings.Author) ? "" : $"Author: {settings.Author}")}<br/>
                        {(string.IsNullOrEmpty(settings.Company) ? "" : settings.Company)}
                    </div>
                    <div style='flex: 1; text-align: center; font-size: 8px; color: #666;'>
                        {settings.FooterCenter}
                    </div>
                    <div style='flex: 1; text-align: right; font-size: 8px; color: #666;'>
                        Page <span class='pageNumber'></span> of <span class='totalPages'></span><br/>
                        {(string.IsNullOrEmpty(settings.Project) ? "" : $"Project: {settings.Project}")}
                    </div>
                </div>";
        }
    }
}