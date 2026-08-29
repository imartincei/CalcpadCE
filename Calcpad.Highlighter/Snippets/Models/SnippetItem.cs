#nullable enable

using System.Linq;
using Calcpad.Highlighter.Linter.Models;

namespace Calcpad.Highlighter.Snippets.Models
{
    /// <summary>
    /// Represents a snippet item that can be passed to the frontend for autocomplete/insertion.
    /// For functions, also contains signature information for type checking.
    /// </summary>
    public class SnippetItem
    {
        /// <summary>
        /// The text to insert when the snippet is selected.
        /// Use § as cursor placeholder.
        /// </summary>
        public required string Insert { get; init; }

        /// <summary>
        /// Keyword type for linter classification: "Command", "Macro", "Variable", "Function",
        /// "Keyword", "Unit", "Constant", "Operator" or "Data". Null or empty (the default)
        /// excludes the snippet from the linter's keyword/function/command sets, making it UI-only.
        /// </summary>
        public string? KeywordType { get; init; }

        /// <summary>
        /// Short label shown in tooltips, the insert tab, and completion details (e.g. "Sine").
        /// </summary>
        public required string Description { get; init; }

        /// <summary>
        /// Long-form Markdown description for hover/completion docstrings, which may include math
        /// (LaTeX in $...$) and links. Falls back to Description when null.
        /// </summary>
        public string? Documentation { get; init; }

        /// <summary>
        /// Optional Calcpad usage example, rendered as a fenced code block in hover/completion docs.
        /// </summary>
        public string? Example { get; init; }

        /// <summary>
        /// Human-readable description of the return value (e.g. "Angle in radians",
        /// "Vector of length n"). Falls back to the ReturnType enum name when null.
        /// </summary>
        public string? ReturnTypeDescription { get; init; }

        /// <summary>
        /// Optional label to display (if different from description).
        /// If null, description is used as label.
        /// </summary>
        public string? Label { get; init; }

        /// <summary>
        /// Category path using / as separator (e.g., "Functions/Trigonometric").
        /// </summary>
        public required string Category { get; init; }

        /// <summary>
        /// When true, the snippet is withheld from the frontend (insert menu and
        /// completion) but still contributes to the linter's built-in name sets.
        /// Use for engine-internal identifiers that must lint as defined without
        /// being offered to the user.
        /// </summary>
        public bool InternalOnly { get; init; }

        /// <summary>
        /// Quick typing shortcut without the ~ prefix, so "a" means typing ~a followed by space
        /// inserts this symbol. Null for snippets without quick typing support.
        /// </summary>
        public string? QuickType { get; init; }

        /// <summary>
        /// Array of parameters with expected types (for functions).
        /// Null for non-function snippets like constants or keywords.
        /// </summary>
        public SnippetParameter[]? Parameters { get; init; }

        /// <summary>
        /// When true, parameter count validation is skipped entirely.
        /// Use for functions where any number of parameters is valid (e.g., switch, gcd, lcm).
        /// </summary>
        public bool AcceptsAnyCount { get; init; }

        #region Function Signature Properties (for linting)

        /// <summary>
        /// Minimum number of required parameters (derived from Parameters array).
        /// Returns 0 if AcceptsAnyCount is true.
        /// </summary>
        public int MinParams => AcceptsAnyCount ? 0 : Parameters?.Count(p => !p.IsOptional && !p.IsVariadic) ?? 0;

        /// <summary>
        /// Maximum number of parameters. -1 if variadic or AcceptsAnyCount (unlimited).
        /// Derived from Parameters array.
        /// </summary>
        public int MaxParams => AcceptsAnyCount || Parameters?.Any(p => p.IsVariadic) == true
            ? -1
            : Parameters?.Length ?? 0;

        /// <summary>
        /// Return type of the function. Null for non-functions.
        /// </summary>
        public CalcpadType? ReturnType { get; init; }

        /// <summary>
        /// Whether the function can operate element-wise on vectors/matrices.
        /// </summary>
        public bool IsElementWise { get; init; }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Gets the expected parameter type for a given parameter index.
        /// For variadic functions, returns the variadic parameter's type for indices beyond defined parameters.
        /// </summary>
        public ParameterType GetParameterType(int index)
        {
            if (Parameters == null || Parameters.Length == 0)
                return ParameterType.Any;

            if (index < Parameters.Length)
                return Parameters[index].Type;

            // For variadic functions, use the variadic parameter's type
            var variadicParam = Parameters.FirstOrDefault(p => p.IsVariadic);
            if (variadicParam != null)
                return variadicParam.Type;

            // Beyond parameters and not variadic - shouldn't happen but return Any
            return ParameterType.Any;
        }

        #endregion
    }

    /// <summary>
    /// Represents a parameter in a function snippet.
    /// </summary>
    public class SnippetParameter
    {
        /// <summary>
        /// Parameter name (e.g., "x", "M", "v").
        /// </summary>
        public required string Name { get; init; }

        /// <summary>
        /// Expected type constraint for validation.
        /// </summary>
        public ParameterType Type { get; init; } = ParameterType.Any;

        /// <summary>
        /// Human-readable type description for display (e.g., "Angle in radians").
        /// If null, uses the Type enum name.
        /// </summary>
        public string? TypeDescription { get; init; }

        /// <summary>
        /// Optional description of the parameter's purpose.
        /// </summary>
        public string? Description { get; init; }

        /// <summary>
        /// Whether this parameter is optional.
        /// </summary>
        public bool IsOptional { get; init; }

        /// <summary>
        /// For variadic parameters, marks the repeating parameter: its type applies to all
        /// remaining arguments, of which a function can accept unlimited many.
        /// </summary>
        public bool IsVariadic { get; init; }
    }

    /// <summary>
    /// Represents a parameter type constraint for function validation.
    /// </summary>
    public enum ParameterType
    {
        /// <summary>Any type allowed</summary>
        Any,

        /// <summary>Scalar value only</summary>
        Scalar,

        /// <summary>Vector only</summary>
        Vector,

        /// <summary>Matrix only</summary>
        Matrix,

        /// <summary>Integer value (for indices, counters)</summary>
        Integer,

        /// <summary>Boolean/condition expression</summary>
        Boolean,

        /// <summary>Expression (unevaluated, for special functions)</summary>
        Expression,

        /// <summary>Various types accepted - type checking should be skipped for this parameter</summary>
        Various
    }
}
