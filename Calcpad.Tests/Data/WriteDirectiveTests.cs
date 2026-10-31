using System.Globalization;
using Calcpad.Core;

namespace Calcpad.Tests;

/// <summary>
/// A host that re-renders on every keystroke must not rewrite the document's output each time,
/// so <see cref="ExpressionParser.AllowDataWrite"/> gates <c>#write</c>/<c>#append</c>. What the
/// gate must not change is what gets reported: a directive still errors, and still says what it
/// would have done, whether or not the write ran.
/// </summary>
public class WriteDirectiveTests
{
    [Fact]
    public void ByDefault_TheFileIsWritten()
    {
        using var temp = new WriteDir();
        var html = temp.Run("M = [1; 2|3; 4]", "#write M to out.csv");

        Assert.True(File.Exists(temp.Path("out.csv")));
        Assert.Contains("was successfully written to", html);
    }

    [Fact]
    public void WritingOff_LeavesNoFile()
    {
        using var temp = new WriteDir();
        var html = temp.Run(false, "M = [1; 2|3; 4]", "#write M to out.csv");

        Assert.False(File.Exists(temp.Path("out.csv")));
        Assert.Contains("will be written to", html);
        Assert.DoesNotContain("was successfully", html);
    }

    /// <summary>A link to a file that was never created leads nowhere, so it is not offered.</summary>
    [Fact]
    public void WritingOff_DoesNotLinkTheTarget()
    {
        using var temp = new WriteDir();
        var html = temp.Run(false, "M = [1; 2]", "#write M to out.csv");

        Assert.DoesNotContain("file:///", html);
        Assert.Contains("out.csv", html);
    }

    [Fact]
    public void WritingOff_StillAppendsUnderItsOwnVerb()
    {
        using var temp = new WriteDir();
        var html = temp.Run(false, "M = [1; 2]", "#append M to out.csv");

        Assert.Contains("will be appended to", html);
    }

    [Fact]
    public void WritingOff_LeavesAnExistingFileAlone()
    {
        using var temp = new WriteDir();
        File.WriteAllText(temp.Path("out.csv"), "keep me\n");
        temp.Run(false, "M = [1; 2|3; 4]", "#write M to out.csv");

        Assert.Equal("keep me\n", File.ReadAllText(temp.Path("out.csv")));
    }

