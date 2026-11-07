namespace Calcpad.Tests
{
    public class RuntimeTraceTests
    {
        private static RuntimeTrace Run(string source, bool debug = true)
        {
            var macroParser = new MacroParser();
            macroParser.Parse(source, out var code, null, 0, true);
            var parser = new ExpressionParser { Settings = new Settings(), Debug = debug };
            parser.Parse(code, true, false);
            return parser.Trace;
        }

        [Fact]
        public void DebugOff_HasNoTrace()
        {
            Assert.Null(Run("x = 1", debug: false));
        }

        [Fact]
        public void If_RecordsOnlyTakenBranch()
        {
            var trace = Run("a = 1\n#if a > 0\nx = 5\n#else\nx = [1; 2]\n#end if");
            Assert.True(trace.IsComplete);
            Assert.True(trace.Lines.ContainsKey(3));
            Assert.False(trace.Lines.ContainsKey(5));
            Assert.Equal(TraceValueKind.Scalar, trace.Assignments[("x", 3)]);
            Assert.False(trace.Assignments.ContainsKey(("x", 5)));
            var x = trace.Variables["x"];
            Assert.Equal(3, x.FirstLine);
            Assert.Equal("5", x.FirstExpression);
            Assert.False(x.KindChanged);
        }

        [Fact]
        public void ForLoop_KeepsFinalKind()
        {
            var trace = Run("x = 0\n#for i = 1 : 3\nx = [i; i]\n#loop");
            Assert.Equal(TraceValueKind.Scalar, trace.Assignments[("x", 1)]);
            Assert.Equal(TraceValueKind.Vector, trace.Assignments[("x", 3)]);
            var x = trace.Variables["x"];
            Assert.True(x.KindChanged);
            Assert.Equal(TraceValueKind.Vector, x.FinalKind);
            var i = trace.Variables["i"];
            Assert.Equal(TraceValueKind.Scalar, i.FinalKind);
            Assert.Null(i.FirstExpression);
        }

        [Fact]
        public void NoCalculation_RecordsNoAssignment()
        {
            var trace = Run("#noc\ny = 5\n#equ");
            Assert.False(trace.Variables.ContainsKey("y"));
            Assert.Equal(TraceOutputMode.NoCalculation, trace.Lines[2].Output);
        }

        [Fact]
        public void Directives_AreRecordedPerLine()
        {
            var trace = Run("a = 1\n#rad\n#hide\nb = 2\n#show\nc = 3");
            Assert.Equal(TraceAngleUnits.Degrees, trace.Lines[1].Angle);
            Assert.Equal(TraceAngleUnits.Radians, trace.Lines[4].Angle);
            Assert.False(trace.Lines[4].IsVisible);
            Assert.True(trace.Lines[6].IsVisible);
        }

        [Fact]
        public void FunctionBody_OuterAssignment_IsRecordedAtCallLine()
        {
            var trace = Run("g = 1\nf(x) = $Block{g ← [x; x]; x}\nz = f(2)");
            Assert.Equal(TraceValueKind.Vector, trace.Assignments[("g", 3)]);
            Assert.Equal("1", trace.Variables["g"].FirstExpression);
        }

        [Fact]
        public void ImplicitVariables_AreCaptured()
        {
            var trace = Run("f(x) = (x - 2)^2\nm = $Inf{f(x) @ x = 0 : 5}\nA = [4; 3 | 6; 3]\nL = lu(A)");
            Assert.Equal(TraceValueKind.Scalar, trace.Variables["x_inf"].FinalKind);
            Assert.Equal(TraceValueKind.Vector, trace.Variables["ind"].FinalKind);
            Assert.Equal(TraceValueKind.Matrix, trace.Variables["L"].FinalKind);
        }

        [Fact]
        public void ConstantsAndAnswer_AreNotTraced()
        {
            var trace = Run("x = pi");
            Assert.False(trace.Variables.ContainsKey("pi"));
            Assert.False(trace.Variables.ContainsKey("ans"));
        }

        [Fact]
        public void ContinuedLine_IsRecordedOnLastPhysicalLine()
        {
            var trace = Run("v = [1; _\n2]");
            Assert.Equal(TraceValueKind.Vector, trace.Assignments[("v", 2)]);
        }
    }
}
