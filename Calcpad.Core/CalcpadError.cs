namespace Calcpad.Core
{
    public sealed class CalcpadError
    {
        public int SourceLine { get; init; }
        public int OutputLine { get; init; }
        public string Message { get; init; }
        public CalcpadErrorSource Source { get; init; }

        /// <summary>Stable CPD-xxxx code, shared with the linter where they report the same problem.</summary>
        public string Code { get; init; }

        public CalcpadErrorKind Kind { get; init; }

        /// <summary>0-based columns in the parsed line text, end exclusive. -1 when the error covers the whole line.</summary>
        public int StartColumn { get; init; } = -1;
        public int EndColumn { get; init; } = -1;
    }

    /// <summary>What an error depends on, which decides the pass that reports it.</summary>
    public enum CalcpadErrorKind
    {
        /// <summary>The line's own text.</summary>
        Syntax,
        /// <summary>Block balance and macro definitions.</summary>
        Structure,
        /// <summary>Which names are defined, so order and branches.</summary>
        Names,
        /// <summary>Computed values: types, units, dimensions, indexes, solvers and files.</summary>
        Values
    }

    public enum CalcpadErrorSource
    {
        Macro,
        Expression
    }
}
