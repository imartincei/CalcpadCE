using System.Collections.Generic;
using Calcpad.Highlighter.ContentResolution;

namespace Calcpad.Highlighter.Linter.Models
{
    public class Stage1Context : StageContext
    {
        /// <summary>
        /// Maps Stage1 line index to the list of original line indices that were merged
        /// via line continuation (_).
        /// </summary>
        public Dictionary<int, List<int>> LineContinuationMap { get; set; } = new();

        /// <summary>
        /// Maps Stage1 line index to detailed segment information for line continuations.
        /// Each segment contains the original line number, starting column in the merged line,
        /// and the length of the segment.
        /// </summary>
        public Dictionary<int, List<LineContinuationSegment>> LineContinuationSegments { get; set; } = new();
    }
}
