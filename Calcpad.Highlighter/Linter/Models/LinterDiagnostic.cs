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

        /// <summary>A macro body error whose call the run already marked, so only the body is reported.</summary>
        internal bool IsBodyOnly { get; set; }

        /// <summary>An included macro body error whose call the run already marked, so it is kept only for the include.</summary>
        internal bool IsIncludeOnly { get; set; }

        /// <summary>
        /// Additional ranges to highlight when the diagnostic spans multiple lines
        /// (e.g., due to line continuations). These are in addition to the primary
        /// Line/Column/EndColumn range.
        /// </summary>
        public List<DiagnosticRange> AdditionalRanges { get; set; }

        /// <summary>Other places this error involves, such as the macro or function body behind a call.</summary>
        public List<DiagnosticRelated> Related { get; set; }

        /// <summary>For a diagnostic on an #include line, where its errors are in the included files.</summary>
        public List<IncludeOrigin> IncludeOrigins { get; set; }

        /// <summary>Where the message starts after its included file and line prefix.</summary>
        internal int IncludeMessageStart { get; set; }
    }

    /// <summary>An original source location linked to a diagnostic.</summary>
    public class DiagnosticRelated : DiagnosticRange
    {
        public string Message { get; set; }
    }

    /// <summary>A position in an included file. Column is -1 for the whole line.</summary>
    public class IncludeOrigin : DiagnosticRange
    {
        public string FilePath { get; set; }

        /// <summary>The diagnostic's message without its included file and line prefix.</summary>
        public string Message { get; set; }
    }
}
