namespace Calcpad.Tests
{
    public class HPDiagonalMatrixComparisonInFunction
    {
        #region HPDiagonalMatrixOperators

        private const string RandomMatrixA = "a = random(diagonal(n; 1))";
        private const string RandomMatrixB = "b = random(diagonal(n; 1))";
        private const string WellConditionedMatrix = "a = vec2diag((0.55 + range(0; n - 1; 1))/n)";

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
            "r = f(a) ≡ f(hp(a))",
            "mcount(r; 0)"
        ];

        private static string[] ScalarTestHelper(string func) => [
            "n = 500",
            RandomMatrixA,
            $"f(a) = {func}(a)",
            $"c_hp = {func}(hp(a))",
            TestCalc.CompareWithTolerance("f(a)", "f(hp(a))", "10^-14")
        ];

        private static string[] InterpolationTestHelper(string func) => [
            "m = 250",
            "n = 500",
            "i = random(m - 1) + 1",
            "j = random(n - 1) + 1",
            RandomMatrixA,
            $"f(i; j; a) = {func}(i; j; a)",
            TestCalc.CompareWithTolerance("f(i; j; a)", "f(i; j; hp(a))", "10^-14")
];

        private static string[] PositiveDefiniteTestHelper(string func, string tol) => [
            "n = 250",
            WellConditionedMatrix,
            $"f(a) = {func}(a)",
            TestCalc.CompareWithTolerance("f(a)", "f(hp(a))", tol),
            "mcount(r; 0)"
        ];

        private static string[] MatrixEquationTestHelper(string func, string tol) => [
            "n = 250",
            WellConditionedMatrix,
            "b = random(fill(vector(n); 1))",
            $"f(a; b) = {func}(a; b)",
            TestCalc.CompareWithTolerance("f(a; b)", "f(hp(a); hp(b))", tol),
            "count(r; 0; 1)"
        ];

        private static string[] MatrixMultiEquationTestHelper(string func, string tol) => [
            "n = 250",
            WellConditionedMatrix,
            "b = random(mfill(matrix(n; 2); 1))",
            $"f(a; b) = {func}(a; b)",
            TestCalc.CompareWithTolerance("f(a; b)", "f(hp(a); hp(b))", tol),
            "mcount(r; 0)"
        ];

