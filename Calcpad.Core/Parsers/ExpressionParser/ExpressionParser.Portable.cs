using System;

namespace Calcpad.Core
{
    public partial class ExpressionParser
    {
        public const int MaxEmbeddedDataSize = 10 * 1024 * 1024;

        public static string EmbedReadDirective(ReadOnlySpan<char> line, string sourceFilePath, PathRoots pathRoots, out int size)
        {
            size = 0;
            var s = line.Trim();
            var sourceDir = string.IsNullOrEmpty(sourceFilePath)
                ? null
                : System.IO.Path.GetDirectoryName(sourceFilePath);
            var options = new ReadWriteOptions(s, 0, sourceDir, pathRoots);
            if (options.Name.IsEmpty || !options.Data.IsEmpty || !TryGetDataPath(s, out var start, out var length))
                return null;

            var file = new System.IO.FileInfo(options.FullPath);
            if (file.Exists && file.Length > MaxEmbeddedDataSize)
                throw Exceptions.EmbeddedDataSizeLimit();

            DataExchange.Read(options);
            var bytes = System.IO.File.ReadAllBytes(options.FullPath);
            size = bytes.Length;
            var uri = $"{DataUri}{ExtensionMime(options.Ext)};base64,{Convert.ToBase64String(bytes)}";
            return $"{s[..start]}{uri}{s[(start + length)..]}";
        }

        public static bool TryGetDataPath(ReadOnlySpan<char> line, out int start, out int length)
        {
            start = 0;
            length = 0;
            var connector = line.StartsWith("#read", StringComparison.OrdinalIgnoreCase) ? "from"
                : line.StartsWith("#write", StringComparison.OrdinalIgnoreCase)
                    || line.StartsWith("#append", StringComparison.OrdinalIgnoreCase) ? "to"
                : null;
            if (connector is null)
                return false;

            var len = line.Length;
            var i = 0;
            for (var token = 0; token < 3; ++token)
            {
                var from = i;
                while (i < len) { if (line[i++] == ' ') break; }
                if (i == len)
                    return false;

                if (token == 2 && !line[from..(i - 1)].Equals(connector, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            // An embedded source is not a path, and the dots in its MIME type would read as one.
            if (line[i..].StartsWith(DataUri, StringComparison.OrdinalIgnoreCase))
                return false;

            var dot = line.LastIndexOf('.');
            if (dot < i)
                return false;

            var end = len;
            for (var j = dot + 1; j < len; ++j)
            {
                var c = line[j];
                if (c is '@' or '!' or ':' or ' ')
                {
                    end = j;
                    break;
                }
            }
            start = i;
            length = end - i;
            return length > 0;
        }
    }
}
