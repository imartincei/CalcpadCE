using Calcpad.Highlighter.Linter.Models;
using Calcpad.Highlighter.Snippets.Models;

namespace Calcpad.Highlighter.Snippets.Data
{
    /// <summary>
    /// Snippet definitions for matrix functions.
    /// Most functions in this file return matrices unless otherwise noted (e.g., det, rank return scalars).
    /// </summary>
    public static class MatrixFunctionSnippets
    {
        public static readonly SnippetItem[] Items =
        [
            // ============================================
            // MATRIX CREATIONAL FUNCTIONS
            // ============================================
            new SnippetItem
            {
                Insert = "matrix(§; §)",
                Description = "Creates an empty matrix with dimensions m x n",
                Documentation = "Allocates a new `m × n` matrix with all elements initialized to 0. Use indexed assignment to populate it.",
                Example = "M = matrix(3; 3)  ' 3x3 zero matrix",
                Category = "Functions/Matrix/Creational",
                KeywordType = "Function",
                ReturnType = CalcpadType.Matrix,
                ReturnTypeDescription = "Matrix m × n",
                Parameters =
                [
                    new SnippetParameter { Name = "m", Type = ParameterType.Integer, Description = "Number of rows" },
                    new SnippetParameter { Name = "n", Type = ParameterType.Integer, Description = "Number of columns" }
                ]
            },
            new SnippetItem
            {
                Insert = "identity(§)",
                Description = "Creates an identity matrix with dimensions n x n",
                Documentation = "Returns the `n × n` identity matrix `I` (1s on the diagonal, 0s elsewhere).",
                Category = "Functions/Matrix/Creational",
                KeywordType = "Function",
                ReturnType = CalcpadType.Matrix,
                ReturnTypeDescription = "Matrix n × n",
                Parameters = [new SnippetParameter { Name = "n", Type = ParameterType.Integer, Description = "Size" }]
            },
            new SnippetItem
            {
                Insert = "diagonal(§; §)",
                Description = "Creates a diagonal matrix n x n filled with value d",
                Documentation = "Returns an `n × n` matrix with `d` on the main diagonal and 0 elsewhere. For `d = 1` use `identity(n)` instead.",
                Category = "Functions/Matrix/Creational",
                KeywordType = "Function",
                ReturnType = CalcpadType.Matrix,
                ReturnTypeDescription = "Matrix n × n (diagonal)",
                Parameters =
                [
                    new SnippetParameter { Name = "n", Type = ParameterType.Integer, Description = "Size" },
                    new SnippetParameter { Name = "d", Type = ParameterType.Scalar, Description = "Diagonal value" }
                ]
            },
            new SnippetItem
            {
                Insert = "column(§; §)",
                Description = "Creates a column matrix m x 1 filled with value c",
                Documentation = "Returns an `m × 1` column matrix with every element equal to `c`. For row vectors use `vec2row`.",
                Category = "Functions/Matrix/Creational",
                KeywordType = "Function",
                ReturnType = CalcpadType.Matrix,
                ReturnTypeDescription = "Matrix m × 1",
                Parameters =
                [
                    new SnippetParameter { Name = "m", Type = ParameterType.Integer, Description = "Number of rows" },
                    new SnippetParameter { Name = "c", Type = ParameterType.Scalar, Description = "Fill value" }
                ]
            },
            new SnippetItem
            {
                Insert = "utriang(§)",
                Description = "Creates an upper triangular matrix n x n",
                Documentation = "Returns an `n × n` matrix that stores only the upper triangle (entries `Mᵢⱼ` with `j ≥ i`). Lower-triangle entries are implicitly zero.",
                Category = "Functions/Matrix/Creational",
                KeywordType = "Function",
                ReturnType = CalcpadType.Matrix,
                ReturnTypeDescription = "Upper-triangular n × n matrix",
                Parameters = [new SnippetParameter { Name = "n", Type = ParameterType.Integer, Description = "Size" }]
            },
            new SnippetItem
            {
                Insert = "ltriang(§)",
                Description = "Creates a lower triangular matrix n x n",
                Documentation = "Returns an `n × n` matrix that stores only the lower triangle (entries `Mᵢⱼ` with `j ≤ i`).",
                Category = "Functions/Matrix/Creational",
                KeywordType = "Function",
                ReturnType = CalcpadType.Matrix,
                ReturnTypeDescription = "Lower-triangular n × n matrix",
                Parameters = [new SnippetParameter { Name = "n", Type = ParameterType.Integer, Description = "Size" }]
            },
            new SnippetItem
            {
                Insert = "symmetric(§)",
                Description = "Creates a symmetric matrix n x n",
                Documentation = "Returns an `n × n` matrix with the symmetric storage layout: assigning `M[i; j]` automatically mirrors to `M[j; i]`.",
                Category = "Functions/Matrix/Creational",
                KeywordType = "Function",
                ReturnType = CalcpadType.Matrix,
                ReturnTypeDescription = "Symmetric n × n matrix",
                Parameters = [new SnippetParameter { Name = "n", Type = ParameterType.Integer, Description = "Size" }]
            },

            // High-Performance Matrix Creation
            new SnippetItem
            {
                Insert = "matrix_hp(§; §)",
                Description = "Creates a high-performance matrix m x n",
                Documentation = "Like `matrix(m; n)` but uses the high-performance representation (single-precision, contiguous storage). See `hp` for tradeoffs.",
                Category = "Functions/Matrix/Creational",
                KeywordType = "Function",
                ReturnType = CalcpadType.Matrix,
                ReturnTypeDescription = "High-performance matrix m × n",
                Parameters =
                [
                    new SnippetParameter { Name = "m", Type = ParameterType.Integer, Description = "Number of rows" },
                    new SnippetParameter { Name = "n", Type = ParameterType.Integer, Description = "Number of columns" }
                ]
            },
            new SnippetItem
            {
                Insert = "identity_hp(§)",
                Description = "Creates a high-performance identity matrix n x n",
                Documentation = "High-performance variant of `identity(n)`.",
                Category = "Functions/Matrix/Creational",
                KeywordType = "Function",
                ReturnType = CalcpadType.Matrix,
                ReturnTypeDescription = "High-performance identity n × n",
                Parameters = [new SnippetParameter { Name = "n", Type = ParameterType.Integer, Description = "Size" }]
            },
            new SnippetItem
            {
                Insert = "diagonal_hp(§; §)",
                Description = "Creates a high-performance diagonal matrix n x n",
                Documentation = "High-performance variant of `diagonal(n; d)`.",
                Category = "Functions/Matrix/Creational",
                KeywordType = "Function",
                ReturnType = CalcpadType.Matrix,
                ReturnTypeDescription = "High-performance n × n diagonal matrix",
                Parameters =
                [
                    new SnippetParameter { Name = "n", Type = ParameterType.Integer, Description = "Size" },
                    new SnippetParameter { Name = "d", Type = ParameterType.Scalar, Description = "Diagonal value" }
                ]
            },
            new SnippetItem
            {
                Insert = "column_hp(§; §)",
                Description = "Creates a high-performance column matrix m x 1",
                Documentation = "High-performance variant of `column(m; c)`.",
                Category = "Functions/Matrix/Creational",
                KeywordType = "Function",
                ReturnType = CalcpadType.Matrix,
                ReturnTypeDescription = "High-performance m × 1 matrix",
                Parameters =
                [
                    new SnippetParameter { Name = "m", Type = ParameterType.Integer, Description = "Number of rows" },
                    new SnippetParameter { Name = "c", Type = ParameterType.Scalar, Description = "Fill value" }
                ]
            },
            new SnippetItem
            {
                Insert = "utriang_hp(§)",
                Description = "Creates a high-performance upper triangular matrix",
                Documentation = "High-performance variant of `utriang(n)`.",
                Category = "Functions/Matrix/Creational",
                KeywordType = "Function",
                ReturnType = CalcpadType.Matrix,
                ReturnTypeDescription = "High-performance upper-triangular n × n",
                Parameters = [new SnippetParameter { Name = "n", Type = ParameterType.Integer, Description = "Size" }]
            },
            new SnippetItem
            {
                Insert = "ltriang_hp(§)",
                Description = "Creates a high-performance lower triangular matrix",
                Documentation = "High-performance variant of `ltriang(n)`.",
                Category = "Functions/Matrix/Creational",
                KeywordType = "Function",
                ReturnType = CalcpadType.Matrix,
                ReturnTypeDescription = "High-performance lower-triangular n × n",
                Parameters = [new SnippetParameter { Name = "n", Type = ParameterType.Integer, Description = "Size" }]
            },
            new SnippetItem
            {
                Insert = "symmetric_hp(§)",
                Description = "Creates a high-performance symmetric matrix",
                Documentation = "High-performance variant of `symmetric(n)`.",
                Category = "Functions/Matrix/Creational",
                KeywordType = "Function",
                ReturnType = CalcpadType.Matrix,
                ReturnTypeDescription = "High-performance symmetric n × n",
                Parameters = [new SnippetParameter { Name = "n", Type = ParameterType.Integer, Description = "Size" }]
            },

            // Vector to Matrix Conversion
            new SnippetItem
            {
                Insert = "vec2diag(§)",
                Description = "Creates a diagonal matrix from vector elements",
                Documentation = "Returns an `n × n` matrix where `M[i; i] = v[i]` and all off-diagonal entries are 0. Inverse is `diag2vec`.",
                Category = "Functions/Matrix/Creational",
                KeywordType = "Function",
                ReturnTypeDescription = "Diagonal matrix",
                Parameters = [new SnippetParameter { Name = "v", Type = ParameterType.Vector, Description = "Source vector" }]
            },
            new SnippetItem
            {
                Insert = "vec2row(§)",
                Description = "Creates a row matrix from vector elements",
                Documentation = "Returns a `1 × n` row matrix containing the elements of `v`.",
                Category = "Functions/Matrix/Creational",
                KeywordType = "Function",
                ReturnTypeDescription = "Row matrix 1 × n",
                Parameters = [new SnippetParameter { Name = "v", Type = ParameterType.Vector, Description = "Source vector" }]
            },
            new SnippetItem
            {
                Insert = "vec2col(§)",
                Description = "Creates a column matrix from vector elements",
                Documentation = "Returns an `n × 1` column matrix containing the elements of `v`.",
                Category = "Functions/Matrix/Creational",
                KeywordType = "Function",
                ReturnTypeDescription = "Column matrix n × 1",
                Parameters = [new SnippetParameter { Name = "v", Type = ParameterType.Vector, Description = "Source vector" }]
            },
            new SnippetItem
            {
                Insert = "join_cols(§; §)",
                Description = "Creates a matrix by joining column vectors",
                Documentation = "Treats each argument as a column and stacks them side by side. All vectors must have the same length.",
                Category = "Functions/Matrix/Creational",
                KeywordType = "Function",
                ReturnTypeDescription = "Matrix",
                AcceptsAnyCount = true,
                Parameters = [new SnippetParameter { Name = "columns", Type = ParameterType.Vector, Description = "Column vectors", IsVariadic = true }]
            },
            new SnippetItem
            {
                Insert = "join_rows(§; §)",
                Description = "Creates a matrix by joining row vectors",
                Documentation = "Treats each argument as a row and stacks them top to bottom. All vectors must have the same length.",
                Category = "Functions/Matrix/Creational",
                KeywordType = "Function",
                ReturnTypeDescription = "Matrix",
                AcceptsAnyCount = true,
                Parameters = [new SnippetParameter { Name = "rows", Type = ParameterType.Vector, Description = "Row vectors", IsVariadic = true }]
            },
            new SnippetItem
            {
                Insert = "augment(§; §)",
                Description = "Creates a matrix by appending matrices side by side",
                Documentation = "Concatenates matrices horizontally. All matrices must have the same number of rows. A vector argument is treated as a single column.",
                Category = "Functions/Matrix/Creational",
                KeywordType = "Function",
                ReturnTypeDescription = "Matrix",
                AcceptsAnyCount = true,
                Parameters = [new SnippetParameter { Name = "matrices", Type = ParameterType.Matrix, Description = "Matrices or vectors to append", IsVariadic = true }]
            },
            new SnippetItem
            {
                Insert = "stack(§; §)",
                Description = "Creates a matrix by stacking matrices vertically",
                Documentation = "Concatenates matrices vertically. All matrices must have the same number of columns. A vector argument is treated as an n×1 column matrix, so it contributes rows of length 1 that are zero-padded to the width of the result.",
                Category = "Functions/Matrix/Creational",
                KeywordType = "Function",
                ReturnTypeDescription = "Matrix",
                AcceptsAnyCount = true,
                Parameters = [new SnippetParameter { Name = "matrices", Type = ParameterType.Matrix, Description = "Matrices or vectors to stack", IsVariadic = true }]
            },

            // ============================================
            // MATRIX STRUCTURAL FUNCTIONS
            // ============================================
            new SnippetItem
            {
                Insert = "n_rows(§)",
                Description = "Number of rows in matrix M",
                Documentation = "Returns the row count of `M`.",
                Category = "Functions/Matrix/Structural",
                KeywordType = "Function",
                ReturnTypeDescription = "Positive integer",
                Parameters = [new SnippetParameter { Name = "M", Type = ParameterType.Matrix, Description = "Matrix" }]
            },
            new SnippetItem
            {
                Insert = "n_cols(§)",
                Description = "Number of columns in matrix M",
                Documentation = "Returns the column count of `M`.",
                Category = "Functions/Matrix/Structural",
                KeywordType = "Function",
                ReturnTypeDescription = "Positive integer",
                Parameters = [new SnippetParameter { Name = "M", Type = ParameterType.Matrix, Description = "Matrix" }]
            },
            new SnippetItem
            {
                Insert = "mresize(§; §; §)",
                Description = "Sets new dimensions m and n for matrix M",
                Documentation = "Returns a copy of `M` reshaped to `m × n`. Existing entries within the new bounds are preserved; new cells are zero-filled.",
                Category = "Functions/Matrix/Structural",
                KeywordType = "Function",
                ReturnTypeDescription = "Matrix m × n",
                Parameters =
                [
                    new SnippetParameter { Name = "M", Type = ParameterType.Matrix, Description = "Matrix" },
                    new SnippetParameter { Name = "m", Type = ParameterType.Integer, Description = "New row count" },
                    new SnippetParameter { Name = "n", Type = ParameterType.Integer, Description = "New column count" }
                ]
            },
            new SnippetItem
            {
                Insert = "mfill(§; §)",
                Description = "Fills the matrix M with value x",
                Documentation = "Returns a copy of `M` with every element replaced by `x`.",
                Category = "Functions/Matrix/Structural",
                KeywordType = "Function",
                ReturnTypeDescription = "Matrix (same shape as M)",
                Parameters =
                [
                    new SnippetParameter { Name = "M", Type = ParameterType.Matrix, Description = "Matrix" },
                    new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Fill value" }
                ]
            },
            new SnippetItem
            {
                Insert = "fill_row(§; §; §)",
                Description = "Fills the i-th row of matrix M with value x",
                Documentation = "Returns a copy of `M` with row `i` set to `x` in every column.",
                Category = "Functions/Matrix/Structural",
                KeywordType = "Function",
                ReturnTypeDescription = "Matrix (same shape as M)",
                Parameters =
                [
                    new SnippetParameter { Name = "M", Type = ParameterType.Matrix, Description = "Matrix" },
                    new SnippetParameter { Name = "i", Type = ParameterType.Integer, Description = "Row index" },
                    new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Fill value" }
                ]
            },
            new SnippetItem
            {
                Insert = "fill_col(§; §; §)",
                Description = "Fills the j-th column of matrix M with value x",
                Documentation = "Returns a copy of `M` with column `j` set to `x` in every row.",
                Category = "Functions/Matrix/Structural",
                KeywordType = "Function",
                ReturnTypeDescription = "Matrix (same shape as M)",
                Parameters =
                [
                    new SnippetParameter { Name = "M", Type = ParameterType.Matrix, Description = "Matrix" },
                    new SnippetParameter { Name = "j", Type = ParameterType.Integer, Description = "Column index" },
                    new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Fill value" }
                ]
            },
            new SnippetItem
            {
                Insert = "copy(§; §; §; §)",
                Description = "Copies all elements from A to B starting at indexes i, j",
                Documentation = "Returns a copy of `B` with the elements of `A` written into the block starting at row `i`, column `j`. Errors if `A` would extend past the bounds of `B`.",
                Category = "Functions/Matrix/Structural",
                KeywordType = "Function",
                ReturnTypeDescription = "Matrix (same shape as B)",
                Parameters =
                [
                    new SnippetParameter { Name = "A", Type = ParameterType.Matrix, Description = "Source matrix" },
                    new SnippetParameter { Name = "B", Type = ParameterType.Matrix, Description = "Destination matrix" },
                    new SnippetParameter { Name = "i", Type = ParameterType.Integer, Description = "Start row" },
                    new SnippetParameter { Name = "j", Type = ParameterType.Integer, Description = "Start column" }
                ]
            },
            new SnippetItem
            {
                Insert = "add(§; §; §; §)",
                Description = "Adds elements from A to B starting at indexes i, j",
                Documentation = "Returns a copy of `B` with `A`'s elements added (rather than overwritten) into the block at `(i, j)`.",
                Category = "Functions/Matrix/Structural",
                KeywordType = "Function",
                ReturnTypeDescription = "Matrix (same shape as B)",
                Parameters =
                [
                    new SnippetParameter { Name = "A", Type = ParameterType.Matrix, Description = "Source matrix" },
                    new SnippetParameter { Name = "B", Type = ParameterType.Matrix, Description = "Destination matrix" },
                    new SnippetParameter { Name = "i", Type = ParameterType.Integer, Description = "Start row" },
                    new SnippetParameter { Name = "j", Type = ParameterType.Integer, Description = "Start column" }
                ]
            },
            new SnippetItem
            {
                Insert = "row(§; §)",
                Description = "Extracts the i-th row of matrix M as a vector",
                Documentation = "Returns the `i`-th row of `M` as a vector of length `n_cols(M)`. A vector is treated as an n×1 column matrix, so `row` yields a single element.",
                Category = "Functions/Matrix/Structural",
                KeywordType = "Function",
                ReturnTypeDescription = "Vector",
                Parameters =
                [
                    new SnippetParameter { Name = "M", Type = ParameterType.Matrix, Description = "Matrix" },
                    new SnippetParameter { Name = "i", Type = ParameterType.Integer, Description = "Row index" }
                ]
            },
            new SnippetItem
            {
                Insert = "col(§; §)",
                Description = "Extracts the j-th column of matrix M as a vector",
                Documentation = "Returns the `j`-th column of `M` as a vector of length `n_rows(M)`. A vector is treated as an n×1 column matrix, so `col(v; 1)` returns the vector itself.",
                Category = "Functions/Matrix/Structural",
                KeywordType = "Function",
                ReturnTypeDescription = "Vector",
                Parameters =
                [
                    new SnippetParameter { Name = "M", Type = ParameterType.Matrix, Description = "Matrix" },
                    new SnippetParameter { Name = "j", Type = ParameterType.Integer, Description = "Column index" }
                ]
            },
            new SnippetItem
            {
                Insert = "extract_rows(§; §)",
                Description = "Extracts rows from M whose indexes are in vector i",
                Documentation = "Returns a new matrix with the rows of `M` selected and reordered by the indices in `i`.",
                Category = "Functions/Matrix/Structural",
                KeywordType = "Function",
                ReturnTypeDescription = "Matrix",
                Parameters =
                [
                    new SnippetParameter { Name = "M", Type = ParameterType.Matrix, Description = "Matrix" },
                    new SnippetParameter { Name = "i", Type = ParameterType.Vector, Description = "Row index vector" }
                ]
            },
            new SnippetItem
            {
                Insert = "extract_cols(§; §)",
                Description = "Extracts columns from M whose indexes are in vector j",
                Documentation = "Returns a new matrix with the columns of `M` selected and reordered by the indices in `j`.",
                Category = "Functions/Matrix/Structural",
                KeywordType = "Function",
                ReturnTypeDescription = "Matrix",
                Parameters =
                [
                    new SnippetParameter { Name = "M", Type = ParameterType.Matrix, Description = "Matrix" },
                    new SnippetParameter { Name = "j", Type = ParameterType.Vector, Description = "Column index vector" }
                ]
            },
            new SnippetItem
            {
                Insert = "diag2vec(§)",
                Description = "Extracts diagonal elements of matrix M to a vector",
                Documentation = "Returns the main diagonal `[M[1;1], M[2;2], …, M[k;k]]` where `k = min(rows, cols)`. Inverse is `vec2diag`.",
                Category = "Functions/Matrix/Structural",
                KeywordType = "Function",
                ReturnTypeDescription = "Vector",
                Parameters = [new SnippetParameter { Name = "M", Type = ParameterType.Matrix, Description = "Matrix" }]
            },
            new SnippetItem
            {
                Insert = "submatrix(§; §; §; §; §)",
                Description = "Extracts submatrix bounded by rows i1-i2 and columns j1-j2",
                Documentation = "Returns the rectangular block of `M` from `(i1, j1)` to `(i2, j2)` inclusive (1-based bounds).",
                Category = "Functions/Matrix/Structural",
                KeywordType = "Function",
                ReturnTypeDescription = "Matrix",
                Parameters =
                [
                    new SnippetParameter { Name = "M", Type = ParameterType.Matrix, Description = "Matrix" },
                    new SnippetParameter { Name = "i1", Type = ParameterType.Integer, Description = "Start row" },
                    new SnippetParameter { Name = "i2", Type = ParameterType.Integer, Description = "End row" },
                    new SnippetParameter { Name = "j1", Type = ParameterType.Integer, Description = "Start column" },
                    new SnippetParameter { Name = "j2", Type = ParameterType.Integer, Description = "End column" }
                ]
            },

            // ============================================
            // MATRIX DATA FUNCTIONS
            // ============================================
            new SnippetItem
            {
                Insert = "sort_cols(§; §)",
                Description = "Sorts columns based on values in row i (ascending)",
                Documentation = "Reorders the columns of `M` so that row `i` is in ascending order. All columns move together as units.",
                Category = "Functions/Matrix/Data",
                KeywordType = "Function",
                ReturnTypeDescription = "Matrix (same shape as M)",
                Parameters =
                [
                    new SnippetParameter { Name = "M", Type = ParameterType.Matrix, Description = "Matrix" },
                    new SnippetParameter { Name = "i", Type = ParameterType.Integer, Description = "Row index for sorting" }
                ]
            },
            new SnippetItem
            {
                Insert = "rsort_cols(§; §)",
                Description = "Sorts columns based on values in row i (descending)",
                Documentation = "Reorders the columns of `M` so that row `i` is in descending order.",
                Category = "Functions/Matrix/Data",
                KeywordType = "Function",
                ReturnTypeDescription = "Matrix (same shape as M)",
                Parameters =
                [
                    new SnippetParameter { Name = "M", Type = ParameterType.Matrix, Description = "Matrix" },
                    new SnippetParameter { Name = "i", Type = ParameterType.Integer, Description = "Row index for sorting" }
                ]
            },
            new SnippetItem
            {
                Insert = "sort_rows(§; §)",
                Description = "Sorts rows based on values in column j (ascending)",
                Documentation = "Reorders the rows of `M` so that column `j` is in ascending order.",
                Category = "Functions/Matrix/Data",
                KeywordType = "Function",
                ReturnTypeDescription = "Matrix (same shape as M)",
                Parameters =
                [
                    new SnippetParameter { Name = "M", Type = ParameterType.Matrix, Description = "Matrix" },
                    new SnippetParameter { Name = "j", Type = ParameterType.Integer, Description = "Column index for sorting" }
                ]
            },
            new SnippetItem
            {
                Insert = "rsort_rows(§; §)",
                Description = "Sorts rows based on values in column j (descending)",
                Documentation = "Reorders the rows of `M` so that column `j` is in descending order.",
                Category = "Functions/Matrix/Data",
                KeywordType = "Function",
                ReturnTypeDescription = "Matrix (same shape as M)",
                Parameters =
                [
                    new SnippetParameter { Name = "M", Type = ParameterType.Matrix, Description = "Matrix" },
                    new SnippetParameter { Name = "j", Type = ParameterType.Integer, Description = "Column index for sorting" }
                ]
            },
            new SnippetItem
            {
                Insert = "order_cols(§; §)",
                Description = "Column indexes in ascending order by row i values",
                Documentation = "Returns the permutation of column indices that would sort row `i` ascending. Use `extract_cols` to apply it.",
                Category = "Functions/Matrix/Data",
                KeywordType = "Function",
                ReturnTypeDescription = "Vector of indices",
                Parameters =
                [
                    new SnippetParameter { Name = "M", Type = ParameterType.Matrix, Description = "Matrix" },
                    new SnippetParameter { Name = "i", Type = ParameterType.Integer, Description = "Row index" }
                ]
            },
            new SnippetItem
            {
                Insert = "revorder_cols(§; §)",
                Description = "Column indexes in descending order by row i values",
                Documentation = "Returns the permutation of column indices that would sort row `i` descending.",
                Category = "Functions/Matrix/Data",
                KeywordType = "Function",
                ReturnTypeDescription = "Vector of indices",
                Parameters =
                [
                    new SnippetParameter { Name = "M", Type = ParameterType.Matrix, Description = "Matrix" },
                    new SnippetParameter { Name = "i", Type = ParameterType.Integer, Description = "Row index" }
                ]
            },
            new SnippetItem
            {
                Insert = "order_rows(§; §)",
                Description = "Row indexes in ascending order by column j values",
                Documentation = "Returns the permutation of row indices that would sort column `j` ascending.",
                Category = "Functions/Matrix/Data",
                KeywordType = "Function",
                ReturnTypeDescription = "Vector of indices",
                Parameters =
                [
                    new SnippetParameter { Name = "M", Type = ParameterType.Matrix, Description = "Matrix" },
                    new SnippetParameter { Name = "j", Type = ParameterType.Integer, Description = "Column index" }
                ]
            },
            new SnippetItem
            {
                Insert = "revorder_rows(§; §)",
                Description = "Row indexes in descending order by column j values",
                Documentation = "Returns the permutation of row indices that would sort column `j` descending.",
                Category = "Functions/Matrix/Data",
                KeywordType = "Function",
                ReturnTypeDescription = "Vector of indices",
                Parameters =
                [
                    new SnippetParameter { Name = "M", Type = ParameterType.Matrix, Description = "Matrix" },
                    new SnippetParameter { Name = "j", Type = ParameterType.Integer, Description = "Column index" }
                ]
            },
            new SnippetItem
            {
                Insert = "mcount(§; §)",
                Description = "Number of occurrences of value x in matrix M",
                Documentation = "Returns the count of elements of `M` equal to `x`.",
                Category = "Functions/Matrix/Data",
                KeywordType = "Function",
                ReturnTypeDescription = "Non-negative integer",
                Parameters =
                [
                    new SnippetParameter { Name = "M", Type = ParameterType.Matrix, Description = "Matrix" },
                    new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Value to count" }
                ]
            },
            new SnippetItem
            {
                Insert = "msearch(§; §; §; §)",
                Description = "Vector with indexes of first occurrence of x starting from i, j",
                Documentation = "Searches `M` row-by-row from `(i, j)` for the first occurrence of `x` and returns `[row; col]`. Returns `[0; 0]` if not found.",
                Category = "Functions/Matrix/Data",
                KeywordType = "Function",
                ReturnTypeDescription = "Vector [row; col]",
                Parameters =
                [
                    new SnippetParameter { Name = "M", Type = ParameterType.Matrix, Description = "Matrix" },
                    new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Value to find" },
                    new SnippetParameter { Name = "i", Type = ParameterType.Integer, Description = "Start row" },
                    new SnippetParameter { Name = "j", Type = ParameterType.Integer, Description = "Start column" }
                ]
            },

            // Matrix Find Functions
            new SnippetItem
            {
                Insert = "mfind(§; §)",
                Description = "Indexes of all elements in M equal to x",
                Documentation = "Returns a 2-row matrix where each column is `[row; col]` of an element equal to `x`. Same as `mfind_eq`.",
                Category = "Functions/Matrix/Data",
                KeywordType = "Function",
                ReturnTypeDescription = "2 × k matrix of indices",
                Parameters =
                [
                    new SnippetParameter { Name = "M", Type = ParameterType.Matrix, Description = "Matrix" },
                    new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Value" }
                ]
            },
            new SnippetItem
            {
                Insert = "mfind_eq(§; §)",
                Description = "Indexes of elements = x",
                Documentation = "Returns the `[row; col]` indices of every element equal to `x`. Same as `mfind`.",
                Category = "Functions/Matrix/Data",
                KeywordType = "Function",
                ReturnTypeDescription = "2 × k matrix of indices",
                Parameters =
                [
                    new SnippetParameter { Name = "M", Type = ParameterType.Matrix, Description = "Matrix" },
                    new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Value" }
                ]
            },
            new SnippetItem
            {
                Insert = "mfind_ne(§; §)",
                Description = "Indexes of elements != x",
                Documentation = "Returns the `[row; col]` indices of every element not equal to `x`.",
                Category = "Functions/Matrix/Data",
                KeywordType = "Function",
                ReturnTypeDescription = "2 × k matrix of indices",
                Parameters =
                [
                    new SnippetParameter { Name = "M", Type = ParameterType.Matrix, Description = "Matrix" },
                    new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Value" }
                ]
            },
            new SnippetItem
            {
                Insert = "mfind_lt(§; §)",
                Description = "Indexes of elements < x",
                Documentation = "Returns the `[row; col]` indices of every element less than `x`.",
                Category = "Functions/Matrix/Data",
                KeywordType = "Function",
                ReturnTypeDescription = "2 × k matrix of indices",
                Parameters =
                [
                    new SnippetParameter { Name = "M", Type = ParameterType.Matrix, Description = "Matrix" },
                    new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Value" }
                ]
            },
            new SnippetItem
            {
                Insert = "mfind_le(§; §)",
                Description = "Indexes of elements <= x",
                Documentation = "Returns the `[row; col]` indices of every element ≤ `x`.",
                Category = "Functions/Matrix/Data",
                KeywordType = "Function",
                ReturnTypeDescription = "2 × k matrix of indices",
                Parameters =
                [
                    new SnippetParameter { Name = "M", Type = ParameterType.Matrix, Description = "Matrix" },
                    new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Value" }
                ]
            },
            new SnippetItem
            {
                Insert = "mfind_gt(§; §)",
                Description = "Indexes of elements > x",
                Documentation = "Returns the `[row; col]` indices of every element greater than `x`.",
                Category = "Functions/Matrix/Data",
                KeywordType = "Function",
                ReturnTypeDescription = "2 × k matrix of indices",
                Parameters =
                [
                    new SnippetParameter { Name = "M", Type = ParameterType.Matrix, Description = "Matrix" },
                    new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Value" }
                ]
            },
            new SnippetItem
            {
                Insert = "mfind_ge(§; §)",
                Description = "Indexes of elements >= x",
                Documentation = "Returns the `[row; col]` indices of every element ≥ `x`.",
                Category = "Functions/Matrix/Data",
                KeywordType = "Function",
                ReturnTypeDescription = "2 × k matrix of indices",
                Parameters =
                [
                    new SnippetParameter { Name = "M", Type = ParameterType.Matrix, Description = "Matrix" },
                    new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Value" }
                ]
            },

            // Matrix Lookup Functions - Horizontal
            new SnippetItem
            {
                Insert = "hlookup(§; §; §; §)",
                Description = "Values from row i2 where row i1 elements = x",
                Documentation = "For every column `k` where `M[i1; k] == x`, returns `M[i2; k]`. Useful for looking up values across a header row. Same as `hlookup_eq`.",
                Category = "Functions/Matrix/Data",
                KeywordType = "Function",
                ReturnTypeDescription = "Vector",
                Parameters =
                [
                    new SnippetParameter { Name = "M", Type = ParameterType.Matrix, Description = "Matrix" },
                    new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Lookup value" },
                    new SnippetParameter { Name = "i1", Type = ParameterType.Integer, Description = "Lookup row" },
                    new SnippetParameter { Name = "i2", Type = ParameterType.Integer, Description = "Result row" }
                ]
            },
            new SnippetItem
            {
                Insert = "hlookup_eq(§; §; §; §)",
                Description = "Values from row i2 where row i1 = x",
                Documentation = "Same as `hlookup`. Returns `M[i2; k]` for each `k` where `M[i1; k] == x`.",
                Category = "Functions/Matrix/Data",
                KeywordType = "Function",
                ReturnTypeDescription = "Vector",
                Parameters =
                [
                    new SnippetParameter { Name = "M", Type = ParameterType.Matrix, Description = "Matrix" },
                    new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Lookup value" },
                    new SnippetParameter { Name = "i1", Type = ParameterType.Integer, Description = "Lookup row" },
                    new SnippetParameter { Name = "i2", Type = ParameterType.Integer, Description = "Result row" }
                ]
            },
            new SnippetItem
            {
                Insert = "hlookup_ne(§; §; §; §)",
                Description = "Values from row i2 where row i1 != x",
                Documentation = "Returns `M[i2; k]` for each `k` where `M[i1; k] ≠ x`.",
                Category = "Functions/Matrix/Data",
                KeywordType = "Function",
                ReturnTypeDescription = "Vector",
                Parameters =
                [
                    new SnippetParameter { Name = "M", Type = ParameterType.Matrix, Description = "Matrix" },
                    new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Lookup value" },
                    new SnippetParameter { Name = "i1", Type = ParameterType.Integer, Description = "Lookup row" },
                    new SnippetParameter { Name = "i2", Type = ParameterType.Integer, Description = "Result row" }
                ]
            },
            new SnippetItem
            {
                Insert = "hlookup_lt(§; §; §; §)",
                Description = "Values from row i2 where row i1 < x",
                Documentation = "Returns `M[i2; k]` for each `k` where `M[i1; k] < x`.",
                Category = "Functions/Matrix/Data",
                KeywordType = "Function",
                ReturnTypeDescription = "Vector",
                Parameters =
                [
                    new SnippetParameter { Name = "M", Type = ParameterType.Matrix, Description = "Matrix" },
                    new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Lookup value" },
                    new SnippetParameter { Name = "i1", Type = ParameterType.Integer, Description = "Lookup row" },
                    new SnippetParameter { Name = "i2", Type = ParameterType.Integer, Description = "Result row" }
                ]
            },
            new SnippetItem
            {
                Insert = "hlookup_le(§; §; §; §)",
                Description = "Values from row i2 where row i1 <= x",
                Documentation = "Returns `M[i2; k]` for each `k` where `M[i1; k] ≤ x`.",
                Category = "Functions/Matrix/Data",
                KeywordType = "Function",
                ReturnTypeDescription = "Vector",
                Parameters =
                [
                    new SnippetParameter { Name = "M", Type = ParameterType.Matrix, Description = "Matrix" },
                    new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Lookup value" },
                    new SnippetParameter { Name = "i1", Type = ParameterType.Integer, Description = "Lookup row" },
                    new SnippetParameter { Name = "i2", Type = ParameterType.Integer, Description = "Result row" }
                ]
            },
            new SnippetItem
            {
                Insert = "hlookup_gt(§; §; §; §)",
                Description = "Values from row i2 where row i1 > x",
                Documentation = "Returns `M[i2; k]` for each `k` where `M[i1; k] > x`.",
                Category = "Functions/Matrix/Data",
                KeywordType = "Function",
                ReturnTypeDescription = "Vector",
                Parameters =
                [
                    new SnippetParameter { Name = "M", Type = ParameterType.Matrix, Description = "Matrix" },
                    new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Lookup value" },
                    new SnippetParameter { Name = "i1", Type = ParameterType.Integer, Description = "Lookup row" },
                    new SnippetParameter { Name = "i2", Type = ParameterType.Integer, Description = "Result row" }
                ]
            },
            new SnippetItem
            {
                Insert = "hlookup_ge(§; §; §; §)",
                Description = "Values from row i2 where row i1 >= x",
                Documentation = "Returns `M[i2; k]` for each `k` where `M[i1; k] ≥ x`.",
                Category = "Functions/Matrix/Data",
                KeywordType = "Function",
                ReturnTypeDescription = "Vector",
                Parameters =
                [
                    new SnippetParameter { Name = "M", Type = ParameterType.Matrix, Description = "Matrix" },
                    new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Lookup value" },
                    new SnippetParameter { Name = "i1", Type = ParameterType.Integer, Description = "Lookup row" },
                    new SnippetParameter { Name = "i2", Type = ParameterType.Integer, Description = "Result row" }
                ]
            },

            // Matrix Lookup Functions - Vertical
            new SnippetItem
            {
                Insert = "vlookup(§; §; §; §)",
                Description = "Values from column j2 where column j1 = x",
                Documentation = "For every row `k` where `M[k; j1] == x`, returns `M[k; j2]`. The matrix-column analog of Excel's VLOOKUP.",
                Example = "vlookup(M; 'A'; 1; 2)  ' all column-2 values where column 1 contains 'A'",
                Category = "Functions/Matrix/Data",
                KeywordType = "Function",
                ReturnTypeDescription = "Vector",
                Parameters =
                [
                    new SnippetParameter { Name = "M", Type = ParameterType.Matrix, Description = "Matrix" },
                    new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Lookup value" },
                    new SnippetParameter { Name = "j1", Type = ParameterType.Integer, Description = "Lookup column" },
                    new SnippetParameter { Name = "j2", Type = ParameterType.Integer, Description = "Result column" }
                ]
            },
            new SnippetItem
            {
                Insert = "vlookup_eq(§; §; §; §)",
                Description = "Values from column j2 where column j1 = x",
                Documentation = "Same as `vlookup`. Returns `M[k; j2]` for each `k` where `M[k; j1] == x`.",
                Category = "Functions/Matrix/Data",
                KeywordType = "Function",
                ReturnTypeDescription = "Vector",
                Parameters =
                [
                    new SnippetParameter { Name = "M", Type = ParameterType.Matrix, Description = "Matrix" },
                    new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Lookup value" },
                    new SnippetParameter { Name = "j1", Type = ParameterType.Integer, Description = "Lookup column" },
                    new SnippetParameter { Name = "j2", Type = ParameterType.Integer, Description = "Result column" }
                ]
            },
            new SnippetItem
            {
                Insert = "vlookup_ne(§; §; §; §)",
                Description = "Values from column j2 where column j1 != x",
                Documentation = "Returns `M[k; j2]` for each `k` where `M[k; j1] ≠ x`.",
                Category = "Functions/Matrix/Data",
                KeywordType = "Function",
                ReturnTypeDescription = "Vector",
                Parameters =
                [
                    new SnippetParameter { Name = "M", Type = ParameterType.Matrix, Description = "Matrix" },
                    new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Lookup value" },
                    new SnippetParameter { Name = "j1", Type = ParameterType.Integer, Description = "Lookup column" },
                    new SnippetParameter { Name = "j2", Type = ParameterType.Integer, Description = "Result column" }
                ]
            },
            new SnippetItem
            {
                Insert = "vlookup_lt(§; §; §; §)",
                Description = "Values from column j2 where column j1 < x",
                Documentation = "Returns `M[k; j2]` for each `k` where `M[k; j1] < x`.",
                Category = "Functions/Matrix/Data",
                KeywordType = "Function",
                ReturnTypeDescription = "Vector",
                Parameters =
                [
                    new SnippetParameter { Name = "M", Type = ParameterType.Matrix, Description = "Matrix" },
                    new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Lookup value" },
                    new SnippetParameter { Name = "j1", Type = ParameterType.Integer, Description = "Lookup column" },
                    new SnippetParameter { Name = "j2", Type = ParameterType.Integer, Description = "Result column" }
                ]
            },
            new SnippetItem
            {
                Insert = "vlookup_le(§; §; §; §)",
                Description = "Values from column j2 where column j1 <= x",
                Documentation = "Returns `M[k; j2]` for each `k` where `M[k; j1] ≤ x`.",
                Category = "Functions/Matrix/Data",
                KeywordType = "Function",
                ReturnTypeDescription = "Vector",
                Parameters =
                [
                    new SnippetParameter { Name = "M", Type = ParameterType.Matrix, Description = "Matrix" },
                    new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Lookup value" },
                    new SnippetParameter { Name = "j1", Type = ParameterType.Integer, Description = "Lookup column" },
                    new SnippetParameter { Name = "j2", Type = ParameterType.Integer, Description = "Result column" }
                ]
            },
            new SnippetItem
            {
                Insert = "vlookup_gt(§; §; §; §)",
                Description = "Values from column j2 where column j1 > x",
                Documentation = "Returns `M[k; j2]` for each `k` where `M[k; j1] > x`.",
                Category = "Functions/Matrix/Data",
                KeywordType = "Function",
                ReturnTypeDescription = "Vector",
                Parameters =
                [
                    new SnippetParameter { Name = "M", Type = ParameterType.Matrix, Description = "Matrix" },
                    new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Lookup value" },
                    new SnippetParameter { Name = "j1", Type = ParameterType.Integer, Description = "Lookup column" },
                    new SnippetParameter { Name = "j2", Type = ParameterType.Integer, Description = "Result column" }
                ]
            },
            new SnippetItem
            {
                Insert = "vlookup_ge(§; §; §; §)",
                Description = "Values from column j2 where column j1 >= x",
                Documentation = "Returns `M[k; j2]` for each `k` where `M[k; j1] ≥ x`.",
                Category = "Functions/Matrix/Data",
                KeywordType = "Function",
                ReturnTypeDescription = "Vector",
                Parameters =
                [
                    new SnippetParameter { Name = "M", Type = ParameterType.Matrix, Description = "Matrix" },
                    new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Lookup value" },
                    new SnippetParameter { Name = "j1", Type = ParameterType.Integer, Description = "Lookup column" },
                    new SnippetParameter { Name = "j2", Type = ParameterType.Integer, Description = "Result column" }
                ]
            },

            // ============================================
            // MATRIX MATH FUNCTIONS
            // ============================================
            new SnippetItem
            {
                Insert = "hprod(§; §)",
                Description = "Hadamard (element-wise) product of matrices A and B",
                Documentation = "Returns the element-wise product `Cᵢⱼ = Aᵢⱼ · Bᵢⱼ`. `A` and `B` must have the same shape. A vector argument is treated as an n×1 column matrix.",
                Category = "Functions/Matrix/Math",
                KeywordType = "Function",
                ReturnTypeDescription = "Matrix (same shape as A)",
                Parameters =
                [
                    new SnippetParameter { Name = "A", Type = ParameterType.Matrix, Description = "First matrix" },
                    new SnippetParameter { Name = "B", Type = ParameterType.Matrix, Description = "Second matrix" }
                ]
            },
            new SnippetItem
            {
                Insert = "fprod(§; §)",
                Description = "Frobenius product of matrices A and B",
                Documentation = "Returns `Σ Aᵢⱼ · Bᵢⱼ` — the matrix analog of the dot product. Requires matching shapes.",
                Category = "Functions/Matrix/Math",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar",
                Parameters =
                [
                    new SnippetParameter { Name = "A", Type = ParameterType.Matrix, Description = "First matrix" },
                    new SnippetParameter { Name = "B", Type = ParameterType.Matrix, Description = "Second matrix" }
                ]
            },
            new SnippetItem
            {
                Insert = "kprod(§; §)",
                Description = "Kronecker product of matrices A and B",
                Documentation = "Returns the block-tensor product `A ⊗ B`. If `A` is `m × n` and `B` is `p × q`, the result is `(m·p) × (n·q)`.",
                Category = "Functions/Matrix/Math",
                KeywordType = "Function",
                ReturnTypeDescription = "Matrix (m·p) × (n·q)",
                Parameters =
                [
                    new SnippetParameter { Name = "A", Type = ParameterType.Matrix, Description = "First matrix" },
                    new SnippetParameter { Name = "B", Type = ParameterType.Matrix, Description = "Second matrix" }
                ]
            },

            // Matrix Norms
            new SnippetItem
            {
                Insert = "mnorm(§)",
                Description = "L2 norm of matrix M",
                Documentation = "Returns the spectral (largest singular value) norm. Same as `mnorm_2`.",
                Category = "Functions/Matrix/Math",
                KeywordType = "Function",
                ReturnTypeDescription = "Non-negative scalar",
                Parameters = [new SnippetParameter { Name = "M", Type = ParameterType.Matrix, Description = "Matrix" }]
            },
            new SnippetItem
            {
                Insert = "mnorm_1(§)",
                Description = "L1 norm of matrix M",
                Documentation = "Returns the maximum absolute column sum: `max_j Σᵢ |Mᵢⱼ|`.",
                Category = "Functions/Matrix/Math",
                KeywordType = "Function",
                ReturnTypeDescription = "Non-negative scalar",
                Parameters = [new SnippetParameter { Name = "M", Type = ParameterType.Matrix, Description = "Matrix" }]
            },
            new SnippetItem
            {
                Insert = "mnorm_2(§)",
                Description = "L2 norm of matrix M",
                Documentation = "Returns the spectral norm — the largest singular value of `M`.",
                Category = "Functions/Matrix/Math",
                KeywordType = "Function",
                ReturnTypeDescription = "Non-negative scalar",
                Parameters = [new SnippetParameter { Name = "M", Type = ParameterType.Matrix, Description = "Matrix" }]
            },
            new SnippetItem
            {
                Insert = "mnorm_e(§)",
                Description = "Frobenius norm of matrix M",
                Documentation = "Returns `sqrt(Σ Mᵢⱼ²)` — the entrywise L2 norm.",
                Category = "Functions/Matrix/Math",
                KeywordType = "Function",
                ReturnTypeDescription = "Non-negative scalar",
                Parameters = [new SnippetParameter { Name = "M", Type = ParameterType.Matrix, Description = "Matrix" }]
            },
            new SnippetItem
            {
                Insert = "mnorm_i(§)",
                Description = "L-infinity norm of matrix M",
                Documentation = "Returns the maximum absolute row sum: `max_i Σⱼ |Mᵢⱼ|`.",
                Category = "Functions/Matrix/Math",
                KeywordType = "Function",
                ReturnTypeDescription = "Non-negative scalar",
                Parameters = [new SnippetParameter { Name = "M", Type = ParameterType.Matrix, Description = "Matrix" }]
            },

            // Condition Numbers
            new SnippetItem
            {
                Insert = "cond(§)",
                Description = "Condition number based on L2 norm",
                Documentation = "Returns `‖M‖₂ · ‖M⁻¹‖₂`. A large condition number indicates an ill-conditioned matrix where small input perturbations cause large output changes. Same as `cond_2`.",
                Category = "Functions/Matrix/Math",
                KeywordType = "Function",
                ReturnTypeDescription = "Non-negative scalar (≥ 1)",
                Parameters = [new SnippetParameter { Name = "M", Type = ParameterType.Matrix, Description = "Matrix" }]
            },
            new SnippetItem
            {
                Insert = "cond_1(§)",
                Description = "Condition number based on L1 norm",
                Documentation = "Returns `‖M‖₁ · ‖M⁻¹‖₁`.",
                Category = "Functions/Matrix/Math",
                KeywordType = "Function",
                ReturnTypeDescription = "Non-negative scalar (≥ 1)",
                Parameters = [new SnippetParameter { Name = "M", Type = ParameterType.Matrix, Description = "Matrix" }]
            },
            new SnippetItem
            {
                Insert = "cond_2(§)",
                Description = "Condition number based on L2 norm",
                Documentation = "Returns `‖M‖₂ · ‖M⁻¹‖₂` — the ratio of the largest to smallest singular value.",
                Category = "Functions/Matrix/Math",
                KeywordType = "Function",
                ReturnTypeDescription = "Non-negative scalar (≥ 1)",
                Parameters = [new SnippetParameter { Name = "M", Type = ParameterType.Matrix, Description = "Matrix" }]
            },
            new SnippetItem
            {
                Insert = "cond_e(§)",
                Description = "Condition number based on Frobenius norm",
                Documentation = "Returns `‖M‖_F · ‖M⁻¹‖_F`.",
                Category = "Functions/Matrix/Math",
                KeywordType = "Function",
                ReturnTypeDescription = "Non-negative scalar (≥ 1)",
                Parameters = [new SnippetParameter { Name = "M", Type = ParameterType.Matrix, Description = "Matrix" }]
            },
            new SnippetItem
            {
                Insert = "cond_i(§)",
                Description = "Condition number based on L-infinity norm",
                Documentation = "Returns `‖M‖_∞ · ‖M⁻¹‖_∞`.",
                Category = "Functions/Matrix/Math",
                KeywordType = "Function",
                ReturnTypeDescription = "Non-negative scalar (≥ 1)",
                Parameters = [new SnippetParameter { Name = "M", Type = ParameterType.Matrix, Description = "Matrix" }]
            },

            // Matrix Properties
            new SnippetItem
            {
                Insert = "det(§)",
                Description = "Determinant of matrix M",
                Documentation = "Computes `det(M)`. `M` must be square. Returns 0 for singular matrices (within floating-point tolerance).",
                Category = "Functions/Matrix/Math",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar",
                Parameters = [new SnippetParameter { Name = "M", Type = ParameterType.Matrix, Description = "Square matrix" }]
            },
            new SnippetItem
            {
                Insert = "rank(§)",
                Description = "Rank of matrix M",
                Documentation = "Returns the rank of `M` — the number of linearly independent rows/columns. Computed via SVD.",
                Category = "Functions/Matrix/Math",
                KeywordType = "Function",
                ReturnTypeDescription = "Non-negative integer",
                Parameters = [new SnippetParameter { Name = "M", Type = ParameterType.Matrix, Description = "Matrix" }]
            },
            new SnippetItem
            {
                Insert = "trace(§)",
                Description = "Trace of matrix M (sum of diagonal elements)",
                Documentation = "Returns `Σ Mᵢᵢ`. `M` must be square.",
                Category = "Functions/Matrix/Math",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar",
                Parameters = [new SnippetParameter { Name = "M", Type = ParameterType.Matrix, Description = "Square matrix" }]
            },
            new SnippetItem
            {
                Insert = "transp(§)",
                Description = "Transpose of matrix M",
                Documentation = "Returns `M^T` — rows become columns and vice versa. An `m × n` matrix becomes `n × m`. A vector is treated as an n×1 column matrix, so its transpose is a 1×n row matrix.",
                Category = "Functions/Matrix/Math",
                KeywordType = "Function",
                ReturnTypeDescription = "Matrix n × m",
                Parameters = [new SnippetParameter { Name = "M", Type = ParameterType.Matrix, Description = "Matrix" }]
            },
            new SnippetItem
            {
                Insert = "adj(§)",
                Description = "Adjugate of matrix M",
                Documentation = "Returns `adj(M)` — the transpose of the cofactor matrix. Satisfies `M · adj(M) = det(M) · I`.",
                Category = "Functions/Matrix/Math",
                KeywordType = "Function",
                ReturnTypeDescription = "Square matrix (same size as M)",
                Parameters = [new SnippetParameter { Name = "M", Type = ParameterType.Matrix, Description = "Square matrix" }]
            },
            new SnippetItem
            {
                Insert = "cofactor(§)",
                Description = "Cofactor matrix of M",
                Documentation = "Returns the matrix of signed minors of `M`. The transpose of this is the adjugate (`adj`).",
                Category = "Functions/Matrix/Math",
                KeywordType = "Function",
                ReturnTypeDescription = "Square matrix (same size as M)",
                Parameters = [new SnippetParameter { Name = "M", Type = ParameterType.Matrix, Description = "Square matrix" }]
            },

            // Eigenvalues and Eigenvectors
            new SnippetItem
            {
                Insert = "eigenvals(§; §)",
                Description = "First ne eigenvalues of matrix M (or all if omitted)",
                Documentation = "Returns the `ne` largest-magnitude eigenvalues of `M` as a vector. If `ne` is omitted, returns all eigenvalues. May return complex values.",
                Category = "Functions/Matrix/Math",
                KeywordType = "Function",
                ReturnTypeDescription = "Vector of (possibly complex) eigenvalues",
                Parameters =
                [
                    new SnippetParameter { Name = "M", Type = ParameterType.Matrix, Description = "Square matrix" },
                    new SnippetParameter { Name = "ne", Type = ParameterType.Integer, Description = "Number of eigenvalues", IsOptional = true }
                ]
            },
            new SnippetItem
            {
                Insert = "eigenvecs(§; §)",
                Description = "First ne eigenvectors of matrix M (or all if omitted)",
                Documentation = "Returns a matrix whose columns are the eigenvectors corresponding to the `ne` largest-magnitude eigenvalues.",
                Category = "Functions/Matrix/Math",
                KeywordType = "Function",
                ReturnTypeDescription = "Matrix with eigenvectors as columns",
                Parameters =
                [
                    new SnippetParameter { Name = "M", Type = ParameterType.Matrix, Description = "Square matrix" },
                    new SnippetParameter { Name = "ne", Type = ParameterType.Integer, Description = "Number of eigenvectors", IsOptional = true }
                ]
            },
            new SnippetItem
            {
                Insert = "eigen(§; §)",
                Description = "First ne eigenvalues and eigenvectors (or all if omitted)",
                Documentation = "Returns a single matrix combining eigenvalues (top row) and corresponding eigenvectors (subsequent rows). Cheaper than calling `eigenvals` and `eigenvecs` separately.",
                Category = "Functions/Matrix/Math",
                KeywordType = "Function",
                ReturnTypeDescription = "Matrix (eigenvalues + eigenvectors)",
                Parameters =
                [
                    new SnippetParameter { Name = "M", Type = ParameterType.Matrix, Description = "Square matrix" },
                    new SnippetParameter { Name = "ne", Type = ParameterType.Integer, Description = "Number", IsOptional = true }
                ]
            },

            // Matrix Decompositions
            new SnippetItem
            {
                Insert = "cholesky(§)",
                Description = "Cholesky decomposition of symmetric positive-definite matrix",
                Documentation = "Returns the lower-triangular factor `L` such that `M = L · L^T`. `M` must be symmetric and positive-definite, otherwise an error is raised.",
                Category = "Functions/Matrix/Math",
                KeywordType = "Function",
                ReturnTypeDescription = "Lower-triangular matrix",
                Parameters = [new SnippetParameter { Name = "M", Type = ParameterType.Matrix, Description = "Symmetric positive-definite matrix" }]
            },
            new SnippetItem
            {
                Insert = "lu(§)",
                Description = "LU decomposition of matrix M",
                Documentation = "Returns the combined `L` (below diagonal) + `U` (above and on diagonal) factors with partial pivoting.",
                Category = "Functions/Matrix/Math",
                KeywordType = "Function",
                ReturnTypeDescription = "Combined L+U matrix",
                Parameters = [new SnippetParameter { Name = "M", Type = ParameterType.Matrix, Description = "Square matrix" }]
            },
            new SnippetItem
            {
                Insert = "qr(§)",
                Description = "QR decomposition of matrix M",
                Documentation = "Returns the combined Q + R factors. `Q` is orthogonal, `R` is upper-triangular, and `M = Q · R`.",
                Category = "Functions/Matrix/Math",
                KeywordType = "Function",
                ReturnTypeDescription = "Combined Q+R matrix",
                Parameters = [new SnippetParameter { Name = "M", Type = ParameterType.Matrix, Description = "Matrix" }]
            },
            new SnippetItem
            {
                Insert = "svd(§)",
                Description = "Singular value decomposition of M",
                Documentation = "Returns `U`, `Σ`, and `Vᵀ` packed into a single matrix such that `M = U · Σ · Vᵀ`. Singular values are in descending order on the diagonal of `Σ`.",
                Category = "Functions/Matrix/Math",
                KeywordType = "Function",
                ReturnTypeDescription = "Combined U, Σ, V^T matrix",
                Parameters = [new SnippetParameter { Name = "M", Type = ParameterType.Matrix, Description = "Matrix" }]
            },

            // Linear Solvers
            new SnippetItem
            {
                Insert = "inverse(§)",
                Description = "Inverse of matrix M",
                Documentation = "Returns `M⁻¹` such that `M · M⁻¹ = I`. Errors for singular matrices. For solving `Ax = b`, prefer `lsolve(A; b)` over `inverse(A) · b`.",
                Category = "Functions/Matrix/Math",
                KeywordType = "Function",
                ReturnTypeDescription = "Square matrix (same size as M)",
                Parameters = [new SnippetParameter { Name = "M", Type = ParameterType.Matrix, Description = "Square matrix" }]
            },
            new SnippetItem
            {
                Insert = "lsolve(§; §)",
                Description = "Solves Ax = b using LDLT (symmetric) or LU decomposition",
                Documentation = "Solves the linear system `A · x = b` for `x`. Automatically picks LDLT for symmetric `A` and LU otherwise.",
                Example = "x = lsolve(A; b)",
                Category = "Functions/Matrix/Math",
                KeywordType = "Function",
                ReturnTypeDescription = "Vector x",
                Parameters =
                [
                    new SnippetParameter { Name = "A", Type = ParameterType.Matrix, Description = "Coefficient matrix" },
                    new SnippetParameter { Name = "b", Type = ParameterType.Vector, Description = "Right-hand side" }
                ]
            },
            new SnippetItem
            {
                Insert = "clsolve(§; §)",
                Description = "Solves Ax = b using Cholesky decomposition",
                Documentation = "Solves `A · x = b` using Cholesky factorization. Faster than `lsolve` but requires `A` to be symmetric and positive-definite.",
                Category = "Functions/Matrix/Math",
                KeywordType = "Function",
                ReturnTypeDescription = "Vector x",
                Parameters =
                [
                    new SnippetParameter { Name = "A", Type = ParameterType.Matrix, Description = "Symmetric positive-definite matrix" },
                    new SnippetParameter { Name = "b", Type = ParameterType.Vector, Description = "Right-hand side" }
                ]
            },
            new SnippetItem
            {
                Insert = "slsolve(§; §)",
                Description = "Solves Ax = b using preconditioned conjugate gradient",
                Documentation = "Iterative solver for very large symmetric positive-definite systems. `A` should be in the high-performance representation.",
                Category = "Functions/Matrix/Math",
                KeywordType = "Function",
                ReturnTypeDescription = "Vector x",
                Parameters =
                [
                    new SnippetParameter { Name = "A", Type = ParameterType.Matrix, Description = "HP symmetric positive-definite matrix" },
                    new SnippetParameter { Name = "b", Type = ParameterType.Vector, Description = "Right-hand side" }
                ]
            },
            new SnippetItem
            {
                Insert = "msolve(§; §)",
                Description = "Solves AX = B using LDLT or LU decomposition",
                Documentation = "Solves the matrix system `A · X = B` for `X` (multiple right-hand sides packed as columns of `B`).",
                Category = "Functions/Matrix/Math",
                KeywordType = "Function",
                ReturnTypeDescription = "Matrix X (same shape as B)",
                Parameters =
                [
                    new SnippetParameter { Name = "A", Type = ParameterType.Matrix, Description = "Coefficient matrix" },
                    new SnippetParameter { Name = "B", Type = ParameterType.Matrix, Description = "Right-hand side matrix" }
                ]
            },
            new SnippetItem
            {
                Insert = "cmsolve(§; §)",
                Description = "Solves AX = B using Cholesky decomposition",
                Documentation = "Cholesky-based variant of `msolve`. `A` must be symmetric positive-definite.",
                Category = "Functions/Matrix/Math",
                KeywordType = "Function",
                ReturnTypeDescription = "Matrix X (same shape as B)",
                Parameters =
                [
                    new SnippetParameter { Name = "A", Type = ParameterType.Matrix, Description = "Symmetric positive-definite matrix" },
                    new SnippetParameter { Name = "B", Type = ParameterType.Matrix, Description = "Right-hand side matrix" }
                ]
            },
            new SnippetItem
            {
                Insert = "smsolve(§; §)",
                Description = "Solves AX = B using preconditioned conjugate gradient",
                Documentation = "Iterative matrix-system solver. `A` should be in the HP representation; suitable for very large symmetric positive-definite systems.",
                Category = "Functions/Matrix/Math",
                KeywordType = "Function",
                ReturnTypeDescription = "Matrix X (same shape as B)",
                Parameters =
                [
                    new SnippetParameter { Name = "A", Type = ParameterType.Matrix, Description = "HP symmetric positive-definite matrix" },
                    new SnippetParameter { Name = "B", Type = ParameterType.Matrix, Description = "Right-hand side matrix" }
                ]
            },

            // FFT
            new SnippetItem
            {
                Insert = "fft(§)",
                Description = "Fast Fourier transform (1 row for real, 2 for complex)",
                Documentation = "Computes the discrete Fourier transform. Pass a 1-row matrix for real input or a 2-row matrix (real, imaginary) for complex input. Output is always a 2-row complex matrix.",
                Category = "Functions/Matrix/Math",
                KeywordType = "Function",
                ReturnTypeDescription = "2-row complex matrix",
                Parameters = [new SnippetParameter { Name = "M", Type = ParameterType.Matrix, Description = "Row-major matrix" }]
            },
            new SnippetItem
            {
                Insert = "ift(§)",
                Description = "Inverse Fourier transform (1 row for real, 2 for complex)",
                Documentation = "Computes the inverse discrete Fourier transform. Inverse of `fft`.",
                Category = "Functions/Matrix/Math",
                KeywordType = "Function",
                ReturnTypeDescription = "Complex matrix",
                Parameters = [new SnippetParameter { Name = "M", Type = ParameterType.Matrix, Description = "Row-major matrix" }]
            },

            // Double Interpolation
            new SnippetItem
            {
                Insert = "take(§; §; §)",
                Description = "Returns element of matrix M at indexes x and y",
                Documentation = "Returns `M[x; y]` (1-based). Equivalent to `M[x; y]` in expression syntax.",
                Category = "Functions/Matrix/Math",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar",
                Parameters =
                [
                    new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Row index" },
                    new SnippetParameter { Name = "y", Type = ParameterType.Scalar, Description = "Column index" },
                    new SnippetParameter { Name = "M", Type = ParameterType.Matrix, Description = "Matrix" }
                ]
            },
            new SnippetItem
            {
                Insert = "line(§; §; §)",
                Description = "Double linear interpolation from matrix M",
                Documentation = "Performs bilinear interpolation at fractional indices `(x, y)` using the values of `M`. Useful for 2-D table lookups.",
                Category = "Functions/Matrix/Math",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar",
                Parameters =
                [
                    new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "X coordinate" },
                    new SnippetParameter { Name = "y", Type = ParameterType.Scalar, Description = "Y coordinate" },
                    new SnippetParameter { Name = "M", Type = ParameterType.Matrix, Description = "Matrix" }
                ]
            },
            new SnippetItem
            {
                Insert = "spline(§; §; §)",
                Description = "Double Hermite spline interpolation from matrix M",
                Documentation = "Performs smooth bicubic Hermite spline interpolation at `(x, y)` using `M` as the data table. Smoother than `line` but more expensive.",
                Category = "Functions/Matrix/Math",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar",
                Parameters =
                [
                    new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "X coordinate" },
                    new SnippetParameter { Name = "y", Type = ParameterType.Scalar, Description = "Y coordinate" },
                    new SnippetParameter { Name = "M", Type = ParameterType.Matrix, Description = "Matrix" }
                ]
            }
        ];
    }
}
