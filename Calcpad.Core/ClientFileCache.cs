using System;
using System.Collections.Generic;
using System.Text;

namespace Calcpad.Core
{
    [Serializable()]
    public class ClientFileCache
    {
        private sealed class Entry
        {
            public string Filename;
            public byte[] Content;
            public string Error;
            public string DiskGuid;
        }

        private readonly List<Entry> _entries = [];

        public string DiskCacheFolder { get; set; }

        [field: NonSerialized]
        public Func<string, byte[]> RefetchDelegate { get; set; }

        private Entry Find(string filename)
        {
            foreach (var e in _entries)
                if (string.Equals(e.Filename, filename, StringComparison.OrdinalIgnoreCase))
                    return e;
            return null;
        }

        public bool TryGetContent(string filename, out string content)
        {
            content = null;
            if (!TryGetBytes(filename, out var bytes))
                return false;
            content = Encoding.UTF8.GetString(bytes);
            return true;
        }

        public bool TryGetContentMultiKey(string primaryKey, string fallbackKey, out string content) =>
            TryGetContent(primaryKey, out content) ||
            (fallbackKey != null && TryGetContent(fallbackKey, out content));

        public bool TryGetBytes(string filename, out byte[] bytes)
        {
            bytes = null;
            var entry = Find(filename);
            if (entry == null)
                return false;

            if (entry.Content != null)
            {
                bytes = entry.Content;
                return true;
            }

            if (entry.DiskGuid != null && DiskCacheFolder != null)
            {
                if (ClientFileDiskCache.TryRead(DiskCacheFolder, entry.DiskGuid, out bytes))
                    return true;

                if (RefetchDelegate != null)
                {
                    try
                    {
                        bytes = RefetchDelegate(filename);
                        if (bytes != null)
                        {
                            entry.DiskGuid = ClientFileDiskCache.Write(DiskCacheFolder, bytes);
                            return true;
                        }
                    }
                    catch (Exception ex)
                    {
                        // Boundary: the delegate is user-supplied (HTTP fetch, S3, etc.) and can throw anything.
                        // Record the message so TryGetError surfaces it and we stop retrying this entry.
                        entry.Error = ex.Message;
                        entry.DiskGuid = null;
                    }
                }
            }

            return false;
        }

        public bool TryGetError(string filename, out string error)
        {
            error = null;
            var entry = Find(filename);
            if (entry == null)
                return false;
            error = entry.Error;
            return error != null;
        }

        public bool TryGetErrorMultiKey(string primaryKey, string fallbackKey, out string error) =>
            TryGetError(primaryKey, out error) ||
            (fallbackKey != null && TryGetError(fallbackKey, out error));

        public void AddEntry(string filename, byte[] content, string error)
        {
            string diskGuid = null;
            byte[] inlineContent = content;

            if (content != null && content.Length > ClientFileDiskCache.DiskThresholdBytes && DiskCacheFolder != null)
            {
                diskGuid = ClientFileDiskCache.Write(DiskCacheFolder, content);
                inlineContent = null;
            }

            _entries.Add(new Entry
            {
                Filename = filename,
                Content = inlineContent,
                Error = error,
                DiskGuid = diskGuid,
            });
        }
    }
}
