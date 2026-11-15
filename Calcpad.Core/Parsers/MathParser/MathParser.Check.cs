using System;
using System.Collections.Generic;

namespace Calcpad.Core
{
    public partial class MathParser
    {
        /// <summary>Check pass only: global names read by each parsed expression, less its own parameters and counters.</summary>
        internal List<ReadName> ReadNames { get; set; }
        private readonly List<ReadName> _pendingNames = [];

        /// <summary>A name and its offset in the outermost parsed expression, or -1.</summary>
        internal readonly record struct ReadName(string Name, int Start);

        /// <summary>Check pass only: custom functions each parsed expression calls.</summary>
        internal List<FunctionCall> FunctionCalls { get; set; }

        /// <summary>A call from the function name to its closing bracket, as offsets in the outermost parsed expression, or -1.</summary>
        internal readonly record struct FunctionCall(string Name, int Start, int End);

        /// <summary>Where the expression being parsed starts in the outermost one.</summary>
        internal int NameOffset { get; set; }
        private readonly HashSet<string> _boundNames = new(StringComparer.Ordinal);
        private int _parseDepth;
        // lu() creates "ind" when it runs
        private bool _hasLu;

        internal void RecordReadName(string name, int start)
        {
            if (ReadNames is not null)
                _pendingNames.Add(new(name, Offset(start)));
        }

        internal void UnrecordReadName(string name)
        {
            if (ReadNames is null)
                return;
            var i = _pendingNames.FindIndex(n => n.Name == name);
            if (i >= 0)
                _pendingNames.RemoveAt(i);
        }

        private void RecordFunctionCall(string name, int start, int end)
        {
            start = Offset(start);
            end = start >= 0 && end > 0 ? Offset(end) : -1;
            FunctionCalls.Add(new(name, start, end));
        }

        private int Offset(int start) => start >= 0 && NameOffset >= 0 ? NameOffset + start : -1;

        internal void BindName(string name)
        {
            if (ReadNames is not null)
                _boundNames.Add(name);
        }

        internal bool IsFunctionDefinition => _functionDefinitionIndex >= 0;

        internal string DefinedFunctionName => IsFunctionDefinition ? _functions.LastName : null;

        /// <summary>The function the last parsed expression defines, or null.</summary>
        internal string DefinedFunction { get; private set; }

        internal bool IsDefinedFunction(string name) => _functions.IndexOf(name) >= 0;

        internal bool IsDefinedName(string name) =>
            _input.DefinedVariables.Contains(name) ||
            _hasLu && name == "ind" ||
            _units.ContainsKey(name) ||
            Unit.TryGet(name, IsUs, out _);

        private void ParseRecordingNames(ReadOnlySpan<char> expression, bool allowAssignment)
        {
            var isOuter = _parseDepth++ == 0;
            _hasLu = _hasLu || System.Text.RegularExpressions.Regex.IsMatch(expression, @"(?<![\w.])lu\s*\(");
            try
            {
                ParseExpression(expression, allowAssignment);
                if (isOuter)
                    foreach (var name in _pendingNames)
                        if (!_boundNames.Contains(name.Name))
                            ReadNames.Add(name);
            }
            finally
            {
                --_parseDepth;
                if (isOuter)
                {
                    _pendingNames.Clear();
                    _boundNames.Clear();
                }
            }
        }
    }
}
