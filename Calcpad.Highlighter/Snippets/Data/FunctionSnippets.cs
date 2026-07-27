using Calcpad.Highlighter.Linter.Models;
using Calcpad.Highlighter.Snippets.Models;

namespace Calcpad.Highlighter.Snippets.Data
{
    /// <summary>
    /// Snippet definitions for built-in functions.
    /// </summary>
    public static class FunctionSnippets
    {
        public static readonly SnippetItem[] Items =
        [
            // ============================================
            // TRIGONOMETRIC FUNCTIONS
            // ============================================
            new SnippetItem
            {
                Insert = "sin(§)",
                IsElementWise = true,
                Description = "Sine",
                Documentation = "Computes the sine of an angle. The angle is interpreted using the current `#deg` / `#rad` / `#gra` setting; pass an angle with explicit units (`deg`, `rad`, `gra`) to override.",
                Example = "sin(30*deg)",
                Category = "Functions/Trigonometric",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar from -1 to 1",
                Parameters = [new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Angle" }]
            },
            new SnippetItem
            {
                Insert = "cos(§)",
                IsElementWise = true,
                Description = "Cosine",
                Documentation = "Computes the cosine of an angle. The angle is interpreted using the current `#deg` / `#rad` / `#gra` setting; pass an angle with explicit units to override.",
                Example = "cos(60*deg)",
                Category = "Functions/Trigonometric",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar from -1 to 1",
                Parameters = [new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Angle" }]
            },
            new SnippetItem
            {
                Insert = "tan(§)",
                IsElementWise = true,
                Description = "Tangent",
                Documentation = "Computes the tangent (sin/cos) of an angle. Undefined at odd multiples of π/2 (90°, 270°, ...).",
                Example = "tan(45*deg)",
                Category = "Functions/Trigonometric",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar",
                Parameters = [new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Angle" }]
            },
            new SnippetItem
            {
                Insert = "csc(§)",
                IsElementWise = true,
                Description = "Cosecant",
                Documentation = "Computes the cosecant (1/sin) of an angle. Undefined at integer multiples of π (0°, 180°, ...).",
                Category = "Functions/Trigonometric",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar",
                Parameters = [new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Angle" }]
            },
            new SnippetItem
            {
                Insert = "sec(§)",
                IsElementWise = true,
                Description = "Secant",
                Documentation = "Computes the secant (1/cos) of an angle. Undefined at odd multiples of π/2 (90°, 270°, ...).",
                Category = "Functions/Trigonometric",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar",
                Parameters = [new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Angle" }]
            },
            new SnippetItem
            {
                Insert = "cot(§)",
                IsElementWise = true,
                Description = "Cotangent",
                Documentation = "Computes the cotangent (cos/sin) of an angle. Undefined at integer multiples of π (0°, 180°, ...).",
                Category = "Functions/Trigonometric",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar",
                Parameters = [new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Angle" }]
            },

            // ============================================
            // INVERSE TRIGONOMETRIC FUNCTIONS
            // ============================================
            // Inverse trig functions return an angle in the unit selected by the current
            // #deg / #rad / #gra setting.
            new SnippetItem
            {
                Insert = "asin(§)",
                IsElementWise = true,
                Description = "Inverse sine (arc sine)",
                Documentation = "Returns the angle whose sine is `x`. Result is in the range [-π/2, π/2] (or [-90°, 90°]). Errors if |x| > 1.",
                Category = "Functions/Inverse Trigonometric",
                KeywordType = "Function",
                ReturnTypeDescription = "Angle in the current angle unit",
                Parameters = [new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Value from -1 to 1" }]
            },
            new SnippetItem
            {
                Insert = "acos(§)",
                IsElementWise = true,
                Description = "Inverse cosine (arc cosine)",
                Documentation = "Returns the angle whose cosine is `x`. Result is in the range [0, π] (or [0°, 180°]). Errors if |x| > 1.",
                Category = "Functions/Inverse Trigonometric",
                KeywordType = "Function",
                ReturnTypeDescription = "Angle in the current angle unit",
                Parameters = [new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Value from -1 to 1" }]
            },
            new SnippetItem
            {
                Insert = "atan(§)",
                IsElementWise = true,
                Description = "Inverse tangent (arc tangent)",
                Documentation = "Returns the angle whose tangent is `x`. Result is in the range (-π/2, π/2). For full-quadrant resolution from `(x, y)` coordinates, use `atan2`.",
                Category = "Functions/Inverse Trigonometric",
                KeywordType = "Function",
                ReturnTypeDescription = "Angle in the current angle unit",
                Parameters = [new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Value" }]
            },
            new SnippetItem
            {
                Insert = "atan2(§; §)",
                IsElementWise = true,
                Description = "Two-argument arc tangent",
                Documentation = "Returns the angle of the vector `(x, y)` measured from the positive x-axis, with the correct quadrant. Result is in (-π, π] (or (-180°, 180°]).",
                Example = "atan2(1; 1)  ' returns 45°",
                Category = "Functions/Inverse Trigonometric",
                KeywordType = "Function",
                ReturnTypeDescription = "Angle in the current angle unit",
                Parameters =
                [
                    new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "X coordinate" },
                    new SnippetParameter { Name = "y", Type = ParameterType.Scalar, Description = "Y coordinate" }
                ]
            },
            new SnippetItem
            {
                Insert = "acsc(§)",
                IsElementWise = true,
                Description = "Inverse cosecant",
                Documentation = "Returns the angle whose cosecant is `x`. Defined for |x| ≥ 1.",
                Category = "Functions/Inverse Trigonometric",
                KeywordType = "Function",
                ReturnTypeDescription = "Angle in the current angle unit",
                Parameters = [new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Value" }]
            },
            new SnippetItem
            {
                Insert = "asec(§)",
                IsElementWise = true,
                Description = "Inverse secant",
                Documentation = "Returns the angle whose secant is `x`. Defined for |x| ≥ 1.",
                Category = "Functions/Inverse Trigonometric",
                KeywordType = "Function",
                ReturnTypeDescription = "Angle in the current angle unit",
                Parameters = [new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Value" }]
            },
            new SnippetItem
            {
                Insert = "acot(§)",
                IsElementWise = true,
                Description = "Inverse cotangent",
                Documentation = "Returns the angle whose cotangent is `x`.",
                Category = "Functions/Inverse Trigonometric",
                KeywordType = "Function",
                ReturnTypeDescription = "Angle in the current angle unit",
                Parameters = [new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Value" }]
            },

            // ============================================
            // HYPERBOLIC FUNCTIONS
            // ============================================
            new SnippetItem
            {
                Insert = "sinh(§)",
                IsElementWise = true,
                Description = "Hyperbolic sine",
                Documentation = "Computes (e^x − e^-x) / 2. Argument is dimensionless.",
                Category = "Functions/Hyperbolic",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar",
                Parameters = [new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Value" }]
            },
            new SnippetItem
            {
                Insert = "cosh(§)",
                IsElementWise = true,
                Description = "Hyperbolic cosine",
                Documentation = "Computes (e^x + e^-x) / 2. Argument is dimensionless. Result is always ≥ 1.",
                Category = "Functions/Hyperbolic",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar ≥ 1",
                Parameters = [new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Value" }]
            },
            new SnippetItem
            {
                Insert = "tanh(§)",
                IsElementWise = true,
                Description = "Hyperbolic tangent",
                Documentation = "Computes sinh(x) / cosh(x). Result is in (-1, 1) and approaches ±1 for large |x|.",
                Category = "Functions/Hyperbolic",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar  > -1 and < 1",
                Parameters = [new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Value" }]
            },
            new SnippetItem
            {
                Insert = "csch(§)",
                IsElementWise = true,
                Description = "Hyperbolic cosecant",
                Documentation = "Computes 1 / sinh(x). Undefined at x = 0.",
                Category = "Functions/Hyperbolic",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar",
                Parameters = [new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Value" }]
            },
            new SnippetItem
            {
                Insert = "sech(§)",
                IsElementWise = true,
                Description = "Hyperbolic secant",
                Documentation = "Computes 1 / cosh(x). Result is > 0 and ≤ 1.",
                Category = "Functions/Hyperbolic",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar > 0 and ≤ 1",
                Parameters = [new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Value" }]
            },
            new SnippetItem
            {
                Insert = "coth(§)",
                IsElementWise = true,
                Description = "Hyperbolic cotangent",
                Documentation = "Computes cosh(x) / sinh(x). Undefined at x = 0.",
                Category = "Functions/Hyperbolic",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar",
                Parameters = [new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Value" }]
            },

            // ============================================
            // INVERSE HYPERBOLIC FUNCTIONS
            // ============================================
            new SnippetItem
            {
                Insert = "asinh(§)",
                IsElementWise = true,
                Description = "Inverse hyperbolic sine",
                Documentation = "Returns the value whose hyperbolic sine is `x`. Equivalent to `ln(x + sqrt(x² + 1))`. Defined for all real `x`.",
                Category = "Functions/Inverse Hyperbolic",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar",
                Parameters = [new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Value" }]
            },
            new SnippetItem
            {
                Insert = "acosh(§)",
                IsElementWise = true,
                Description = "Inverse hyperbolic cosine",
                Documentation = "Returns the non-negative value whose hyperbolic cosine is `x`. Equivalent to `ln(x + sqrt(x² − 1))`. Errors if `x < 1`.",
                Category = "Functions/Inverse Hyperbolic",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar ≥ 0",
                Parameters = [new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Value ≥ 1" }]
            },
            new SnippetItem
            {
                Insert = "atanh(§)",
                IsElementWise = true,
                Description = "Inverse hyperbolic tangent",
                Documentation = "Returns the value whose hyperbolic tangent is `x`. Equivalent to `0.5 · ln((1 + x) / (1 − x))`. Errors if `|x| ≥ 1`.",
                Category = "Functions/Inverse Hyperbolic",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar",
                Parameters = [new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Value > -1 and < 1" }]
            },
            new SnippetItem
            {
                Insert = "acsch(§)",
                IsElementWise = true,
                Description = "Inverse hyperbolic cosecant",
                Documentation = "Returns the value whose hyperbolic cosecant is `x`. Equivalent to `asinh(1/x)`. Undefined at `x = 0`.",
                Category = "Functions/Inverse Hyperbolic",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar",
                Parameters = [new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Value ≠ 0" }]
            },
            new SnippetItem
            {
                Insert = "asech(§)",
                IsElementWise = true,
                Description = "Inverse hyperbolic secant",
                Documentation = "Returns the value whose hyperbolic secant is `x`. Equivalent to `acosh(1/x)`. Defined for `x ∈ (0, 1]`.",
                Category = "Functions/Inverse Hyperbolic",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar ≥ 0",
                Parameters = [new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Value > 0 and ≤ 1" }]
            },
            new SnippetItem
            {
                Insert = "acoth(§)",
                IsElementWise = true,
                Description = "Inverse hyperbolic cotangent",
                Documentation = "Returns the value whose hyperbolic cotangent is `x`. Equivalent to `atanh(1/x)`. Defined for `|x| > 1`.",
                Category = "Functions/Inverse Hyperbolic",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar",
                Parameters = [new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Value with |x| > 1" }]
            },

            // ============================================
            // LOGARITHMIC, EXPONENTIAL AND ROOTS
            // ============================================
            new SnippetItem
            {
                Insert = "log(§)",
                IsElementWise = true,
                Description = "Decimal (base-10) logarithm",
                Documentation = "Computes log₁₀(x). Errors when `x ≤ 0` (real mode). For complex mode, the principal branch is used.",
                Category = "Functions/Logarithmic, Exponential and Roots",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar",
                Parameters = [new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Value > 0" }]
            },
            new SnippetItem
            {
                Insert = "ln(§)",
                IsElementWise = true,
                Description = "Natural logarithm",
                Documentation = "Computes the natural logarithm (base e) of `x`. Errors when `x ≤ 0` (real mode).",
                Category = "Functions/Logarithmic, Exponential and Roots",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar",
                Parameters = [new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Value > 0" }]
            },
            new SnippetItem
            {
                Insert = "log_2(§)",
                IsElementWise = true,
                Description = "Binary (base-2) logarithm",
                Documentation = "Computes log₂(x). Errors when `x ≤ 0` (real mode).",
                Category = "Functions/Logarithmic, Exponential and Roots",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar",
                Parameters = [new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Value > 0" }]
            },
            new SnippetItem
            {
                Insert = "exp(§)",
                IsElementWise = true,
                Description = "Exponential function (e^x)",
                Documentation = "Computes Euler's number `e` raised to the power `x`. Inverse of `ln`.",
                Category = "Functions/Logarithmic, Exponential and Roots",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar > 0",
                Parameters = [new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Exponent" }]
            },
            new SnippetItem
            {
                Insert = "sqr(§)",
                IsElementWise = true,
                Description = "Square root",
                Documentation = "Returns the principal square root of `x`. Identical to `sqrt`. Errors for negative `x` in real mode.",
                Label = "sqr(x)",
                Category = "Functions/Logarithmic, Exponential and Roots",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar ≥ 0",
                Parameters = [new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Value ≥ 0" }]
            },
            new SnippetItem
            {
                Insert = "sqrt(§)",
                IsElementWise = true,
                Description = "Square root",
                Documentation = "Returns the principal square root of `x`. Identical to `sqr`. Errors for negative `x` in real mode.",
                Label = "sqrt(x)",
                Category = "Functions/Logarithmic, Exponential and Roots",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar ≥ 0",
                Parameters = [new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Value ≥ 0" }]
            },
            new SnippetItem
            {
                Insert = "cbrt(§)",
                IsElementWise = true,
                Description = "Cubic root",
                Documentation = "Returns the real cube root of `x`. Defined for all real `x`, including negatives.",
                Category = "Functions/Logarithmic, Exponential and Roots",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar",
                Parameters = [new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Value" }]
            },
            new SnippetItem
            {
                Insert = "root(§; §)",
                IsElementWise = true,
                Description = "N-th root",
                Documentation = "Returns the principal n-th root of `x`, equivalent to `x^(1/n)`. Use a positive integer for `n`.",
                Example = "root(8; 3)  ' returns 2",
                Category = "Functions/Logarithmic, Exponential and Roots",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar",
                Parameters =
                [
                    new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Value" },
                    new SnippetParameter { Name = "n", Type = ParameterType.Integer, Description = "Root degree" }
                ]
            },

            // ============================================
            // ROUNDING FUNCTIONS
            // ============================================
            new SnippetItem
            {
                Insert = "round(§)",
                IsElementWise = true,
                Description = "Round to the nearest integer",
                Documentation = "Rounds `x` to the nearest integer using banker's rounding (ties go to even). Preserves units.",
                Example = "round(2.5)  ' returns 2",
                Category = "Functions/Rounding",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar (integer-valued)",
                Parameters = [new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Value" }]
            },
            new SnippetItem
            {
                Insert = "floor(§)",
                IsElementWise = true,
                Description = "Round down (towards -∞)",
                Documentation = "Returns the largest integer not exceeding `x`. Preserves units. For negative numbers, rounds away from zero.",
                Example = "floor(-1.2)  ' returns -2",
                Category = "Functions/Rounding",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar (integer-valued)",
                Parameters = [new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Value" }]
            },
            new SnippetItem
            {
                Insert = "ceiling(§)",
                IsElementWise = true,
                Description = "Round up (towards +∞)",
                Documentation = "Returns the smallest integer not less than `x`. Preserves units.",
                Example = "ceiling(1.2)  ' returns 2",
                Category = "Functions/Rounding",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar (integer-valued)",
                Parameters = [new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Value" }]
            },
            new SnippetItem
            {
                Insert = "trunc(§)",
                IsElementWise = true,
                Description = "Truncate towards zero",
                Documentation = "Discards the fractional part of `x`, leaving only the integer portion. Preserves units. Same as `floor` for positive `x`, `ceiling` for negative `x`.",
                Category = "Functions/Rounding",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar (integer-valued)",
                Parameters = [new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Value" }]
            },

            // ============================================
            // INTEGER FUNCTIONS
            // ============================================
            new SnippetItem
            {
                Insert = "mod(§; §)",
                IsElementWise = true,
                Description = "Remainder of integer division",
                Documentation = "Computes `x mod y`, i.e., the remainder after dividing `x` by `y`. Sign of the result follows `x`.",
                Example = "mod(10; 3)  ' returns 1",
                Category = "Functions/Integer",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar (integer-valued)",
                Parameters =
                [
                    new SnippetParameter { Name = "x", Type = ParameterType.Integer, Description = "Dividend" },
                    new SnippetParameter { Name = "y", Type = ParameterType.Integer, Description = "Divisor" }
                ]
            },
            new SnippetItem
            {
                Insert = "gcd(§; §)",
                Description = "Greatest common divisor",
                Documentation = "Returns the largest positive integer that divides every argument exactly. Accepts two or more arguments. Vector/matrix arguments are flattened element-wise first.",
                Example = "gcd(12; 18; 30)  ' returns 6",
                Category = "Functions/Integer",
                KeywordType = "Function",
                AcceptsAnyCount = true,
                ReturnTypeDescription = "Positive integer",
                Parameters =
                [
                    new SnippetParameter { Name = "values", Type = ParameterType.Various, Description = "Two or more integers, vectors, or matrices", IsVariadic = true }
                ]
            },
            new SnippetItem
            {
                Insert = "lcm(§; §)",
                Description = "Least common multiple",
                Documentation = "Returns the smallest positive integer that is a multiple of every argument. Accepts two or more arguments. Vector/matrix arguments are flattened element-wise first.",
                Example = "lcm(4; 6)  ' returns 12",
                Category = "Functions/Integer",
                KeywordType = "Function",
                AcceptsAnyCount = true,
                ReturnTypeDescription = "Positive integer",
                Parameters =
                [
                    new SnippetParameter { Name = "values", Type = ParameterType.Various, Description = "Two or more integers, vectors, or matrices", IsVariadic = true }
                ]
            },
            new SnippetItem
            {
                Insert = "fact(§)",
                IsElementWise = true,
                Description = "Factorial",
                Documentation = "Returns `n!` = 1·2·3·…·n, the product of all positive integers up to `n`. The argument must be a dimensionless integer in the range 0 to 170; larger values overflow. Equivalent to the postfix `!` operator.",
                Example = "fact(5)  ' returns 120",
                Category = "Functions/Integer",
                KeywordType = "Function",
                ReturnTypeDescription = "Positive integer",
                Parameters = [new SnippetParameter { Name = "n", Type = ParameterType.Integer, Description = "Integer from 0 to 170" }]
            },

            // ============================================
            // COMPLEX NUMBER FUNCTIONS
            // ============================================
            new SnippetItem
            {
                Insert = "re(§)",
                IsElementWise = true,
                Description = "Real part of a complex number",
                Documentation = "Returns the real part of `z`. For a real input, returns `z` unchanged.",
                Category = "Functions/Complex",
                KeywordType = "Function",
                ReturnTypeDescription = "Real scalar",
                Parameters = [new SnippetParameter { Name = "z", Type = ParameterType.Scalar, Description = "Complex number" }]
            },
            new SnippetItem
            {
                Insert = "im(§)",
                IsElementWise = true,
                Description = "Imaginary part of a complex number",
                Documentation = "Returns the imaginary part of `z` as a real scalar. For a real input, returns 0.",
                Category = "Functions/Complex",
                KeywordType = "Function",
                ReturnTypeDescription = "Real scalar",
                Parameters = [new SnippetParameter { Name = "z", Type = ParameterType.Scalar, Description = "Complex number" }]
            },
            new SnippetItem
            {
                Insert = "abs(§)",
                Description = "Absolute value / magnitude",
                Documentation = "Returns |z|. For a real number, the unsigned magnitude. For a complex number, `sqrt(re² + im²)`. Element-wise on vectors and matrices.",
                Category = "Functions/Complex",
                KeywordType = "Function",
                IsElementWise = true,
                ReturnTypeDescription = "Non-negative scalar",
                Parameters = [new SnippetParameter { Name = "z", Type = ParameterType.Any, Description = "Value or complex number" }]
            },
            new SnippetItem
            {
                Insert = "phase(§)",
                IsElementWise = true,
                Description = "Phase angle of a complex number",
                Documentation = "Returns the argument (phase angle) of `z`, equivalent to `atan2(im(z); re(z))`. Result is in the current angle unit.",
                Category = "Functions/Complex",
                KeywordType = "Function",
                ReturnTypeDescription = "Angle in the current angle unit",
                Parameters = [new SnippetParameter { Name = "z", Type = ParameterType.Scalar, Description = "Complex number" }]
            },
            new SnippetItem
            {
                Insert = "conj(§)",
                IsElementWise = true,
                Description = "Complex conjugate",
                Documentation = "Returns the complex conjugate of `z`, i.e., flips the sign of the imaginary part. For real input, returns `z` unchanged.",
                Category = "Functions/Complex",
                KeywordType = "Function",
                ReturnTypeDescription = "Complex scalar",
                Parameters = [new SnippetParameter { Name = "z", Type = ParameterType.Scalar, Description = "Complex number" }]
            },

            // ============================================
            // AGGREGATE AND INTERPOLATION FUNCTIONS
            // ============================================
            // min - variadic (accepts scalars, vectors, matrices)
            new SnippetItem
            {
                Insert = "min(v₁; v₂; ...)",
                Description = "Minimum of multiple values (scalars, vectors, or matrices)",
                Documentation = "Returns the smallest value among all arguments. Vector/matrix arguments are flattened element-wise before comparison.",
                Example = "min(3; 7; 1; 5)  ' returns 1",
                Category = "Functions/Aggregate and Interpolation",
                KeywordType = "Function",
                AcceptsAnyCount = true,
                ReturnTypeDescription = "Scalar",
                Parameters = [new SnippetParameter { Name = "values", Type = ParameterType.Various, Description = "Values (expanded if vector/matrix)", IsVariadic = true }]
            },
            // min - single vector
            new SnippetItem
            {
                Insert = "min(§)",
                Description = "Minimum element of a vector",
                Documentation = "Returns the smallest element of `v`.",
                Category = "Functions/Aggregate and Interpolation",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar",
                Parameters = [new SnippetParameter { Name = "v", Type = ParameterType.Vector, Description = "Vector" }]
            },
            // min - single matrix
            new SnippetItem
            {
                Insert = "min(§)",
                Description = "Minimum element of a matrix",
                Documentation = "Returns the smallest element of `M` across all rows and columns.",
                Category = "Functions/Aggregate and Interpolation",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar",
                Parameters = [new SnippetParameter { Name = "M", Type = ParameterType.Matrix, Description = "Matrix" }]
            },
            // max - variadic (accepts scalars, vectors, matrices)
            new SnippetItem
            {
                Insert = "max(v₁; v₂; ...)",
                Description = "Maximum of multiple values (scalars, vectors, or matrices)",
                Documentation = "Returns the largest value among all arguments. Vector/matrix arguments are flattened element-wise before comparison.",
                Example = "max(3; 7; 1; 5)  ' returns 7",
                Category = "Functions/Aggregate and Interpolation",
                KeywordType = "Function",
                AcceptsAnyCount = true,
                ReturnTypeDescription = "Scalar",
                Parameters = [new SnippetParameter { Name = "values", Type = ParameterType.Various, Description = "Values (expanded if vector/matrix)", IsVariadic = true }]
            },
            // max - single vector
            new SnippetItem
            {
                Insert = "max(§)",
                Description = "Maximum element of a vector",
                Documentation = "Returns the largest element of `v`.",
                Category = "Functions/Aggregate and Interpolation",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar",
                Parameters = [new SnippetParameter { Name = "v", Type = ParameterType.Vector, Description = "Vector" }]
            },
            // max - single matrix
            new SnippetItem
            {
                Insert = "max(§)",
                Description = "Maximum element of a matrix",
                Documentation = "Returns the largest element of `M` across all rows and columns.",
                Category = "Functions/Aggregate and Interpolation",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar",
                Parameters = [new SnippetParameter { Name = "M", Type = ParameterType.Matrix, Description = "Matrix" }]
            },
            // sum - variadic (accepts scalars, vectors, matrices)
            new SnippetItem
            {
                Insert = "sum(v₁; v₂; ...)",
                Description = "Sum of multiple values (scalars, vectors, or matrices)",
                Documentation = "Adds every element of every argument. Vector/matrix arguments are flattened first.",
                Category = "Functions/Aggregate and Interpolation",
                KeywordType = "Function",
                AcceptsAnyCount = true,
                ReturnTypeDescription = "Scalar",
                Parameters = [new SnippetParameter { Name = "values", Type = ParameterType.Various, Description = "Values (expanded if vector/matrix)", IsVariadic = true }]
            },
            // sum - single vector
            new SnippetItem
            {
                Insert = "sum(§)",
                Description = "Sum of all elements in a vector",
                Documentation = "Adds every element of `v` and returns the result.",
                Category = "Functions/Aggregate and Interpolation",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar",
                Parameters = [new SnippetParameter { Name = "v", Type = ParameterType.Vector, Description = "Vector" }]
            },
            // sum - single matrix
            new SnippetItem
            {
                Insert = "sum(§)",
                Description = "Sum of all elements in a matrix",
                Documentation = "Adds every element of `M` (across rows and columns) and returns the result.",
                Category = "Functions/Aggregate and Interpolation",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar",
                Parameters = [new SnippetParameter { Name = "M", Type = ParameterType.Matrix, Description = "Matrix" }]
            },
            // sumsq - variadic (accepts scalars, vectors, matrices)
            new SnippetItem
            {
                Insert = "sumsq(v₁; v₂; ...)",
                Description = "Sum of squares of multiple values (scalars, vectors, or matrices)",
                Documentation = "Sums `xᵢ²` over all arguments. Useful for variance/energy calculations.",
                Category = "Functions/Aggregate and Interpolation",
                KeywordType = "Function",
                AcceptsAnyCount = true,
                ReturnTypeDescription = "Non-negative scalar",
                Parameters = [new SnippetParameter { Name = "values", Type = ParameterType.Various, Description = "Values (expanded if vector/matrix)", IsVariadic = true }]
            },
            // sumsq - single vector
            new SnippetItem
            {
                Insert = "sumsq(§)",
                Description = "Sum of squares of all elements in a vector",
                Documentation = "Returns Σ vᵢ². Equivalent to `v · v` (dot product with itself).",
                Category = "Functions/Aggregate and Interpolation",
                KeywordType = "Function",
                ReturnTypeDescription = "Non-negative scalar",
                Parameters = [new SnippetParameter { Name = "v", Type = ParameterType.Vector, Description = "Vector" }]
            },
            // sumsq - single matrix
            new SnippetItem
            {
                Insert = "sumsq(§)",
                Description = "Sum of squares of all elements in a matrix",
                Documentation = "Returns Σ Mᵢⱼ² over the entire matrix (Frobenius norm squared).",
                Category = "Functions/Aggregate and Interpolation",
                KeywordType = "Function",
                ReturnTypeDescription = "Non-negative scalar",
                Parameters = [new SnippetParameter { Name = "M", Type = ParameterType.Matrix, Description = "Matrix" }]
            },
            // srss - variadic (accepts scalars, vectors, matrices)
            new SnippetItem
            {
                Insert = "srss(v₁; v₂; ...)",
                Description = "Square root of sum of squares of multiple values (scalars, vectors, or matrices)",
                Documentation = "Computes `sqrt(Σ xᵢ²)`. Common in engineering for combining orthogonal components (e.g., load cases, modal responses).",
                Example = "srss(3; 4)  ' returns 5",
                Category = "Functions/Aggregate and Interpolation",
                KeywordType = "Function",
                AcceptsAnyCount = true,
                ReturnTypeDescription = "Non-negative scalar",
                Parameters = [new SnippetParameter { Name = "values", Type = ParameterType.Various, Description = "Values (expanded if vector/matrix)", IsVariadic = true }]
            },
            // srss - single vector
            new SnippetItem
            {
                Insert = "srss(§)",
                Description = "Square root of sum of squares of all elements in a vector",
                Documentation = "Returns the Euclidean norm `sqrt(Σ vᵢ²)` of the vector.",
                Category = "Functions/Aggregate and Interpolation",
                KeywordType = "Function",
                ReturnTypeDescription = "Non-negative scalar",
                Parameters = [new SnippetParameter { Name = "v", Type = ParameterType.Vector, Description = "Vector" }]
            },
            // srss - single matrix
            new SnippetItem
            {
                Insert = "srss(§)",
                Description = "Square root of sum of squares of all elements in a matrix",
                Documentation = "Returns the Frobenius norm `sqrt(Σ Mᵢⱼ²)` of the matrix.",
                Category = "Functions/Aggregate and Interpolation",
                KeywordType = "Function",
                ReturnTypeDescription = "Non-negative scalar",
                Parameters = [new SnippetParameter { Name = "M", Type = ParameterType.Matrix, Description = "Matrix" }]
            },
            // average - variadic (accepts scalars, vectors, matrices)
            new SnippetItem
            {
                Insert = "average(v₁; v₂; ...)",
                Description = "Arithmetic mean of multiple values (scalars, vectors, or matrices)",
                Documentation = "Returns `(Σ xᵢ) / n`, where `n` is the total element count after flattening vector/matrix arguments.",
                Category = "Functions/Aggregate and Interpolation",
                KeywordType = "Function",
                AcceptsAnyCount = true,
                ReturnTypeDescription = "Scalar",
                Parameters = [new SnippetParameter { Name = "values", Type = ParameterType.Various, Description = "Values (expanded if vector/matrix)", IsVariadic = true }]
            },
            // average - single vector
            new SnippetItem
            {
                Insert = "average(§)",
                Description = "Arithmetic mean of all elements in a vector",
                Documentation = "Returns `sum(v) / length(v)`.",
                Category = "Functions/Aggregate and Interpolation",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar",
                Parameters = [new SnippetParameter { Name = "v", Type = ParameterType.Vector, Description = "Vector" }]
            },
            // average - single matrix
            new SnippetItem
            {
                Insert = "average(§)",
                Description = "Arithmetic mean of all elements in a matrix",
                Documentation = "Returns the sum of all elements divided by the total number of elements (rows × cols).",
                Category = "Functions/Aggregate and Interpolation",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar",
                Parameters = [new SnippetParameter { Name = "M", Type = ParameterType.Matrix, Description = "Matrix" }]
            },
            // product - variadic (accepts scalars, vectors, matrices)
            new SnippetItem
            {
                Insert = "product(v₁; v₂; ...)",
                Description = "Product of multiple values (scalars, vectors, or matrices)",
                Documentation = "Multiplies every element of every argument together. Returns 1 for an empty input list.",
                Category = "Functions/Aggregate and Interpolation",
                KeywordType = "Function",
                AcceptsAnyCount = true,
                ReturnTypeDescription = "Scalar",
                Parameters = [new SnippetParameter { Name = "values", Type = ParameterType.Various, Description = "Values (expanded if vector/matrix)", IsVariadic = true }]
            },
            // product - single vector
            new SnippetItem
            {
                Insert = "product(§)",
                Description = "Product of all elements in a vector",
                Documentation = "Returns Π vᵢ over the elements of `v`.",
                Category = "Functions/Aggregate and Interpolation",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar",
                Parameters = [new SnippetParameter { Name = "v", Type = ParameterType.Vector, Description = "Vector" }]
            },
            // product - single matrix
            new SnippetItem
            {
                Insert = "product(§)",
                Description = "Product of all elements in a matrix",
                Documentation = "Returns Π Mᵢⱼ over the entire matrix.",
                Category = "Functions/Aggregate and Interpolation",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar",
                Parameters = [new SnippetParameter { Name = "M", Type = ParameterType.Matrix, Description = "Matrix" }]
            },
            // mean - variadic (accepts scalars, vectors, matrices)
            new SnippetItem
            {
                Insert = "mean(v₁; v₂; ...)",
                Description = "Geometric mean of multiple values (scalars, vectors, or matrices)",
                Documentation = "Returns the n-th root of the product of all `n` values: `(Π xᵢ)^(1/n)`. All arguments must be positive.",
                Category = "Functions/Aggregate and Interpolation",
                KeywordType = "Function",
                AcceptsAnyCount = true,
                ReturnTypeDescription = "Positive scalar",
                Parameters = [new SnippetParameter { Name = "values", Type = ParameterType.Various, Description = "Values (expanded if vector/matrix)", IsVariadic = true }]
            },
            // mean - single vector
            new SnippetItem
            {
                Insert = "mean(§)",
                Description = "Geometric mean of all elements in a vector",
                Documentation = "Returns `(Π vᵢ)^(1/n)` where `n = length(v)`. All elements must be positive.",
                Category = "Functions/Aggregate and Interpolation",
                KeywordType = "Function",
                ReturnTypeDescription = "Positive scalar",
                Parameters = [new SnippetParameter { Name = "v", Type = ParameterType.Vector, Description = "Vector" }]
            },
            // mean - single matrix
            new SnippetItem
            {
                Insert = "mean(§)",
                Description = "Geometric mean of all elements in a matrix",
                Documentation = "Returns `(Π Mᵢⱼ)^(1/n)` over all `n = rows × cols` elements. All elements must be positive.",
                Category = "Functions/Aggregate and Interpolation",
                KeywordType = "Function",
                ReturnTypeDescription = "Positive scalar",
                Parameters = [new SnippetParameter { Name = "M", Type = ParameterType.Matrix, Description = "Matrix" }]
            },
            // take - scalar variadic (n; v1; v2; v3; ...)
            new SnippetItem
            {
                Insert = "take(§; §)",
                Description = "Returns the n-th element from a list of scalars",
                Documentation = "Picks the `n`-th argument (1-based) from the variadic list. Errors if `n` is out of range.",
                Example = "take(2; 10; 20; 30)  ' returns 20",
                Category = "Functions/Aggregate and Interpolation",
                AcceptsAnyCount = true,
                ReturnTypeDescription = "Scalar",
                Parameters =
                [
                    new SnippetParameter { Name = "n", Type = ParameterType.Integer, Description = "Index (1-based)" },
                    new SnippetParameter { Name = "values", Type = ParameterType.Scalar, Description = "Scalar values", IsVariadic = true }
                ]
            },
            // take - from vector (n; vector)
            new SnippetItem
            {
                Insert = "take(§; §)",
                Description = "Returns the n-th element from a vector",
                Documentation = "Returns `v[n]` (1-based). Errors if `n` is out of range. To take multiple elements, use `extractRows`/`vec_slice`.",
                Category = "Functions/Aggregate and Interpolation",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar",
                Parameters =
                [
                    new SnippetParameter { Name = "n", Type = ParameterType.Integer, Description = "Index (1-based)" },
                    new SnippetParameter { Name = "v", Type = ParameterType.Vector, Description = "Vector" }
                ]
            },
            // take - from matrix (n; matrix)
            new SnippetItem
            {
                Insert = "take(§; §)",
                Description = "Returns the n-th element from a matrix",
                Documentation = "The matrix is linearized into a vector (row by row) and `M[n]` (1-based) is returned. Errors if `n` is out of range.",
                Category = "Functions/Aggregate and Interpolation",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar",
                Parameters =
                [
                    new SnippetParameter { Name = "n", Type = ParameterType.Integer, Description = "Index (1-based)" },
                    new SnippetParameter { Name = "m", Type = ParameterType.Matrix, Description = "Matrix" }
                ]
            },
            // line - scalar pairs (x; x1; y1; x2; y2; ...)
            new SnippetItem
            {
                Insert = "line(§; §)",
                Description = "Linear interpolation with scalar data pairs",
                Documentation = "Performs piecewise linear interpolation at `x` over the data pairs `(x1, y1), (x2, y2), …`. Pairs must be sorted by `x` ascending.",
                Example = "line(2.5; 0; 0; 1; 10; 5; 50)  ' interpolates between (1,10) and (5,50)",
                Category = "Functions/Aggregate and Interpolation",
                AcceptsAnyCount = true,
                ReturnTypeDescription = "Scalar",
                Parameters =
                [
                    new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Interpolation point" },
                    new SnippetParameter { Name = "data", Type = ParameterType.Scalar, Description = "Data pairs (x1; y1; x2; y2; ...)", IsVariadic = true }
                ]
            },
            // line - with vectors (x; xVector; yVector)
            new SnippetItem
            {
                Insert = "line(§; §; §)",
                Description = "Linear interpolation with x and y vectors",
                Documentation = "Performs piecewise linear interpolation at `x` using parallel `xData` and `yData` vectors. `xData` must be sorted ascending and have the same length as `yData`.",
                Category = "Functions/Aggregate and Interpolation",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar",
                Parameters =
                [
                    new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Interpolation point" },
                    new SnippetParameter { Name = "xData", Type = ParameterType.Vector, Description = "X data points vector" },
                    new SnippetParameter { Name = "yData", Type = ParameterType.Vector, Description = "Y data points vector" }
                ]
            },
            // line - with matrix (x; matrix)
            new SnippetItem
            {
                Insert = "line(§; §)",
                Description = "Linear interpolation over a linearized matrix",
                Documentation = "The matrix is linearized into a vector (row by row) and used as the data pairs `(x1, y1), (x2, y2), …` for piecewise linear interpolation at `x`.",
                Category = "Functions/Aggregate and Interpolation",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar",
                Parameters =
                [
                    new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Interpolation point" },
                    new SnippetParameter { Name = "m", Type = ParameterType.Matrix, Description = "Matrix" }
                ]
            },
            // spline - scalar pairs (x; x1; y1; x2; y2; ...)
            new SnippetItem
            {
                Insert = "spline(§; §)",
                Description = "Hermite spline interpolation with scalar data pairs",
                Documentation = "Performs smooth Hermite cubic spline interpolation at `x` over the data pairs. Smoother than `line` but more expensive.",
                Category = "Functions/Aggregate and Interpolation",
                AcceptsAnyCount = true,
                ReturnTypeDescription = "Scalar",
                Parameters =
                [
                    new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Interpolation point" },
                    new SnippetParameter { Name = "data", Type = ParameterType.Scalar, Description = "Data pairs (x1; y1; x2; y2; ...)", IsVariadic = true }
                ]
            },
            // spline - with vectors (x; xVector; yVector)
            new SnippetItem
            {
                Insert = "spline(§; §; §)",
                Description = "Hermite spline interpolation with x and y vectors",
                Documentation = "Performs smooth Hermite cubic spline interpolation at `x` using parallel `xData` and `yData` vectors. `xData` must be sorted ascending.",
                Category = "Functions/Aggregate and Interpolation",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar",
                Parameters =
                [
                    new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Interpolation point" },
                    new SnippetParameter { Name = "xData", Type = ParameterType.Vector, Description = "X data points vector" },
                    new SnippetParameter { Name = "yData", Type = ParameterType.Vector, Description = "Y data points vector" }
                ]
            },
            // spline - with matrix (x; matrix)
            new SnippetItem
            {
                Insert = "spline(§; §)",
                Description = "Hermite spline interpolation over a linearized matrix",
                Documentation = "The matrix is linearized into a vector (row by row) and used as the data pairs for smooth Hermite cubic spline interpolation at `x`.",
                Category = "Functions/Aggregate and Interpolation",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar",
                Parameters =
                [
                    new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Interpolation point" },
                    new SnippetParameter { Name = "m", Type = ParameterType.Matrix, Description = "Matrix" }
                ]
            },

            // ============================================
            // CONDITIONAL AND LOGICAL FUNCTIONS
            // ============================================
            new SnippetItem
            {
                Insert = "if(§; §; §)",
                Description = "Conditional evaluation",
                Documentation = "Returns `value-if-true` when `cond` is non-zero, otherwise `value-if-false`. Both branches are evaluated.",
                Example = "if(x > 0; x; -x)  ' absolute value",
                Category = "Functions/Conditional and Logical",
                KeywordType = "Function",
                ReturnTypeDescription = "Same type as the chosen branch",
                Parameters =
                [
                    new SnippetParameter { Name = "cond", Type = ParameterType.Boolean, Description = "Condition" },
                    new SnippetParameter { Name = "value-if-true", Type = ParameterType.Any, Description = "Value if condition is true" },
                    new SnippetParameter { Name = "value-if-false", Type = ParameterType.Any, Description = "Value if condition is false" }
                ]
            },
            new SnippetItem
            {
                Insert = "switch(c₁; v₁; c₂; v₂; …; def)",
                Description = "Selective evaluation (multiple conditions)",
                Documentation = "Evaluates conditions in order and returns the value paired with the first true condition. The final unpaired argument is the default returned when no condition matches.",
                Example = "switch(x < 0; -1; x > 0; 1; 0)  ' sign of x",
                Category = "Functions/Conditional and Logical",
                KeywordType = "Function",
                AcceptsAnyCount = true,
                ReturnTypeDescription = "Same type as the matched value",
                Parameters =
                [
                    new SnippetParameter { Name = "pairs", Type = ParameterType.Various, Description = "Condition/value pairs followed by default value", IsVariadic = true }
                ]
            },
            new SnippetItem
            {
                Insert = "not(§)",
                Description = "Logical NOT",
                Documentation = "Returns 1 if `x` is 0, otherwise 0. Use with comparisons and the boolean `and`/`or` family.",
                Category = "Functions/Conditional and Logical",
                KeywordType = "Function",
                ReturnTypeDescription = "Boolean (0 or 1)",
                Parameters = [new SnippetParameter { Name = "x", Type = ParameterType.Boolean, Description = "Value" }]
            },
            new SnippetItem
            {
                Insert = "and(§; §)",
                Description = "Logical AND of multiple boolean values",
                Documentation = "Returns 1 if every argument is non-zero, otherwise 0. Accepts two or more arguments. Note: this is **not** short-circuit — all arguments are evaluated.",
                Category = "Functions/Conditional and Logical",
                KeywordType = "Function",
                AcceptsAnyCount = true,
                ReturnTypeDescription = "Boolean (0 or 1)",
                Parameters =
                [
                    new SnippetParameter { Name = "values", Type = ParameterType.Boolean, Description = "Boolean values", IsVariadic = true }
                ]
            },
            new SnippetItem
            {
                Insert = "or(§; §)",
                Description = "Logical OR of multiple boolean values",
                Documentation = "Returns 1 if any argument is non-zero, otherwise 0. Accepts two or more arguments. Not short-circuit — all arguments are evaluated.",
                Category = "Functions/Conditional and Logical",
                KeywordType = "Function",
                AcceptsAnyCount = true,
                ReturnTypeDescription = "Boolean (0 or 1)",
                Parameters =
                [
                    new SnippetParameter { Name = "values", Type = ParameterType.Boolean, Description = "Boolean values", IsVariadic = true }
                ]
            },
            new SnippetItem
            {
                Insert = "xor(§; §)",
                Description = "Logical XOR of multiple boolean values",
                Documentation = "Returns 1 if an odd number of arguments are non-zero, otherwise 0. Accepts two or more arguments.",
                Category = "Functions/Conditional and Logical",
                KeywordType = "Function",
                AcceptsAnyCount = true,
                ReturnTypeDescription = "Boolean (0 or 1)",
                Parameters =
                [
                    new SnippetParameter { Name = "values", Type = ParameterType.Boolean, Description = "Boolean values", IsVariadic = true }
                ]
            },

            // ============================================
            // OTHER FUNCTIONS
            // ============================================
            new SnippetItem
            {
                Insert = "sign(§)",
                IsElementWise = true,
                Description = "Sign of a number (-1, 0, or 1)",
                Documentation = "Returns -1 for negative `x`, 0 for `x = 0`, and 1 for positive `x`. Strips units.",
                Category = "Functions/Other",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar with values -1, 0, or 1",
                Parameters = [new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Value" }]
            },
            new SnippetItem
            {
                Insert = "random(§)",
                Description = "Random number between 0 and x",
                Documentation = "Returns a uniformly distributed random scalar from 0 to x. With no argument, returns a value from 0 to 1. Re-evaluating the document re-rolls the value.",
                Example = "random(100)  ' random integer-ish value from 0 to 100",
                Category = "Functions/Other",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar from 0 to x",
                Parameters = [new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Upper bound", IsOptional = true }]
            },
            new SnippetItem
            {
                Insert = "getunits(§)",
                Description = "Gets the units of x (returns 1 if unitless)",
                Documentation = "Extracts the unit dimension of `x` as a value of magnitude 1. Useful for stripping the numeric part: `x / getunits(x)` gives the magnitude.",
                Example = "u = getunits(50*kN)  ' u = 1*kN",
                Category = "Functions/Other",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar with the same units as x, magnitude 1",
                Parameters = [new SnippetParameter { Name = "x", Type = ParameterType.Any, Description = "Value with units" }]
            },
            new SnippetItem
            {
                Insert = "setunits(§; §)",
                Description = "Sets the units to a scalar, vector, or matrix",
                Documentation = "Reinterprets `x` as having the units of `u` (without numerical conversion). To convert between compatible units, use the `|` operator instead.",
                Category = "Functions/Other",
                KeywordType = "Function",
                ReturnTypeDescription = "Same shape as x, with units of u",
                Parameters =
                [
                    new SnippetParameter { Name = "x", Type = ParameterType.Any, Description = "Value" },
                    new SnippetParameter { Name = "u", Type = ParameterType.Any, Description = "Units to set" }
                ]
            },
            new SnippetItem
            {
                Insert = "clrunits(§)",
                Description = "Clears the units from a scalar, vector, or matrix",
                Documentation = "Strips units from `x`, returning a dimensionless value with the same magnitude. Useful before passing to functions that require dimensionless input.",
                Category = "Functions/Other",
                KeywordType = "Function",
                ReturnTypeDescription = "Same shape as x, dimensionless",
                Parameters = [new SnippetParameter { Name = "x", Type = ParameterType.Any, Description = "Value with units" }]
            },
            new SnippetItem
            {
                Insert = "hp(§)",
                Description = "Converts to high-performance type",
                Documentation = "Converts a vector or matrix to a high-performance internal representation (single-precision, contiguous storage). Use for large numerical workloads where memory and speed matter; precision is reduced.",
                Category = "Functions/Other",
                KeywordType = "Function",
                ReturnTypeDescription = "Vector or matrix (high-performance)",
                Parameters = [new SnippetParameter { Name = "x", Type = ParameterType.Any, Description = "Vector or matrix" }]
            },
            new SnippetItem
            {
                Insert = "ishp(§)",
                Description = "Checks if the type is high-performance",
                Documentation = "Returns 1 if `x` is stored in the high-performance representation (see `hp`), otherwise 0.",
                Category = "Functions/Other",
                KeywordType = "Function",
                ReturnTypeDescription = "Boolean (0 or 1)",
                Parameters = [new SnippetParameter { Name = "x", Type = ParameterType.Any, Description = "Value to check" }]
            },
            new SnippetItem
            {
                Insert = "timer(§)",
                Description = "Elapsed time in seconds",
                Documentation = "Returns the time in seconds elapsed since the calculation started. The argument is a value the timer depends on, so it is evaluated after that value; pass the expression whose runtime you want to measure.",
                Example = "timer(x)  ' seconds elapsed up to the evaluation of x",
                Category = "Functions/Other",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar in seconds",
                Parameters = [new SnippetParameter { Name = "x", Type = ParameterType.Any, Description = "Value the timer depends on" }]
            },
            new SnippetItem
            {
                Insert = "mandelbrot(§; §)",
                Description = "Mandelbrot set escape value",
                Documentation = "Returns a smooth escape-time value for the point `(x, y)` in the complex plane, used to render the Mandelbrot set. Points that belong to the set (do not escape) return `Undefined`. The result carries the units of `x`.",
                Example = "mandelbrot(-0.5; 0.5)",
                Category = "Functions/Other",
                KeywordType = "Function",
                ReturnTypeDescription = "Scalar (or Undefined inside the set)",
                Parameters =
                [
                    new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Real part" },
                    new SnippetParameter { Name = "y", Type = ParameterType.Scalar, Description = "Imaginary part" }
                ]
            },
        ];
    }
}
