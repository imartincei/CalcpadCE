using System;
using System.Collections.Generic;
using System.Text;

namespace Calcpad.Core
{
    public partial class ExpressionParser
    {
        /// <summary>
        /// Rewrites a <c>#read</c> directive as the code that assigns the data it imports, so
        /// a worksheet can be bundled with nothing beside it. The value keeps the structure
        /// the directive asked for — a symmetric read stays symmetric — but never a high
        /// performance one: <c>type=S_hp</c> yields a plain symmetric matrix.
        /// </summary>
        /// <param name="line">The directive, from <c>#read</c> onwards.</param>
        /// <param name="sourceFilePath">
        /// The worksheet holding the directive. A relative data path resolves against its
        /// folder, exactly as it does when the directive runs.
        /// </param>
        /// <returns>
        /// The assignment, or <c>null</c> when the directive names no variable or the file
        /// holds no data — both assign nothing when the directive runs.
        /// </returns>
        public static string InlineReadDirective(ReadOnlySpan<char> line, string sourceFilePath)
        {
            var sourceDir = string.IsNullOrEmpty(sourceFilePath)
                ? null
                : System.IO.Path.GetDirectoryName(sourceFilePath);
            var options = new ReadWriteOptions(line.Trim(), 0, sourceDir);
            if (options.Name.IsEmpty)
                return null;

            var literal = DataLiteral(DataExchange.Read(options), options.Type);
            return literal is null ? null : $"{options.Name} = {literal}";
        }

        /// <summary>
        /// The literal that recreates <paramref name="data"/> the way <paramref name="type"/>
        /// asks for, mirroring <see cref="MathParser"/>'s <c>SetMatrix</c>/<c>SetVector</c>.
        /// Structured types are built by copying a rectangle into an empty matrix of that
        /// type, which drops the cells falling outside the structure just as the assignment
        /// does.
        /// </summary>
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

        /// <summary>
        /// The values a column or diagonal read takes: a single row is read along, anything
        /// else down its first column.
        /// </summary>
        private static string[] Axis(string[][] data)
        {
            if (data.Length == 1)
                return data[0];

            var values = new string[data.Length];
            for (var i = 0; i < data.Length; ++i)
                values[i] = data[i] is { Length: > 0 } row ? row[0] : null;

            return values;
        }

        /// <summary>
        /// Shifts row <c>i</c> right by <c>i</c> columns. Upper triangular and symmetric
        /// reads take each row as starting on the diagonal, so the cells have to be moved
        /// there before they can be written as a rectangle.
        /// </summary>
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
