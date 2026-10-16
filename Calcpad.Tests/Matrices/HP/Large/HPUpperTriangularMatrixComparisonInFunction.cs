namespace Calcpad.Tests
{
    public class HPUpperTriangularMatrixComparisonInFunction
    {
        #region HPUpperTriangularMatrixOperators

        private const string RandomMatrixA = "a = random(mfill(utriang(n); 1))";
        private const string RandomMatrixB = "b = random(mfill(utriang(n); 1))";
        private const string WellConditionedMatrix = "a = diagonal(n; 1) + random(mfill(utriang(n); 0.1))";

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
            "m = 250",
            "n = 500",
            RandomMatrixA,
            $"f(a) = {func}(a)",
            "r = f(a) ≡ f(hp(a))",
            "mcount(r; 0)"
        ];

        private static string[] ScalarTestHelper(string func) => [
            "m = 250",
            "n = 500",
            RandomMatrixA,
            $"f(a) = {func}(a)",
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

        private static readonly string[] PositiveDefiniteArray = [
            "n = 250",
            "a = utriang(n)",
            "v = transp([50000; 20000; 10000; 5000; 2000; 1000; 500; 200; 100; 50])",
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
        [Trait("Category", "HPUpperTriangularMatrixOperators")]
        public void HPUpperTriangularMatrixAddition()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(OperatorTestHelper('+'));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPUpperTriangularMatrixOperators")]
        public void HPUpperTriangularMatrixSubtraction()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(OperatorTestHelper('-'));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPUpperTriangularMatrixOperators")]
        public void HPUpperTriangularMatrixMultiplication()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(OperatorTestHelper('*', "10^-11"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPUpperTriangularMatrixOperators")]
        public void HPUpperTriangularMatrixDivision()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(OperatorTestHelper('/'));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPUpperTriangularMatrixOperators")]
        public void HPUpperTriangularMatrixForceDivisionBar()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(OperatorTestHelper('÷'));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPUpperTriangularMatrixOperators")]
        public void HPUpperTriangularMatrixIntegerDivision()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(OperatorTestHelper('\\'));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPUpperTriangularMatrixOperators")]
        public void HPUpperTriangularMatrixModulo()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(OperatorTestHelper('⦼'));
            Assert.Equal(0, result);
        }
        #endregion

        #region HPUpperTriangularMatrixScalarOperators
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
        [Trait("Category", "HPUpperTriangularMatrixScalarOperators")]
        public void HPUpperTriangularMatrixScalarAddition()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(MatrixScalarOperatorTestHelper('+'));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPUpperTriangularMatrixScalarOperators")]
        public void HPUpperTriangularMatrixScalarSubtraction()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(MatrixScalarOperatorTestHelper('-'));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPUpperTriangularMatrixScalarOperators")]
        public void HPUpperTriangularMatrixScalarMultiplication()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(MatrixScalarOperatorTestHelper('*', "10^-12"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPUpperTriangularMatrixScalarOperators")]
        public void HPUpperTriangularMatrixScalarDivision()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(MatrixScalarOperatorTestHelper('/'));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPUpperTriangularMatrixScalarOperators")]
        public void HPUpperTriangularMatrixScalarForceDivisionBar()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(MatrixScalarOperatorTestHelper('÷'));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPUpperTriangularMatrixScalarOperators")]
        public void HPUpperTriangularMatrixScalarIntegerDivision()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(MatrixScalarOperatorTestHelper('\\'));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPUpperTriangularMatrixScalarOperators")]
        public void HPUpperTriangularMatrixScalarModulo()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(MatrixScalarOperatorTestHelper('⦼'));
            Assert.Equal(0, result);
        }

        #endregion

        #region HPScalarUpperTriangularMatrixOperators
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
        [Trait("Category", "HPScalarUpperTriangularMatrixOperators")]
        public void HPScalarUpperTriangularMatrixAddition()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(ScalarMatrixOperatorTestHelper('+'));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPScalarUpperTriangularMatrixOperators")]
        public void HPScalarUpperTriangularMatrixSubtraction()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(ScalarMatrixOperatorTestHelper('-'));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPScalarUpperTriangularMatrixOperators")]
        public void HPScalarUpperTriangularMatrixMultiplication()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(ScalarMatrixOperatorTestHelper('*', "10^-12"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPScalarUpperTriangularMatrixOperators")]
        public void HPScalarUpperTriangularMatrixDivision()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(ScalarMatrixOperatorTestHelper('/'));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPScalarUpperTriangularMatrixOperators")]
        public void HPScalarUpperTriangularMatrixForceDivisionBar()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(ScalarMatrixOperatorTestHelper('÷'));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPScalarUpperTriangularMatrixOperators")]
        public void HPScalarUpperTriangularMatrixIntegerDivision()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(ScalarMatrixOperatorTestHelper('\\'));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPScalarUpperTriangularMatrixOperators")]
        public void HPScalarUpperTriangularMatrixModulo()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(ScalarMatrixOperatorTestHelper('⦼'));
            Assert.Equal(0, result);
        }

        #endregion

        #region HPUpperTriangularMatrixFunctions
        [Fact]
        [Trait("Category", "HPUpperTriangularMatrixFunctions")]
        public void HPUpperTriangularMatrixSin()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("sin"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPUpperTriangularMatrixFunctions")]
        public void HPUpperTriangularMatrixCos()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("cos"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPUpperTriangularMatrixFunctions")]
        public void HPUpperTriangularMatrixTan()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("tan"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPUpperTriangularMatrixFunctions")]
        public void HPUpperTriangularMatrixCsc()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("csc"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPUpperTriangularMatrixFunctions")]
        public void HPUpperTriangularMatrixSec()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("sec"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPUpperTriangularMatrixFunctions")]
        public void HPUpperTriangularMatrixCot()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("cot"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPUpperTriangularMatrixFunctions")]
        public void HPUpperTriangularMatrixSinh()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("Sinh"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPUpperTriangularMatrixFunctions")]
        public void HPUpperTriangularMatrixCosh()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("cosh"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPUpperTriangularMatrixFunctions")]
        public void HPUpperTriangularMatrixTanh()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("Tanh"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPUpperTriangularMatrixFunctions")]
        public void HPUpperTriangularMatrixCsch()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("Csch"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPUpperTriangularMatrixFunctions")]
        public void HPUpperTriangularMatrixSech()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("Sech"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPUpperTriangularMatrixFunctions")]
        public void HPUpperTriangularMatrixCoth()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("coth"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPUpperTriangularMatrixFunctions")]
        public void HPUpperTriangularMatrixAsin()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("asin"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPUpperTriangularMatrixFunctions")]
        public void HPUpperTriangularMatrixAcos()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("acos"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPUpperTriangularMatrixFunctions")]
        public void HPUpperTriangularMatrixAtan()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("atan"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPUpperTriangularMatrixFunctions")]
        public void HPUpperTriangularMatrixAtan2()
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
        [Trait("Category", "HPUpperTriangularMatrixFunctions")]
        public void HPUpperTriangularMatrixAcsc()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("acsc"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPUpperTriangularMatrixFunctions")]
        public void HPUpperTriangularMatrixAsec()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("asec"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPUpperTriangularMatrixFunctions")]
        public void HPUpperTriangularMatrixAcot()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("acot"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPUpperTriangularMatrixFunctions")]
        public void HPUpperTriangularMatrixAsinh()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("asinh"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPUpperTriangularMatrixFunctions")]
        public void HPUpperTriangularMatrixAcosh()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("acosh"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPUpperTriangularMatrixFunctions")]
        public void HPUpperTriangularMatrixAtanh()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("atanh"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPUpperTriangularMatrixFunctions")]
        public void HPUpperTriangularMatrixAcsch()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("acsch"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPUpperTriangularMatrixFunctions")]
        public void HPUpperTriangularMatrixAsech()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("asech"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPUpperTriangularMatrixFunctions")]
        public void HPUpperTriangularMatrixAcoth()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("acoth"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPUpperTriangularMatrixFunctions")]
        public void HPUpperTriangularMatrixLog()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("log"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPUpperTriangularMatrixFunctions")]
        public void HPUpperTriangularMatrixLn()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("ln"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPUpperTriangularMatrixFunctions")]
        public void HPUpperTriangularMatrixLog2()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("log_2"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPUpperTriangularMatrixFunctions")]
        public void HPUpperTriangularMatrixExp()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("exp"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPUpperTriangularMatrixFunctions")]
        public void HPUpperTriangularMatrixSqrt()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("sqrt"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPUpperTriangularMatrixFunctions")]
        public void HPUpperTriangularMatrixCbrt()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("cbrt"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPUpperTriangularMatrixFunctions")]
        public void HPUpperTriangularMatrixRoot()
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
        [Trait("Category", "HPUpperTriangularMatrixFunctions")]
        public void HPUpperTriangularMatrixRound()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("round"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPUpperTriangularMatrixFunctions")]
        public void HPUpperTriangularMatrixFloor()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("floor"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPUpperTriangularMatrixFunctions")]
        public void HPUpperTriangularMatrixCeiling()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("ceiling"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPUpperTriangularMatrixFunctions")]
        public void HPUpperTriangularMatrixTrunc()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("trunc"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPUpperTriangularMatrixFunctions")]
        public void HPUpperTriangularMatrixMin()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(ScalarTestHelper("min"));
            Assert.Equal(1, result);
        }

        [Fact]
        [Trait("Category", "HPUpperTriangularMatrixFunctions")]
        public void HPUpperTriangularMatrixMax()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(ScalarTestHelper("max"));
            Assert.Equal(1, result);
        }

        [Fact]
        [Trait("Category", "HPUpperTriangularMatrixFunctions")]
        public void HPUpperTriangularMatrixSum()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(ScalarTestHelper("sum"));
            Assert.Equal(1, result);
        }

        [Fact]
        [Trait("Category", "HPUpperTriangularMatrixFunctions")]
        public void HPUpperTriangularMatrixSumsq()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(ScalarTestHelper("sumsq"));
            Assert.Equal(1, result);
        }

        [Fact]
        [Trait("Category", "HPUpperTriangularMatrixFunctions")]
        public void HPUpperTriangularMatrixSrss()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(ScalarTestHelper("srss"));
            Assert.Equal(1, result);
        }

        [Fact]
        [Trait("Category", "HPUpperTriangularMatrixFunctions")]
        public void HPUpperTriangularMatrixAverage()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(ScalarTestHelper("average"));
            Assert.Equal(1, result);
        }

        [Fact]
        [Trait("Category", "HPUpperTriangularMatrixFunctions")]
        public void HPUpperTriangularMatrixProduct()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(ScalarTestHelper("product"));
            Assert.Equal(1, result);
        }

        [Fact]
        [Trait("Category", "HPUpperTriangularMatrixFunctions")]
        public void HPUpperTriangularMatrixMean()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(ScalarTestHelper("mean"));
            Assert.Equal(1, result);
        }

        [Fact]
        [Trait("Category", "HPUpperTriangularMatrixFunctions")]
        public void HPUpperTriangularMatrixTake()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(InterpolationTestHelper("take"));
            Assert.Equal(1, result);
        }

        [Fact]
        [Trait("Category", "HPUpperTriangularMatrixFunctions")]
        public void HPUpperTriangularMatrixLine()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(InterpolationTestHelper("line"));
            Assert.Equal(1, result);
        }

        [Fact]
        [Trait("Category", "HPUpperTriangularMatrixFunctions")]
        public void HPUpperTriangularMatrixSpline()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(InterpolationTestHelper("spline"));
            Assert.Equal(1, result);
        }

        [Fact]
        [Trait("Category", "HPUpperTriangularMatrixFunctions")]
        public void HPUpperTriangularMatrixSortCols()
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
        [Trait("Category", "HPUpperTriangularMatrixFunctions")]
        public void HPUpperTriangularMatrixRSortCols()
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
        [Trait("Category", "HPUpperTriangularMatrixFunctions")]
        public void HPUpperTriangularMatrixSortRows()
        {
            var calc = new TestCalc(new());
            var result = calc.Run([
                "n = 500",
                "j = 50",
                RandomMatrixA,
                "f(a; j) = sort_rows(a; j)",
                "c_hp = sort_rows(hp(a); j)",
                "r = f(a; j) ≡ f(hp(a); j)",
                "mcount(r; 0)"
            ]);
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPUpperTriangularMatrixFunctions")]
        public void HPUpperTriangularMatrixRSortRows()
        {
            var calc = new TestCalc(new());
            var result = calc.Run([
                "n = 500",
                "j = 50",
                RandomMatrixA,
                "f(a; j) = rsort_rows(a; j)",
                "c_hp = rsort_rows(hp(a); j)",
                "r = f(a; j) ≡ f(hp(a); j)",
                "mcount(r; 0)"
            ]);
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPUpperTriangularMatrixFunctions")]
        public void HPUpperTriangularMatrixOrderCols()
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
        [Trait("Category", "HPUpperTriangularMatrixFunctions")]
        public void HPUpperTriangularMatrixRevOrderCols()
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
        [Trait("Category", "HPUpperTriangularMatrixFunctions")]
        public void HPUpperTriangularMatrixOrderRows()
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
        [Trait("Category", "HPUpperTriangularMatrixFunctions")]
        public void HPUpperTriangularMatrixMcount()
        {
            var calc = new TestCalc(new());
            var result = calc.Run([
                "n = 500",
                "x = 1",
                "a = round(random(mfill(utriang(n); 1000)))",
                "f(a; x) = mcount(a; x)",
                "f(a; x) ≡ f(hp(a); x)"
            ]);
            Assert.Equal(1, result);
        }

        [Fact]
        [Trait("Category", "HPUpperTriangularMatrixFunctions")]
        public void HPUpperTriangularMatrixHprod()
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
        [Trait("Category", "HPUpperTriangularMatrixFunctions")]
        public void HPUpperTriangularMatrixFprod()
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
        [Trait("Category", "HPUpperTriangularMatrixFunctions")]
        public void HPUpperTriangularMatrixKprod()
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
        [Trait("Category", "HPUpperTriangularMatrixFunctions")]
        public void HPUpperTriangularMatrixMnorm1()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(ScalarTestHelper("mnorm_1"));
            Assert.Equal(1, result);
        }

        [Fact]
        [Trait("Category", "HPUpperTriangularMatrixFunctions")]
        public void HPUpperTriangularMatrixMnorm2()
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
        [Trait("Category", "HPUpperTriangularMatrixFunctions")]
        public void HPUpperTriangularMatrixMnormi()
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
        [Trait("Category", "HPUpperTriangularMatrixFunctions")]
        public void HPUpperTriangularMatrixCond1()
        {
            var calc = new TestCalc(new());
            var result = calc.Run([
                "n = 250",
                WellConditionedMatrix,
                "f(a) = cond_1(a)",
                "c_hp = cond_1(hp(a))",
                TestCalc.CompareWithToleranceDirect("f(a)", "f(hp(a))", "10^-12")
                ]);
            Assert.Equal(1, result);
        }

        [Fact]
        [Trait("Category", "HPUpperTriangularMatrixFunctions")]
        public void HPUpperTriangularMatrixCond2()
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
        [Trait("Category", "HPUpperTriangularMatrixFunctions")]
        public void HPUpperTriangularMatrixCondE()
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
        [Trait("Category", "HPUpperTriangularMatrixFunctions")]
        public void HPUpperTriangularMatrixCondI()
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
        [Trait("Category", "HPUpperTriangularMatrixFunctions")]
        public void HPUpperTriangularMatrixDet()
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
        [Trait("Category", "HPUpperTriangularMatrixFunctions")]
        public void HPUpperTriangularMatrixRank()
        {
            var calc = new TestCalc(new());
            var result = calc.Run([
                "m = 100",
                "n = 100",
                WellConditionedMatrix,
                "f(a) = rank(a)",
                "r = f(a) ≡ f(hp(a))"
            ]);
            Assert.Equal(1, result);
        }

        [Fact]
        [Trait("Category", "HPUpperTriangularMatrixFunctions")]
        public void HPUpperTriangularMatrixTrace()
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
        [Trait("Category", "HPUpperTriangularMatrixFunctions")]
        public void HPUpperTriangularMatrixTransp()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(FunctionTestHelper("transp"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPUpperTriangularMatrixFunctions")]
        public void HPUpperTriangularMatrixAdj()
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
        [Trait("Category", "HPUpperTriangularMatrixFunctions")]
        public void HPUpperTriangularMatrixCofactor()
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
        [Trait("Category", "HPUpperTriangularMatrixFunctions")]
        public void HPUpperTriangularMatrixLu()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(PositiveDefiniteTestHelper("lu", "10^-14"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPUpperTriangularMatrixFunctions")]
        public void HPUpperTriangularMatrixQr()
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
        [Trait("Category", "HPUpperTriangularMatrixFunctions")]
        public void HPUpperTriangularMatrixSvd()
        {
            var calc = new TestCalc(new());
            var result = calc.Run([
                "n = 200",
                WellConditionedMatrix,
                "f(a) = svd(a)",
                "r = abs(abs(f(a)) - abs(f(hp(a)))) ≤ 10^-8*max(abs(f(a)); 1)",
                "mcount(r; 0)"
            ]);
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPUpperTriangularMatrixFunctions")]
        public void HPUpperTriangularMatrixInverse()
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
        [Trait("Category", "HPUpperTriangularMatrixFunctions")]
        public void HPUpperTriangularMatrixLsolve()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(MatrixEquationTestHelper("lsolve", "10^-12"));
            Assert.Equal(0, result);
        }

        [Fact]
        [Trait("Category", "HPUpperTriangularMatrixFunctions")]
        public void HPUpperTriangularMatrixMsolve()
        {
            var calc = new TestCalc(new());
            var result = calc.Run(MatrixMultiEquationTestHelper("msolve", "10^-12"));
            Assert.Equal(0, result);
        }
        #endregion
    }
}
