using System;
using System.Collections.Generic;
using Calcpad.Highlighter.Linter.Helpers;

namespace Calcpad.Highlighter.Linter.Models
{
    public class LinterResult
    {
        public List<LinterDiagnostic> Diagnostics { get; set; } = new();
        private int _errorCount;
        private int _warningCount;

        public bool HasErrors => _errorCount > 0;
        public bool HasWarnings => _warningCount > 0;
        public int ErrorCount => _errorCount;
        public int WarningCount => _warningCount;

        internal void AppendDiagnostic(LinterDiagnostic diagnostic)
        {
            Diagnostics.Add(diagnostic);
            switch (diagnostic.Severity)
            {
                case LinterSeverity.Error: _errorCount++; break;
                case LinterSeverity.Warning: _warningCount++; break;
            }
        }

        internal void RemoveDiagnostics(Predicate<LinterDiagnostic> match)
        {
            Diagnostics.RemoveAll(d =>
            {
                if (!match(d)) return false;
                switch (d.Severity)
                {
                    case LinterSeverity.Error: _errorCount--; break;
                    case LinterSeverity.Warning: _warningCount--; break;
                }
                return true;
            });
        }

        // Stage contexts for line continuation mapping
        internal Stage1Context Stage1Context { get; set; }
        internal Stage2Context Stage2Context { get; set; }
        internal Stage3Context Stage3Context { get; set; }

        /// <summary>
        /// Sets the stage contexts for automatic line continuation mapping.
        /// Call this once before validation begins.
        /// </summary>
        internal void SetStageContexts(Stage1Context stage1, Stage2Context stage2, Stage3Context stage3)
        {
            Stage1Context = stage1;
            Stage2Context = stage2;
            Stage3Context = stage3;
        }

        /// <summary>
        /// Checks if line continuation mapping is available.
        /// </summary>
        internal bool HasLineContinuationMapping =>
            Stage1Context?.LineContinuationSegments != null &&
            Stage1Context.LineContinuationSegments.Count > 0;

        /// <summary>
        /// Maps all diagnostics from their stage line numbers to original line numbers.
        /// Call this after all validation is complete, before returning the result.
        /// </summary>
        internal void MapDiagnosticsToOriginal()
        {
            if (Stage1Context == null)
                return;

            foreach (var diagnostic in Diagnostics)
            {
                MapDiagnosticToOriginal(diagnostic);
            }
        }

        /// <summary>
        /// Maps a single diagnostic from its stage line to the original line.
        /// </summary>
        private void MapDiagnosticToOriginal(LinterDiagnostic diagnostic)
        {
            switch (diagnostic.Stage)
            {
                case LineStage.Stage1:
                    diagnostic.Line = SourceMapper.MapStage1ToOriginal(diagnostic.StageLine, Stage1Context);
                    break;

                case LineStage.Stage2:
                    if (Stage2Context != null)
                    {
                        diagnostic.Line = SourceMapper.MapStage2ToOriginal(diagnostic.StageLine, Stage2Context, Stage1Context);
                    }
                    break;

                case LineStage.Stage3:
                    if (Stage2Context != null && Stage3Context != null)
                    {
                        // Use full mapping with line continuation support
                        var mapped = SourceMapper.MapPositionToOriginal(
                            diagnostic.StageLine,
                            diagnostic.Column,
                            diagnostic.EndColumn,
                            Stage3Context,
                            Stage2Context,
                            Stage1Context);

                        diagnostic.Line = mapped.Line;
                        diagnostic.Column = mapped.Column;
                        diagnostic.EndColumn = mapped.EndColumn;

                        // Add additional ranges if the diagnostic spans multiple lines
                        if (mapped.AdditionalRanges != null && mapped.AdditionalRanges.Count > 0)
                        {
                            diagnostic.AdditionalRanges = new List<DiagnosticRange>();
                            foreach (var range in mapped.AdditionalRanges)
                            {
                                diagnostic.AdditionalRanges.Add(new DiagnosticRange
                                {
                                    Line = range.Line,
                                    Column = range.Column,
                                    EndColumn = range.EndColumn
                                });
                            }
                        }
                    }
                    break;
            }
        }
    }
}
