using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using Calcpad.Core;
using Calcpad.S3Server.Models;

namespace Calcpad.S3Server.Services
{
    public sealed class UploadConflictException(string message) : Exception(message);

    public record UploadResult(ObjectInfo Info, byte[] Content);

    /// <summary>
    /// Versioned objects. The latest version lives at its key and earlier ones are archived under
    /// <c>.versions/{key}/{id}{ext}</c>, since Garage has no bucket versioning. A worksheet's
    /// fileInfo footer and its S3 metadata carry the same baseId/id/version.
    /// </summary>
    public class ObjectService(IObjectStore store)
    {
        public const string VersionsPrefix = ".versions/";
        public const string AnyVersion = "*";

        private const string MetaBaseId = "base-id";
        private const string MetaId = "id";
        private const string MetaVersion = "version";
        private const string MetaSha256 = "sha256";
        private const string MetaTitle = "title";
        private const string MetaAuthor = "author";
        private const string MetaUpdatedBy = "updated-by";
        private const string MetaUpdatedAt = "updated-at";

        // Every write goes through this server, so a per-bucket index stays coherent once built.
        private readonly ConcurrentDictionary<string, Lazy<Task<ConcurrentDictionary<string, ObjectInfo>>>> _indexes = new();
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _keyLocks = new();

        public static string NormalizeKey(string key)
        {
            key = key.Replace('\\', '/').Trim('/');
            if (key.Length == 0
                || key.StartsWith(VersionsPrefix, StringComparison.Ordinal)
                || key.Split('/').Any(s => s is "" or "." or ".."))
                throw new ArgumentException($"Invalid object key '{key}'.");
            return key;
        }

        public static string NormalizePrefix(string? prefix)
        {
            prefix = (prefix ?? string.Empty).Replace('\\', '/').Trim('/');
            return prefix.Length == 0 ? string.Empty : prefix + "/";
        }

        public static string ArchiveKey(string key, string id) =>
            $"{VersionsPrefix}{key}/{id}{Path.GetExtension(key)}";

        public async Task<IReadOnlyList<ObjectInfo>> ListAsync(string bucket, string? prefix, bool includeVersions, CancellationToken ct = default)
        {
            var normalized = NormalizePrefix(prefix);
            var index = await GetIndexAsync(bucket, ct);
            return index.Values
                .Where(o => (includeVersions || o.Latest) && o.Key.StartsWith(normalized, StringComparison.Ordinal))
                .OrderBy(o => o.Key, StringComparer.Ordinal)
                .ThenByDescending(o => o.Version)
                .ToList();
        }

        /// <summary>An id match is that exact version; a baseId match is the latest version.</summary>
        public async Task<ObjectInfo?> FindByIdAsync(string bucket, Guid id, string? prefix, CancellationToken ct = default)
        {
            var candidates = await ListAsync(bucket, prefix, includeVersions: true, ct);
            return candidates.FirstOrDefault(o => Guid.TryParse(o.Id, out var g) && g == id)
                ?? candidates.Where(o => o.Latest && Guid.TryParse(o.BaseId, out var g) && g == id)
                    .MaxBy(o => o.Version);
        }

        public async Task<IReadOnlyList<ObjectInfo>> ListVersionsAsync(string bucket, Guid baseId, CancellationToken ct = default) =>
            (await ListAsync(bucket, null, includeVersions: true, ct))
                .Where(o => Guid.TryParse(o.BaseId, out var g) && g == baseId)
                .OrderByDescending(o => o.Version)
                .ToList();

        public Task<byte[]?> GetContentAsync(string bucket, ObjectInfo info, CancellationToken ct = default) =>
            store.GetAsync(bucket, info.StorageKey, ct);

        public async Task<ObjectInfo?> HeadAsync(string bucket, string key, CancellationToken ct = default)
        {
            var head = await store.HeadAsync(bucket, NormalizeKey(key), ct);
            return head is null ? null : ToInfo(head);
        }

