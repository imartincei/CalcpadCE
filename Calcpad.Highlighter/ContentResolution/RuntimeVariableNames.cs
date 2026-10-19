using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using Calcpad.Core;

namespace Calcpad.Highlighter.ContentResolution
{
    /// <summary>
    /// The variable names a document's last run assigned, kept across edits so unit-or-variable
    /// colors only change after a run.
    /// </summary>
    /// <param name="assigned">Names Core assigned during the run.</param>
    /// <param name="statics">Names the content that ran defines statically.</param>
    public sealed class RuntimeVariableNames(IEnumerable<string> assigned, IEnumerable<string> statics, bool isComplete)
    {
        private readonly FrozenSet<string> _assigned = assigned.ToFrozenSet(StringComparer.Ordinal);
        private readonly FrozenSet<string> _statics = statics.ToFrozenSet(StringComparer.Ordinal);

        public static RuntimeVariableNames From(RuntimeTrace trace, StagedResolvedContent staged) =>
            new(trace.Variables.Keys, staged.Stage3.VariableFirstLines.Keys, trace.IsComplete);

        /// <summary>
        /// Whether a unit-named identifier is a variable. A static definition the complete run
        /// never reached (an untaken branch) leaves the unit; a name only the run assigned is a
        /// variable.
        /// </summary>
        /// <param name="definedBefore">The static answer for the current content.</param>
        public bool IsVariable(string name, bool definedBefore)
        {
            var isStatic = _statics.Contains(name);
            var isAssigned = _assigned.Contains(name);
            if (isStatic && !isAssigned && isComplete)
                return false;

            return definedBefore || !isStatic && isAssigned;
        }
    }
}
