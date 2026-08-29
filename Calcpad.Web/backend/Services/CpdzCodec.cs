using System.IO.Compression;
using System.Text;

namespace Calcpad.Server.Services
{
    /// <summary>
    /// Reads and writes the compiled <c>.cpdz</c> worksheet format, matching
    /// <c>Calcpad.Wpf</c>'s <c>Zip</c> helper so files round-trip between the two. Two shapes
    /// exist — a raw deflate stream of the source text, and a ZIP archive (recognized by the
    /// <c>PK</c> signature) holding a deflated <c>code.cpd</c> entry plus referenced images —
    /// and encoding preserves whichever shape the original had.
    /// </summary>
    internal static class CpdzCodec
    {
        private const string CodeEntryName = "code.cpd";

        public sealed record DecodeResult(string Text, bool IsComposite);

        public static DecodeResult Decode(byte[] bytes)
        {
            if (!IsComposite(bytes))
                return new DecodeResult(Inflate(new MemoryStream(bytes)), false);

            using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
            var entry = FindCodeEntry(archive)
                ?? throw new InvalidDataException($"The archive contains no '{CodeEntryName}' entry.");

            using var entryStream = entry.Open();
            return new DecodeResult(Inflate(entryStream), true);
        }

        /// <summary>
        /// Encodes <paramref name="text"/>. When <paramref name="original"/> is a
        /// composite archive its other entries are carried over unchanged and only
        /// the code entry is rewritten; otherwise the result is a plain deflate stream.
        /// </summary>
        public static byte[] Encode(string text, byte[]? original)
        {
            if (original is null || !IsComposite(original))
            {
                using var plain = new MemoryStream();
                Deflate(text, plain);
                return plain.ToArray();
            }

            using var output = new MemoryStream();
            using (var source = new ZipArchive(new MemoryStream(original), ZipArchiveMode.Read))
            using (var target = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var entry in source.Entries)
                {
                    var copy = target.CreateEntry(entry.FullName, CompressionLevel.Fastest);
                    using var to = copy.Open();
                    if (IsCodeEntry(entry))
                    {
                        Deflate(text, to);
                        continue;
                    }
                    using var from = entry.Open();
                    from.CopyTo(to);
                }
            }
            return output.ToArray();
        }

        /// <summary>ZIP local-file-header signature; a plain deflate stream never starts with it.</summary>
        private static bool IsComposite(byte[] bytes) =>
            bytes.Length >= 2 && bytes[0] == 'P' && bytes[1] == 'K';

        private static ZipArchiveEntry? FindCodeEntry(ZipArchive archive)
        {
            foreach (var entry in archive.Entries)
                if (IsCodeEntry(entry))
                    return entry;
            return null;
        }

        private static bool IsCodeEntry(ZipArchiveEntry entry) =>
            string.Equals(entry.Name, CodeEntryName, StringComparison.Ordinal);

        private static string Inflate(Stream source)
        {
            using var ms = new MemoryStream();
            using (var ds = new DeflateStream(source, CompressionMode.Decompress, leaveOpen: true))
                ds.CopyTo(ms);

            ms.Position = 0;
            using var reader = new StreamReader(ms);
            return reader.ReadToEnd();
        }

        private static void Deflate(string text, Stream destination)
        {
            using var ds = new DeflateStream(destination, CompressionMode.Compress, leaveOpen: true);
            // UTF-8 without a BOM, matching the StreamWriter default the WPF app writes with.
            var bytes = new UTF8Encoding(false).GetBytes(text);
            ds.Write(bytes, 0, bytes.Length);
        }
    }
}
