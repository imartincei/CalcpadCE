using System.Collections.Generic;

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

        /// <summary>Only the check pass reports warnings.</summary>
        public CalcpadErrorSeverity Severity { get; init; }

        /// <summary>0-based columns in the parsed line text, end exclusive. -1 when the error covers the whole line.</summary>
        public int StartColumn { get; init; } = -1;
        public int EndColumn { get; init; } = -1;

        /// <summary>For an error in a custom function body: the call it was found at.</summary>
        public CalcpadCallSite CallSite { get; init; }

        /// <summary>The error is in a custom function body, which the run only checks when the function is called.</summary>
        public bool IsFunctionBody { get; init; }

        /// <summary>For an error raised while calculating custom functions: those functions, innermost first, or null.</summary>
        public IReadOnlyList<CalcpadFunctionFrame> Functions { get; init; }
    }

    /// <summary>A custom function and the first source line of its definition.</summary>
    public sealed record CalcpadFunctionFrame(string Name, int SourceLine);

    /// <summary>A call to a custom function. Columns as in <see cref="CalcpadError"/>.</summary>
    public sealed record CalcpadCallSite(string Function, int SourceLine, int StartColumn, int EndColumn);

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

    public enum CalcpadErrorSeverity
    {
        Error,
        Warning
    }

    public enum CalcpadErrorSource
    {
        Macro,
        Expression
    }
}
