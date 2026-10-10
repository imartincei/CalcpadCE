using System;
using System.Collections.Generic;
using Calcpad.Core;
using Calcpad.Highlighter.Linter.Models;

namespace Calcpad.Highlighter.ContentResolution
{
    public enum LineExecution
    {
        Unknown,
        Executed,
        NotExecuted
    }

    /// <summary>
    /// Answers questions about Stage 3 lines from a <see cref="RuntimeTrace"/> recorded by Core
    /// for the same content. Trace lines are 1-based root document lines.
    /// </summary>
    public sealed class RuntimeOverlay
    {
        private readonly RuntimeTrace _trace;
        private readonly Dictionary<string, List<(int Line, TraceValueKind Kind)>> _assignments = new(StringComparer.Ordinal);
        private readonly int[][] _traceLines;
        // Last line of a continued statement → its first line, both 1-based.
        private readonly Dictionary<int, int> _statementStarts = [];

        public RuntimeOverlay(RuntimeTrace trace, StagedResolvedContent staged)
        {
            _trace = trace;
            foreach (var ((name, line), kind) in trace.Assignments)
            {
                if (!_assignments.TryGetValue(name, out var list))
                    _assignments[name] = list = [];

                list.Add((line, kind));
            }
            foreach (var list in _assignments.Values)
                list.Sort((a, b) => a.Line.CompareTo(b.Line));

            var stage3 = staged.Stage3;
            _traceLines = new int[stage3.Lines.Count][];
            for (var i = 0; i < _traceLines.Length; i++)
            {
                var lines = _traceLines[i] = GetTraceLines(staged, i);
                if (lines.Length > 1)
                    _statementStarts[lines[^1]] = lines[0];
            }
        }

        public RuntimeTrace Trace => _trace;

        public LineExecution GetExecution(int stage3Line)
        {
            if (TryGetState(stage3Line, out _))
                return LineExecution.Executed;

            return _trace.IsComplete ? LineExecution.NotExecuted : LineExecution.Unknown;
        }

        public TraceOutputMode? GetOutputMode(int stage3Line) =>
            TryGetState(stage3Line, out var state) ? state.Output : null;

        /// <summary>
        /// The kind of the last assignment before the line, or the final kind when none
        /// precedes it. Null when the trace never assigned the variable.
        /// </summary>
        public CalcpadType? GetVariableType(string name, int stage3Line)
        {
            if (!_assignments.TryGetValue(name, out var list))
                return null;

            var lines = GetLines(stage3Line);
            var position = lines.Length == 0 ? int.MaxValue : lines[0];
            TraceValueKind? kind = null;
            foreach (var (line, k) in list)
            {
                if (line >= position)
                    break;

                kind = k;
            }
            return ToCalcpadType(kind ?? _trace.Variables[name].FinalKind);
        }

        /// <summary>The variable's type over the whole run: its kind, or Various if it changed.</summary>
        public CalcpadType? GetVariableType(string name) =>
            _trace.Variables.TryGetValue(name, out var variable)
                ? variable.KindChanged ? CalcpadType.Various : ToCalcpadType(variable.FinalKind)
                : null;

        /// <summary>
        /// The 0-based root document line where each new type starts (a continued statement's
        /// first line), for a variable whose kind changed. Null when it never changed.
        /// </summary>
        public List<(int Line, CalcpadType Type)> GetTypeChanges(string name)
        {
            if (!_trace.Variables.TryGetValue(name, out var variable) || !variable.KindChanged)
                return null;

            var changes = new List<(int, CalcpadType)>();
            TraceValueKind? previous = null;
            foreach (var (line, kind) in _assignments[name])
            {
                if (kind == previous)
                    continue;

                changes.Add((_statementStarts.GetValueOrDefault(line, line) - 1, ToCalcpadType(kind)));
                previous = kind;
            }
            return changes;
        }

        public TraceVariable GetFirstDefinition(string name) =>
            _trace.Variables.GetValueOrDefault(name);

        private int[] GetLines(int stage3Line) =>
            stage3Line >= 0 && stage3Line < _traceLines.Length ? _traceLines[stage3Line] : [];

        private bool TryGetState(int stage3Line, out TraceLineState state)
        {
            foreach (var line in GetLines(stage3Line))
                if (_trace.Lines.TryGetValue(line, out state))
                    return true;

            state = default;
            return false;
        }

        private static CalcpadType ToCalcpadType(TraceValueKind kind) => kind switch
        {
            TraceValueKind.Vector => CalcpadType.Vector,
            TraceValueKind.Matrix => CalcpadType.Matrix,
            _ => CalcpadType.Value
        };

        // Every physical line a Stage 3 line came from, ascending. Core reports a continued
        // statement on its last physical line.
        private static int[] GetTraceLines(StagedResolvedContent staged, int stage3Line)
        {
            if (!staged.Stage3.SourceMap.TryGetValue(stage3Line, out var stage2Line) ||
                !staged.Stage2.SourceMap.TryGetValue(stage2Line, out var stage1Line))
                return [];

            var stage1 = staged.Stage1;
            if (stage1.LineContinuationSegments != null &&
                stage1.LineContinuationSegments.TryGetValue(stage1Line, out var segments) &&
                segments.Count > 0)
            {
                var lines = new int[segments.Count];
                for (var i = 0; i < lines.Length; i++)
                    lines[i] = segments[i].OriginalLine + 1;

                return lines;
            }
            return [stage1.SourceMap.GetValueOrDefault(stage1Line, stage1Line) + 1];
        }
    }
}
