using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Calcpad.Highlighter.ContentResolution;
using Microsoft.Extensions.Caching.Memory;

namespace Calcpad.Server.Services
{
    /// <summary>
    /// Caches <see cref="ContentResolver.GetStagedContent"/>'s output so lint, highlight,
    /// definitions, and symbol-at-position — which each resolve the same document
    /// independently — don't redo the same include resolution and macro expansion when
    /// fired together for the same content.
    /// </summary>
    public class ContentResolutionCache
    {
        /// <summary>
        /// How long a cached entry is trusted before its <c>#include</c>s are re-checked. One
        /// keystroke fans out into four endpoints within a few hundred ms; re-validating on each
        /// buys nothing.
        /// </summary>
        private const int RevalidateIntervalMs = 500;

        private readonly IMemoryCache _cache;
        private readonly ConcurrentDictionary<string, string> _activeKeyBySourceFile = new();

        /// <summary>
        /// Resolutions currently running. Without this the four endpoints one keystroke triggers
        /// all miss at once and each runs a full resolution, three of which are discarded.
        /// </summary>
        private readonly ConcurrentDictionary<string, Lazy<CacheEntry>> _inFlight = new();

        private readonly int _sizeLimit;

        /// <summary>
        /// Cache budget in flattened source lines, not entries: an entry costs ~2-3 KB per line,
        /// so an entry count says nothing about memory.
        /// </summary>
        public static int ResolveSizeLimit() =>
            int.TryParse(Environment.GetEnvironmentVariable("CALCPAD_CONTENT_CACHE_SIZE_LIMIT"), out var limit) && limit > 0
                ? limit : 50_000;

        private sealed class CacheEntry
        {
            public required StagedResolvedContent Staged { get; init; }
            public required ConcurrentDictionary<string, DateTime> IncludeWriteTimes { get; init; }
            public long LastValidatedTicks;
        }

        private readonly MemoryCacheEntryOptions _baseOptions;

        public ContentResolutionCache(IMemoryCache cache)
        {
            _cache = cache;
            _sizeLimit = ResolveSizeLimit();
            var expirationSeconds = int.TryParse(
                Environment.GetEnvironmentVariable("CALCPAD_CONTENT_CACHE_EXPIRATION_SECONDS"), out var seconds)
                ? seconds : 120;
            _baseOptions = new MemoryCacheEntryOptions
            {
                SlidingExpiration = TimeSpan.FromSeconds(expirationSeconds),
            };
        }

        public StagedResolvedContent GetOrResolve(string content, string? sourceFilePath)
        {
            var fileKey = sourceFilePath ?? string.Empty;
            var key = $"{fileKey}|{Sha256Hex(content)}";

            if (_cache.TryGetValue(key, out CacheEntry? cached) && cached != null && IsIncludesFresh(cached))
                return cached.Staged;

            // Concurrent callers for the same key share one resolution instead of racing.
            var lazy = _inFlight.GetOrAdd(key, _ => new Lazy<CacheEntry>(
                () => Resolve(content, sourceFilePath, fileKey, key),
                LazyThreadSafetyMode.ExecutionAndPublication));
            try
            {
                return lazy.Value.Staged;
            }
            finally
            {
                _inFlight.TryRemove(new KeyValuePair<string, Lazy<CacheEntry>>(key, lazy));
            }
        }

        private CacheEntry Resolve(string content, string? sourceFilePath, string fileKey, string key)
        {
            // New content for this file — drop its old entry now instead of waiting on the TTL.
            if (_activeKeyBySourceFile.TryGetValue(fileKey, out var oldKey) && oldKey != key)
                _cache.Remove(oldKey);

            var staged = new ContentResolver().GetStagedContent(content, sourceFilePath: sourceFilePath);

            var writeTimes = new ConcurrentDictionary<string, DateTime>();
            foreach (var (path, _) in staged.Stage2.IncludedFileHashes)
                writeTimes[path] = SafeWriteTime(path);

            var entry = new CacheEntry
            {
                Staged = staged,
                IncludeWriteTimes = writeTimes,
                LastValidatedTicks = Environment.TickCount64,
            };

            var options = new MemoryCacheEntryOptions
            {
                SlidingExpiration = _baseOptions.SlidingExpiration,
                Size = Math.Clamp(staged.Stage3.Lines?.Count ?? 1, 1, _sizeLimit),
            };
            // Without this the map keeps one entry per file path ever seen, outliving by far
            // the cache entries it points at.
            options.RegisterPostEvictionCallback((_, _, _, _) =>
                _activeKeyBySourceFile.TryRemove(new KeyValuePair<string, string>(fileKey, key)));
            _cache.Set(key, entry, options);
            _activeKeyBySourceFile[fileKey] = key;
            return entry;
        }

        /// <summary>
        /// Confirms the <c>#include</c>d files recorded on <paramref name="entry"/> are unchanged,
        /// so an edit to an included file (without touching the open document) still invalidates
        /// the cache promptly.
        /// </summary>
        /// <remarks>
        /// Checks last-write time before hashing. Includes commonly live in OneDrive folders,
        /// where a read is a round trip through the cloud filter driver and can stall on the sync
        /// engine — a timestamp query does not hydrate a placeholder, a read does.
        /// </remarks>
        private static bool IsIncludesFresh(CacheEntry entry)
        {
            if (Environment.TickCount64 - Interlocked.Read(ref entry.LastValidatedTicks) < RevalidateIntervalMs)
                return true;

            foreach (var (path, expectedHash) in entry.Staged.Stage2.IncludedFileHashes)
            {
                try
                {
                    var writeTime = SafeWriteTime(path);
                    if (writeTime == default) return false; // deleted or unreadable since it was cached
                    if (entry.IncludeWriteTimes.TryGetValue(path, out var known) && known == writeTime)
                        continue;

                    if (Sha256Hex(File.ReadAllText(path)) != expectedHash) return false;
                    entry.IncludeWriteTimes[path] = writeTime;
                }
                catch
                {
                    return false;
                }
            }

            Interlocked.Exchange(ref entry.LastValidatedTicks, Environment.TickCount64);
            return true;
        }

        private static DateTime SafeWriteTime(string path)
        {
            try
            {
                var info = new FileInfo(path);
                return info.Exists ? info.LastWriteTimeUtc : default;
            }
            catch
            {
                return default;
            }
        }

        private static string Sha256Hex(string text) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }
}
