using System.Collections.Generic;
using Calcpad.Highlighter.ContentResolution;
using Calcpad.Highlighter.Linter.Models;

namespace Calcpad.Highlighter.Linter.Helpers
{
    /// <summary>
    /// Result of mapping a position from a processed stage line back to the original source.
    /// Contains the original line/column as well as the end column for proper highlighting.
    /// </summary>
    public class MappedPosition
    {
        /// <summary>Original line number (0-based)</summary>
        public int Line { get; set; }

        /// <summary>Column in the original line (0-based)</summary>
        public int Column { get; set; }

        /// <summary>End column in the original line for highlighting</summary>
        public int EndColumn { get; set; }

        /// <summary>
        /// If the diagnostic spans multiple original lines (due to line continuations),
        /// this contains additional line ranges to highlight.
        /// </summary>
        public List<(int Line, int Column, int EndColumn)> AdditionalRanges { get; set; }
    }

    public static class SourceMapper
    {
        public static int MapStage1ToOriginal(int stage1Line, Stage1Context stage1)
        {
            return stage1.SourceMap.TryGetValue(stage1Line, out var original) ? original : stage1Line;
        }

        public static int MapStage2ToOriginal(int stage2Line, Stage2Context stage2, Stage1Context stage1)
        {
            var stage1Line = stage2.Stage2ToStage1Map.TryGetValue(stage2Line, out var s1) ? s1 : stage2Line;
            return stage1.SourceMap.TryGetValue(stage1Line, out var original) ? original : stage1Line;
        }

        public static int MapStage3ToOriginal(int stage3Line, Stage3Context stage3, Stage2Context stage2, Stage1Context stage1)
        {
            var stage2Line = stage3.Stage3ToStage2Map.TryGetValue(stage3Line, out var s2) ? s2 : stage3Line;
            var stage1Line = stage2.Stage2ToStage1Map.TryGetValue(stage2Line, out var s1) ? s1 : stage2Line;
            return stage1.SourceMap.TryGetValue(stage1Line, out var original) ? original : stage1Line;
        }

        /// <summary>
        /// Maps a position from a Stage 3 line back to the original source, handling line continuations.
        /// When the position falls within a merged continuation line, this returns the correct
        /// original line and column.
        /// </summary>
        /// <param name="stage3Line">Line number in Stage 3</param>
        /// <param name="column">Column in the Stage 3 line</param>
        /// <param name="endColumn">End column in the Stage 3 line</param>
        /// <param name="stage3">Stage 3 context</param>
        /// <param name="stage2">Stage 2 context</param>
        /// <param name="stage1">Stage 1 context</param>
        /// <returns>Mapped position with original line/column and any additional ranges</returns>
        public static MappedPosition MapPositionToOriginal(
            int stage3Line,
            int column,
            int endColumn,
            Stage3Context stage3,
            Stage2Context stage2,
            Stage1Context stage1)
        {
            // First, map from Stage 3 to Stage 1
            var stage2Line = stage3.Stage3ToStage2Map.TryGetValue(stage3Line, out var s2) ? s2 : stage3Line;
            var stage1Line = stage2.Stage2ToStage1Map.TryGetValue(stage2Line, out var s1) ? s1 : stage2Line;

            // Macro-expanded lines: the Stage 3 columns index into the substituted macro body,
            // which shares no layout with the call site, so highlight the whole call site line
            // instead of a range that would land on unrelated text (or past the end of the line).
            if (stage3.MacroExpansions != null && stage3.MacroExpansions.ContainsKey(stage3Line))
            {
                return MapWholeLineToOriginal(stage1Line, stage1);
            }

            // Check if this Stage 1 line has line continuation segments
            if (stage1.LineContinuationSegments.TryGetValue(stage1Line, out var segments) && segments.Count > 1)
            {
                return MapPositionWithContinuations(column, endColumn, segments);
            }

            // No line continuations - simple mapping
            var originalLine = stage1.SourceMap.TryGetValue(stage1Line, out var orig) ? orig : stage1Line;
            return new MappedPosition
            {
                Line = originalLine,
                Column = column,
                EndColumn = endColumn,
                AdditionalRanges = null
            };
        }

        /// <summary>
        /// Builds a position covering the full extent of a Stage 1 line in the original source.
        /// When the Stage 1 line is a continuation merge, every original line it was built from
        /// is covered: the first as the primary range, the rest as additional ranges.
        /// </summary>
        private static MappedPosition MapWholeLineToOriginal(int stage1Line, Stage1Context stage1)
        {
            if (stage1.LineContinuationSegments.TryGetValue(stage1Line, out var segments) && segments.Count > 0)
            {
                List<(int Line, int Column, int EndColumn)> additionalRanges = null;
                if (segments.Count > 1)
                {
                    additionalRanges = new List<(int, int, int)>(segments.Count - 1);
                    for (int i = 1; i < segments.Count; i++)
                    {
                        additionalRanges.Add((segments[i].OriginalLine, 0, segments[i].Length));
                    }
                }

                return new MappedPosition
                {
                    Line = segments[0].OriginalLine,
                    Column = 0,
                    EndColumn = segments[0].Length,
                    AdditionalRanges = additionalRanges
                };
            }

            var originalLine = stage1.SourceMap.TryGetValue(stage1Line, out var orig) ? orig : stage1Line;
            var length = stage1Line >= 0 && stage1Line < stage1.Lines.Count
                ? stage1.Lines[stage1Line].Length
                : 0;

            return new MappedPosition
            {
                Line = originalLine,
                Column = 0,
                EndColumn = length,
                AdditionalRanges = null
            };
        }

