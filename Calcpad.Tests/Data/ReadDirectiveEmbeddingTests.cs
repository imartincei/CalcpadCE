using System.Text;
using Calcpad.Core;

namespace Calcpad.Tests;

/// <summary>
/// A <c>#read</c> is given its file to carry when a worksheet is compiled into a <c>.cpdz</c>,
/// which has to run with no data files beside it. Each test writes a file, embeds the directive
/// that reads it, and checks that the embedded read puts the same values in the same variable as
/// the original — the embedded and the original directive take the same path through the parser.
/// </summary>
public class ReadDirectiveEmbeddingTests
{
    [Fact]
    public void EmbeddedDirective_KeepsEverythingButTheSource()
    {
        using var temp = new DataDir("1,3\n2,4\n");
        Assert.Equal($"#read M from data:text/csv;base64,{temp.Base64} type=R_hp",
            temp.Embed("#read M from data.csv type=R_hp"));
    }

    [Fact]
    public void EmbeddedDirective_KeepsTheSeparatorAndTheRange()
    {
        using var temp = new DataDir("1;2;3\n4;5;6\n7;8;9\n");
        Assert.Equal($"#read M from data:text/csv;base64,{temp.Base64}@R2C2:R3C3 sep=';'",
            temp.Embed("#read M from data.csv@R2C2:R3C3 sep=';'"));
    }

    [Theory]
    [InlineData("", "1,2\n3,4\n", "M.(2; 1)", 3)]
    [InlineData("type=V", "1,3\n2,4\n", "v.(2)", 3)]
    [InlineData("type=C", "1\n2\n3\n", "M.(3; 1)", 3)]
    [InlineData("type=D", "1,2,3\n", "M.(2; 2)", 2)]
    [InlineData("type=L", "1\n2,3\n4,5,6\n", "M.(3; 2)", 5)]
    [InlineData("type=U", "1,2,3\n4,5\n6\n", "M.(2; 3)", 5)]
    [InlineData("type=S", "1,2\n3\n", "M.(2; 1)", 2)]
    [InlineData("sep=';'", "1;2\n3;4\n", "M.(1; 2)", 2)]
    public void EmbeddedRead_AssignsWhatTheOriginalDid(string options, string csv, string cell, double expected)
    {
        using var temp = new DataDir(csv);
        var name = options == "type=V" ? "v" : "M";
        Assert.Equal(expected, temp.Run($"#read {name} from data.csv {options}", cell));
    }

    [Theory]
    [InlineData("type=R_hp")]
    [InlineData("type=S_hp")]
    [InlineData("type=V_hp")]
    public void HighPerformanceRead_StaysHighPerformance(string options)
    {
        using var temp = new DataDir("1,2\n3,4\n");
        Assert.Equal(1, temp.Run($"#read M from data.csv {options}", "ishp(M)"));
    }

    [Fact]
    public void HighPerformanceRead_KeepsItsShape()
    {
        using var temp = new DataDir("1,2\n3\n");
        Assert.Equal(2, temp.Run("#read M from data.csv type=S_hp", "M.(2; 1)"));
    }

    /// <summary>
    /// A single row is a one-row matrix, not a vector — the shape a literal could not express.
    /// </summary>
    [Fact]
    public void SingleRow_IsAMatrixOfOneRow()
    {
        using var temp = new DataDir("1,2,3\n");
        Assert.Equal(3, temp.Run("#read M from data.csv", "n_cols(M)"));
    }

    /// <summary>Cells the source parser has no notation for: exponents are read by <c>#read</c>.</summary>
    [Fact]
    public void ScientificNotation_IsRead()
    {
        using var temp = new DataDir("7.5e-06,2e3\n");
        Assert.Equal(7.5, temp.Run("#read M from data.csv", "M.(1; 1)*10^6"));
        Assert.Equal(2000, temp.Run("#read M from data.csv", "M.(1; 2)"));
    }