        /// <summary>
        /// Stores a new version of <paramref name="key"/>. <paramref name="expectedId"/> is the id the
        /// client last saw (<see cref="AnyVersion"/> to overwrite); when null it is read from the
        /// file's own footer.
        /// </summary>
        public async Task<UploadResult> UploadAsync(string bucket, string key, byte[] body, string? expectedId,
            string updatedBy, CancellationToken ct = default)
        {
            key = NormalizeKey(key);
            var keyLock = _keyLocks.GetOrAdd($"{bucket}/{key}", _ => new SemaphoreSlim(1, 1));
            await keyLock.WaitAsync(ct);
            try
            {
                var isWorksheet = FileMetadata.IsWorksheet(key);
                var text = isWorksheet ? FileMetadata.DecodeText(body, key) : null;
                var footer = text is null ? null : FileMetadata.TryReadFileInfo(text);
                expectedId ??= FileMetadata.GetString(footer, FileMetadata.IdKey);

                var head = await store.HeadAsync(bucket, key, ct);
                var current = head is null ? null : ToInfo(head);
                if (current is not null && expectedId != AnyVersion
                    && !string.Equals(expectedId, current.Id, StringComparison.OrdinalIgnoreCase))
                    throw new UploadConflictException(
                        $"'{key}' is at version {current.Version} (id {current.Id}); upload was based on id {expectedId ?? "none"}.");

                if (current?.Id is { } currentId)
                    await store.CopyAsync(bucket, key, ArchiveKey(key, currentId), ct);

                var baseId = current?.BaseId
                    ?? FileMetadata.GetString(footer, FileMetadata.BaseIdKey)
                    ?? Guid.NewGuid().ToString();
                var version = (current?.Version ?? 0) + 1;
                var id = Guid.NewGuid().ToString();
                var updatedAt = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);

                if (text is not null)
                {
                    text = FileMetadata.WithFileInfo(text, FileInfoPatch(baseId, id, version, updatedBy, updatedAt));
                    body = FileMetadata.EncodeText(text, key, body);
                }

                var metadata = new Dictionary<string, string>
                {
                    [MetaBaseId] = baseId,
                    [MetaId] = id,
                    [MetaVersion] = version.ToString(CultureInfo.InvariantCulture),
                    [MetaSha256] = Convert.ToHexStringLower(SHA256.HashData(body)),
                    [MetaUpdatedBy] = Uri.EscapeDataString(updatedBy),
                    [MetaUpdatedAt] = updatedAt
                };
                if (FileMetadata.GetString(footer, "title") is { Length: > 0 } title)
                    metadata[MetaTitle] = Uri.EscapeDataString(title);
                if (FileMetadata.GetString(footer, "author") is { Length: > 0 } author)
                    metadata[MetaAuthor] = Uri.EscapeDataString(author);

                await store.PutAsync(bucket, key, body, ContentType(key), metadata, ct);
                var info = ToInfo(new StoredObject(key, body.LongLength, metadata));
                await UpdateIndexAsync(bucket, ct, index =>
                {
                    if (current?.Id is { } archivedId)
                        index[ArchiveKey(key, archivedId)] = current with { Latest = false, StorageKey = ArchiveKey(key, archivedId) };
                    index[key] = info;
                });
                return new UploadResult(info, body);
            }
            finally
            {
                keyLock.Release();
            }
        }

        /// <summary>Removes the latest version; archived versions stay addressable by id.</summary>
        public async Task<bool> DeleteAsync(string bucket, string key, CancellationToken ct = default)
        {
            key = NormalizeKey(key);
            if (await store.HeadAsync(bucket, key, ct) is null)
                return false;

            await store.DeleteAsync(bucket, key, ct);
            await UpdateIndexAsync(bucket, ct, index => index.TryRemove(key, out _));
            return true;
        }

        public static Dictionary<string, string> FileInfoPatch(string baseId, string id, int version, string updatedBy, string updatedAt) => new()
        {
            [FileMetadata.BaseIdKey] = baseId,
            [FileMetadata.IdKey] = id,
            [FileMetadata.VersionKey] = version.ToString(CultureInfo.InvariantCulture),
            ["lastUpdatedBy"] = updatedBy,
            ["lastUpdatedAt"] = updatedAt
        };

        public void ForgetIndex(string bucket) => _indexes.TryRemove(bucket, out _);

        private async Task UpdateIndexAsync(string bucket, CancellationToken ct, Action<ConcurrentDictionary<string, ObjectInfo>> update)
        {
            if (_indexes.ContainsKey(bucket))
                update(await GetIndexAsync(bucket, ct));
        }

        private async Task<ConcurrentDictionary<string, ObjectInfo>> GetIndexAsync(string bucket, CancellationToken ct)
        {
            var lazy = _indexes.GetOrAdd(bucket, b => new Lazy<Task<ConcurrentDictionary<string, ObjectInfo>>>(() => BuildIndexAsync(b, CancellationToken.None)));
            try
            {
                return await lazy.Value.WaitAsync(ct);
            }
            catch
            {
                _indexes.TryRemove(new KeyValuePair<string, Lazy<Task<ConcurrentDictionary<string, ObjectInfo>>>>(bucket, lazy));
                throw;
            }
        }

        private async Task<ConcurrentDictionary<string, ObjectInfo>> BuildIndexAsync(string bucket, CancellationToken ct)
        {
            var index = new ConcurrentDictionary<string, ObjectInfo>(StringComparer.Ordinal);
            var keys = await store.ListKeysAsync(bucket, string.Empty, ct);
            await Parallel.ForEachAsync(keys, new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = ct }, async (key, token) =>
            {
                if (await store.HeadAsync(bucket, key, token) is { } head)
                    index[key] = ToInfo(head);
            });
            return index;
        }

        private static ObjectInfo ToInfo(StoredObject head)
        {
            var archived = head.Key.StartsWith(VersionsPrefix, StringComparison.Ordinal);
            var logicalKey = archived
                ? head.Key[VersionsPrefix.Length..head.Key.LastIndexOf('/')]
                : head.Key;
            string? Meta(string name) => head.Metadata.TryGetValue(name, out var v) ? v : null;
            string? Decoded(string name) => Meta(name) is { } v ? Uri.UnescapeDataString(v) : null;

            return new ObjectInfo(
                logicalKey,
                head.Key,
                !archived,
                Meta(MetaBaseId),
                Meta(MetaId),
                int.TryParse(Meta(MetaVersion), out var version) ? version : 0,
                head.Size,
                Meta(MetaSha256),
                Decoded(MetaTitle),
                Decoded(MetaAuthor),
                Decoded(MetaUpdatedBy),
                Meta(MetaUpdatedAt));
        }

        private static string ContentType(string key) => Path.GetExtension(key).ToLowerInvariant() switch
        {
            ".cpd" or ".txt" => "text/plain; charset=utf-8",
            ".csv" => "text/csv",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".xlsm" => "application/vnd.ms-excel.sheet.macroEnabled.12",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".svg" => "image/svg+xml",
            _ => "application/octet-stream"
        };
    }
}
