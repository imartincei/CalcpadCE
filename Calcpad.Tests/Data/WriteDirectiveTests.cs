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
