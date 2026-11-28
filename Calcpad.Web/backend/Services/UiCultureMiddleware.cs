using System;
using System.Globalization;
using System.Linq;
using Microsoft.AspNetCore.Builder;

namespace Calcpad.Server.Services
{
    /// <summary>Sets CurrentUICulture from Accept-Language; CurrentCulture (math/output numbers) is left alone.</summary>
    public static class UiCultureMiddleware
    {
        private static readonly string[] Supported =
            ["en", "bg", "cs", "de", "es", "fr", "it", "ja", "ko", "pl", "pt-BR", "ru", "tr", "zh", "zh-Hant"];

        public static IApplicationBuilder UseUiCulture(this IApplicationBuilder app) =>
            app.Use(async (context, next) =>
            {
                var culture = Match(context.Request.Headers.AcceptLanguage.ToString());
                if (culture is null)
                {
                    await next();
                    return;
                }

                var previous = CultureInfo.CurrentUICulture;
                CultureInfo.CurrentUICulture = culture;
                try
                {
                    await next();
                }
                finally
                {
                    CultureInfo.CurrentUICulture = previous;
                }
            });

        internal static CultureInfo? Match(string header)
        {
            foreach (var part in header.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var tag = part.Split(';')[0].Trim();
                if (tag.Length == 0 || tag == "*") continue;
                var normalized = tag.Replace('_', '-');
                var lower = normalized.ToLowerInvariant();
                if (lower == "zh" || lower.StartsWith("zh-", StringComparison.Ordinal))
                {
                    var traditional = lower.Contains("-hant", StringComparison.Ordinal)
                        || lower.Contains("-tw", StringComparison.Ordinal)
                        || lower.Contains("-hk", StringComparison.Ordinal)
                        || lower.Contains("-mo", StringComparison.Ordinal);
                    return CultureInfo.GetCultureInfo(traditional ? "zh-Hant" : "zh");
                }
                // Bare "pt" is ambiguous (Portugal vs Brazil); require an explicit pt-BR tag.
                if (lower == "pt" || lower.StartsWith("pt-", StringComparison.Ordinal))
                {
                    if (lower == "pt-br" || lower.StartsWith("pt-br-", StringComparison.Ordinal))
                        return CultureInfo.GetCultureInfo("pt-BR");
                    continue;
                }
                var match = Supported.FirstOrDefault(candidate =>
                    string.Equals(normalized, candidate, StringComparison.OrdinalIgnoreCase) ||
                    normalized.StartsWith(candidate + "-", StringComparison.OrdinalIgnoreCase));
                if (match is not null)
                    return CultureInfo.GetCultureInfo(match);
            }
            return null;
        }
    }
}
