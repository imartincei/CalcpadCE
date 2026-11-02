using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Calcpad.Core;

namespace Calcpad.Server.Services.S3
{
    /// <summary>
    /// Mirrors S3 server objects into <c>s3cache/{server}/{bucket}/{key}</c> next to the logs folder,
    /// so the engine reads them as ordinary files. Archived versions are written beside the
    /// latest as <c>{id}{ext}</c>, keeping their relative includes pointed at the same folder.
    /// </summary>
    public sealed class S3RemoteFileProvider(S3Session session) : IRemoteFileProvider
    {
        private static readonly TimeSpan RecheckAfter = TimeSpan.FromSeconds(30);
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        private readonly string _cacheBase = Path.Combine(
            Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(FileLogger.LogsDirectory)) ?? AppContext.BaseDirectory,
            "s3cache");
        private readonly ConcurrentDictionary<string, (string? Id, DateTime CheckedAt)> _checked = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, (string Path, DateTime CheckedAt)> _ids = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, object> _locks = new(StringComparer.OrdinalIgnoreCase);

        public sealed record RemoteObject(string Key, bool Latest, string? Id, string? BaseId, int Version, string? UpdatedBy, string? UpdatedAt);

        public string? CacheRoot => session.BaseUrl is { } url
            ? Path.Combine(_cacheBase, $"{url.Host}_{url.Port}")
            : null;

        public bool TryEnsureLocal(string localPath)
        {
            if (!TrySplit(localPath, out var bucket, out var key) || key.Length == 0)
                return false;
            if (IsVersionCopy(localPath) && File.Exists(localPath))
                return true;
            if (_checked.TryGetValue(localPath, out var seen) && DateTime.UtcNow - seen.CheckedAt < RecheckAfter)
                return File.Exists(localPath);

            lock (_locks.GetOrAdd(localPath, _ => new object()))
            {
                try
                {
                    using var request = session.CreateRequest(HttpMethod.Get, $"buckets/{bucket}/objects/{S3Session.EscapeKey(key)}");
                    if (File.Exists(localPath) && seen.Id is { } cachedId)
                        request.Headers.TryAddWithoutValidation("If-None-Match", $"\"{cachedId}\"");

                    using var response = S3Session.Http.Send(request);
                    switch (response.StatusCode)
                    {
                        case HttpStatusCode.NotModified:
                            _checked[localPath] = (seen.Id, DateTime.UtcNow);
                            return true;
                        case HttpStatusCode.NotFound:
                            File.Delete(localPath);
                            _checked[localPath] = (null, DateTime.UtcNow);
                            return false;
                    }
                    response.EnsureSuccessStatusCode();
                    WriteAtomically(localPath, response);
                    _checked[localPath] = (Header(response, "X-Calcpad-Id"), DateTime.UtcNow);
                    return true;
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or TaskCanceledException or InvalidOperationException)
                {
                    FileLogger.LogWarning("S3 fetch failed", $"{bucket}/{key}: {ex.Message}");
                    return File.Exists(localPath);
                }
            }
        }

        public string? ResolveId(string folderPath, Guid id)
        {
            if (!TrySplit(folderPath, out var bucket, out var prefix))
                return null;

            var cacheKey = $"{bucket}/{prefix}/{id}";
            if (_ids.TryGetValue(cacheKey, out var known) && DateTime.UtcNow - known.CheckedAt < RecheckAfter)
                return TryEnsureLocal(known.Path) ? known.Path : null;

            try
            {
                var query = $"buckets/{bucket}/ids/{id}?prefix={Uri.EscapeDataString(prefix)}";
                using var request = session.CreateRequest(HttpMethod.Get, query);
                using var response = S3Session.Http.Send(request);
                if (response.StatusCode == HttpStatusCode.NotFound)
                    return null;
                response.EnsureSuccessStatusCode();

                using var stream = response.Content.ReadAsStream();
                var info = JsonSerializer.Deserialize<RemoteObject>(stream, JsonOptions);
                if (info?.Id is null)
                    return null;

                var bucketDir = Path.Combine(CacheRoot!, bucket);
                var latestPath = Path.Combine(bucketDir, info.Key.Replace('/', Path.DirectorySeparatorChar));
                if (info.Latest)
                {
                    _ids[cacheKey] = (latestPath, DateTime.UtcNow);
                    return TryEnsureLocal(latestPath) ? latestPath : null;
                }

                var versionPath = Path.Combine(Path.GetDirectoryName(latestPath)!, info.Id + Path.GetExtension(info.Key));
                if (!File.Exists(versionPath))
                {
                    using var contentRequest = session.CreateRequest(HttpMethod.Get, $"buckets/{bucket}/ids/{info.Id}/content?prefix={Uri.EscapeDataString(prefix)}");
                    using var content = S3Session.Http.Send(contentRequest);
                    content.EnsureSuccessStatusCode();
                    lock (_locks.GetOrAdd(versionPath, _ => new object()))
                        WriteAtomically(versionPath, content);
                }
                _ids[cacheKey] = (versionPath, DateTime.UtcNow);
                return versionPath;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException or InvalidOperationException or JsonException)
            {
                FileLogger.LogWarning("S3 id lookup failed", $"{bucket}/{prefix}/{id}: {ex.Message}");
                return null;
            }
        }

        /// <summary>Forces the next read of <paramref name="key"/> to go back to the server.</summary>
        public void Invalidate(string bucket, string key)
        {
            if (CacheRoot is { } root)
                _checked.TryRemove(Path.Combine(root, bucket, key.Replace('/', Path.DirectorySeparatorChar)), out _);
            foreach (var entry in _ids.Keys.Where(k => k.StartsWith(bucket + "/", StringComparison.OrdinalIgnoreCase)))
                _ids.TryRemove(entry, out _);
        }

        private bool TrySplit(string localPath, out string bucket, out string key)
        {
            bucket = key = string.Empty;
            if (CacheRoot is not { } root)
                return false;

            var relative = Path.GetRelativePath(root, localPath);
            if (relative == "." || relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
                return false;

            var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            bucket = parts[0];
            key = string.Join('/', parts.Skip(1));
            return true;
        }

        private static bool IsVersionCopy(string path) =>
            Guid.TryParse(Path.GetFileNameWithoutExtension(path), out _);

        private static string? Header(HttpResponseMessage response, string name) =>
            response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;

        private static void WriteAtomically(string path, HttpResponseMessage response)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = path + ".download";
            using (var file = File.Create(temp))
                response.Content.ReadAsStream().CopyTo(file);
            File.Move(temp, path, overwrite: true);
        }
    }
}
