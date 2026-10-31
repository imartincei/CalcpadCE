using System.Linq;

namespace Calcpad.Tests
{
    public class ComplexModeSwitchTests
    {
        private const string ComplexOn = "#settings {\"complex\": true}\n";
        private const string ComplexOff = "#settings {\"complex\": false}\n";
        private const string NotSupported = "Complex variable \"z\" is not supported outside of complex mode.";
        private const string NumbersNotSupported = "Complex numbers are not supported outside of complex mode.";

        private static string FunctionNotSupported(string name) =>
            $"Function \"{name}\" with complex values is not supported outside of complex mode.";

        private static ExpressionParser Run(string source)
        {
            var parser = new ExpressionParser { Settings = new Settings(), Debug = true };
            parser.Parse(source, true, false);
            return parser;
        }

        private static string[] Errors(string source) =>
            Run(source).Errors.Select(e => e.Message).ToArray();

        [Fact]
        public void SettingsComplex_EnablesImaginaryLiterals()
        {
            var parser = Run(ComplexOn + "z = 3 + 2i");
            Assert.Empty(parser.Errors);
            Assert.Contains("2i", parser.HtmlResult);
        }

        [Fact]
        public void SettingsComplex_EnablesImaginaryConstants()
        {
            Assert.Empty(Errors(ComplexOn + "a = i\nb = ei\nc = πi"));
        }

        [Fact]
        public void SettingsComplexOff_RemovesImaginaryConstant()
        {
            var errors = Errors(ComplexOn + "a = i\n" + ComplexOff + "b = i");
            Assert.Equal(["Undefined variable or units: \"i\"."], errors);
        }

        [Fact]
        public void SettingsComplexOff_KeepsUserDefinedI()
        {
            Assert.Empty(Errors(ComplexOn + "i = 5\n" + ComplexOff + "b = i"));
        }

        [Theory]
        [InlineData("y = z")]
        [InlineData("z")]
        [InlineData("y = z + 1")]
        [InlineData("y = abs(z)")]
        [InlineData("y = re(z)")]
        [InlineData("v = [z; 1]")]
        [InlineData("y = $Root{x - z @ x = 0 : 10}")]
        [InlineData("$Block{y = z}")]
        public void ComplexVariable_AfterComplexOff_ReportsError(string line)
        {
            var errors = Errors(ComplexOn + "z = 3 + 2i\n" + ComplexOff + line);
            Assert.Equal([NotSupported], errors);
        }

        [Fact]
        public void ComplexVariableWithUnits_AfterComplexOff_ReportsError()
        {
            var errors = Errors(ComplexOn + "z = (3 + 2i)*1m\n" + ComplexOff + "y = z");
            Assert.Equal([NotSupported], errors);
        }

        [Fact]
        public void FunctionReadingComplexVariable_CalledAfterComplexOff_ReportsFunctionError()
        {
            var errors = Errors(ComplexOn + "z = 3 + 2i\nf(x) = x + z\n" + ComplexOff + "y = f(1)");
            Assert.Equal([FunctionNotSupported("f")], errors);
        }

        [Fact]
        public void FunctionDefinedAfterComplexOff_ReadingComplexVariable_ReportsFunctionError()
        {
            var errors = Errors(ComplexOn + "z = 3 + 2i\n" + ComplexOff + "f(x) = x + z\ny = f(1)");
            Assert.Equal([FunctionNotSupported("f")], errors);
        }

        [Fact]
        public void ComplexArgument_ToRealFunction_ReportsVariableError()
        {
            var errors = Errors(ComplexOn + "z = 3 + 2i\n" + ComplexOff + "f(x) = x + 1\ny = f(z)");
            Assert.Equal([NotSupported], errors);
        }

        [Fact]
        public void ComplexVariableAndFunction_AfterComplexOff_ReportErrors()
        {
            var parser = Run(ComplexOn + "x = 2i + 4\nf(x) = 2i + 6\nf(6)\n" + ComplexOff + "x\nf(5)");
            Assert.Equal([6, 7], parser.Errors.Select(e => e.SourceLine));
            Assert.Equal(
                ["Complex variable \"x\" is not supported outside of complex mode.",
                 FunctionNotSupported("f")],
                parser.Errors.Select(e => e.Message));
        }

