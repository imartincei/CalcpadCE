namespace Calcpad.Highlighter.Prettifier
{
    public class PrettifierOptions
    {
        public static readonly PrettifierOptions Default = new();

        /// <summary>String emitted per indent level. Default is a single tab.</summary>
        public string IndentUnit { get; set; } = "\t";

        /// <summary>Trim trailing whitespace from each line before re-emitting.</summary>
        public bool TrimTrailingWhitespace { get; set; } = true;
    }
}
