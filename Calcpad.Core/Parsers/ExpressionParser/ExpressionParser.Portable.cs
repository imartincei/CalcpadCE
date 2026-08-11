using System;
using System.Collections.Generic;
using System.Text;

namespace Calcpad.Core
{
    public partial class ExpressionParser
    {
        public static string InlineReadDirective(ReadOnlySpan<char> line, string sourceFilePath, PathRoots pathRoots = null)
        {
            var sourceDir = string.IsNullOrEmpty(sourceFilePath)
                ? null
                : System.IO.Path.GetDirectoryName(sourceFilePath);
            var options = new ReadWriteOptions(line.Trim(), 0, sourceDir, pathRoots);
            if (options.Name.IsEmpty)
                return null;

            var literal = DataLiteral(DataExchange.Read(options), options.Type);
            return literal is null ? null : $"{options.Name} = {literal}";
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

            // The keyword, the variable name and the connector: one space-delimited token each,
            // consumed exactly as ReadWriteOptions consumes them.
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

        private static string DataLiteral(string[][] data, char type)
        {
            var rows = data?.Length ?? 0;
            if (rows == 0 || (data[0]?.Length ?? 0) == 0)
                return null;

            if (type == 'V')
                return Brackets(Flatten(data));

            var cols = 0;
            for (var i = 0; i < rows; ++i)
                cols = Math.Max(cols, data[i]?.Length ?? 0);

            if ((type == 'C' || type == 'D') && cols != 1 && rows != 1)
                throw Exceptions.IndexOutOfRange($"{rows}, {cols}");

            return type switch
            {
                'R' => Rows(data),
                'C' => $"vec2col({Brackets(Axis(data))})",
                'D' => $"vec2diag({Brackets(Axis(data))})",
                'L' => $"copy({Rows(data)}; ltriang({rows}); 1; 1)",
                'U' => $"copy({Rows(Skyline(data))}; utriang({rows}); 1; 1)",
                'S' => $"copy({Rows(Skyline(data))}; symmetric({rows}); 1; 1)",
                _ => throw Exceptions.InvalidType(type),
            };
        }

        private static string Rows(string[][] data)
        {
            var sb = new StringBuilder("[");
            for (int i = 0, n = data.Length; i < n; ++i)
            {
                if (i > 0)
                    sb.Append('|');

                var row = data[i];
                // An empty row still has to hold something to keep the brackets valid;
                // zero is what the missing cells are read as anyway.
                if (row is null || row.Length == 0)
                    sb.Append('0');
                else
                    AppendCells(sb, row);
            }
            return sb.Append(']').ToString();
        }

        private static string Brackets(string[] values)
        {
            var sb = new StringBuilder("[");
            AppendCells(sb, values);
            return sb.Append(']').ToString();
        }

        private static void AppendCells(StringBuilder sb, string[] cells)
        {
            for (int j = 0, m = cells.Length; j < m; ++j)
            {
                if (j > 0)
                    sb.Append("; ");

                var cell = cells[j];
                sb.Append(string.IsNullOrWhiteSpace(cell) ? "0" : cell.Trim());
            }
        }

        /// <summary>Every cell, row by row — how a <c>type=V</c> read fills its vector.</summary>
        private static string[] Flatten(string[][] data)
        {
            var cells = new List<string>();
            foreach (var row in data)
                if (row is not null)
                    cells.AddRange(row);

            return [.. cells];
        }

        private static string[] Axis(string[][] data)
        {
            if (data.Length == 1)
                return data[0];

            var values = new string[data.Length];
            for (var i = 0; i < data.Length; ++i)
                values[i] = data[i] is { Length: > 0 } row ? row[0] : null;

            return values;
        }

        private static string[][] Skyline(string[][] data)
        {
            var n = data.Length;
            var shifted = new string[n][];
            for (var i = 0; i < n; ++i)
            {
                var row = data[i];
                var m = Math.Min(row?.Length ?? 0, n - i);
                shifted[i] = new string[i + m];
                for (var j = 0; j < m; ++j)
                    shifted[i][i + j] = row[j];
            }
            return shifted;
        }
    }
}
