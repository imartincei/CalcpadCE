using System.Collections.Concurrent;
using Calcpad.Core;
using Calcpad.Highlighter.ContentResolution;
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
        private readonly ConcurrentDictionary<string, RuntimeVariableNames> _names = new();
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

        public void Store(string? sourceFilePath, string? documentId, string content, RuntimeTrace trace)
        {
            var entry = new Entry(ContentKey.For(sourceFilePath, content), trace, DateTime.UtcNow,
                Interlocked.Increment(ref _version));
            _cache.Set("trace|" + ContentKey.Document(sourceFilePath, documentId), entry, new MemoryCacheEntryOptions
            {
                SlidingExpiration = _expiration,
                Size = Math.Clamp(trace.Lines.Count, 1, _sizeLimit),
            });
        }

        /// <summary>The trace for exactly this content, or null.</summary>
        public Entry? Get(string? sourceFilePath, string? documentId, string key) =>
            _cache.TryGetValue("trace|" + ContentKey.Document(sourceFilePath, documentId), out Entry? entry) &&
            entry?.Key == key ? entry : null;

        /// <summary>
        /// Keeps the run's variable names for the document whatever its content becomes, for as
        /// long as the server runs, so highlighting doesn't fall back to static between runs. A
        /// document with neither a path nor an id gets none.
        /// </summary>
        public void StoreNames(string? sourceFilePath, string? documentId, RuntimeVariableNames names)
        {
            var document = ContentKey.Document(sourceFilePath, documentId);
            if (document.Length > 0)
                _names[document] = names;
        }

        public RuntimeVariableNames? GetNames(string? sourceFilePath, string? documentId) =>
            _names.GetValueOrDefault(ContentKey.Document(sourceFilePath, documentId));
    }
}