        /// <summary>
        /// Maps a position within a merged continuation line to the original line(s).
        /// </summary>
        private static MappedPosition MapPositionWithContinuations(
            int column,
            int endColumn,
            List<LineContinuationSegment> segments)
        {
            // Find which segment(s) the column range falls into
            LineContinuationSegment startSegment = null;
            LineContinuationSegment endSegment = null;
            int startOffsetInSegment = 0;
            int endOffsetInSegment = 0;

            foreach (var segment in segments)
            {
                var segmentEnd = segment.StartColumn + segment.Length;

                // Check if column falls within this segment
                if (startSegment == null && column >= segment.StartColumn && column < segmentEnd)
                {
                    startSegment = segment;
                    startOffsetInSegment = column - segment.StartColumn;
                }

                // Check if endColumn falls within this segment
                if (endColumn > segment.StartColumn && endColumn <= segmentEnd)
                {
                    endSegment = segment;
                    endOffsetInSegment = endColumn - segment.StartColumn;
                }

                // Handle case where endColumn is exactly at segment boundary
                if (endColumn == segment.StartColumn && endSegment == null)
                {
                    // End is at the very start of this segment, use previous segment's end
                    continue;
                }
            }

            // If we couldn't find the segments, use the first/last
            if (startSegment == null)
            {
                startSegment = segments[0];
                startOffsetInSegment = 0;
            }
            if (endSegment == null)
            {
                endSegment = segments[^1];
                endOffsetInSegment = endSegment.Length;
            }

            // If start and end are in the same segment, simple case
            if (startSegment.OriginalLine == endSegment.OriginalLine)
            {
                return new MappedPosition
                {
                    Line = startSegment.OriginalLine,
                    Column = startOffsetInSegment,
                    EndColumn = endOffsetInSegment,
                    AdditionalRanges = null
                };
            }

            // Range spans multiple original lines - return primary range and additional ranges
            var additionalRanges = new List<(int Line, int Column, int EndColumn)>();

            // Find all segments between start and end
            bool inRange = false;
            foreach (var segment in segments)
            {
                if (segment == startSegment)
                {
                    inRange = true;
                    continue; // Primary range handles the start segment
                }

                if (inRange)
                {
                    if (segment == endSegment)
                    {
                        // Final segment in the range
                        additionalRanges.Add((segment.OriginalLine, 0, endOffsetInSegment));
                        break;
                    }
                    else
                    {
                        // Middle segment - highlight the entire segment content
                        additionalRanges.Add((segment.OriginalLine, 0, segment.Length));
                    }
                }
            }

            return new MappedPosition
            {
                Line = startSegment.OriginalLine,
                Column = startOffsetInSegment,
                EndColumn = startSegment.Length, // Highlight to end of this line segment
                AdditionalRanges = additionalRanges
            };
        }

        /// <summary>
        /// For diagnostics that should highlight to the end of a line continuation segment,
        /// this returns the appropriate end column for the original line.
        /// </summary>
        public static int GetOriginalLineEndColumn(
            int stage1Line,
            int columnInMergedLine,
            Stage1Context stage1,
            List<string> originalLines)
        {
            if (!stage1.LineContinuationSegments.TryGetValue(stage1Line, out var segments))
            {
                // No continuation - return the line length or a reasonable default
                var originalLine = stage1.SourceMap.TryGetValue(stage1Line, out var orig) ? orig : stage1Line;
                if (originalLine < originalLines.Count)
                {
                    return originalLines[originalLine].Length;
                }
                return columnInMergedLine;
            }

            // Find which segment the column is in
            foreach (var segment in segments)
            {
                if (columnInMergedLine >= segment.StartColumn &&
                    columnInMergedLine < segment.StartColumn + segment.Length)
                {
                    // Return the end of this segment's content
                    return segment.Length;
                }
            }

            // Fallback
            return columnInMergedLine;
        }

        /// <summary>
        /// Creates a LinterDiagnostic with proper line continuation mapping.
        /// This maps the position from the processed (merged) line back to the original source,
        /// handling cases where the diagnostic spans multiple original lines.
        /// </summary>
        public static Models.LinterDiagnostic CreateMappedDiagnostic(
            int stage3Line,
            int column,
            int endColumn,
            string code,
            string message,
            Models.LinterSeverity severity,
            Stage3Context stage3,
            Stage2Context stage2,
            Stage1Context stage1)
        {
            var mappedPos = MapPositionToOriginal(stage3Line, column, endColumn, stage3, stage2, stage1);

            var diagnostic = new Models.LinterDiagnostic
            {
                Line = mappedPos.Line,
                Column = mappedPos.Column,
                EndColumn = mappedPos.EndColumn,
                Code = code,
                Message = message,
                Severity = severity
            };

            // Add additional ranges if the diagnostic spans multiple lines
            if (mappedPos.AdditionalRanges != null && mappedPos.AdditionalRanges.Count > 0)
            {
                diagnostic.AdditionalRanges = new List<Models.DiagnosticRange>();
                foreach (var range in mappedPos.AdditionalRanges)
                {
                    diagnostic.AdditionalRanges.Add(new Models.DiagnosticRange
                    {
                        Line = range.Line,
                        Column = range.Column,
                        EndColumn = range.EndColumn
                    });
                }
            }

            return diagnostic;
        }
    }
}
