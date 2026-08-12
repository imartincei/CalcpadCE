using System.Collections.Generic;

namespace Calcpad.Highlighter.Linter.Models
{
    /// <summary>
    /// Holds type information and metadata about a Calcpad variable or definition.
    /// </summary>
    public class VariableInfo
    {
        /// <summary>
        /// The name of the variable/function/macro
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// The inferred type of the variable
        /// </summary>
        public CalcpadType Type { get; set; } = CalcpadType.Unknown;

        /// <summary>
        /// Line number where defined (0-based)
        /// </summary>
        public int LineNumber { get; set; }

        /// <summary>
        /// Column where defined (0-based)
        /// </summary>
        public int Column { get; set; }

        /// <summary>
        /// For functions/macros: parameter names
        /// </summary>
        public List<string> Parameters { get; set; } = new();

        /// <summary>
        /// For functions/macros: parameter count
        /// </summary>
        public int ParameterCount => Parameters.Count;

        /// <summary>
        /// The raw expression on the right side of the assignment
        /// </summary>
        public string Expression { get; set; } = string.Empty;

        /// <summary>
        /// For functions: the inferred return type based on the function body expression.
        /// For command block functions: inferred from the last statement.
        /// </summary>
        public CalcpadType ReturnType { get; set; } = CalcpadType.Unknown;

        /// <summary>
        /// For custom units: the unit name without the leading dot
        /// </summary>
        public string UnitName { get; set; } = string.Empty;

        /// <summary>
        /// Whether this variable supports element access via .() syntax.
        /// Various type allows it since we can't be certain.
        /// </summary>
        public bool SupportsElementAccess =>
            Type == CalcpadType.Vector ||
            Type == CalcpadType.Matrix ||
            Type == CalcpadType.Various ||
            Type == CalcpadType.Unknown;

        /// <summary>
        /// Whether this variable/function was defined with #const (readonly)
        /// </summary>
        public bool IsConst { get; set; }

        /// <summary>
        /// Whether this is a $ suffixed identifier (macro or string variable)
        /// </summary>
        public bool IsDollarSuffixed => Name.EndsWith("$");

        /// <summary>
        /// Source of the definition (local file or included file)
        /// </summary>
        public string Source { get; set; } = "local";

        /// <summary>User-provided description from a metadata comment on the preceding line.</summary>
        public string Description { get; set; }

        /// <summary>User-provided type hints per parameter (e.g., "vector", "scalar").</summary>
        public List<string> ParamTypes { get; set; }

        /// <summary>User-provided descriptions per parameter.</summary>
        public List<string> ParamDescriptions { get; set; }

        public override string ToString()
        {
            return Type switch
            {
                CalcpadType.Function => Name + "(" + string.Join("; ", Parameters) + ")",
                CalcpadType.InlineMacro or CalcpadType.MultilineMacro => Name + (Parameters.Count > 0
                    ? "(" + string.Join("; ", Parameters) + ")"
                    : ""),
                CalcpadType.CustomUnit => "." + UnitName,
                _ => Name
            };
        }
    }
}
