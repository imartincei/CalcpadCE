using Calcpad.Server.Services;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.RateLimiting;

namespace Calcpad.Server.Services
{
    /// <summary>
    /// Shared service for configuring and running the Calcpad API server
    /// This contains all the common logic between Windows and Linux implementations
    /// </summary>
    public static class CalcpadApiService
    {
        /// <summary>
        /// When true, strict security restrictions are applied (public Docker server).
        /// When false (default), the server runs in permissive local mode (Windows tray app).
        /// Controlled by the CALCPAD_PUBLIC_MODE environment variable.
        /// </summary>
        public static bool IsPublicMode { get; } =
            string.Equals(Environment.GetEnvironmentVariable("CALCPAD_PUBLIC_MODE"), "true", StringComparison.OrdinalIgnoreCase)
            || Environment.GetEnvironmentVariable("CALCPAD_PUBLIC_MODE") == "1";

        /// <summary>
        /// Configure the web application builder with all necessary services
        /// </summary>
        public static WebApplicationBuilder ConfigureBuilder(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Limit request body size: 512 KB in public mode, 50 MB locally
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.Limits.MaxRequestBodySize = IsPublicMode ? 512_000 : 50_000_000;
            });

            // Add services to the container
            builder.Services.AddControllers()
                .AddApplicationPart(typeof(CalcpadApiService).Assembly); // Discover controllers from Core assembly
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();
            builder.Services.AddScoped<CalcpadService>();
            builder.Services.AddScoped<PdfGeneratorService>();

            // Add CORS policy — restricted in public mode, open locally
            builder.Services.AddCors(options =>
            {
                options.AddPolicy("AllowAll", policy =>
                {
                    if (IsPublicMode)
                    {
                        var corsOrigins = Environment.GetEnvironmentVariable("CALCPAD_CORS_ORIGINS")
                            ?? "https://calcpad-ce.org";
                        policy.WithOrigins(corsOrigins.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                              .AllowAnyMethod()
                              .AllowAnyHeader();
                    }
                    else
                    {
                        policy.AllowAnyOrigin()
                              .AllowAnyMethod()
                              .AllowAnyHeader();
                    }
                });
            });

            // Rate limiting — strict in public mode, effectively unlimited locally
            builder.Services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                var convertLimit = IsPublicMode ? 20 : int.MaxValue;
                var pdfLimit = IsPublicMode ? 5 : int.MaxValue;
                var generalLimit = IsPublicMode ? 100 : int.MaxValue;

                options.AddFixedWindowLimiter("convert", opt =>
                {
                    opt.PermitLimit = convertLimit;
                    opt.Window = TimeSpan.FromMinutes(1);
                    opt.QueueLimit = 0;
                });
                options.AddFixedWindowLimiter("pdf", opt =>
                {
                    opt.PermitLimit = pdfLimit;
                    opt.Window = TimeSpan.FromMinutes(1);
                    opt.QueueLimit = 0;
                });
                options.AddFixedWindowLimiter("general", opt =>
                {
                    opt.PermitLimit = generalLimit;
                    opt.Window = TimeSpan.FromMinutes(1);
                    opt.QueueLimit = 0;
                });
            });

            return builder;
        }

        /// <summary>
        /// Configure the web application pipeline
        /// </summary>
        public static WebApplication ConfigureApp(WebApplication app)
        {
            // Expose Swagger only in local mode (not on the public server)
            if (!IsPublicMode)
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseHttpsRedirection();

            // Security headers
            app.Use(async (context, next) =>
            {
                context.Response.Headers["X-Content-Type-Options"] = "nosniff";
                context.Response.Headers["X-Frame-Options"] = "DENY";
                context.Response.Headers["Cache-Control"] = "no-store";
                context.Response.Headers["Referrer-Policy"] = "no-referrer";
                await next();
            });

            app.UseCors("AllowAll");
            app.UseRateLimiter();
            app.MapControllers();

            var mode = IsPublicMode ? "PUBLIC" : "LOCAL";
            Console.WriteLine($"Calcpad server starting in {mode} mode");

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
            
            // Handle Docker scenario where host might be *
            if (host == "*")
            {
                host = "0.0.0.0"; // Bind to all interfaces in Docker
            }
            
            return $"{protocol}://{host}:{port}";
        }

        /// <summary>
        /// Test if the server is responding at the given URL
        /// </summary>
        public static async Task<bool> TestServerAsync(string serverUrl, int timeoutSeconds = 3)
        {
            try
            {
                using var httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
                var response = await httpClient.GetAsync($"{serverUrl}/api/calcpad/sample");
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
            var serverUrl = GetServerUrl();
            
            FileLogger.LogInfo("Configuring server URLs", serverUrl);
            builder.WebHost.UseUrls(serverUrl);

            FileLogger.LogInfo("Building application");
            var app = builder.Build();
            
            ConfigureApp(app);
            
            return (app, serverUrl);
        }
    }
}