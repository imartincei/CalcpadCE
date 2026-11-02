using System.Text.Json.Serialization;
using Calcpad.S3Server.Data;
using Calcpad.S3Server.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace Calcpad.S3Server
{
    public static class Program
    {
        public const string AdminOnly = "AdminOnly";
        private const int MinSecretLength = 32;

        public static async Task Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
            var config = builder.Configuration;

            var jwt = config.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
            if (jwt.Secret.Length < MinSecretLength)
                throw new InvalidOperationException($"Jwt__Secret must be set to at least {MinSecretLength} characters.");

            var s3 = config.GetSection("S3").Get<S3Options>() ?? new S3Options();
            var dbPath = config["Data:Path"] ?? Path.Combine(AppContext.BaseDirectory, "data", "s3server.db");
            Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

            builder.Services.AddSingleton(jwt);
            builder.Services.AddSingleton<IObjectStore>(new S3ObjectStore(s3));
            builder.Services.AddSingleton<ObjectService>();
            builder.Services.AddDbContext<S3ServerDbContext>(o => o.UseSqlite($"Data Source={dbPath}"));
            builder.Services.AddScoped<AuthService>();
            builder.Services.AddScoped<BucketAccess>();
            builder.Services.AddControllers()
                .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

            builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(o =>
                {
                    o.MapInboundClaims = false;
                    o.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidIssuer = jwt.Issuer,
                        ValidateAudience = true,
                        ValidAudience = jwt.Audience,
                        ValidateLifetime = true,
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = jwt.SigningKey
                    };
                });
            builder.Services.AddAuthorizationBuilder()
                .AddPolicy(AdminOnly, p => p.RequireClaim(AuthService.RoleClaim, "3"));

            var app = builder.Build();

            using (var scope = app.Services.CreateScope())
            {
                scope.ServiceProvider.GetRequiredService<S3ServerDbContext>().Database.EnsureCreated();
                await scope.ServiceProvider.GetRequiredService<AuthService>()
                    .SeedAdminAsync(config["Admin:Username"], config["Admin:Password"], app.Logger);
            }

            app.UseAuthentication();
            app.UseAuthorization();
            app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));
            app.MapControllers();
            await app.RunAsync();
        }
    }
}
