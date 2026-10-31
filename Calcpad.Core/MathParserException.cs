using System;

namespace Calcpad.Core
{
    public class MathParserException : Exception
    {
        internal MathParserException(string message, string code) : base(message)
        {
            Code = code;
        }

        public string Code { get; }
    }

    internal sealed class ComplexInRealModeException(string message) : MathParserException(message, "CPD-5537");
}
