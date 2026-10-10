using System;
using System.Collections.Generic;

namespace Calcpad.Core
{
    public partial class MathParser
    {
        private RuntimeTrace _trace;
        private readonly HashSet<string> _watched = new(StringComparer.Ordinal);
        private int _watchedCount;

        internal RuntimeTrace Trace
        {
            get => _trace;
            set
            {
                _trace = value;
                _watched.Clear();
                // Built-in constants already exist and are not traced.
                foreach (var name in _variables.Keys)
                    _watched.Add(name);

                _watchedCount = _variables.Count;
            }
        }

        /// <summary>
        /// Starts watching globals created since the last call. Globals are added from several
        /// places (identifier binding, SetVariable, #read, lu), so this catches them all at once.
        /// </summary>
        internal void SyncTrace()
        {
            if (_variables.Count == _watchedCount)
                return;

            foreach (var (name, variable) in _variables)
                if (_watched.Add(name) && name is not ("ans" or "ANS"))
                    Watch(name, variable);

            _watchedCount = _variables.Count;
        }

        // OnChange is raised by Variable.Assign, which both interpreted and compiled code use.
        private void Watch(string name, Variable variable)
        {
            var lastLine = -1;
            TraceValueKind? lastKind = null;
            Record();
            variable.OnChange += Record;

            void Record()
            {
                var kind = RuntimeTrace.KindOf(variable.Value);
                if (kind is null || kind == lastKind && Line == lastLine)
                    return;

                lastKind = kind;
                lastLine = Line;
                _trace.Assign(name, kind.Value, Line);
            }
        }
    }
}
