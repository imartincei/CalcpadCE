using System.Collections.Generic;

namespace Calcpad.Highlighter.Linter.Models
{
    /// <summary>
    /// A region of original source lines in which specific diagnostic codes are suppressed.
    /// Extracted from <c>&lt;!--{"LintIgnore": [...]}</c> HTML comment blocks.
    /// </summary>
    public sealed record LintIgnoreRegion(
        /// <summary>First suppressed original source line (0-based, inclusive).</summary>
        int StartLine,
        /// <summary>Last suppressed original source line (0-based, inclusive).
        /// <see cref="int.MaxValue"/> means suppress to end of file.</summary>
        int EndLine,
        /// <summary>
        /// CPD codes to suppress. An empty set means suppress all codes within the region.
        /// </summary>
        IReadOnlySet<string> Codes
    );
}