    [Fact]
    public void UnitsOnCells_AreKept()
    {
        using var temp = new DataDir("1m,2m\n");
        Assert.Equal(3, temp.Run("#read M from data.csv", "M.(1; 1)/1m + M.(1; 2)/1m"));
    }

    [Fact]
    public void EmptyCells_ReadAsZero()
    {
        using var temp = new DataDir("1,,3\n");
        Assert.Equal(0, temp.Run("#read M from data.csv", "M.(1; 2)"));
    }

    [Fact]
    public void MissingFile_Throws()
    {
        using var temp = new DataDir("1\n");
        Assert.Throws<MathParserException>(() => temp.Embed("#read M from absent.csv"));
    }

    /// <summary>A workbook cannot be written as text, so it is carried as bytes.</summary>
    [Fact]
    public void ExcelRead_IsCarriedAsAWorkbook()
    {
        using var temp = new DataDir("1\n");
        temp.Write("M = [1; 2|3; 4]", "#write M to book.xlsx@Sheet1!A1:B2");
        var directive = temp.Embed("#read N from book.xlsx@Sheet1!A1:B2");

        Assert.StartsWith("#read N from data:application/vnd.openxmlformats-officedocument"
            + ".spreadsheetml.sheet;base64,", directive);
        Assert.EndsWith("@Sheet1!A1:B2", directive);
        Assert.Equal(3, temp.Run("#read N from book.xlsx@Sheet1!A1:B2", "N.(2; 1)"));
    }

    [Fact]
    public void AnEmbeddedSourceIsNotAPath()
    {
        Assert.False(ExpressionParser.TryGetDataPath(
            "#read M from data:text/csv;base64,MSwyCg== type=R", out _, out _));
    }

    [Fact]
    public void AWriteToAnEmbeddedTarget_Throws()
    {
        var calc = new ExpressionParser();
        calc.Parse("M = [1; 2]\n#write M to data:text/csv;base64,MSwyCg==", true, false);
        Assert.Contains("Invalid syntax", calc.HtmlResult);
    }

    [Fact]
    public void AnUnknownMediaType_Throws()
    {
        var calc = new ExpressionParser();
        calc.Parse("#read M from data:image/png;base64,MSwyCg==", true, false);
        Assert.Contains("not supported", calc.HtmlResult);
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

        public string Base64 => Convert.ToBase64String(File.ReadAllBytes(Path.Combine(_path, "data.csv")));

        public string Embed(string directive) =>
            ExpressionParser.EmbedReadDirective(directive, Path.Combine(_path, "worksheet.cpd"), null, out _);

        /// <summary>Runs lines in the folder, for the ones that put a file there to read back.</summary>
        public void Write(params string[] lines)
        {
            var parser = new ExpressionParser { SourceFilePath = Path.Combine(_path, "worksheet.cpd") };
            parser.Parse(string.Join('\n', lines), true, false);
            Assert.DoesNotContain("class=\"err\"", parser.HtmlResult);
        }

        /// <summary>What <paramref name="expression"/> comes to once the embedded read has run.</summary>
        public double Run(string directive, string expression)
        {
            var parser = new ExpressionParser();
            var sb = new StringBuilder(Embed(directive)).Append('\n').Append(expression);
            parser.Parse(sb.ToString(), true, false);
            var html = parser.HtmlResult;
            Assert.DoesNotContain("class=\"err\"", html);
            return Result(html);
        }

        public void Dispose() => Directory.Delete(_path, true);
    }

    /// <summary>The number the last line rendered to, read back out of the report.</summary>
    private static double Result(string html)
    {
        var end = html.LastIndexOf("</p>", StringComparison.Ordinal);
        var text = System.Text.RegularExpressions.Regex.Replace(html[..end], "<[^>]*>", string.Empty);
        var i = text.LastIndexOf('=');
        return double.Parse(text[(i + 1)..].Trim().Replace("−", "-"),
            System.Globalization.CultureInfo.InvariantCulture);
    }
}
