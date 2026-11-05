namespace Calcpad.Core
{
    public sealed class CalcpadError
    {
        public int SourceLine { get; init; }
        public int OutputLine { get; init; }
        public string Message { get; init; }
        public CalcpadErrorSource Source { get; init; }

        /// <summary>Stable CPD-xxxx code, shared with the linter where they report the same problem.</summary>
        public string Code { get; init; }
    }

    public enum CalcpadErrorSource
    {
        Macro,
        Expression
    }
}
