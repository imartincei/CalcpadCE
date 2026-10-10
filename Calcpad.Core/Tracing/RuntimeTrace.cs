using System;
using System.Collections.Generic;

namespace Calcpad.Core
{
    public enum TraceValueKind : byte { Scalar, Vector, Matrix }
    public enum TraceOutputMode : sbyte { NoCalculation = -1, Equations, Values }
    public enum TraceAngleUnits : byte { Degrees, Radians, Gradians }
    public enum TraceSubstitution : byte { VariablesAndSubstitutions, VariablesOnly, SubstitutionsOnly }
    public enum TraceParseMode : byte { Cpd, Html, Markdown }

    public readonly record struct TraceLineState(
        TraceOutputMode Output,
        bool IsVisible,
        TraceAngleUnits Angle,
        bool Phasor,
        bool IsMarkdown,
        bool Split,
        TraceSubstitution Substitution,
        TraceParseMode ParseMode);

    public sealed class TraceVariable
    {
        public string Name { get; init; }

        /// <summary>1-based source line of the first executed assignment.</summary>
        public int FirstLine { get; init; }

        /// <summary>Right-hand side of the first assignment, or null when it came from elsewhere (a loop counter, #read, a function body).</summary>
        public string FirstExpression { get; init; }

        public TraceValueKind FinalKind { get; internal set; }
        public bool KindChanged { get; internal set; }
    }

    /// <summary>
    /// What <see cref="ExpressionParser"/> executed, for tooling. Only created in Debug mode.
    /// Lines are 1-based source lines, as carried by MacroParser's line markers.
    /// </summary>
    public sealed class RuntimeTrace
    {
        private readonly Dictionary<int, TraceLineState> _lines = [];
        private readonly Dictionary<(string Name, int Line), TraceValueKind> _assignments = [];
        private readonly Dictionary<string, TraceVariable> _variables = new(StringComparer.Ordinal);

        /// <summary>State at each executed line's last execution. Lines in untaken branches are absent.</summary>
        public IReadOnlyDictionary<int, TraceLineState> Lines => _lines;

        /// <summary>Final kind assigned to each variable on each line.</summary>
        public IReadOnlyDictionary<(string Name, int Line), TraceValueKind> Assignments => _assignments;

        public IReadOnlyDictionary<string, TraceVariable> Variables => _variables;

        /// <summary>False when the run paused, was canceled or stopped on errors.</summary>
        public bool IsComplete { get; internal set; }

        internal string CurrentExpression;
        internal string PendingTarget;

        internal void MarkLine(int line, in TraceLineState state) => _lines[line] = state;

        internal void Assign(string name, TraceValueKind kind, int line)
        {
            _assignments[(name, line)] = kind;
            if (_variables.TryGetValue(name, out var variable))
            {
                if (variable.FinalKind != kind)
                {
                    variable.KindChanged = true;
                    variable.FinalKind = kind;
                }
                return;
            }
            _variables.Add(name, new TraceVariable
            {
                Name = name,
                FirstLine = line,
                FirstExpression = name == PendingTarget ? GetRightSide(CurrentExpression) : null,
                FinalKind = kind
            });
        }

        internal static TraceValueKind? KindOf(IValue value) => value switch
        {
            IScalarValue => TraceValueKind.Scalar,
            Vector => TraceValueKind.Vector,
            Matrix => TraceValueKind.Matrix,
            _ => null
        };

        private static string GetRightSide(string expression)
        {
            if (string.IsNullOrEmpty(expression))
                return null;

            var depth = 0;
            for (int i = 0, len = expression.Length; i < len; ++i)
            {
                var c = expression[i];
                if (c is '(' or '[' or '{')
                    ++depth;
                else if (c is ')' or ']' or '}')
                    --depth;
                else if (c == '=' && depth == 0)
                    return expression[(i + 1)..].Trim();
            }
            return expression.Trim();
        }
    }
}