        [Fact]
        public void NestedComplexFunction_AfterComplexOff_ReportsCalledFunction()
        {
            var parser = Run(ComplexOn +
                "x = 2i + 4\nf(x) = 2i + 6\ng(x) = f(x)*ei\nf(6)\ng(10)\n" +
                ComplexOff + "x\nf(5)\ng(7)");
            Assert.Equal([8, 9, 10], parser.Errors.Select(e => e.SourceLine));
            Assert.Equal(
                ["Complex variable \"x\" is not supported outside of complex mode.",
                 FunctionNotSupported("f"),
                 FunctionNotSupported("g")],
                parser.Errors.Select(e => e.Message));
        }

        [Fact]
        public void FunctionUsingImaginaryConstant_AfterComplexOff_ReportsFunctionError()
        {
            var errors = Errors(ComplexOn + "g(x) = x*ei\na = g(1)\n" + ComplexOff + "b = g(2)");
            Assert.Equal([FunctionNotSupported("g")], errors);
        }

        [Fact]
        public void FunctionWithComplexLiteral_FirstCalledAfterComplexOff_ReportsError()
        {
            var errors = Errors(ComplexOn + "f(x) = 2i + 6\n" + ComplexOff + "y = f(5)");
            Assert.Equal([FunctionNotSupported("f")], errors);
        }

        [Fact]
        public void ComplexLiteral_InLoopThatTurnsComplexOff_ReportsError()
        {
            var parser = Run(ComplexOn + "#for k = 1 : 2\ny = 2i + k\n" + ComplexOff + "#loop");
            Assert.Equal([NumbersNotSupported], parser.Errors.Select(e => e.Message));
        }

        [Fact]
        public void RealFunction_DefinedInComplexMode_WorksAfterComplexOff()
        {
            var parser = Run(ComplexOn + "f(x) = x + 1\na = f(2i)\n" + ComplexOff + "b = f(2)");
            Assert.Empty(parser.Errors);
            Assert.Contains("3", parser.HtmlResult);
        }

        [Fact]
        public void RealFunction_DefinedInRealMode_AcceptsComplexAfterComplexOn()
        {
            var parser = Run("f(x) = x + 1\na = f(2)\n" + ComplexOn + "b = f(2i)");
            Assert.Empty(parser.Errors);
            Assert.Contains("1 + 2i", parser.HtmlResult);
        }

        [Fact]
        public void ComplexVariable_AfterComplexOff_ReportsErrorOnEveryUse()
        {
            var parser = Run(ComplexOn + "z = 3 + 2i\n" + ComplexOff + "a = z\nb = 1\nc = z*2");
            Assert.Equal([4, 6], parser.Errors.Select(e => e.SourceLine));
            Assert.All(parser.Errors, e => Assert.Equal(NotSupported, e.Message));
        }

        [Fact]
        public void RealValuedVariable_FromComplexMode_IsAllowed()
        {
            var parser = Run(ComplexOn + "z = (3 + 2i)*(3 - 2i)\n" + ComplexOff + "y = z + 1");
            Assert.Empty(parser.Errors);
            Assert.Contains("14", parser.HtmlResult);
        }

        [Fact]
        public void ComplexVariable_ReassignedAfterComplexOff_IsAllowed()
        {
            Assert.Empty(Errors(ComplexOn + "z = 3 + 2i\n" + ComplexOff + "z = 5\ny = z + 1"));
        }

        [Fact]
        public void ComplexVariable_ReassignedInBlockAfterComplexOff_IsAllowed()
        {
            Assert.Empty(Errors(ComplexOn + "z = 3 + 2i\n" + ComplexOff + "$Block{z = 5; y = z + 1}"));
        }

        [Fact]
        public void ComplexVariable_AfterComplexBackOn_IsAllowed()
        {
            var errors = Errors(ComplexOn + "z = 3 + 2i\n" + ComplexOff + "x = 1\n" + ComplexOn + "y = z + 1");
            Assert.Empty(errors);
        }

        [Fact]
        public void ComplexVariable_InUnusedBranch_IsNotReported()
        {
            var errors = Errors(ComplexOn + "z = 3 + 2i\n" + ComplexOff + "#if 0\ny = z\n#end if");
            Assert.Empty(errors);
        }
    }
}
