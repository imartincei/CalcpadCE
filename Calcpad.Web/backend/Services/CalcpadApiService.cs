using Microsoft.Extensions.Caching.Memory;
using System.Security.Cryptography;
using System.Text;

namespace Calcpad.Server.Services
{
    /// <summary>
    /// Shared service for configuring and running the Calcpad API server
    /// </summary>
    public static class CalcpadApiService
    {
        private static readonly HttpClient _healthCheckClient = new();

        /// <summary>
        /// Configure the web application builder with all necessary services
        /// </summary>
        public static WebApplicationBuilder ConfigureBuilder(string[] args)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                Args = args,
                ContentRootPath = AppContext.BaseDirectory
            });

            // ASP.NET's console provider writes through Console.Out, which InstallConsoleRelay
            // has replaced, so its output reaches the hosts' Output panels without passing
            // FileLogger's filter — it has to track MinLevel too.
            //
            // A catch-all rule, not SetMinimumLevel: that applies only when no rule matches, and
            // appsettings.json's Logging:LogLevel:Default is such a rule, so it would always win.
            //
            // Microsoft.Hosting.Lifetime is pinned back for "Now listening on: <url>", which the
            // Tauri host sniffs (extract_listening_url) when the port file is unreadable.
            builder.Logging.AddFilter((string?)null, FrameworkLevelFor(FileLogger.MinLevel));
            builder.Logging.AddFilter("Microsoft.Hosting.Lifetime", Microsoft.Extensions.Logging.LogLevel.Information);

            builder.Services.AddControllers()
                .AddApplicationPart(typeof(CalcpadApiService).Assembly);
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();
            builder.Services.AddScoped<CalcpadService>();

            // PDF generation service (singleton for browser reuse)
            builder.Services.AddSingleton<PdfGeneratorService>();

            // Caches ContentResolver's staged-content output across lint/highlight/definitions/
            // symbol-at-position for the same content, singleton so entries survive across requests.
            // Budget is in flattened source lines, not entries — see ContentResolutionCache.
            builder.Services.AddMemoryCache(options =>
            {
                options.SizeLimit = ContentResolutionCache.ResolveSizeLimit();
            });
            builder.Services.AddSingleton<ContentResolutionCache>();

            builder.Services.AddCors(options =>
            {
                options.AddPolicy(CorsPolicyName, policy =>
                {
                    policy.SetIsOriginAllowed(IsAllowedOrigin)
                          .AllowAnyMethod()
                          .AllowAnyHeader()
                          .WithExposedHeaders("X-Calcpad-Errors");
                });
            });

            return builder;
        }

        /// <summary>
        /// Our verbosity mapped onto the framework's. Startup only: the framework caches filters
        /// per (provider, category), so a later <see cref="FileLogger.MinLevel"/> change misses.
        /// </summary>
        private static Microsoft.Extensions.Logging.LogLevel FrameworkLevelFor(LogLevel level) => level switch
        {
            LogLevel.Verbose => Microsoft.Extensions.Logging.LogLevel.Debug,
            LogLevel.Information => Microsoft.Extensions.Logging.LogLevel.Information,
            LogLevel.Warning => Microsoft.Extensions.Logging.LogLevel.Warning,
            _ => Microsoft.Extensions.Logging.LogLevel.Error,
        };

        internal const string CorsPolicyName = "CalcpadHosts";

        /// <summary>
        /// Header carrying the per-launch token that <see cref="RequireApiToken"/> checks.
        /// </summary>
        internal const string ApiTokenHeader = "X-Calcpad-Token";

        private const string HealthPath = "/api/calcpad/health";

        /// <summary>
        /// Per-launch shared secret, handed to us by the host that spawned this process.
        /// </summary>
        /// <remarks>
        /// From the environment, not argv: argv is world-readable (<c>/proc/{pid}/cmdline</c>,
        /// WMI). Absent under <c>dotnet run</c> and tests, where auth is off and the CORS +
        /// Host-header policy is the only control; both shipped hosts always set it.
        /// </remarks>
        private static readonly byte[]? ApiTokenBytes =
            Environment.GetEnvironmentVariable("CALCPAD_API_TOKEN") is { Length: > 0 } token
                ? Encoding.UTF8.GetBytes(token)
                : null;

        /// <summary>
        /// Rejects any <c>/api</c> request that does not present the launch token.
        /// </summary>
        /// <remarks>
        /// After <c>UseCors</c> so preflights are answered before reaching this: a preflight
        /// carries no custom headers, so checking it would fail every cross-origin request.
        /// <c>OPTIONS</c> is skipped for the same reason.
        /// </remarks>
        private static void RequireApiToken(WebApplication app)
        {
            var expected = ApiTokenBytes;
            if (expected is null)
            {
                FileLogger.LogWarning(
                    "CALCPAD_API_TOKEN not set — /api is unauthenticated",
                    "Any local process can reach this server. Expected only for development launches.");
                return;
            }

            app.Use(async (context, next) =>
            {
                if (!context.Request.Path.StartsWithSegments("/api")
                    || HttpMethods.IsOptions(context.Request.Method))
                {
                    await next();
                    return;
                }

                var presented = Encoding.UTF8.GetBytes(context.Request.Headers[ApiTokenHeader].ToString());
                if (!CryptographicOperations.FixedTimeEquals(presented, expected))
                {
                    context.Response.StatusCode = 401;
                    await context.Response.WriteAsync("Unauthorized");
                    return;
                }
                await next();
            });
        }

        /// <summary>
        /// Origins permitted to call the API from a browsing context.
        /// </summary>
        /// <remarks>
        /// Loopback keeps remote machines out but not the user's own browser: a page they visit
        /// can POST <c>#include ~/.ssh/id_rsa</c> to <c>/convert</c> and read the file back out
        /// of the HTML. <c>[FromBody]</c> forces a preflight, so this blocks the request itself.
        /// <para>
        /// Native callers send no Origin and never reach this; <c>vscode-webview://</c> and
        /// "null" (a sandboxed opaque origin) are deliberately rejected. DNS rebinding is the
        /// Host-header check in <see cref="ConfigureApp"/> instead.
        /// </para>
        /// </remarks>
        internal static bool IsAllowedOrigin(string origin)
        {
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)) return false;

            // tauri://localhost on Linux/macOS, tauri.localhost on Windows (WebView2).
            if (uri.Scheme.Equals("tauri", StringComparison.OrdinalIgnoreCase)) return true;
            if (uri.Host.Equals("tauri.localhost", StringComparison.OrdinalIgnoreCase)) return true;

            // The browser build, on any loopback port. Admits other local servers' pages too —
            // the accepted floor, since serving on loopback is already past this boundary.
            return Program.IsLoopbackHost(uri.Host);
        }

        /// <summary>
        /// Configure the web application pipeline
        /// </summary>
        public static WebApplication ConfigureApp(WebApplication app)
        {
            // Outermost, so the in-flight set covers the whole pipeline.
            HangWatchdog.Start();
            app.Use(async (context, next) =>
            {
                // Tracking the poll would refresh the stall clock forever and hide a real hang.
                if (context.Request.Path.Equals(HealthPath, StringComparison.OrdinalIgnoreCase))
                {
                    await next();
                    return;
                }
                using (HangWatchdog.Track($"{context.Request.Method} {context.Request.Path}"))
                    await next();
            });

            app.Use(async (context, next) =>
            {
                try
                {
                    await next();
                }
                catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
                {
                    // Client disconnected or superseded the request — not a server error.
                }
                catch (Exception ex)
                {
                    FileLogger.LogError($"Unhandled exception in request: {context.Request.Method} {context.Request.Path}", ex);
                    context.Response.StatusCode = 500;
                    await context.Response.WriteAsync("Internal Server Error");
                }
            });

            // DNS rebinding defense, and why CORS alone is not enough: a hostname pointed at
            // 127.0.0.1 makes the request same-origin, so no CORS check runs, but the Host
            // header still names them. No Host at all is a native client, never the exposure.
            app.Use(async (context, next) =>
            {
                var host = context.Request.Host.Host;
                if (!string.IsNullOrEmpty(host) && !Program.IsLoopbackHost(host))
                {
                    FileLogger.LogWarning(
                        "Rejected request with non-loopback Host header",
                        $"{host} — {context.Request.Method} {context.Request.Path}");
                    context.Response.StatusCode = 421; // Misdirected Request
                    await context.Response.WriteAsync("Misdirected Request");
                    return;
                }
                await next();
            });

            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseHttpsRedirection();
            app.UseCors(CorsPolicyName);
            RequireApiToken(app);

            app.MapControllers();

            return app;
        }

        /// <summary>
        /// Get the server URL from environment variables
        /// </summary>
        public static string GetServerUrl()
        {
            var port = Environment.GetEnvironmentVariable("CALCPAD_PORT") ?? "9420";
            var host = Environment.GetEnvironmentVariable("CALCPAD_HOST") ?? "localhost";
            var protocol = Environment.GetEnvironmentVariable("CALCPAD_ENABLE_HTTPS")?.ToLower() == "true" ? "https" : "http";
            return $"{protocol}://{host}:{port}";
        }

        /// <summary>
        /// Test if the server is responding at the given URL
        /// </summary>
        public static async Task<bool> TestServerAsync(string serverUrl, int timeoutSeconds = 3)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
                var response = await _healthCheckClient.GetAsync($"{serverUrl}/api/calcpad/sample", cts.Token).ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Create a configured web application ready to run
        /// </summary>
        public static (WebApplication app, string serverUrl) CreateConfiguredApp(string[] args)
        {
            var builder = ConfigureBuilder(args);

            string? cliUrls = null;
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "--urls")
                {
                    cliUrls = args[i + 1];
                    break;
                }
            }

            var serverUrl = cliUrls ?? GetServerUrl();

            FileLogger.LogVerbose("Configuring server URLs", serverUrl);
            builder.WebHost.UseUrls(serverUrl);

            FileLogger.LogVerbose("Building application");
            var app = builder.Build();

            ConfigureApp(app);

            return (app, serverUrl);
        }
    }
}
