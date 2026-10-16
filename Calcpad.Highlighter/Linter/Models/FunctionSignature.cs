using Calcpad.Highlighter.Snippets.Models;

namespace Calcpad.Highlighter.Linter.Models
{
    /// <summary>
    /// Represents the signature of a built-in function including parameter types and return type.
    /// This class is derived from SnippetItem data at runtime.
    /// </summary>
    public class FunctionSignature
    {
        /// <summary>Function name</summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>Minimum number of parameters</summary>
        public int MinParams { get; init; }

        /// <summary>Maximum number of parameters (-1 for unlimited/variadic)</summary>
        public int MaxParams { get; init; }

        /// <summary>Expected types for each parameter position (use last type for variadic)</summary>
        public ParameterType[] ParameterTypes { get; init; } = System.Array.Empty<ParameterType>();

        /// <summary>Return type of the function</summary>
        public CalcpadType ReturnType { get; init; } = CalcpadType.Value;

        /// <summary>Whether the function can operate element-wise on vectors/matrices</summary>
        public bool IsElementWise { get; init; }

        /// <summary>
        /// When true, parameter count validation is skipped entirely.
        /// Use for functions where any number of parameters is valid (e.g., switch, gcd, lcm).
        /// </summary>
        public bool AcceptsAnyCount { get; init; }

        /// <summary>Brief description for hover info</summary>
        public string Description { get; init; } = string.Empty;

        /// <summary>
        /// Gets the expected parameter type for a given parameter index.
        /// For variadic functions, returns the last defined type.
        /// </summary>
        public ParameterType GetParameterType(int index)
        {
            if (ParameterTypes.Length == 0)
                return ParameterType.Any;

            if (index < ParameterTypes.Length)
                return ParameterTypes[index];

            // For variadic functions, use the last parameter type
            return ParameterTypes[ParameterTypes.Length - 1];
        }

        /// <summary>
        /// Checks whether an actual argument type is compatible with the expected parameter
        /// type for this signature. Element-wise functions (sin, sqrt, floor, ...) also accept
        /// vectors and matrices wherever a scalar or integer is expected, since Core applies
        /// them element-wise and returns a vector/matrix of the same shape.
        /// </summary>
        public bool IsArgumentCompatible(ParameterType expected, CalcpadType actual)
        {
            if (IsElementWise &&
                (actual == CalcpadType.Vector || actual == CalcpadType.Matrix) &&
                (expected == ParameterType.Scalar || expected == ParameterType.Integer || expected == ParameterType.Any))
                return true;

            return IsTypeCompatible(expected, actual);
        }

        /// <summary>
        /// Checks if the given CalcpadType is compatible with the expected ParameterType.
        /// </summary>
        public static bool IsTypeCompatible(ParameterType expected, CalcpadType actual)
        {
            // Skip type checking when:
            // - actual type is Unknown or Various (can't verify)
            // - expected type is Various or Boolean (can't reliably infer boolean expressions)
            if (actual == CalcpadType.Unknown || actual == CalcpadType.Various ||
                expected == ParameterType.Various || expected == ParameterType.Boolean)
                return true;

            return expected switch
            {
                ParameterType.Any => true,
                ParameterType.Scalar => actual == CalcpadType.Value,
                ParameterType.Vector => actual == CalcpadType.Vector,
                // Core coerces a vector into an n×1 column matrix (IValue.AsMatrix) wherever a
                // matrix is expected, so matrix functions (augment, stack, hprod, transp, row, col, ...)
                // accept vectors too.
                ParameterType.Matrix => actual == CalcpadType.Matrix || actual == CalcpadType.Vector,
                ParameterType.Integer => actual == CalcpadType.Value, // Can't distinguish int from float
                ParameterType.Expression => true, // Can't validate expressions
                _ => true
            };
        }

        /// <summary>
        /// Gets a human-readable name for the parameter type.
        /// </summary>
        public static string GetTypeName(ParameterType type)
        {
            return type switch
            {
                ParameterType.Any => "any",
                ParameterType.Scalar => "scalar",
                ParameterType.Vector => "vector",
                ParameterType.Matrix => "matrix",
                ParameterType.Integer => "integer",
                ParameterType.Boolean => "boolean",
                ParameterType.Expression => "expression",
                ParameterType.Various => "various",
                _ => "unknown"
            };
        }
    }
}
