using System.Numerics;

namespace Calcpad.Tests
{
    internal class TestCalc(MathSettings settings)
    {
        // Prints Calcpad code for comparing two expressions.
        //
        // If the first argument is smaller than 1, the absolute tolerance is used.
        // Otherwise, the relative tolerance is used.
        //
        // The combination of relative and absolute tolerance cover the case of near zero values:
        // Lets say a=10^-6 and tolerance=10^-12.
        // If compared only relatively, the right "≤" operand in the implementation would be 10^-6 * 10^-12 = 10^-18, which is beyond double precision.
        internal static string CompareWithTolerance(string a, string b, string tolerance) =>
            $"r = abs({a} - {b}) ≤ {tolerance}*max(abs({a}); 1)";

        internal static string CompareWithToleranceDirect(string a, string b, string tolerance) =>
            $"abs({a} - {b}) ≤ {tolerance}*max(abs({a}); 1)";

        private readonly MathParser _parser = new(settings);

        public double Run(string expression)
        {
            _parser.Parse(expression);
            _parser.Calculate();
            return _parser.Real;
        }

        public double Run(string[] expressions)
        {
            foreach (var expression in expressions)
            {
                _parser.Parse(expression);
                _parser.Calculate();
            }
            return _parser.Real;
        }

        public double Real => _parser.Real;
        public double Imaginary => _parser.Imaginary;
        public Complex Complex => _parser.Complex;

        public override string ToString() => _parser.ResultAsString;
    }
}
