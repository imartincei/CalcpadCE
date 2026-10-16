namespace Calcpad.Tests
{
    public class HPLowerTriangularMatrixComparisonInFunction
    {
        #region HPLowerTriangularMatrixOperators

        private const string RandomMatrixA = "a = random(mfill(ltriang(n); 1))";
        private const string RandomMatrixB = "b = random(mfill(ltriang(n); 1))";
        private const string WellConditionedMatrix = "a = diagonal(n; 1) + random(mfill(ltriang(n); 0.1))";

        private static string[] OperatorTestHelper(char o, string tol = "0") => [
            "n = 500",
            RandomMatrixA,
            RandomMatrixB,
            $"f(a; b) = a {o} b",
            "a_hp = hp(a)",
            "b_hp = hp(b)",
            $"r = if({tol} ≡ 0; f(a_hp; b_hp) ≡ f(a; b); abs(f(a_hp; b_hp) - f(a; b)) ≤ {tol})",
            "mcount(r; 0)"
        ];

        private static string[] FunctionTestHelper(string func) => [
            "n = 500",
            RandomMatrixA,
            $"f(a) = {func}(a)",
            $"c_hp = {func}(hp(a))",
            "r = f(a) ≡ f(hp(a))",
            "mcount(r; 0)"
        ];

        private static string[] ScalarTestHelper(string func) => [
            "n = 500",
            RandomMatrixA,
            $"f(a) = {func}(a)",
            TestCalc.CompareWithTolerance("f(a)", "f(hp(a))", "10^-14")
        ];

        private static string[] InterpolationTestHelper(string func) => [
            "n = 500",
            "i = random(n - 1) + 1",
            "j = random(n - 1) + 1",
            RandomMatrixA,
            $"f(i; j; a) = {func}(i; j; a)",
            TestCalc.CompareWithTolerance("f(i; j; a)", "f(i; j; hp(a))", "10^-14")
        ];

        private static readonly string[] PositiveDefiniteArray = [
            "n = 250",
            "a = ltriang(n)",
            "v = [50000; 20000; 10000; 5000; 2000; 1000; 500; 200; 100; 50]",
            "$Repeat{add(v; a; i; i) @ i = 1 : n}",
        ];

        private static string[] PositiveDefiniteTestHelper(string func, string tol) =>
            PositiveDefiniteArray.Concat([
            $"f(a) = {func}(a)",
            TestCalc.CompareWithTolerance("f(a)", "f(hp(a))", tol),
            "mcount(r; 0)"
        ]).ToArray();

        private static string[] MatrixEquationTestHelper(string func, string tol) =>
            PositiveDefiniteArray.Concat([
            "b = random(fill(vector(n); 1))",
            $"f(a; b) = {func}(a; b)",
            TestCalc.CompareWithTolerance("f(a; b)", "f(hp(a); hp(b))", tol),
            "count(r; 0; 1)"
         ]).ToArray();

        private static string[] MatrixMultiEquationTestHelper(string func, string tol) =>
            PositiveDefiniteArray.Concat([
            "b = random(mfill(matrix(n; 2); 1))",
            $"f(a; b) = {func}(a; b)",
            TestCalc.CompareWithTolerance("f(a; b)", "f(hp(a); hp(b))", tol),
            "mcount(r; 0)"
         ]).ToArray();

        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixOperators")]
        public void HPLowerTriangularMatrixAddition()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(OperatorTestHelper('+'));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixOperators")]
        public void HPLowerTriangularMatrixSubtraction()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(OperatorTestHelper('-'));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixOperators")]
        public void HPLowerTriangularMatrixMultiplication()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(OperatorTestHelper('*', "10^-12"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixOperators")]
        public void HPLowerTriangularMatrixDivision()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(OperatorTestHelper('/'));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixOperators")]
        public void HPLowerTriangularMatrixForceDivisionBar()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(OperatorTestHelper('÷'));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixOperators")]
        public void HPLowerTriangularMatrixIntegerDivision()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(OperatorTestHelper('\\'));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixOperators")]
        public void HPLowerTriangularMatrixModulo()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(OperatorTestHelper('⦼'));
            Assert.Equal(0, result);
        }
        #endregion

        #region HPLowerTriangularMatrixScalarOperators
        private const string RandomNum = "b = random(50)";
        private static string[] MatrixScalarOperatorTestHelper(char o, string tol = "0") => [
            "n = 500",
            RandomMatrixA,
            RandomNum,
            $"f(a; b) = a {o} b",
            "a_hp = hp(a)",
            $"r = if({tol} ≡ 0; f(a_hp; b) ≡ f(a; b); abs(f(a_hp; b) - f(a; b)) ≤ {tol})",
            "mcount(r; 0)"
        ];
        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixScalarOperators")]
        public void HPLowerTriangularMatrixScalarAddition()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(MatrixScalarOperatorTestHelper('+'));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixScalarOperators")]
        public void HPLowerTriangularMatrixScalarSubtraction()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(MatrixScalarOperatorTestHelper('-'));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixScalarOperators")]
        public void HPLowerTriangularMatrixScalarMultiplication()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(MatrixScalarOperatorTestHelper('*', "10^-12"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixOperators")]
        public void HPLowerTriangularMatrixScalarDivision()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(MatrixScalarOperatorTestHelper('/'));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixScalarOperators")]
        public void HPLowerTriangularMatrixScalarForceDivisionBar()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(MatrixScalarOperatorTestHelper('÷'));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixScalarOperators")]
        public void HPLowerTriangularMatrixScalarIntegerDivision()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(MatrixScalarOperatorTestHelper('\\'));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixScalarOperators")]
        public void HPLowerTriangularMatrixScalarModulo()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(MatrixScalarOperatorTestHelper('⦼'));
            Assert.Equal(0, result);
        }
        #endregion

        #region HpScalarLowerTriangularMatrixOperators
        private static string[] ScalarMatrixOperatorTestHelper(char o, string tol = "0") => [
            "n = 500",
            RandomMatrixA,
            RandomNum,
            $"f(b; a) = b {o} a",
            "a_hp = hp(a)",
            $"c_hp = b {o} a_hp",
            $"r = if({tol} ≡ 0; f(b; a_hp) ≡ f(b; a); abs(f(b; a_hp) - f(b; a)) ≤ {tol})",
            "mcount(r; 0)"
        ];
        [Fact]
        [Trait("Category", "HPScalarLowerTriangularMatrixOperators")]
        public void HPScalarLowerTriangularMatrixAddition()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(ScalarMatrixOperatorTestHelper('+'));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPScalarLowerTriangularMatrixOperators")]
        public void HPScalarLowerTriangularMatrixSubtraction()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(ScalarMatrixOperatorTestHelper('-'));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPScalarLowerTriangularMatrixOperators")]
        public void HPScalarLowerTriangularMatrixMultiplication()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(ScalarMatrixOperatorTestHelper('*', "10^-12"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPScalarLowerTriangularMatrixOperators")]
        public void HPScalarLowerTriangularMatrixDivision()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(ScalarMatrixOperatorTestHelper('/'));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPScalarLowerTriangularMatrixOperators")]
        public void HPScalarLowerTriangularMatrixForceDivisionBar()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(ScalarMatrixOperatorTestHelper('÷'));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPScalarLowerTriangularMatrixOperators")]
        public void HPScalarLowerTriangularMatrixIntegerDivision()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(ScalarMatrixOperatorTestHelper('\\'));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPScalarLowerTriangularMatrixOperators")]
        public void HPScalarLowerTriangularMatrixModulo()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(ScalarMatrixOperatorTestHelper('⦼'));
            Assert.Equal(0, result);
        }
        #endregion

        #region HPLowerTriangularMatrixFunctions
        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixFunctions")]
        public void HPLowerTriangularMatrixSin()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("sin"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixFunctions")]
        public void HPLowerTriangularMatrixCos()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("cos"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixFunctions")]
        public void HPLowerTriangularMatrixTan()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("tan"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixFunctions")]
        public void HPLowerTriangularMatrixCsc()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("csc"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixFunctions")]
        public void HPLowerTriangularMatrixSec()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("sec"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixFunctions")]
        public void HPLowerTriangularMatrixCot()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("cot"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixFunctions")]
        public void HPLowerTriangularMatrixSinh()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("Sinh"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixFunctions")]
        public void HPLowerTriangularMatrixCosh()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("cosh"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixFunctions")]
        public void HPLowerTriangularMatrixTanh()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("Tanh"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixFunctions")]
        public void HPLowerTriangularMatrixCsch()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("Csch"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixFunctions")]
        public void HPLowerTriangularMatrixSech()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("Sech"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixFunctions")]
        public void HPLowerTriangularMatrixCoth()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("coth"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixFunctions")]
        public void HPLowerTriangularMatrixAsin()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("asin"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixFunctions")]
        public void HPLowerTriangularMatrixAcos()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("acos"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixFunctions")]
        public void HPLowerTriangularMatrixAtan()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("atan"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixFunctions")]
        public void HPLowerTriangularMatrixAtan2()
        {
            var calc = new TestCalc(new());
            var result = calc.Run([
                "n = 500",
                RandomMatrixA,
                RandomMatrixB,
                "f(a; b) = atan2(a; b)",
                "r = f(a; b) ≡ f(hp(a); hp(b))",
                "mcount(r; 0)"
            ]);
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPMatrixFunctions")]
        public void HPMatrixMatMul()
        {
            var calc = new TestCalc(new());
            var result = calc.Run([
                "n = 500",
                RandomMatrixA,
                RandomMatrixB,
                "f(a; b) = a * b",
                "c_hp = matmul(hp(a); hp(b))",
                "r = abs(abs(f(a; b)) - abs(c_hp)) ≤ 10^-8*max(abs(f(a; b)); 1)",
                "mcount(r; 0)"
            ]);
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixFunctions")]
        public void HPLowerTriangularMatrixAcsc()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("acsc"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixFunctions")]
        public void HPLowerTriangularMatrixAsec()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("asec"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixFunctions")]
        public void HPLowerTriangularMatrixAcot()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("acot"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixFunctions")]
        public void HPLowerTriangularMatrixAsinh()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("asinh"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixFunctions")]
        public void HPLowerTriangularMatrixAcosh()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("acosh"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixFunctions")]
        public void HPLowerTriangularMatrixAtanh()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("atanh"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixFunctions")]
        public void HPLowerTriangularMatrixAcsch()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("acsch"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixFunctions")]
        public void HPLowerTriangularMatrixAsech()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("asech"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixFunctions")]
        public void HPLowerTriangularMatrixAcoth()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("acoth"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixFunctions")]
        public void HPLowerTriangularMatrixLog()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("log"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixFunctions")]
        public void HPLowerTriangularMatrixLn()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("ln"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixFunctions")]
        public void HPLowerTriangularMatrixLog2()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("log_2"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixFunctions")]
        public void HPLowerTriangularMatrixExp()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("exp"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixFunctions")]
        public void HPLowerTriangularMatrixSqrt()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("sqrt"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixFunctions")]
        public void HPLowerTriangularMatrixCbrt()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("cbrt"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixFunctions")]
        public void HPLowerTriangularMatrixRoot()
        {
            var calc = new TestCalc(new());
            var result = calc.Run([
                "n = 500",
                "nth = 4",
                RandomMatrixA,
                "f(a; nth) = root(a; nth)",
                "r = f(a; nth) ≡ f(hp(a); nth)",
                "mcount(r; 0)"
            ]);
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixFunctions")]
        public void HPLowerTriangularMatrixRound()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("round"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixFunctions")]
        public void HPLowerTriangularMatrixFloor()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("floor"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixFunctions")]
        public void HPLowerTriangularMatrixCeiling()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("ceiling"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixFunctions")]
        public void HPLowerTriangularMatrixTrunc()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("trunc"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixFunctions")]
        public void HPLowerTriangularMatrixMin()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(ScalarTestHelper("min"));
            Assert.Equal(1, result);
        }

        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixFunctions")]
        public void HPLowerTriangularMatrixMax()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(ScalarTestHelper("max"));
            Assert.Equal(1, result);
        }

        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixFunctions")]
        public void HPLowerTriangularMatrixSum()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(ScalarTestHelper("sum"));
            Assert.Equal(1, result);
        }

        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixFunctions")]
        public void HPLowerTriangularMatrixSumsq()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(ScalarTestHelper("sumsq"));
            Assert.Equal(1, result);
        }

        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixFunctions")]
        public void HPLowerTriangularMatrixSrss()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(ScalarTestHelper("srss"));
            Assert.Equal(1, result);
        }

        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixFunctions")]
        public void HPLowerTriangularMatrixAverage()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(ScalarTestHelper("average"));
            Assert.Equal(1, result);
        }

        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixFunctions")]
        public void HPLowerTriangularMatrixProduct()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(ScalarTestHelper("product"));
            Assert.Equal(1, result);
        }

        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixFunctions")]
        public void HPLowerTriangularMatrixMean()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(ScalarTestHelper("mean"));
            Assert.Equal(1, result);
        }

        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixFunctions")]
        public void HPLowerTriangularMatrixTake()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(InterpolationTestHelper("take"));
            Assert.Equal(1, result);
        }

        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixFunctions")]
        public void HPLowerTriangularMatrixLine()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(InterpolationTestHelper("line"));
            Assert.Equal(1, result);
        }

        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixFunctions")]
        public void HPLowerTriangularMatrixSpline()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(InterpolationTestHelper("spline"));
            Assert.Equal(1, result);
        }

        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixFunctions")]
        public void HPLowerTriangularMatrixSortCols()
        {
            var calc = new TestCalc(new());
            var result = calc.Run([
                "n = 500",
                "i = 50",
                RandomMatrixA,
                "f(a; i) = sort_cols(a; i)",
                "r = f(a; i) ≡ f(hp(a); i)",
                "mcount(r; 0)"
            ]);
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixFunctions")]
        public void HPLowerTriangularMatrixRSortCols()
        {
            var calc = new TestCalc(new());
            var result = calc.Run([
                "n = 500",
                "i = 50",
                RandomMatrixA,
                "f(a; i) = rsort_cols(a; i)",
                "r = f(a; i) ≡ f(hp(a); i)",
                "mcount(r; 0)"
            ]);
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixFunctions")]
        public void HPLowerTriangularMatrixSortRows()
        {
            var calc = new TestCalc(new());
            var result = calc.Run([
                "n = 500",
                "j = 50",
                RandomMatrixA,
                "f(a; j) = sort_rows(a; j)",
                "r = f(a; j) ≡ f(hp(a); j)",
                "mcount(r; 0)"
            ]);
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixFunctions")]
        public void HPLowerTriangularMatrixRSortRows()
        {
            var calc = new TestCalc(new());
            var result = calc.Run([
                "n = 500",
                "j = 50",
                RandomMatrixA,
                "f(a; j) = rsort_rows(a; j)",
                "r = f(a; j) ≡ f(hp(a); j)",
                "mcount(r; 0)"
            ]);
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixFunctions")]
        public void HPLowerTriangularMatrixOrderCols()
        {
            var calc = new TestCalc(new());
            var result = calc.Run([
                "n = 500",
                "i = 50",
                RandomMatrixA,
                "f(a; i) = order_cols(a; i)",
                "r = f(a; i) ≡ f(hp(a); i)",
                "mcount(r; 0)"
            ]);
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixFunctions")]
        public void HPLowerTriangularMatrixRevOrderCols()
        {
            var calc = new TestCalc(new());
            var result = calc.Run([
                "n = 500",
                "i = 50",
                RandomMatrixA,
                "f(a; i) = revorder_cols(a; i)",
                "r = f(a; i) ≡ f(hp(a); i)",
                "mcount(r; 0)"
            ]);
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixFunctions")]
        public void HPLowerTriangularMatrixOrderRows()
        {
            var calc = new TestCalc(new());
            var result = calc.Run([
                "n = 500",
                "j = 50",
                RandomMatrixA,
                "f(a; j) = order_rows(a; j)",
                "r = f(a; j) ≡ f(hp(a); j)",
                "mcount(r; 0)"
            ]);
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixFunctions")]
        public void HPLowerTriangularMatrixMcount()
        {
            var calc = new TestCalc(new());
            var result = calc.Run([
                "n = 500",
                "x = 1",
                "a = round(random(mfill(ltriang(n); 1000)))",
                "f(a; x) = mcount(a; x)",
                "f(a; x) ≡ f(hp(a); x)"
            ]);
            Assert.Equal(1, result);
        }

        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixFunctions")]
        public void HPLowerTriangularMatrixHprod()
        {
            var calc = new TestCalc(new());
            var result = calc.Run([
                "n = 500",
                RandomMatrixA,
                RandomMatrixB,
                "f(a; b) = hprod(a; b)",
                "r = f(a; b) ≡ f(hp(a); hp(b))",
                "mcount(r; 0)"
            ]);
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixFunctions")]
        public void HPLowerTriangularMatrixFprod()
        {
            var calc = new TestCalc(new());
            var result = calc.Run([
                "n = 500",
                RandomMatrixA,
                RandomMatrixB,
                "f(a; b) = fprod(a; b)",
                TestCalc.CompareWithToleranceDirect("f(a; b)", "f(hp(a); hp(b))", "10^-12")
            ]);
            Assert.Equal(1, result);
        }

        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixFunctions")]
        public void HPLowerTriangularMatrixKprod()
        {
            var calc = new TestCalc(new());
            var result = calc.Run([
                "n = 100",
                RandomMatrixA,
                RandomMatrixB,
                "f(a; b) = kprod(a; b)",
                "r = f(a; b) ≡ f(hp(a); hp(b))",
                "mcount(r; 0)"
            ]);
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixFunctions")]
        public void HPLowerTriangularMatrixMnorm1()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(ScalarTestHelper("mnorm_1"));
            Assert.Equal(1, result);
        }

        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixFunctions")]
        public void HPLowerTriangularMatrixMnorm2()
        {
            var calc = new TestCalc(new());
            var result = calc.Run([
                "n = 200",
                RandomMatrixA,
                "f(a) = mnorm_2(a)",
                TestCalc.CompareWithToleranceDirect("f(a)", "f(hp(a))", "10^-12")
            ]);
            Assert.Equal(1, result);
        }

        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixFunctions")]
        public void HPLowerTriangularMatrixMnormi()
        {
            var calc = new TestCalc(new());
            var result = calc.Run([
                "n = 500",
                RandomMatrixA,
                "f(a) = mnorm_i(a)",
                TestCalc.CompareWithTolerance("f(a)", "f(hp(a))", "10^-12")
            ]);
            Assert.Equal(1, result);
        }

        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixFunctions")]
        public void HPLowerTriangularMatrixCond1()
        {
            var calc = new TestCalc(new());
            var result = calc.Run([
                "n = 250",
                WellConditionedMatrix,
                "f(a) = cond_1(a)",
                TestCalc.CompareWithToleranceDirect("f(a)", "f(hp(a))", "10^-12")
                ]);
            Assert.Equal(1, result);
        }

        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixFunctions")]
        public void HPLowerTriangularMatrixCond2()
        {
            var calc = new TestCalc(new());
            var result = calc.Run([
                "n = 200",
                WellConditionedMatrix,
                "f(a)  = cond_2(a)",
                TestCalc.CompareWithToleranceDirect("f(a) ", "f(hp(a))", "10^-12")
            ]);
            Assert.Equal(1, result);
        }

        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixFunctions")]
        public void HPLowerTriangularMatrixCondE()
        {
            var calc = new TestCalc(new());
            var result = calc.Run([
                "n = 250",
                WellConditionedMatrix,
                "f(a) = cond_e(a)",
                TestCalc.CompareWithToleranceDirect("f(a)", "f(hp(a))", "10^-12")
            ]);
            Assert.Equal(1, result);
        }

        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixFunctions")]
        public void HPLowerTriangularMatrixCondI()
        {
            var calc = new TestCalc(new());
            var result = calc.Run([
                "n = 250",
                WellConditionedMatrix,
                "f(a) = cond_i(a)",
                TestCalc.CompareWithToleranceDirect("f(a)", "f(hp(a))", "10^-12")
            ]);
            Assert.Equal(1, result);
        }

        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixFunctions")]
        public void HPLowerTriangularMatrixDet()
        {
            var calc = new TestCalc(new());
            var result = calc.Run([
                "n = 250",
                WellConditionedMatrix,
                "f(a) = det(a)",
                TestCalc.CompareWithToleranceDirect("f(a)", "f(hp(a))", "10^-12")
            ]);
            Assert.Equal(1, result);
        }

        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixFunctions")]
        public void HPLowerTriangularMatrixRank()
        {
            var calc = new TestCalc(new());
            var result = calc.Run([
                "n = 100",
                WellConditionedMatrix,
                "f(a) = rank(a)",
                "r = f(a) ≡ f(hp(a))"
            ]);
            Assert.Equal(1, result);
        }

        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixFunctions")]
        public void HPLowerTriangularMatrixTrace()
        {
            var calc = new TestCalc(new());
            var result = calc.Run([
                "n = 500",
                RandomMatrixA,
                "f(a) = trace(a)",
                "r = abs(f(a) - f(hp(a))) ≤ 10^-12"
            ]);
            Assert.Equal(1, result);
        }

        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixFunctions")]
        public void HPLowerTriangularMatrixTransp()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("transp"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixFunctions")]
        public void HPLowerTriangularMatrixAdj()
        {
            var calc = new TestCalc(new());
            var result = calc.Run([
                "n = 250",
                WellConditionedMatrix,
                "f(a) = adj(a)",
                TestCalc.CompareWithTolerance("f(a)", "f(hp(a))", "10^-8"),
                "mcount(r; 0)"
            ]);
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixFunctions")]
        public void HPLowerTriangularMatrixCofactor()
        {
            var calc = new TestCalc(new());
            var result = calc.Run([
                "n = 250",
                WellConditionedMatrix,
                "f(a) = cofactor(a)",
                TestCalc.CompareWithTolerance("f(a)", "f(hp(a))", "10^-8"),
                "mcount(r; 0)"
                ]);
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixFunctions")]
        public void HPLowerTriangularMatrixLu()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(PositiveDefiniteTestHelper("lu", "10^-14"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixFunctions")]
        public void HPLowerTriangularMatrixQr()
        {
            var calc = new TestCalc(new());
            var result = calc.Run([
                "n = 250",
                RandomMatrixA,
                "f(a) = qr(a)",
                "c_hp = qr(hp(a))",
                "r = abs(f(a) - f(hp(a))) ≤ 10^-12",
                "mcount(r; 0)"
            ]);
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixFunctions")]
        public void HPLowerTriangularMatrixSvd()
        {
            var calc = new TestCalc(new());
            var result = calc.Run([
                "n = 200",
                WellConditionedMatrix,
                "f(a) = svd(a)",
                "c_hp = svd(hp(a))",
                "r = abs(abs(f(a)) - abs(f(hp(a)))) ≤ 10^-8*max(abs(f(a)); 1)",
                "mcount(r; 0)"
            ]);
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixFunctions")]
        public void HPLowerTriangularMatrixInverse()
        {
            var calc = new TestCalc(new());
            var result = calc.Run([
                "n = 250",
                WellConditionedMatrix,
                "f(a) = inverse(a)",
                TestCalc.CompareWithTolerance("f(a)", "f(hp(a))", "10^-8"),
                "mcount(r; 0)"
            ]);
            Assert.Equal(0, result);
        }
        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixFunctions")]
        public void HPLowerTriangularMatrixLsolve()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(MatrixEquationTestHelper("lsolve", "10^-12"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPLowerTriangularMatrixFunctions")]
        public void HPLowerTriangularMatrixMsolve()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(MatrixMultiEquationTestHelper("msolve", "10^-12"));
            Assert.Equal(0, result);
        }
        #endregion
    }
}