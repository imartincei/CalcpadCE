namespace Calcpad.Core
{
    public sealed class CalcpadError
    {
        public int SourceLine { get; init; }
        public int OutputLine { get; init; }
        public string Message { get; init; }
        public CalcpadErrorSource Source { get; init; }
    }

    public enum CalcpadErrorSource
    {
        Macro,
        Expression
    }
}
