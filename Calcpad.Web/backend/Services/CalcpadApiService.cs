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

            builder.Services.AddControllers()
                .AddApplicationPart(typeof(CalcpadApiService).Assembly);
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();
            builder.Services.AddScoped<CalcpadService>();

            // PDF generation service (singleton for browser reuse)
            builder.Services.AddSingleton<PdfGeneratorService>();

            // Caches ContentResolver's staged-content output across lint/highlight/definitions/
            // symbol-at-position for the same content, singleton so entries survive across requests.
            builder.Services.AddMemoryCache(options =>
            {
                options.SizeLimit = int.TryParse(
                    Environment.GetEnvironmentVariable("CALCPAD_CONTENT_CACHE_SIZE_LIMIT"), out var limit)
                    ? limit : 100;
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

        internal const string CorsPolicyName = "CalcpadHosts";

        /// <summary>
        /// Header carrying the per-launch token that <see cref="RequireApiToken"/> checks.
        /// </summary>
        internal const string ApiTokenHeader = "X-Calcpad-Token";

        /// <summary>
        /// Per-launch shared secret, handed to us by the host that spawned this process.
        /// </summary>
        /// <remarks>
        /// Read from the environment rather than argv: argv is world-readable through
        /// <c>/proc/{pid}/cmdline</c> on Linux and through WMI on Windows, which would hand
        /// the token to exactly the local processes it exists to keep out. The environment
        /// block is readable only by the same user (and root), which is the boundary this
        /// server already lives inside.
        /// <para>
        /// Absent when nobody set it — <c>dotnet run</c> during development, the test
        /// harness, a shell launch. Auth is then off and the CORS + Host-header policy in
        /// <see cref="IsAllowedOrigin"/> is the only control, which is the behavior that
        /// predates this. Both shipped hosts (the Tauri desktop shell and the VS Code
        /// extension) always set it, so a user-facing launch is always authenticated.
        /// </para>
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
        /// Binding to loopback keeps remote machines out, but not other programs on
        /// this one — and that includes the user's ordinary web browser. A page the
        /// user visits in Chrome can POST to <c>http://127.0.0.1:{port}/api/calcpad/convert</c>
        /// with <c>#include ~/.ssh/id_rsa</c>; include resolution reads whatever path
        /// it is handed (see <see cref="CalcpadService.CreateIncludeDelegate"/>) and
        /// returns it in the rendered HTML. The server has no authentication, so the
        /// requesting origin is the only thing standing between that page and the
        /// file. <c>AllowAnyOrigin</c> removed it. The desktop app's own WebView is
        /// not involved in any of this.
        /// <para>
        /// The random port is not a defense: an ephemeral range is a few tens of
        /// thousands of ports, sweepable from a page in seconds and fingerprintable
        /// off <c>/api/calcpad/sample</c>.
        /// </para>
        /// <para>
        /// Restricting the origin blocks the request rather than just the response.
        /// The endpoints take <c>[FromBody]</c> on an <c>[ApiController]</c>, so they
        /// require <c>Content-Type: application/json</c> — a non-simple request that
        /// needs a successful preflight before the browser will send it at all.
        /// </para>
        /// <para>
        /// This does not stop DNS rebinding, where the attacker's own hostname
        /// resolves to 127.0.0.1 and the request is therefore same-origin with no
        /// CORS check at all. <see cref="ConfigureApp"/> validates the Host header
        /// for that.
        /// </para>
        /// <para>
        /// Native callers (the VS Code extension host, build scripts) send no Origin
        /// header and never reach this — CORS is enforced by browsers, not servers.
        /// <c>vscode-webview://</c> is deliberately absent: VS Code webviews reach
        /// the API through the extension host over postMessage, never by fetch.
        /// </para>
        /// <para>
        /// "null" is rejected. That is what a sandboxed, opaque-origin frame sends,
        /// which is exactly how the desktop app renders untrusted worksheet HTML.
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

            // DNS rebinding defense, and the reason CORS alone is not enough. An
            // attacker who points their own hostname at 127.0.0.1 makes the request
            // same-origin from the browser's point of view, so no CORS check runs —
            // but the Host header still carries their name rather than ours. Only
            // loopback names are served. Requests with no Host header at all are let
            // through: HTTP/1.1 requires one and browsers always send it, so its
            // absence means a native client, which was never the exposure here.
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

            FileLogger.LogInfo("Configuring server URLs", serverUrl);
            builder.WebHost.UseUrls(serverUrl);

            FileLogger.LogInfo("Building application");
            var app = builder.Build();

            ConfigureApp(app);

            return (app, serverUrl);
        }
    }
}
