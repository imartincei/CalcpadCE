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
    }
}
