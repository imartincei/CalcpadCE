using Calcpad.Core;
using Microsoft.Extensions.Caching.Memory;

namespace Calcpad.Server.Services
{
    /// <summary>
    /// The latest <see cref="RuntimeTrace"/> per document, recorded by a debug /convert, so lint
    /// and definitions can use what Core actually executed for the same content.
    /// </summary>
    public class RuntimeTraceCache
    {
        public sealed record Entry(string Key, RuntimeTrace Trace, DateTime StoredUtc, long Version);

        private readonly IMemoryCache _cache;
        private readonly TimeSpan _expiration;
        private readonly int _sizeLimit;
        private long _version;

        public RuntimeTraceCache(IMemoryCache cache)
        {
            _cache = cache;
            _sizeLimit = ContentResolutionCache.ResolveSizeLimit();
            _expiration = TimeSpan.FromSeconds(int.TryParse(
                Environment.GetEnvironmentVariable("CALCPAD_CONTENT_CACHE_EXPIRATION_SECONDS"), out var seconds)
                ? seconds : 120);
        }

        public void Store(string? sourceFilePath, string content, RuntimeTrace trace)
        {
            var entry = new Entry(ContentKey.For(sourceFilePath, content), trace, DateTime.UtcNow,
                Interlocked.Increment(ref _version));
            _cache.Set(CacheKey(sourceFilePath), entry, new MemoryCacheEntryOptions
            {
                SlidingExpiration = _expiration,
                Size = Math.Clamp(trace.Lines.Count, 1, _sizeLimit),
            });
        }

        /// <summary>The trace for exactly this content, or null.</summary>
        public Entry? Get(string? sourceFilePath, string key) =>
            _cache.TryGetValue(CacheKey(sourceFilePath), out Entry? entry) && entry?.Key == key ? entry : null;

        private static string CacheKey(string? sourceFilePath) => "trace|" + (sourceFilePath ?? string.Empty);
    }
}
