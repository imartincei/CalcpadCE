using Calcpad.Highlighter.Linter.Models;
using Calcpad.Highlighter.Snippets.Models;

namespace Calcpad.Highlighter.Snippets.Data
{
    /// <summary>
    /// Snippet definitions for vector functions.
    /// All functions in this file return vectors unless otherwise noted.
    /// </summary>
    public static class VectorFunctionSnippets
    {
        public static readonly SnippetItem[] Items =
        [
            // ============================================
            // VECTOR CREATIONAL FUNCTIONS
            // ============================================
            new SnippetItem
            {
                Insert = "vector(§)",
                Description = "Creates an empty vector with length n",
                Documentation = "Allocates a new vector of length `n` with all elements initialized to 0. Use indexed assignment to populate it.",
                Example = "v = vector(5)  ' [0; 0; 0; 0; 0]",
                Category = "Functions/Vector/Creational",
                KeywordType = "Function",
                ReturnType = CalcpadType.Vector,
                ReturnTypeDescription = "Vector of length n",
                Parameters = [new SnippetParameter { Name = "n", Type = ParameterType.Integer, Description = "Length of vector" }]
            },
            new SnippetItem
            {
                Insert = "vector_hp(§)",
                Description = "Creates an empty high-performance vector with length n",
                Documentation = "Like `vector(n)` but uses the high-performance representation (single-precision, contiguous storage). See `hp` for tradeoffs.",
                Category = "Functions/Vector/Creational",
                KeywordType = "Function",
                ReturnType = CalcpadType.Vector,
                ReturnTypeDescription = "High-performance vector of length n",
                Parameters = [new SnippetParameter { Name = "n", Type = ParameterType.Integer, Description = "Length of vector" }]
            },
            new SnippetItem
            {
                Insert = "range(§; §; §)",
                Description = "Creates a vector with values from x1 to xn with step s",
                Documentation = "Generates a sequence starting at `x1`, ending at or before `xn`, with step `s` (defaults to 1). Step may be negative for descending sequences.",
                Example = "range(0; 10; 2)  ' [0; 2; 4; 6; 8; 10]",
                Category = "Functions/Vector/Creational",
                KeywordType = "Function",
                ReturnType = CalcpadType.Vector,
                ReturnTypeDescription = "Vector",
                Parameters =
                [
                    new SnippetParameter { Name = "x1", Type = ParameterType.Scalar, Description = "Start value" },
                    new SnippetParameter { Name = "xn", Type = ParameterType.Scalar, Description = "End value" },
                    new SnippetParameter { Name = "s", Type = ParameterType.Scalar, Description = "Step", IsOptional = true }
                ]
            },
            new SnippetItem
            {
                Insert = "range_hp(§; §; §)",
                Description = "Creates a high-performance vector from a range",
                Documentation = "Like `range(x1; xn; s)` but returns the high-performance vector representation.",
                Category = "Functions/Vector/Creational",
                KeywordType = "Function",
                ReturnType = CalcpadType.Vector,
                ReturnTypeDescription = "High-performance vector",
                Parameters =
                [
                    new SnippetParameter { Name = "x1", Type = ParameterType.Scalar, Description = "Start value" },
                    new SnippetParameter { Name = "xn", Type = ParameterType.Scalar, Description = "End value" },
                    new SnippetParameter { Name = "s", Type = ParameterType.Scalar, Description = "Step", IsOptional = true }
                ]
            },

            // ============================================
            // VECTOR STRUCTURAL FUNCTIONS
            // ============================================
            new SnippetItem
            {
                Insert = "len(§)",
                Description = "Returns the length of the vector",
                Documentation = "Returns the allocated length of `v` (number of element slots). For the index of the last non-zero element, use `size`.",
                Category = "Functions/Vector/Structural",
                KeywordType = "Function",
                ReturnTypeDescription = "Non-negative integer",
                Parameters = [new SnippetParameter { Name = "v", Type = ParameterType.Vector, Description = "Vector" }]
            },
            new SnippetItem
            {
                Insert = "size(§)",
                Description = "Returns the actual size (index of last non-zero element)",
                Documentation = "Returns the index (1-based) of the last non-zero element of `v`. For a fully populated vector this equals `len(v)`. Useful for trimming trailing zeros.",
                Category = "Functions/Vector/Structural",
                KeywordType = "Function",
                ReturnTypeDescription = "Non-negative integer",
                Parameters = [new SnippetParameter { Name = "v", Type = ParameterType.Vector, Description = "Vector" }]
            },
            new SnippetItem
            {
                Insert = "resize(§; §)",
                Description = "Sets a new length for the vector",
                Documentation = "Returns a copy of `v` with length `n`. Truncates if `n < len(v)`, pads with zeros if `n > len(v)`.",
                Category = "Functions/Vector/Structural",
                KeywordType = "Function",
                ReturnType = CalcpadType.Vector,
                ReturnTypeDescription = "Vector of length n",
                Parameters =
                [
                    new SnippetParameter { Name = "v", Type = ParameterType.Vector, Description = "Vector" },
                    new SnippetParameter { Name = "n", Type = ParameterType.Integer, Description = "New length" }
                ]
            },
            new SnippetItem
            {
                Insert = "fill(§; §)",
                Description = "Fills the vector with a value",
                Documentation = "Returns a copy of `v` with every element replaced by `x`. Length is preserved.",
                Category = "Functions/Vector/Structural",
                KeywordType = "Function",
                ReturnType = CalcpadType.Vector,
                ReturnTypeDescription = "Vector",
                Parameters =
                [
                    new SnippetParameter { Name = "v", Type = ParameterType.Vector, Description = "Vector" },
                    new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Fill value" }
                ]
            },
            new SnippetItem
            {
                Insert = "join(§)",
                Description = "Creates a vector by joining matrices, vectors, and scalars",
                Documentation = "Concatenates all arguments into a single vector. Matrices are flattened in row-major order; scalars are appended as single elements.",
                Example = "join([1; 2]; 3; [4; 5])  ' [1; 2; 3; 4; 5]",
                Category = "Functions/Vector/Structural",
                KeywordType = "Function",
                ReturnType = CalcpadType.Vector,
                ReturnTypeDescription = "Vector",
                AcceptsAnyCount = true,
                Parameters = [new SnippetParameter { Name = "items", Type = ParameterType.Various, Description = "Matrices, vectors, and scalars to join", IsVariadic = true }]
            },
            new SnippetItem
            {
                Insert = "slice(§; §; §)",
                Description = "Returns part of vector bounded by indexes i1 and i2",
                Documentation = "Returns the sub-vector `v[i1..i2]` (1-based, inclusive). Errors if indices are out of range or `i1 > i2`.",
                Category = "Functions/Vector/Structural",
                KeywordType = "Function",
                ReturnType = CalcpadType.Vector,
                ReturnTypeDescription = "Vector of length i2 - i1 + 1",
                Parameters =
                [
                    new SnippetParameter { Name = "v", Type = ParameterType.Vector, Description = "Vector" },
                    new SnippetParameter { Name = "i1", Type = ParameterType.Integer, Description = "Start index" },
                    new SnippetParameter { Name = "i2", Type = ParameterType.Integer, Description = "End index" }
                ]
            },
            new SnippetItem
            {
                Insert = "first(§; §)",
                Description = "Returns the first n elements of the vector",
                Documentation = "Equivalent to `slice(v; 1; n)`. Returns the leading `n` elements of `v`.",
                Category = "Functions/Vector/Structural",
                KeywordType = "Function",
                ReturnType = CalcpadType.Vector,
                ReturnTypeDescription = "Vector of length n",
                Parameters =
                [
                    new SnippetParameter { Name = "v", Type = ParameterType.Vector, Description = "Vector" },
                    new SnippetParameter { Name = "n", Type = ParameterType.Integer, Description = "Number of elements" }
                ]
            },
            new SnippetItem
            {
                Insert = "last(§; §)",
                Description = "Returns the last n elements of the vector",
                Documentation = "Returns the trailing `n` elements of `v`, in their original order.",
                Category = "Functions/Vector/Structural",
                KeywordType = "Function",
                ReturnType = CalcpadType.Vector,
                ReturnTypeDescription = "Vector of length n",
                Parameters =
                [
                    new SnippetParameter { Name = "v", Type = ParameterType.Vector, Description = "Vector" },
                    new SnippetParameter { Name = "n", Type = ParameterType.Integer, Description = "Number of elements" }
                ]
            },
            new SnippetItem
            {
                Insert = "extract(§; §)",
                Description = "Extracts elements from v whose indexes are in i",
                Documentation = "Returns a new vector containing `v[i₁], v[i₂], …` in the order given by `i`. Indices may repeat to duplicate elements.",
                Example = "extract([10; 20; 30; 40]; [1; 3])  ' [10; 30]",
                Category = "Functions/Vector/Structural",
                KeywordType = "Function",
                ReturnType = CalcpadType.Vector,
                ReturnTypeDescription = "Vector of length len(i)",
                Parameters =
                [
                    new SnippetParameter { Name = "v", Type = ParameterType.Vector, Description = "Source vector" },
                    new SnippetParameter { Name = "i", Type = ParameterType.Vector, Description = "Index vector" }
                ]
            },

            // ============================================
            // VECTOR DATA FUNCTIONS
            // ============================================
            new SnippetItem
            {
                Insert = "sort(§)",
                Description = "Sorts the vector in ascending order",
                Documentation = "Returns a copy of `v` with elements sorted in ascending order. For descending order use `rsort`. To get the sort permutation use `order`.",
                Category = "Functions/Vector/Data",
                KeywordType = "Function",
                ReturnType = CalcpadType.Vector,
                ReturnTypeDescription = "Vector (sorted)",
                Parameters = [new SnippetParameter { Name = "v", Type = ParameterType.Vector, Description = "Vector to sort" }]
            },
            new SnippetItem
            {
                Insert = "rsort(§)",
                Description = "Sorts the vector in descending order",
                Documentation = "Returns a copy of `v` with elements sorted in descending order.",
                Category = "Functions/Vector/Data",
                KeywordType = "Function",
                ReturnType = CalcpadType.Vector,
                ReturnTypeDescription = "Vector (sorted)",
                Parameters = [new SnippetParameter { Name = "v", Type = ParameterType.Vector, Description = "Vector to sort" }]
            },
            new SnippetItem
            {
                Insert = "order(§)",
                Description = "Returns indexes in ascending order by element values",
                Documentation = "Returns the permutation `p` such that `v[p₁] ≤ v[p₂] ≤ …`. Use with `extract(v; order(v))` to sort while keeping a parallel vector aligned.",
                Category = "Functions/Vector/Data",
                KeywordType = "Function",
                ReturnType = CalcpadType.Vector,
                ReturnTypeDescription = "Vector of indices",
                Parameters = [new SnippetParameter { Name = "v", Type = ParameterType.Vector, Description = "Vector" }]
            },
            new SnippetItem
            {
                Insert = "revorder(§)",
                Description = "Returns indexes in descending order by element values",
                Documentation = "Returns the permutation that sorts `v` in descending order. Inverse-direction counterpart to `order`.",
                Category = "Functions/Vector/Data",
                KeywordType = "Function",
                ReturnType = CalcpadType.Vector,
                ReturnTypeDescription = "Vector of indices",
                Parameters = [new SnippetParameter { Name = "v", Type = ParameterType.Vector, Description = "Vector" }]
            },
            new SnippetItem
            {
                Insert = "reverse(§)",
                Description = "Returns vector with elements in reverse order",
                Documentation = "Returns `v` with element order flipped. Length is preserved.",
                Category = "Functions/Vector/Data",
                KeywordType = "Function",
                ReturnType = CalcpadType.Vector,
                ReturnTypeDescription = "Vector",
                Parameters = [new SnippetParameter { Name = "v", Type = ParameterType.Vector, Description = "Vector" }]
            },
            new SnippetItem
            {
                Insert = "count(§; §; §)",
                Description = "Counts elements equal to x starting from index i",
                Documentation = "Counts how many elements of `v` (from index `i` to the end) equal `x`.",
                Category = "Functions/Vector/Data",
                KeywordType = "Function",
                ReturnTypeDescription = "Non-negative integer",
                Parameters =
                [
                    new SnippetParameter { Name = "v", Type = ParameterType.Vector, Description = "Vector" },
                    new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Value to count" },
                    new SnippetParameter { Name = "i", Type = ParameterType.Integer, Description = "Start index" }
                ]
            },
            new SnippetItem
            {
                Insert = "search(§; §; §)",
                Description = "Returns index of first element equal to x starting from i",
                Documentation = "Linear search. Returns the 1-based index of the first occurrence of `x` in `v` at or after position `i`, or 0 if not found.",
                Category = "Functions/Vector/Data",
                KeywordType = "Function",
                ReturnTypeDescription = "Non-negative integer (0 if not found)",
                Parameters =
                [
                    new SnippetParameter { Name = "v", Type = ParameterType.Vector, Description = "Vector" },
                    new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Value to find" },
                    new SnippetParameter { Name = "i", Type = ParameterType.Integer, Description = "Start index" }
                ]
            },

            // Vector Find Functions - all return vectors of indices
            new SnippetItem
            {
                Insert = "find(§; §; §)",
                Description = "Returns indexes of elements equal to x after index i",
                Documentation = "Returns a vector of all 1-based indices ≥ `i` where `v[k] == x`. Empty result if none match. Same as `find_eq`.",
                Category = "Functions/Vector/Data",
                KeywordType = "Function",
                ReturnType = CalcpadType.Vector,
                ReturnTypeDescription = "Vector of indices",
                Parameters =
                [
                    new SnippetParameter { Name = "v", Type = ParameterType.Vector, Description = "Vector" },
                    new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Value" },
                    new SnippetParameter { Name = "i", Type = ParameterType.Integer, Description = "Start index" }
                ]
            },
            new SnippetItem
            {
                Insert = "find_eq(§; §; §)",
                Description = "Returns indexes of elements = x after index i",
                Documentation = "Returns indices where `v[k] == x` for `k ≥ i`. Same as `find`.",
                Category = "Functions/Vector/Data",
                KeywordType = "Function",
                ReturnType = CalcpadType.Vector,
                ReturnTypeDescription = "Vector of indices",
                Parameters =
                [
                    new SnippetParameter { Name = "v", Type = ParameterType.Vector, Description = "Vector" },
                    new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Value" },
                    new SnippetParameter { Name = "i", Type = ParameterType.Integer, Description = "Start index" }
                ]
            },
            new SnippetItem
            {
                Insert = "find_ne(§; §; §)",
                Description = "Returns indexes of elements != x after index i",
                Documentation = "Returns indices where `v[k] ≠ x` for `k ≥ i`.",
                Category = "Functions/Vector/Data",
                KeywordType = "Function",
                ReturnType = CalcpadType.Vector,
                ReturnTypeDescription = "Vector of indices",
                Parameters =
                [
                    new SnippetParameter { Name = "v", Type = ParameterType.Vector, Description = "Vector" },
                    new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Value" },
                    new SnippetParameter { Name = "i", Type = ParameterType.Integer, Description = "Start index" }
                ]
            },
            new SnippetItem
            {
                Insert = "find_lt(§; §; §)",
                Description = "Returns indexes of elements < x after index i",
                Documentation = "Returns indices where `v[k] < x` for `k ≥ i`.",
                Category = "Functions/Vector/Data",
                KeywordType = "Function",
                ReturnType = CalcpadType.Vector,
                ReturnTypeDescription = "Vector of indices",
                Parameters =
                [
                    new SnippetParameter { Name = "v", Type = ParameterType.Vector, Description = "Vector" },
                    new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Value" },
                    new SnippetParameter { Name = "i", Type = ParameterType.Integer, Description = "Start index" }
                ]
            },
            new SnippetItem
            {
                Insert = "find_le(§; §; §)",
                Description = "Returns indexes of elements <= x after index i",
                Documentation = "Returns indices where `v[k] ≤ x` for `k ≥ i`.",
                Category = "Functions/Vector/Data",
                KeywordType = "Function",
                ReturnType = CalcpadType.Vector,
                ReturnTypeDescription = "Vector of indices",
                Parameters =
                [
                    new SnippetParameter { Name = "v", Type = ParameterType.Vector, Description = "Vector" },
                    new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Value" },
                    new SnippetParameter { Name = "i", Type = ParameterType.Integer, Description = "Start index" }
                ]
            },
            new SnippetItem
            {
                Insert = "find_gt(§; §; §)",
                Description = "Returns indexes of elements > x after index i",
                Documentation = "Returns indices where `v[k] > x` for `k ≥ i`.",
                Category = "Functions/Vector/Data",
                KeywordType = "Function",
                ReturnType = CalcpadType.Vector,
                ReturnTypeDescription = "Vector of indices",
                Parameters =
                [
                    new SnippetParameter { Name = "v", Type = ParameterType.Vector, Description = "Vector" },
                    new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Value" },
                    new SnippetParameter { Name = "i", Type = ParameterType.Integer, Description = "Start index" }
                ]
            },
            new SnippetItem
            {
                Insert = "find_ge(§; §; §)",
                Description = "Returns indexes of elements >= x after index i",
                Documentation = "Returns indices where `v[k] ≥ x` for `k ≥ i`.",
                Category = "Functions/Vector/Data",
                KeywordType = "Function",
                ReturnType = CalcpadType.Vector,
                ReturnTypeDescription = "Vector of indices",
                Parameters =
                [
                    new SnippetParameter { Name = "v", Type = ParameterType.Vector, Description = "Vector" },
                    new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Value" },
                    new SnippetParameter { Name = "i", Type = ParameterType.Integer, Description = "Start index" }
                ]
            },

            // Vector Lookup Functions
            new SnippetItem
            {
                Insert = "lookup(§; §; §)",
                Description = "Elements of a where corresponding elements of b equal x",
                Documentation = "For every position `k` where `b[k] == x`, returns `a[k]`. `a` and `b` must have the same length. Same as `lookup_eq`.",
                Example = "lookup([10; 20; 30]; [1; 2; 1]; 1)  ' [10; 30]",
                Category = "Functions/Vector/Data",
                KeywordType = "Function",
                ReturnTypeDescription = "Vector of values from a",
                Parameters =
                [
                    new SnippetParameter { Name = "a", Type = ParameterType.Vector, Description = "Result vector" },
                    new SnippetParameter { Name = "b", Type = ParameterType.Vector, Description = "Lookup vector" },
                    new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Lookup value" }
                ]
            },
            new SnippetItem
            {
                Insert = "lookup_eq(§; §; §)",
                Description = "Elements of a where b elements = x",
                Documentation = "Returns `a[k]` for every `k` where `b[k] == x`. Same as `lookup`.",
                Category = "Functions/Vector/Data",
                KeywordType = "Function",
                ReturnTypeDescription = "Vector of values from a",
                Parameters =
                [
                    new SnippetParameter { Name = "a", Type = ParameterType.Vector, Description = "Result vector" },
                    new SnippetParameter { Name = "b", Type = ParameterType.Vector, Description = "Lookup vector" },
                    new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Lookup value" }
                ]
            },
            new SnippetItem
            {
                Insert = "lookup_ne(§; §; §)",
                Description = "Elements of a where b elements != x",
                Documentation = "Returns `a[k]` for every `k` where `b[k] ≠ x`.",
                Category = "Functions/Vector/Data",
                KeywordType = "Function",
                ReturnTypeDescription = "Vector of values from a",
                Parameters =
                [
                    new SnippetParameter { Name = "a", Type = ParameterType.Vector, Description = "Result vector" },
                    new SnippetParameter { Name = "b", Type = ParameterType.Vector, Description = "Lookup vector" },
                    new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Lookup value" }
                ]
            },
            new SnippetItem
            {
                Insert = "lookup_lt(§; §; §)",
                Description = "Elements of a where b elements < x",
                Documentation = "Returns `a[k]` for every `k` where `b[k] < x`.",
                Category = "Functions/Vector/Data",
                KeywordType = "Function",
                ReturnTypeDescription = "Vector of values from a",
                Parameters =
                [
                    new SnippetParameter { Name = "a", Type = ParameterType.Vector, Description = "Result vector" },
                    new SnippetParameter { Name = "b", Type = ParameterType.Vector, Description = "Lookup vector" },
                    new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Lookup value" }
                ]
            },
            new SnippetItem
            {
                Insert = "lookup_le(§; §; §)",
                Description = "Elements of a where b elements <= x",
                Documentation = "Returns `a[k]` for every `k` where `b[k] ≤ x`.",
                Category = "Functions/Vector/Data",
                KeywordType = "Function",
                ReturnTypeDescription = "Vector of values from a",
                Parameters =
                [
                    new SnippetParameter { Name = "a", Type = ParameterType.Vector, Description = "Result vector" },
                    new SnippetParameter { Name = "b", Type = ParameterType.Vector, Description = "Lookup vector" },
                    new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Lookup value" }
                ]
            },
            new SnippetItem
            {
                Insert = "lookup_gt(§; §; §)",
                Description = "Elements of a where b elements > x",
                Documentation = "Returns `a[k]` for every `k` where `b[k] > x`.",
                Category = "Functions/Vector/Data",
                KeywordType = "Function",
                ReturnTypeDescription = "Vector of values from a",
                Parameters =
                [
                    new SnippetParameter { Name = "a", Type = ParameterType.Vector, Description = "Result vector" },
                    new SnippetParameter { Name = "b", Type = ParameterType.Vector, Description = "Lookup vector" },
                    new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Lookup value" }
                ]
            },
            new SnippetItem
            {
                Insert = "lookup_ge(§; §; §)",
                Description = "Elements of a where b elements >= x",
                Documentation = "Returns `a[k]` for every `k` where `b[k] ≥ x`.",
                Category = "Functions/Vector/Data",
                KeywordType = "Function",
                ReturnTypeDescription = "Vector of values from a",
                Parameters =
                [
                    new SnippetParameter { Name = "a", Type = ParameterType.Vector, Description = "Result vector" },
                    new SnippetParameter { Name = "b", Type = ParameterType.Vector, Description = "Lookup vector" },
                    new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Lookup value" }
                ]
            },

            // ============================================
            // VECTOR MATH FUNCTIONS
            // ============================================
            new SnippetItem
            {
                Insert = "norm(§)",
                Description = "L2 (Euclidean) norm of vector",
                Documentation = "Computes `sqrt(Σ vᵢ²)`, the Euclidean (L2) norm. Same as `norm_2` and `norm_e`.",
                Category = "Functions/Vector/Math",
                KeywordType = "Function",
                ReturnTypeDescription = "Non-negative scalar",
                Parameters = [new SnippetParameter { Name = "v", Type = ParameterType.Vector, Description = "Vector" }]
            },
            new SnippetItem
            {
                Insert = "norm_1(§)",
                Description = "L1 (Manhattan) norm of vector",
                Documentation = "Computes `Σ |vᵢ|`, the L1 (taxicab) norm. Less sensitive to outliers than L2.",
                Category = "Functions/Vector/Math",
                KeywordType = "Function",
                ReturnTypeDescription = "Non-negative scalar",
                Parameters = [new SnippetParameter { Name = "v", Type = ParameterType.Vector, Description = "Vector" }]
            },
            new SnippetItem
            {
                Insert = "norm_2(§)",
                Description = "L2 (Euclidean) norm of vector",
                Documentation = "Computes `sqrt(Σ vᵢ²)`, the Euclidean norm. Alias of `norm`.",
                Category = "Functions/Vector/Math",
                KeywordType = "Function",
                ReturnTypeDescription = "Non-negative scalar",
                Parameters = [new SnippetParameter { Name = "v", Type = ParameterType.Vector, Description = "Vector" }]
            },
            new SnippetItem
            {
                Insert = "norm_e(§)",
                Description = "L2 (Euclidean) norm of vector",
                Documentation = "Alias of `norm` / `norm_2`.",
                Category = "Functions/Vector/Math",
                KeywordType = "Function",
                ReturnTypeDescription = "Non-negative scalar",
                Parameters = [new SnippetParameter { Name = "v", Type = ParameterType.Vector, Description = "Vector" }]
            },
            new SnippetItem
            {
                Insert = "norm_p(§; §)",
                Description = "Lp norm of vector",
                Documentation = "Computes `(Σ |vᵢ|^p)^(1/p)` for arbitrary `p ≥ 1`. With `p = 1` reduces to `norm_1`, with `p = 2` to `norm_2`.",
                Category = "Functions/Vector/Math",
                KeywordType = "Function",
                ReturnTypeDescription = "Non-negative scalar",
                Parameters =
                [
                    new SnippetParameter { Name = "v", Type = ParameterType.Vector, Description = "Vector" },
                    new SnippetParameter { Name = "p", Type = ParameterType.Scalar, Description = "Norm order" }
                ]
            },
            new SnippetItem
            {
                Insert = "norm_i(§)",
                Description = "L-infinity norm of vector",
                Documentation = "Returns `max(|vᵢ|)` — the largest absolute element. Limit of `norm_p` as `p → ∞`.",
                Category = "Functions/Vector/Math",
                KeywordType = "Function",
                ReturnTypeDescription = "Non-negative scalar",
                Parameters = [new SnippetParameter { Name = "v", Type = ParameterType.Vector, Description = "Vector" }]
            },
            new SnippetItem
            {
                Insert = "unit(§)",
                Description = "Normalized vector (with L2 norm = 1)",
                Documentation = "Returns `v / norm(v)`, a vector pointing in the same direction with unit length. Errors if `v` is the zero vector.",
                Category = "Functions/Vector/Math",
                KeywordType = "Function",
                ReturnType = CalcpadType.Vector,
                ReturnTypeDescription = "Unit vector (same length as v)",
                Parameters = [new SnippetParameter { Name = "v", Type = ParameterType.Vector, Description = "Vector" }]
            },
            new SnippetItem
            {
                Insert = "dot(§; §)",
                Description = "Scalar (dot) product of two vectors",
                Documentation = "Returns `Σ aᵢ · bᵢ`. Vectors must have the same length. Equals `|a| · |b| · cos(θ)` for the angle `θ` between them.",
                Example = "dot([1; 2; 3]; [4; 5; 6])  ' returns 32",
                Category = "Functions/Vector/Math",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar",
                // dot returns a scalar, not a vector
                Parameters =
                [
                    new SnippetParameter { Name = "a", Type = ParameterType.Vector, Description = "First vector" },
                    new SnippetParameter { Name = "b", Type = ParameterType.Vector, Description = "Second vector" }
                ]
            },
            new SnippetItem
            {
                Insert = "cross(§; §)",
                Description = "Cross product of two vectors (length 2 or 3)",
                Documentation = "Returns the cross product `a × b`. For 3-vectors, yields a vector perpendicular to both. For 2-vectors, returns the scalar z-component.",
                Example = "cross([1; 0; 0]; [0; 1; 0])  ' [0; 0; 1]",
                Category = "Functions/Vector/Math",
                KeywordType = "Function",
                ReturnType = CalcpadType.Vector,
                ReturnTypeDescription = "Vector (length 3) or scalar (for 2-vectors)",
                Parameters =
                [
                    new SnippetParameter { Name = "a", Type = ParameterType.Vector, Description = "First vector" },
                    new SnippetParameter { Name = "b", Type = ParameterType.Vector, Description = "Second vector" }
                ]
            }
        ];
    }
}
