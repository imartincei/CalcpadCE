using Calcpad.Core;
using Microsoft.Extensions.Caching.Memory;

namespace Calcpad.Server.Services
{
    /// <summary>
    /// <see cref="RuntimeTrace"/>s recorded by debug /convert runs, keyed by path and content, so lint
    /// and definitions can use what Core actually executed for the same content. Has its own budget,
    /// apart from resolved content; entries don't expire when idle and the least recently used go first.
    /// </summary>
    public sealed class RuntimeTraceCache : IDisposable
    {
        public sealed record Entry(RuntimeTrace Trace, DateTime StoredUtc, long Version);

        private readonly MemoryCache _cache;
        private readonly int _sizeLimit;
        private long _version;

        public RuntimeTraceCache() : this(ResolveSizeLimit()) { }

        public RuntimeTraceCache(int sizeLimit)
        {
            _sizeLimit = sizeLimit;
            _cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = sizeLimit, TrackStatistics = true });
        }

        /// <summary>Executed lines budgeted across all stored traces.</summary>
        public static int ResolveSizeLimit() =>
            int.TryParse(Environment.GetEnvironmentVariable("CALCPAD_TRACE_CACHE_SIZE_LIMIT"), out var limit) && limit > 0
                ? limit : 20_000;

        public void Store(string? sourceFilePath, string content, RuntimeTrace trace)
        {
            var size = Math.Clamp(trace.Lines.Count, 1, _sizeLimit);
            // A full MemoryCache rejects the new entry and only compacts afterwards, which would drop
            // the trace just recorded, so make room first.
            while (_cache.Count > 0 && (_cache.GetCurrentStatistics()?.CurrentEstimatedSize ?? 0) + size > _sizeLimit)
                _cache.Compact(Math.Min(1.0, Math.Max(0.25, 1.5 / _cache.Count)));

            var entry = new Entry(trace, DateTime.UtcNow, Interlocked.Increment(ref _version));
            _cache.Set(ContentKey.For(sourceFilePath, content), entry, new MemoryCacheEntryOptions { Size = size });
        }

        /// <summary>The trace for the content <paramref name="key"/> identifies, or null.</summary>
        public Entry? Get(string key) => _cache.TryGetValue(key, out Entry? entry) ? entry : null;

        public void Dispose() => _cache.Dispose();
    }
}
