using System;
using System.Globalization;

namespace Calcpad.Core
{
    public partial class MathParser
    {
        internal (int Rows, int Columns) GetVariableShape(string name) =>
            !_variables.TryGetValue(name, out var v) || !v.IsInitialized
                ? (0, 0)
                : v.Value switch
                {
                    Vector vector => (1, vector.Length),
                    Matrix matrix => (matrix.RowCount, matrix.ColCount),
                    _ => (0, 0)
                };

        /// <summary>
        /// The cells of a vector/matrix variable, padded to <paramref name="rows"/> ×
        /// <paramref name="cols"/>, with each cell's unit returned separately so a datagrid can
        /// show bare numbers and put the unit back when it writes the literal. A padded cell has
        /// no unit of its own, which is a null rather than an empty one. Null if the variable is
        /// not a vector or a matrix.
        /// </summary>
        internal (string[][] Values, string[][] Units)? GetUiGridValues(string name, int rows, int cols)
        {
            if (!_variables.TryGetValue(name, out var v) || !v.IsInitialized)
                return null;

            Matrix matrix = v.Value switch
            {
                Vector vector => new Matrix(vector),
                Matrix m => m,
                _ => null
            };
            if (matrix is null)
                return null;

            var values = new string[rows][];
            var units = new string[rows][];
            for (var i = 0; i < rows; ++i)
            {
                values[i] = new string[cols];
                units[i] = new string[cols];
                for (var j = 0; j < cols; ++j)
                {
                    if (i < matrix.RowCount && j < matrix.ColCount)
                    {
                        var cell = matrix[i, j];
                        values[i][j] = FormatUiCell(Math.Round(cell.D, _settings.Decimals));
                        units[i][j] = UnitInputText(cell.Units);
                    }
                    else
                        values[i][j] = "0";
                }
            }
            return (values, units);
        }

        /// <summary>Plain decimal: the exponent form is read back as a unit, not as a number.</summary>
        private static string FormatUiCell(double d) =>
            d.ToString("0.###############", CultureInfo.InvariantCulture);

        /// <summary>The result's unit as the parser reads it back in, empty when it has none.</summary>
        internal string ResultUnitsInputText => UnitInputText(Units);

        /// <summary>'·' and '∕' are written for display and are not input operators.</summary>
        private static string UnitInputText(Unit units) =>
            units is null ? string.Empty : units.Text.Replace('·', '*').Replace('∕', '/');
    }
}
