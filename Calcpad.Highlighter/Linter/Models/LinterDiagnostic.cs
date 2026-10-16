using System.Collections.Generic;
using Calcpad.Highlighter.Linter.Helpers;

namespace Calcpad.Highlighter.Linter.Models
{
    /// <summary>
    /// Represents a range in the source code for highlighting.
    /// </summary>
    public class DiagnosticRange
    {
        public int Line { get; set; }       // 0-based line number
        public int Column { get; set; }     // 0-based start column
        public int EndColumn { get; set; }  // End column for highlighting
    }

    public class LinterDiagnostic
    {
        public int Line { get; set; }              // 0-based original line number (after mapping)
        public int Column { get; set; }            // 0-based column position
        public int EndColumn { get; set; }         // End position for highlighting
        public string Code { get; set; }           // Error code (e.g., "CPD-1101")
        public string Message { get; set; }        // Human-readable message
        public LinterSeverity Severity { get; set; }
        public string Source { get; set; } = "Calcpad Linter";

        /// <summary>
        /// The line number in the processing stage (before mapping to original).
        /// Used internally for deferred line mapping.
        /// </summary>
        internal int StageLine { get; set; }

        /// <summary>
        /// Which processing stage the StageLine refers to.
        /// Used internally for deferred line mapping.
        /// </summary>
        internal LineStage Stage { get; set; } = LineStage.Stage3;

        /// <summary>
        /// Additional ranges to highlight when the diagnostic spans multiple lines
        /// (e.g., due to line continuations). These are in addition to the primary
        /// Line/Column/EndColumn range.
        /// </summary>
        public List<DiagnosticRange> AdditionalRanges { get; set; }
    }
}