    /// <summary>
    /// The preview must not hide an error that only surfaces once the write is allowed to run,
    /// so the directive is parsed and the matrix fetched either way.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnUndefinedVariable_ErrorsEitherWay(bool allowWrite)
    {
        using var temp = new WriteDir();
        Assert.Contains("class=\"err\"", temp.Run(allowWrite, "#write Missing to out.csv"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AMissingTarget_ErrorsEitherWay(bool allowWrite)
    {
        using var temp = new WriteDir();
        Assert.Contains("class=\"err\"", temp.Run(allowWrite, "M = [1; 2]", "#write M to"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AMissingFolder_ErrorsEitherWay(bool allowWrite)
    {
        using var temp = new WriteDir();
        var html = temp.Run(allowWrite, "M = [1; 2]", "#write M to no_such_folder/out.csv");

        Assert.Contains("class=\"err\"", html);
        Assert.DoesNotContain("written to", html);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnUnsupportedExcelFormat_ErrorsEitherWay(bool allowWrite)
    {
        using var temp = new WriteDir();
        Assert.Contains("class=\"err\"", temp.Run(allowWrite, "M = [1; 2]", "#write M to out.xls@Sheet1"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AFalseCondition_SkipsTheDirectiveEitherWay(bool allowWrite)
    {
        using var temp = new WriteDir();
        var html = temp.Run(allowWrite, "M = [1; 2]", "#if 0", "#write M to out.csv", "#end if");

        Assert.False(File.Exists(temp.Path("out.csv")));
        Assert.DoesNotContain("written to", html);
    }

    /// <summary>A decimal comma would collide with the column separator and read back as a thousands one.</summary>
    [Fact]
    public void UnderADecimalCommaCulture_CsvUsesInvariantNumbers()
    {
        using var temp = new WriteDir();
        InCulture("it-IT", () => temp.Run("M = [1.5; 0|0; 2.25kN]", "#write M to out.csv"));

        Assert.Equal(["1.5,0", "0,2.25kN"], File.ReadAllLines(temp.Path("out.csv")));
    }

    [Theory]
    [InlineData("out.csv sep=';'")]
    [InlineData("out.xlsx")]
    public void UnderADecimalCommaCulture_ValuesReadBack(string target)
    {
        using var temp = new WriteDir();
        var html = InCulture("it-IT", () => temp.Run(
            "M = [1.5; 0|0; 2.25]", $"#write M to {target}", $"#read R from {target}", "r = R.(1; 1) + R.(2; 2)"));

        Assert.DoesNotContain("class=\"err\"", html);
        Assert.Contains("= 3,75", html);
    }

    /// <summary>Trailing zeros are written, so a zero last column survives the round trip.</summary>
    [Fact]
    public void TrailingZeroColumns_AreWritten()
    {
        using var temp = new WriteDir();
        var html = temp.Run("M = [1; 0|2; 0]", "#write M to out.csv", "#read R from out.csv", "c = n_cols(R)");

        Assert.Equal(["1,0", "2,0"], File.ReadAllLines(temp.Path("out.csv")));
        Assert.Contains("= 2</span>", html);
    }

    [Fact]
    public void TriangularRows_AreWrittenInFull()
    {
        using var temp = new WriteDir();
        temp.Run("U = utriang(3)", "U.(1; 1) = 1", "#write U to out.csv type=Y");

        Assert.Equal(["1,0,0", "0,0", "0"], File.ReadAllLines(temp.Path("out.csv")));
    }

    /// <summary>An appended zero must overwrite the cell, not leave the old value behind.</summary>
    [Fact]
    public void AppendedZeros_OverwriteTheRange()
    {
        using var temp = new WriteDir();
        var html = temp.Run(
            "A = [1; 2|3; 4]",
            "#write A to out.xlsx@Sheet1!A1",
            "B = [5; 0|6; 0]",
            "#append B to out.xlsx@Sheet1!A1",
            "#read R from out.xlsx@Sheet1!A1:B2",
            "s = R.(1; 2) + R.(2; 2)");

        Assert.Contains("= 0</span>", html);
    }

    /// <summary>A comma is never a thousands separator, so "1,5" is 1.5 or an error, never 15.</summary>
    [Theory]
    [InlineData("it-IT", "= 3,75")]
    [InlineData("en-US", "Cannot evaluate")]
    public void ReadingADecimalComma_FollowsTheCulture(string culture, string expected)
    {
        using var temp = new WriteDir();
        File.WriteAllText(temp.Path("in.csv"), "1,5;2,25\n");
        var html = InCulture(culture, () => temp.Run("#read R from in.csv sep=';'", "r = R.(1; 1) + R.(1; 2)"));

        Assert.Contains(expected, html);
        Assert.DoesNotContain("37,5", html);
        Assert.DoesNotContain("240", html);
    }

    private static string InCulture(string name, Func<string> run)
    {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
        try
        {
            return run();
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    /// <summary>A folder to write into, with a worksheet path to resolve relative targets against.</summary>
    private sealed class WriteDir : IDisposable
    {
        private readonly string _dir = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName());

        public WriteDir() => Directory.CreateDirectory(_dir);

        public string Path(string name) => System.IO.Path.Combine(_dir, name);

        public string Run(params string[] lines) => Run(true, lines);

        public string Run(bool allowWrite, params string[] lines)
        {
            var parser = new ExpressionParser
            {
                SourceFilePath = Path("worksheet.cpd"),
                AllowDataWrite = allowWrite
            };
            parser.Parse(string.Join('\n', lines), true, false);
            return parser.HtmlResult;
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch { /* best-effort cleanup */ }
        }
    }
}
