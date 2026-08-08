using Calcpad.Core;

namespace Calcpad.Tests;

/// <summary>
/// <c>#read</c> directives are rewritten as literals when a worksheet is bundled into a
/// compiled <c>.cpdz</c>, which has to run with no data files beside it. Each test writes a
/// CSV and checks that the code standing in for the directive puts the same cells in the
/// same places.
/// </summary>
public class ReadDirectiveInliningTests
{
    [Fact]
    public void Rectangular_ReadsRowsAsMatrixLiteral()
    {
        using var temp = new DataDir("1,3\n2,4\n");
        Assert.Equal("M = [1; 3|2; 4]", temp.Inline("#read M from data.csv"));
    }

    [Fact]
    public void Vector_FlattensEveryCell()
    {
        using var temp = new DataDir("1,3\n2,4\n");
        Assert.Equal("v = [1; 3; 2; 4]", temp.Inline("#read v from data.csv type=V"));
    }

    [Fact]
    public void Column_TakesTheFirstColumn()
    {
        using var temp = new DataDir("1\n2\n3\n");
        Assert.Equal("c = vec2col([1; 2; 3])", temp.Inline("#read c from data.csv type=C"));
    }

    [Fact]
    public void Column_TakesASingleRowAlong()
    {
        using var temp = new DataDir("1,2,3\n");
        Assert.Equal("c = vec2col([1; 2; 3])", temp.Inline("#read c from data.csv type=C"));
    }

    [Fact]
    public void Diagonal_TakesTheDiagonalValues()
    {
        using var temp = new DataDir("1,2,3\n");
        Assert.Equal("D = vec2diag([1; 2; 3])", temp.Inline("#read D from data.csv type=D"));
    }

    [Fact]
    public void LowerTriangular_CopiesTheRowsIntoAnLtriang()
    {
        using var temp = new DataDir("1\n2,3\n4,5,6\n");
        Assert.Equal("L = copy([1|2; 3|4; 5; 6]; ltriang(3); 1; 1)",
            temp.Inline("#read L from data.csv type=L"));
    }

    [Fact]
    public void UpperTriangular_ShiftsEachRowToTheDiagonal()
    {
        using var temp = new DataDir("1,2,3\n4,5\n6\n");
        Assert.Equal("U = copy([1; 2; 3|0; 4; 5|0; 0; 6]; utriang(3); 1; 1)",
            temp.Inline("#read U from data.csv type=U"));
    }

    [Fact]
    public void Symmetric_ShiftsEachRowToTheDiagonal()
    {
        using var temp = new DataDir("1,2\n3\n");
        Assert.Equal("S = copy([1; 2|0; 3]; symmetric(2); 1; 1)",
            temp.Inline("#read S from data.csv type=S"));
    }

    [Fact]
    public void HighPerformanceType_YieldsAPlainMatrix()
    {
        using var temp = new DataDir("1,2\n3,4\n");
        Assert.Equal("M = [1; 2|3; 4]", temp.Inline("#read M from data.csv type=R_hp"));
    }

    [Fact]
    public void Range_ReadsOnlyTheRequestedCells()
    {
        using var temp = new DataDir("1,2,3\n4,5,6\n7,8,9\n");
        Assert.Equal("M = [5; 6|8; 9]", temp.Inline("#read M from data.csv@R2C2:R3C3"));
    }

    [Fact]
    public void Separator_IsHonored()
    {
        using var temp = new DataDir("1;2\n3;4\n");
        Assert.Equal("M = [1; 2|3; 4]", temp.Inline("#read M from data.csv sep=';'"));
    }

    [Fact]
    public void EmptyCells_ReadAsZero()
    {
        using var temp = new DataDir("1,,3\n");
        Assert.Equal("M = [1; 0; 3]", temp.Inline("#read M from data.csv"));
    }

    [Fact]
    public void UnitsOnCells_AreKept()
    {
        using var temp = new DataDir("1m,2m\n");
        Assert.Equal("M = [1m; 2m]", temp.Inline("#read M from data.csv"));
    }

    [Fact]
    public void MissingFile_Throws()
    {
        using var temp = new DataDir("1\n");
        Assert.Throws<MathParserException>(() => temp.Inline("#read M from absent.csv"));
    }

    [Fact]
    public void InlinedSymmetric_EvaluatesToTheMatrixTheDirectiveWouldAssign()
    {
        using var temp = new DataDir("1,2\n3\n");
        var calc = new TestCalc(new());
        Assert.Equal(2, calc.Run([temp.Inline("#read S from data.csv type=S"), "S.(1; 2)"]));
        Assert.Equal(2, calc.Run("S.(2; 1)"));
        Assert.Equal(3, calc.Run("S.(2; 2)"));
    }

    /// <summary>A folder holding <c>data.csv</c>, with a worksheet path to resolve against.</summary>
    private sealed class DataDir : IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        public DataDir(string csv)
        {
            Directory.CreateDirectory(_path);
            File.WriteAllText(Path.Combine(_path, "data.csv"), csv);
        }

        public string Inline(string directive) =>
            ExpressionParser.InlineReadDirective(directive, Path.Combine(_path, "worksheet.cpd"));

        public void Dispose() => Directory.Delete(_path, true);
    }
}
