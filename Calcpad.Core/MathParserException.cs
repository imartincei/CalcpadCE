using System;

namespace Calcpad.Core
{
    public class MathParserException : Exception
    {
        internal MathParserException(string message, string code, CalcpadErrorKind kind) : base(message)
        {
            Code = code;
            Kind = kind;
        }

        public string Code { get; }
        public CalcpadErrorKind Kind { get; }

        /// <summary>Offset in the parsed expression where the error was found, or -1.</summary>
        internal int Position { get; set; } = -1;

        /// <summary>Offset past the error's last character, or -1 to widen <see cref="Position"/> to its word.</summary>
        internal int EndPosition { get; set; } = -1;

        /// <summary>The missing variable or function, for errors a later definition can fix.</summary>
        internal string Name { get; init; }

        /// <summary>Moves the position into the text that contains the parsed one, or drops it when that offset is unknown.</summary>
        internal void Shift(int offset)
        {
            if (Position < 0)
                return;
            if (offset < 0)
            {
                Position = EndPosition = -1;
                return;
            }
            Position += offset;
            if (EndPosition >= 0)
                EndPosition += offset;
        }
    }

    internal sealed class ComplexInRealModeException(string message)
        : MathParserException(message, "CPD-5537", CalcpadErrorKind.Values);
}