        [Fact]
        [Trait("Category", "HPDiagonalMatrixOperators")]
        public void HPDiagonalMatrixAddition()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(OperatorTestHelper('+'));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPDiagonalMatrixOperators")]
        public void HPDiagonalMatrixSubtraction()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(OperatorTestHelper('-'));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPDiagonalMatrixOperators")]
        public void HPDiagonalMatrixMultiplication()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(OperatorTestHelper('*'));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPDiagonalMatrixOperators")]
        public void HPDiagonalMatrixDivision()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(OperatorTestHelper('/'));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPDiagonalMatrixOperators")]
        public void HPDiagonalMatrixForceDivisionBar()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(OperatorTestHelper('÷'));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPDiagonalMatrixOperators")]
        public void HPDiagonalMatrixIntegerDivision()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(OperatorTestHelper('\\'));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPDiagonalMatrixOperators")]
        public void HPDiagonalMatrixModulo()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(OperatorTestHelper('⦼'));
            Assert.Equal(0, result);
        }
        #endregion

        #region HPDiagonalMatrixScalarOperators
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
        [Trait("Category", "HPDiagonalMatrixScalarOperators")]
        public void HPDiagonalMatrixScalarAddition()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(MatrixScalarOperatorTestHelper('+'));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPDiagonalMatrixScalarOperators")]
        public void HPDiagonalMatrixScalarSubtraction()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(MatrixScalarOperatorTestHelper('-'));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPDiagonalMatrixScalarOperators")]
        public void HPDiagonalMatrixScalarMultiplication()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(MatrixScalarOperatorTestHelper('*'));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPDiagonalMatrixScalarOperators")]
        public void HPDiagonalMatrixScalarDivision()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(MatrixScalarOperatorTestHelper('/'));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPDiagonalMatrixScalarOperators")]
        public void HPDiagonalMatrixScalarForceDivisionBar()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(MatrixScalarOperatorTestHelper('÷'));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPDiagonalMatrixScalarOperators")]
        public void HPDiagonalMatrixScalarIntegerDivision()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(MatrixScalarOperatorTestHelper('\\'));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPDiagonalMatrixScalarOperators")]
        public void HPDiagonalMatrixScalarModulo()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(MatrixScalarOperatorTestHelper('⦼'));
            Assert.Equal(0, result);
        }

        #endregion

        #region HPScalarDiagonalMatrixOperators
        private static string[] ScalarMatrixOperatorTestHelper(char o, string tol = "0") => [
            "n = 500",
            RandomMatrixA,
            RandomNum,
            $"f(b; a) = b {o} a",
            "a_hp = hp(a)",
            $"r = if({tol} ≡ 0; f(b; a_hp) ≡ f(b; a); abs(f(b; a_hp) - f(b; a)) ≤ {tol})",
            "mcount(r; 0)"
        ];
        [Fact]
        [Trait("Category", "HPScalarDiagonalMatrixOperators")]
        public void HPScalarDiagonalMatrixAddition()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(ScalarMatrixOperatorTestHelper('+'));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPScalarDiagonalMatrixOperators")]
        public void HPScalarDiagonalMatrixSubtraction()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(ScalarMatrixOperatorTestHelper('-'));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPScalarDiagonalMatrixOperators")]
        public void HPScalarDiagonalMatrixMultiplication()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(ScalarMatrixOperatorTestHelper('*'));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPScalarDiagonalMatrixOperators")]
        public void HPScalarDiagonalMatrixDivision()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(ScalarMatrixOperatorTestHelper('/'));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPScalarDiagonalMatrixOperators")]
        public void HPScalarDiagonalMatrixForceDivisionBar()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(ScalarMatrixOperatorTestHelper('÷'));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPScalarDiagonalMatrixOperators")]
        public void HPScalarDiagonalMatrixIntegerDivision()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(ScalarMatrixOperatorTestHelper('\\'));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPScalarDiagonalMatrixOperators")]
        public void HPScalarDiagonalMatrixModulo()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(ScalarMatrixOperatorTestHelper('⦼'));
            Assert.Equal(0, result);
        }

        #endregion

        #region HPDiagonalMatrixFunctions
        [Fact]
        [Trait("Category", "HPDiagonalMatrixFunctions")]
        public void HPDiagonalMatrixSin()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("sin"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPDiagonalMatrixFunctions")]
        public void HPDiagonalMatrixCos()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("cos"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPDiagonalMatrixFunctions")]
        public void HPDiagonalMatrixTan()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("tan"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPDiagonalMatrixFunctions")]
        public void HPDiagonalMatrixCsc()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("csc"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPDiagonalMatrixFunctions")]
        public void HPDiagonalMatrixSec()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("sec"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPDiagonalMatrixFunctions")]
        public void HPDiagonalMatrixCot()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("cot"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPDiagonalMatrixFunctions")]
        public void HPDiagonalMatrixSinh()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("Sinh"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPDiagonalMatrixFunctions")]
        public void HPDiagonalMatrixCosh()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("cosh"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPDiagonalMatrixFunctions")]
        public void HPDiagonalMatrixTanh()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("Tanh"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPDiagonalMatrixFunctions")]
        public void HPDiagonalMatrixCsch()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("Csch"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPDiagonalMatrixFunctions")]
        public void HPDiagonalMatrixSech()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("Sech"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPDiagonalMatrixFunctions")]
        public void HPDiagonalMatrixCoth()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("coth"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPDiagonalMatrixFunctions")]
        public void HPDiagonalMatrixAsin()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("asin"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPDiagonalMatrixFunctions")]
        public void HPDiagonalMatrixAcos()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("acos"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPDiagonalMatrixFunctions")]
        public void HPDiagonalMatrixAtan()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("atan"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPDiagonalMatrixFunctions")]
        public void HPDiagonalMatrixAtan2()
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
                TestCalc.CompareWithTolerance("f(a; b)", "c_hp", "10^-14"),
                "mcount(r; 0)"
            ]);
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPDiagonalMatrixFunctions")]
        public void HPDiagonalMatrixAcsc()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("acsc"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPDiagonalMatrixFunctions")]
        public void HPDiagonalMatrixAsec()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("asec"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPDiagonalMatrixFunctions")]
        public void HPDiagonalMatrixAcot()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("acot"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPDiagonalMatrixFunctions")]
        public void HPDiagonalMatrixAsinh()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("asinh"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPDiagonalMatrixFunctions")]
        public void HPDiagonalMatrixAcosh()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("acosh"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPDiagonalMatrixFunctions")]
        public void HPDiagonalMatrixAtanh()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("atanh"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPDiagonalMatrixFunctions")]
        public void HPDiagonalMatrixAcsch()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("acsch"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPDiagonalMatrixFunctions")]
        public void HPDiagonalMatrixAsech()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("asech"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPDiagonalMatrixFunctions")]
        public void HPDiagonalMatrixAcoth()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("acoth"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPDiagonalMatrixFunctions")]
        public void HPDiagonalMatrixLog()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("log"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPDiagonalMatrixFunctions")]
        public void HPDiagonalMatrixLn()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("ln"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPDiagonalMatrixFunctions")]
        public void HPDiagonalMatrixLog2()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("log_2"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPDiagonalMatrixFunctions")]
        public void HPDiagonalMatrixExp()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("exp"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPDiagonalMatrixFunctions")]
        public void HPDiagonalMatrixSqrt()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("sqrt"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPDiagonalMatrixFunctions")]
        public void HPDiagonalMatrixCbrt()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("cbrt"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPDiagonalMatrixFunctions")]
        public void HPDiagonalMatrixRoot()
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
        [Trait("Category", "HPDiagonalMatrixFunctions")]
        public void HPDiagonalMatrixRound()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("round"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPDiagonalMatrixFunctions")]
        public void HPDiagonalMatrixFloor()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("floor"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPDiagonalMatrixFunctions")]
        public void HPDiagonalMatrixCeiling()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("ceiling"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPDiagonalMatrixFunctions")]
        public void HPDiagonalMatrixTrunc()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("trunc"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPDiagonalMatrixFunctions")]
        public void HPDiagonalMatrixMin()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(ScalarTestHelper("min"));
            Assert.Equal(1, result);
        }

        [Fact]
        [Trait("Category", "HPDiagonalMatrixFunctions")]
        public void HPDiagonalMatrixMax()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(ScalarTestHelper("max"));
            Assert.Equal(1, result);
        }

        [Fact]
        [Trait("Category", "HPDiagonalMatrixFunctions")]
        public void HPDiagonalMatrixSum()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(ScalarTestHelper("sum"));
            Assert.Equal(1, result);
        }

        [Fact]
        [Trait("Category", "HPDiagonalMatrixFunctions")]
        public void HPDiagonalMatrixSumsq()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(ScalarTestHelper("sumsq"));
            Assert.Equal(1, result);
        }

        [Fact]
        [Trait("Category", "HPDiagonalMatrixFunctions")]
        public void HPDiagonalMatrixSrss()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(ScalarTestHelper("srss"));
            Assert.Equal(1, result);
        }

        [Fact]
        [Trait("Category", "HPDiagonalMatrixFunctions")]
        public void HPDiagonalMatrixAverage()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(ScalarTestHelper("average"));
            Assert.Equal(1, result);
        }

        [Fact]
        [Trait("Category", "HPDiagonalMatrixFunctions")]
        public void HPDiagonalMatrixProduct()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(ScalarTestHelper("product"));
            Assert.Equal(1, result);
        }

        [Fact]
        [Trait("Category", "HPDiagonalMatrixFunctions")]
        public void HPDiagonalMatrixMean()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(ScalarTestHelper("mean"));
            Assert.Equal(1, result);
        }

        [Fact]
        [Trait("Category", "HPDiagonalMatrixFunctions")]
        public void HPDiagonalMatrixTake()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(InterpolationTestHelper("take"));
            Assert.Equal(1, result);
        }

        [Fact]
        [Trait("Category", "HPDiagonalMatrixFunctions")]
        public void HPDiagonalMatrixLine()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(InterpolationTestHelper("line"));
            Assert.Equal(1, result);
        }

        [Fact]
        [Trait("Category", "HPDiagonalMatrixFunctions")]
        public void HPDiagonalMatrixSpline()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(InterpolationTestHelper("spline"));
            Assert.Equal(1, result);
        }

        [Fact]
        [Trait("Category", "HPDiagonalMatrixFunctions")]
        public void HPDiagonalMatrixSortCols()
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
        [Trait("Category", "HPDiagonalMatrixFunctions")]
        public void HPDiagonalMatrixRSortCols()
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
        [Trait("Category", "HPDiagonalMatrixFunctions")]
        public void HPDiagonalMatrixSortRows()
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
        [Trait("Category", "HPDiagonalMatrixFunctions")]
        public void HPDiagonalMatrixRSortRows()
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
        [Trait("Category", "HPDiagonalMatrixFunctions")]
        public void HPDiagonalMatrixOrderCols()
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
        [Trait("Category", "HPDiagonalMatrixFunctions")]
        public void HPDiagonalMatrixRevOrderCols()
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
        [Trait("Category", "HPDiagonalMatrixFunctions")]
        public void HPDiagonalMatrixOrderRows()
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
        [Trait("Category", "HPDiagonalMatrixFunctions")]
        public void HPDiagonalMatrixMcount()
        {
            var calc = new TestCalc(new());
            var result = calc.Run([
                "n = 500",
                "x = 1",
                "a = round(random(diagonal(n; 1000)))",
                "f(a; x) = mcount(a; x)",
                "f(a; x) ≡ f(hp(a); x)"
            ]);
            Assert.Equal(1, result);
        }

        [Fact]
        [Trait("Category", "HPDiagonalMatrixFunctions")]
        public void HPDiagonalMatrixHprod()
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
        [Trait("Category", "HPDiagonalMatrixFunctions")]
        public void HPDiagonalMatrixFprod()
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
        [Trait("Category", "HPDiagonalMatrixFunctions")]
        public void HPDiagonalMatrixKprod()
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
        [Trait("Category", "HPDiagonalMatrixFunctions")]
        public void HPDiagonalMatrixMnorm1()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(ScalarTestHelper("mnorm_1"));
            Assert.Equal(1, result);
        }

        [Fact]
        [Trait("Category", "HPDiagonalMatrixFunctions")]
        public void HPDiagonalMatrixMnorm2()
        {
            var calc = new TestCalc(new());
            var result = calc.Run([
                "n = 200",
                RandomMatrixA,
                "f(a) = mnorm_2(a)",
                "c_hp = mnorm_2(hp(a))",
                TestCalc.CompareWithToleranceDirect("f(a)", "f(hp(a))", "10^-12")
            ]);
            Assert.Equal(1, result);
        }

        [Fact]
        [Trait("Category", "HPDiagonalMatrixFunctions")]
        public void HPDiagonalMatrixMnormi()
        {
            var calc = new TestCalc(new());
            var result = calc.Run([
                "n = 500",
                RandomMatrixA,
                "f(a) = mnorm_i(a)",
                "c_hp = mnorm_i(hp(a))",
                TestCalc.CompareWithTolerance("f(a)", "f(hp(a))", "10^-12")
            ]);
            Assert.Equal(1, result);
        }

        [Fact]
        [Trait("Category", "HPDiagonalMatrixFunctions")]
        public void HPDiagonalMatrixCond1()
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
        [Trait("Category", "HPDiagonalMatrixFunctions")]
        public void HPDiagonalMatrixCond2()
        {
            var calc = new TestCalc(new());
            var result = calc.Run([
                "n = 200",
                WellConditionedMatrix,
                "f(a) = cond_2(a)",
                TestCalc.CompareWithToleranceDirect("f(a)", "f(hp(a))", "10^-12")
            ]);
            Assert.Equal(1, result);
        }

        [Fact]
        [Trait("Category", "HPDiagonalMatrixFunctions")]
        public void HPDiagonalMatrixCondE()
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
        [Trait("Category", "HPDiagonalMatrixFunctions")]
        public void HPDiagonalMatrixCondI()
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
        [Trait("Category", "HPDiagonalMatrixFunctions")]
        public void HPDiagonalMatrixDet()
        {
            var calc = new TestCalc(new());
            var result = calc.Run([
                "n = 250",
                RandomMatrixA,
                "f(a) = det(a)",
                TestCalc.CompareWithToleranceDirect("f(a)", "f(hp(a))", "10^-12")
            ]);
            Assert.Equal(1, result);
        }

        [Fact]
        [Trait("Category", "HPDiagonalMatrixFunctions")]
        public void HPDiagonalMatrixRank()
        {
            var calc = new TestCalc(new());
            var result = calc.Run([
                "n = 100",
                RandomMatrixA,
                "f(a) = rank(a)",
                "r = f(a) ≡ f(hp(a))"
            ]);
            Assert.Equal(1, result);
        }

        [Fact]
        [Trait("Category", "HPDiagonalMatrixFunctions")]
        public void HPDiagonalMatrixTrace()
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
        [Trait("Category", "HPDiagonalMatrixFunctions")]
        public void HPDiagonalMatrixTransp()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("transp"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPDiagonalMatrixFunctions")]
        public void HPDiagonalMatrixAdj()
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
        [Trait("Category", "HPDiagonalMatrixFunctions")]
        public void HPDiagonalMatrixCofactor()
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
        [Trait("Category", "HPDiagonalMatrixFunctions")]
        public void HPDiagonalMatrixEigenvals()
        {
            var calc = new TestCalc(new());
            var result = calc.Run([
                "n = 250",
                WellConditionedMatrix,
                "f(a) = eigenvals(a)",
                TestCalc.CompareWithTolerance("f(a)", "f(hp(a))", "10^-12"),
                "count(r; 0; 1)"
            ]);
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPDiagonalMatrixFunctions")]
        public void HPDiagonalMatrixEigenvecs()
        {
            var calc = new TestCalc(new());
            var result = calc.Run([
                "n = 250",
               WellConditionedMatrix,
                "f(a) = eigenvecs(a)",
                "r = abs(abs(f(a)) - abs(f(hp(a)))) ≤ 10^-12",
                "mcount(r; 0)"
            ]);
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPDiagonalMatrixFunctions")]
        public void HPDiagonalMatrixEigen()
        {
            var calc = new TestCalc(new());
            var result = calc.Run([
                "n = 200",
                WellConditionedMatrix,
                "f(a) = eigen(a)",
                "r = abs(abs(f(a)) - abs(f(hp(a)))) ≤ 10^-12*max(abs(f(a));1)",
                "mcount(r; 0)"
            ]);
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPDiagonalMatrixFunctions")]
        public void HPDiagonalMatrixCholesky()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(PositiveDefiniteTestHelper("cholesky", "10^-10"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPDiagonalMatrixFunctions")]
        public void HPDiagonalMatrixLu()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(PositiveDefiniteTestHelper("lu", "10^-14"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPDiagonalMatrixFunctions")]
        public void HPDiagonalMatrixQr()
        {
            var calc = new TestCalc(new());
            var result = calc.Run([
                "n = 250",
                RandomMatrixA,
                "f(a) = qr(a)",
                "r = abs(f(a) - f(hp(a))) ≤ 10^-12",
                "mcount(r; 0)"
            ]);
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPDiagonalMatrixFunctions")]
        public void HPDiagonalMatrixSvd()
        {
            var calc = new TestCalc(new());
            var result = calc.Run([
                "n = 200",
                RandomMatrixA,
                "f(a) = svd(a)",
                "r = abs(abs(f(a)) - abs(f(hp(a)))) ≤ 10^-8*max(abs(f(a)); 1)",
                "mcount(r; 0)"
            ]);
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPDiagonalMatrixFunctions")]
        public void HPDiagonalMatrixInverse()
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
        [Trait("Category", "HPDiagonalMatrixFunctions")]
        public void HPDiagonalMatrixLsolve()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(MatrixEquationTestHelper("lsolve", "10^-12"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPDiagonalMatrixFunctions")]
        public void HPDiagonalMatrixClsolve()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(MatrixEquationTestHelper("clsolve", "10^-8"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPDiagonalMatrixFunctions")]
        public void HPDiagonalMatrixSlsolve()
        {
            var calc = new TestCalc(new());
            var result = calc.Run([
                "Tol = 10^-5",
                "n = 250",
                "a = vec2diag((1000 + range_hp(0; n - 1; 1))/1234)",
                "b = random(fill(vector_hp(n); 1))",
                "f(a; b) = slsolve(a; b)",
                "r = a * f(a; b) - b ≤ 10^-5",
                "count(r; 0; 1)",
            ]);
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPDiagonalMatrixFunctions")]
        public void HPDiagonalMatrixMsolve()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(MatrixMultiEquationTestHelper("msolve", "10^-8"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPDiagonalMatrixFunctions")]
        public void HPDiagonalMatrixCmsolve()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(MatrixMultiEquationTestHelper("cmsolve", "10^-8"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPDiagonalMatrixFunctions")]
        public void HPDiagonalMatrixSmsolve()
        {
            var calc = new TestCalc(new());
            var result = calc.Run([
                "Tol = 10^-5",
                "n = 250",
                "a = vec2diag((1000 + range_hp(0; n - 1; 1))/1234)",
                "b = random(mfill(matrix_hp(n; 2); 1))",
                "f(a; b) = smsolve(a; b)",
                "r = a * f(a; b) - b ≤ 10^-5",
                "mcount(r; 0)",
            ]);
            Assert.Equal(0, result);
        }
        #endregion

    }
}