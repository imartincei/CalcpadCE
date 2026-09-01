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

            // ASP.NET's own console provider writes through Console.Out, which
            // InstallConsoleRelay has replaced — so its output reaches stdout and the hosts'
            // Output panels without ever passing FileLogger's filter. At Information that is a
            // "Request starting"/"Request finished" pair per request, as much noise as our own
            // logging, so it tracks MinLevel too.
            //
            // Microsoft.Hosting.Lifetime is pinned back: it logs "Now listening on: <url>",
            // which the Tauri host sniffs (extract_listening_url) as its fallback when the port
            // file is unreadable. Raising the floor without this silently removes that path.
            builder.Logging.SetMinimumLevel(FrameworkLevelFor(FileLogger.MinLevel));
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
        /// Our verbosity mapped onto the framework's. Only read at startup — the framework
        /// caches filters per (provider, category), so a later <see cref="FileLogger.MinLevel"/>
        /// change does not reach it.
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
        /// Read from the environment rather than argv, which is world-readable through
        /// <c>/proc/{pid}/cmdline</c> on Linux and WMI on Windows; the environment block is
        /// readable only by the same user. Absent when nobody set it (<c>dotnet run</c>, the
        /// test harness), in which case auth is off and the CORS + Host-header policy in
        /// <see cref="IsAllowedOrigin"/> is the only control — both shipped hosts always set it.
        /// </remarks>
        private static readonly byte[]? ApiTokenBytes =
            Environment.GetEnvironmentVariable("CALCPAD_API_TOKEN") is { Length: > 0 } token
                ? Encoding.UTF8.GetBytes(token)
                : null;

        /// <summary>
        /// Rejects any <c>/api</c> request that does not present the launch token.
        /// </summary>
        /// <remarks>
        /// Registered after <c>UseCors</c> so a browser preflight is answered by the CORS
        /// middleware and never reaches this — the preflight itself carries no custom
        /// headers, so checking it would fail every cross-origin request before the real
        /// one was ever sent. <c>OPTIONS</c> is skipped for the same reason.
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
        /// Binding to loopback keeps remote machines out but not the user's own browser: a page
        /// they visit can POST <c>#include ~/.ssh/id_rsa</c> to <c>/api/calcpad/convert</c> and
        /// read the file back out of the rendered HTML, so the requesting origin is what stands
        /// between that page and the file. Restricting it blocks the request rather than just
        /// the response, since <c>[FromBody]</c> on an <c>[ApiController]</c> forces a preflight.
        /// <para>
        /// Native callers send no Origin header and never reach this; <c>vscode-webview://</c>
        /// is deliberately absent, and "null" — what a sandboxed opaque-origin frame sends — is
        /// rejected. DNS rebinding is handled by the Host-header check in
        /// <see cref="ConfigureApp"/> instead.
        /// </para>
        /// </remarks>
        internal static bool IsAllowedOrigin(string origin)
        {
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)) return false;

            // Tauri serves the desktop shell from a custom scheme: tauri://localhost
            // on Linux/macOS, http(s)://tauri.localhost on Windows (WebView2).
            if (uri.Scheme.Equals("tauri", StringComparison.OrdinalIgnoreCase)) return true;
            if (uri.Host.Equals("tauri.localhost", StringComparison.OrdinalIgnoreCase)) return true;

            // The browser build of the editor, served from loopback on any port. This
            // also admits any other local server's pages, which is the accepted floor:
            // anything able to serve on this machine's loopback is already past the
            // boundary this policy defends.
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
                    // The client disconnected or superseded this request — expected under
                    // rapid tab-switching, not a server error, so it isn't logged as one.
                }
                catch (Exception ex)
                {
                    FileLogger.LogError($"Unhandled exception in request: {context.Request.Method} {context.Request.Path}", ex);
                    context.Response.StatusCode = 500;
                    await context.Response.WriteAsync("Internal Server Error");
                }
            });

            // DNS rebinding defense, and the reason CORS alone is not enough: an attacker who
            // points their own hostname at 127.0.0.1 makes the request same-origin, so no CORS
            // check runs, but the Host header still carries their name. Only loopback names are
            // served, and a request with no Host header at all is let through as a native
            // client, which was never the exposure here.
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
