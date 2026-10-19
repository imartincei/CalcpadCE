namespace Calcpad.Highlighter.Tokenizer.Models
{
    /// <summary>
    /// Represents a single token with position information for syntax highlighting
    /// </summary>
    public readonly struct Token
    {
        /// <summary>Zero-based line number</summary>
        public int Line { get; }

        /// <summary>Zero-based column (character offset from start of line)</summary>
        public int Column { get; }

        /// <summary>Length of the token in characters</summary>
        public int Length { get; }

        /// <summary>The token type for colorization</summary>
        public TokenType Type { get; }

        /// <summary>The actual text content of the token</summary>
        public string Text { get; }

        public Token(int line, int column, int length, TokenType type, string text)
        {
            Line = line;
            Column = column;
            Length = length;
            Type = type;
            Text = text;
        }

        /// <summary>End column (exclusive)</summary>
        public int EndColumn => Column + Length;

        public override string ToString()
        {
            return $"[{Line}:{Column}-{EndColumn}] {Type}: \"{Text}\"";
        }
    }
}
