using System;
using System.Collections.Concurrent;
using System.IO;

namespace Calcpad.Core
{
    /// <summary>Supplies <c>{s3:bucket}</c> files by mirroring them into <see cref="CacheRoot"/>.</summary>
    public interface IRemoteFileProvider
    {
        /// <summary>Local folder mirroring the buckets as <c>{CacheRoot}/{bucket}/{key}</c>.</summary>
        string CacheRoot { get; }

        /// <summary>Downloads or refreshes the object behind a cache path; false when it doesn't exist.</summary>
        bool TryEnsureLocal(string localPath);

        /// <summary>The cache path of the file in <paramref name="folderPath"/> whose id (exact) or baseId (latest) matches.</summary>
        string ResolveId(string folderPath, Guid id);
    }

    /// <summary>
    /// Resolves a full path to the file it names: an extension-less GUID is looked up by id/baseId,
    /// and a path inside the S3 cache is fetched first.
    /// </summary>
    public static class FileReferences
    {
        public static IRemoteFileProvider RemoteProvider { get; set; }

        private static readonly StringComparison PathComparison =
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        private sealed record Entry(DateTime Modified, string BaseId, string Id, int Version);
        private static readonly ConcurrentDictionary<string, Entry> LocalEntries =
            new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        public static bool IsIdReference(string path, out Guid id)
        {
            id = default;
            var name = Path.GetFileName(path);
            return !string.IsNullOrEmpty(name) && !Path.HasExtension(name) && Guid.TryParse(name, out id);
        }

        public static bool IsRemote(string fullPath)
        {
            var root = RemoteProvider?.CacheRoot;
            if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(fullPath))
                return false;

            root = Path.TrimEndingDirectorySeparator(root);
            return fullPath.StartsWith(root, PathComparison)
                && (fullPath.Length == root.Length || fullPath[root.Length] is '/' or '\\');
        }

        public static string Resolve(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath))
                return fullPath;

            var remote = IsRemote(fullPath);
            if (IsIdReference(fullPath, out var id))
            {
                var folder = Path.GetDirectoryName(fullPath);
                return (remote ? RemoteProvider.ResolveId(folder, id) : ResolveLocalId(folder, id)) ?? fullPath;
            }
            if (remote)
                RemoteProvider.TryEnsureLocal(fullPath);
            return fullPath;
        }

        /// <summary>Scans the worksheet footers in <paramref name="folder"/>; an id match wins over the newest baseId match.</summary>
        public static string ResolveLocalId(string folder, Guid id)
        {
            if (!Directory.Exists(folder))
                return null;

            var key = id.ToString();
            string latest = null;
            var latestVersion = -1;
            foreach (var file in Directory.EnumerateFiles(folder))
            {
                if (!FileMetadata.IsWorksheet(file))
                    continue;

                var entry = GetLocalEntry(file);
                if (entry is null)
                    continue;
                if (string.Equals(entry.Id, key, StringComparison.OrdinalIgnoreCase))
                    return file;
                if (string.Equals(entry.BaseId, key, StringComparison.OrdinalIgnoreCase) && entry.Version > latestVersion)
                {
                    latest = file;
                    latestVersion = entry.Version;
                }
            }
            return latest;
        }

        private static Entry GetLocalEntry(string file)
        {
            var modified = File.GetLastWriteTimeUtc(file);
            if (LocalEntries.TryGetValue(file, out var cached) && cached.Modified == modified)
                return cached;

            var info = FileMetadata.ReadFileInfo(file);
            var entry = new Entry(modified,
                FileMetadata.GetString(info, FileMetadata.BaseIdKey),
                FileMetadata.GetString(info, FileMetadata.IdKey),
                FileMetadata.GetVersion(info));
            LocalEntries[file] = entry;
            return entry;
        }
    }
}
