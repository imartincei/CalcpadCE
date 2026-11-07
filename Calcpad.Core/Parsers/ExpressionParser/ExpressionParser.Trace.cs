namespace Calcpad.Core
{
    public partial class ExpressionParser
    {
        /// <summary>What the last run executed. Only recorded when <see cref="Debug"/> is set.</summary>
        public RuntimeTrace Trace { get; private set; }

        private void TraceLine() => Trace.MarkLine(_parser.Line, new(
            (TraceOutputMode)_isVal,
            _isVisible,
            (TraceAngleUnits)_parser.Degrees,
            _parser.Phasor,
            _isMarkdownOn,
            _parser.Split,
            (TraceSubstitution)_parser.VariableSubstitution,
            (TraceParseMode)_parseMode));

        private void TraceExpression(string expression)
        {
            Trace.CurrentExpression = expression;
            Trace.PendingTarget = null;
        }
    }
}
