using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Calcpad.Core
{
    /// <summary>
    /// Reads and writes the <c>fileInfo</c> footer comment, the last non-blank line of a worksheet
    /// (see metadata-footer.ts for the frontend half).
    /// </summary>
    public static class FileMetadata
    {
        public const string FileInfoKey = "fileInfo";
        public const string BaseIdKey = "baseId";
        public const string IdKey = "id";
        public const string VersionKey = "version";
        public const string Anchor = "'<!--CalcpadCE Metadata-->";
        private const string UiOverridesKey = "uiOverrides";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public static bool IsWorksheet(string path)
        {
            var ext = Path.GetExtension(path);
            return ext.Equals(".cpd", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".cpdz", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsCompiled(string path) =>
            Path.GetExtension(path).Equals(".cpdz", StringComparison.OrdinalIgnoreCase);

        public static string DecodeText(byte[] bytes, string path) =>
            IsCompiled(path) ? CpdzCodec.Decode(bytes).Text : new UTF8Encoding(false).GetString(bytes);

        public static byte[] EncodeText(string text, string path, byte[] original) =>
            IsCompiled(path) ? CpdzCodec.Encode(text, original) : new UTF8Encoding(false).GetBytes(text);

        /// <summary>The fileInfo object of a worksheet on disk, or null when it has none.</summary>
        public static JsonObject ReadFileInfo(string path)
        {
            try
            {
                return TryReadFileInfo(DecodeText(File.ReadAllBytes(path), path));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        public static JsonObject TryReadFileInfo(string text)
        {
            var lines = SplitLines(text);
            var block = FindLastComment(lines, LastContentLine(lines, lines.Length));
            return block.Data?[FileInfoKey] as JsonObject;
        }

        public static string GetString(JsonObject info, string key) =>
            info?[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

        public static int GetVersion(JsonObject info) =>
            int.TryParse(GetString(info, VersionKey), out var n) ? n : 0;

        /// <summary>
        /// <paramref name="text"/> with <paramref name="patch"/> merged into its fileInfo, writing a
        /// new footer when there is none.
        /// </summary>
        public static string WithFileInfo(string text, IReadOnlyDictionary<string, string> patch)
        {
            var eol = text.Contains("\r\n") ? "\r\n" : "\n";
            var lines = new List<string>(SplitLines(text));
            var last = LastContentLine(lines, lines.Count);
            var block = FindLastComment(lines, last);
            var data = block.Data?[FileInfoKey] is JsonObject ? block.Data : null;

            var info = data?[FileInfoKey] as JsonObject ?? new JsonObject();
            foreach (var (key, value) in patch)
                info[key] = value;

            if (data is not null)
            {
                var line = $"{block.Indent}'<!--{data.ToJsonString(JsonOptions)}-->";
                lines.RemoveRange(block.Start, block.End - block.Start + 1);
                lines.Insert(block.Start, line);
                return string.Join(eol, lines);
            }

            var footer = new List<string>();
            var hasOverrides = block.Data?[UiOverridesKey] is not null;
            var anchorLine = hasOverrides ? LastContentLine(lines, block.Start) : last;
            if (anchorLine < 0 || lines[anchorLine].Trim() != Anchor)
            {
                if (hasOverrides)
                    lines.Insert(block.Start, Anchor);
                else
                    footer.Add(Anchor);
            }
            footer.Add($"'<!--{new JsonObject { [FileInfoKey] = info }.ToJsonString(JsonOptions)}-->");

            // A trailing newline stays where it was, after the footer.
            var insertAt = lines.Count > 0 && lines[^1].Length == 0 ? lines.Count - 1 : lines.Count;
            lines.InsertRange(insertAt, footer);
            return string.Join(eol, lines);
        }

        private static string[] SplitLines(string text) =>
            Array.ConvertAll(text.Split('\n'), p => p.TrimEnd('\r'));

        private static int LastContentLine(IReadOnlyList<string> lines, int before)
        {
            for (var i = before - 1; i >= 0; --i)
                if (!string.IsNullOrWhiteSpace(lines[i]))
                    return i;
            return -1;
        }

        private readonly record struct CommentBlock(int Start, int End, string Indent, JsonObject Data);

        /// <summary>The <c>'&lt;!--{json}--&gt;</c> comment ending on <paramref name="end"/>, which may span lines via <c>_</c>.</summary>
        private static CommentBlock FindLastComment(IReadOnlyList<string> lines, int end)
        {
            if (end < 0 || !lines[end].TrimEnd().EndsWith("-->", StringComparison.Ordinal))
                return default;

            var start = end;
            while (start >= 0 && !IsOpener(lines[start]))
            {
                if (start < end && !lines[start].TrimEnd().EndsWith('_'))
                    return default;
                --start;
            }
            if (start < 0)
                return default;

            var sb = new StringBuilder();
            for (var i = start; i <= end; ++i)
            {
                var line = lines[i];
                if (i == start)
                    line = line[(line.IndexOf("<!--", StringComparison.Ordinal) + 4)..];
                if (i == end)
                    line = line[..line.LastIndexOf("-->", StringComparison.Ordinal)];
                else
                    line = line.TrimEnd().TrimEnd('_');
                sb.Append(line).Append('\n');
            }

            var opener = lines[start];
            var indent = opener[..(opener.Length - opener.TrimStart().Length)];
            try
            {
                return new CommentBlock(start, end, indent, JsonNode.Parse(sb.ToString()) as JsonObject);
            }
            catch (JsonException)
            {
                return default;
            }
        }

        private static bool IsOpener(string line)
        {
            var t = line.AsSpan().TrimStart();
            return t.Length > 4 && (t[0] == '\'' || t[0] == '"') && t[1..].StartsWith("<!--{", StringComparison.Ordinal);
        }
    }
}
