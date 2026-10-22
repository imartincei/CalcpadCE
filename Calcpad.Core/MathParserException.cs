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
    }
}
