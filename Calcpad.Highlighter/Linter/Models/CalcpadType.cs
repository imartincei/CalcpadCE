namespace Calcpad.Highlighter.Linter.Models
{
    /// <summary>
    /// Represents the type of a Calcpad variable or definition.
    /// </summary>
    public enum CalcpadType
    {
        /// <summary>
        /// Unknown or unresolved type
        /// </summary>
        Unknown = 0,

        /// <summary>
        /// Scalar numeric value (real or complex)
        /// </summary>
        Value,

        /// <summary>
        /// One-dimensional array of values
        /// </summary>
        Vector,

        /// <summary>
        /// Two-dimensional array of values
        /// </summary>
        Matrix,

        /// <summary>
        /// Custom unit definition (.unitName = expression)
        /// </summary>
        CustomUnit,

        /// <summary>
        /// User-defined function (name(params) = expression)
        /// </summary>
        Function,

        /// <summary>
        /// Inline macro definition (#def name$(params) = expression)
        /// </summary>
        InlineMacro,

        /// <summary>
        /// Multiline macro definition (#def name$(params) ... #end def)
        /// </summary>
        MultilineMacro,

        /// <summary>
        /// Variable type changed during execution - less strict linting applies
        /// </summary>
        Various
    }
}
