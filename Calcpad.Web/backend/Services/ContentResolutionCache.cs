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
        private readonly IMemoryCache _cache;
        private readonly ConcurrentDictionary<string, string> _activeKeyBySourceFile = new();
        private readonly MemoryCacheEntryOptions _entryOptions;

        public ContentResolutionCache(IMemoryCache cache)
        {
            _cache = cache;
            var expirationSeconds = int.TryParse(
                Environment.GetEnvironmentVariable("CALCPAD_CONTENT_CACHE_EXPIRATION_SECONDS"), out var seconds)
                ? seconds : 120;
            _entryOptions = new MemoryCacheEntryOptions
            {
                SlidingExpiration = TimeSpan.FromSeconds(expirationSeconds),
                Size = 1
            };
        }

        public StagedResolvedContent GetOrResolve(string content, string? sourceFilePath)
        {
            var fileKey = sourceFilePath ?? string.Empty;
            var key = $"{fileKey}|{Sha256Hex(content)}";

            if (_cache.TryGetValue(key, out StagedResolvedContent? cached) && cached != null && IsIncludesFresh(cached))
                return cached;

            // New content for this file — drop its old entry now instead of waiting on the TTL.
            if (_activeKeyBySourceFile.TryGetValue(fileKey, out var oldKey) && oldKey != key)
                _cache.Remove(oldKey);

            var staged = new ContentResolver().GetStagedContent(content, sourceFilePath: sourceFilePath);
            _cache.Set(key, staged, _entryOptions);
            _activeKeyBySourceFile[fileKey] = key;
            return staged;
        }

        /// <summary>
        /// Re-hashes just the `#include`d files recorded on <paramref name="staged"/> instead of
        /// rerunning include resolution/macro expansion, so an edit to an included file (without
        /// touching the open document) still invalidates the cache promptly.
        /// </summary>
        private static bool IsIncludesFresh(StagedResolvedContent staged)
        {
            foreach (var (path, expectedHash) in staged.Stage2.IncludedFileHashes)
            {
                try
                {
                    if (Sha256Hex(File.ReadAllText(path)) != expectedHash) return false;
                }
                catch
                {
                    return false; // file deleted or unreadable since it was cached
                }
            }
            return true;
        }

        private static string Sha256Hex(string text) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }
}
