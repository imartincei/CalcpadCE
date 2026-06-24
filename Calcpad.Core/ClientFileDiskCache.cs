using System;
using System.IO;
using System.Security.Cryptography;

namespace Calcpad.Core
{
    internal static class ClientFileDiskCache
    {
        internal const int DiskThresholdBytes = 51_200;
        private const string CacheFileExtension = ".cache";

        internal static bool TryRead(string folder, string guid, out byte[] bytes)
        {
            bytes = null;
            if (folder == null || guid == null)
                return false;

            var path = Path.Combine(folder, guid + CacheFileExtension);
            if (!File.Exists(path))
                return false;

            bytes = File.ReadAllBytes(path);
            try { File.SetLastWriteTimeUtc(path, DateTime.UtcNow); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return true;
        }

        // Keys by content hash, so the same path with new content gets a fresh cache entry
        // instead of serving stale bytes. Orphaned files are removed by the LRU cleanup job.
        internal static string Write(string folder, byte[] bytes)
        {
            var cacheKey = GetCacheKey(bytes);
            var path = Path.Combine(folder, cacheKey + CacheFileExtension);
            if (File.Exists(path))
            {
                try { File.SetLastWriteTimeUtc(path, DateTime.UtcNow); } catch { }
            }
            else
            {
                Directory.CreateDirectory(folder);
                File.WriteAllBytes(path, bytes);
            }
            return cacheKey;
        }

        private static string GetCacheKey(byte[] bytes) =>
            Convert.ToHexStringLower(SHA256.HashData(bytes))[..32];
    }
}
