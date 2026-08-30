using Calcpad.Core;

namespace Calcpad.Tests;

/// <summary>
/// The file name a directive names, and where it sits in the line. Bundling a worksheet
/// rewrites exactly that span, so anything around it — a trailing comment, an input field
/// block, a sheet, a range, <c>type=</c>, <c>sep=</c> — has to fall outside it.
/// </summary>
public class DirectivePathTests
{
    private static string IncludePath(string line) =>
        MacroParser.TryGetIncludePath(line, out var start, out var length)
            ? line.Substring(start, length)
            : null;

    private static string DataPath(string line) =>
        ExpressionParser.TryGetDataPath(line, out var start, out var length)
            ? line.Substring(start, length)
            : null;

    [Theory]
    [InlineData("#include lib.cpd", "lib.cpd")]
    [InlineData("#include   lib.cpd  ", "lib.cpd")]
    [InlineData("#include ./inc/lib.cpd", "./inc/lib.cpd")]
    [InlineData("#include ../shared/lib.cpd", "../shared/lib.cpd")]
    [InlineData("#include 8 Masonry.cpd", "8 Masonry.cpd")]
    [InlineData("#INCLUDE lib.cpd", "lib.cpd")]
    [InlineData("#include lib.cpd 'a note", "lib.cpd")]
    [InlineData("#include lib.cpd \"a note", "lib.cpd")]
    [InlineData("#include lib.cpd #{1;2}", "lib.cpd")]
    [InlineData("#include lib.cpd #{1;2} 'a note", "lib.cpd")]
    public void Include_TakesTheFileName(string line, string expected) =>
        Assert.Equal(expected, IncludePath(line));

    [Theory]
    [InlineData("#include")]
    [InlineData("#include ")]
    [InlineData("#read M from data.csv")]
    [InlineData("' #include lib.cpd")]
    public void Include_TakesNothingFromWhatNamesNoFile(string line) =>
        Assert.Null(IncludePath(line));

    /// <summary>
    /// A <c>#</c> in the name ends it, since the parser reads the rest as an input field block.
    /// The exporter reports the truncated name as unreadable, which is what the directive does
    /// with it too.
    /// </summary>
    [Fact]
    public void Include_EndsTheNameAtAHash() =>
        Assert.Equal("rev", IncludePath("#include rev#3/lib.cpd"));

    [Theory]
    [InlineData("#read M from data.csv", "data.csv")]
    [InlineData("#READ M from data.csv", "data.csv")]
    [InlineData("#read M from ./tables/fy.csv", "./tables/fy.csv")]
    [InlineData("#read M from data.csv type=R", "data.csv")]
    [InlineData("#read M from data.csv type=S_hp", "data.csv")]
    [InlineData("#read M from data.txt type=R sep=';'", "data.txt")]
    [InlineData("#read M from data.csv@R1C1:R2C2 type=V", "data.csv")]
    [InlineData("#read M from book.xlsx@Sheet1!A1:B2 type=R", "book.xlsx")]
    [InlineData("#read M from book.xlsm!A1:B2", "book.xlsm")]
    [InlineData("#read M from book.xls@Sheet1", "book.xls")]
    [InlineData("#write M to out.csv", "out.csv")]
    [InlineData("#append M to out.csv type=N sep=','", "out.csv")]
    public void Data_TakesThePathAndExtension(string line, string expected) =>
        Assert.Equal(expected, DataPath(line));

    [Theory]
    [InlineData("#read")]
    [InlineData("#read M")]
    [InlineData("#read M from")]
    [InlineData("#read M with data.csv")]
    [InlineData("#write M from out.csv")]
    [InlineData("#read M from data_csv")]
    [InlineData("#include lib.cpd")]
    public void Data_TakesNothingFromWhatNamesNoFile(string line) =>
        Assert.Null(DataPath(line));

    /// <summary>
    /// The span is the one the directive reads, so replacing it leaves the options alone. This
    /// is what lets a rewritten path keep a sheet and a range that were never about the file.
    /// </summary>
    [Fact]
    public void Data_LeavesEverythingAfterThePathAlone()
    {
        const string line = "#read M from book.xlsx@Sheet1!A1:B2 type=R sep=';'";
        Assert.True(ExpressionParser.TryGetDataPath(line, out var start, out var length));
        Assert.Equal("#read M from calc.cpd.refs/book.xlsx@Sheet1!A1:B2 type=R sep=';'",
            string.Concat(line.AsSpan(0, start), "calc.cpd.refs/book.xlsx", line.AsSpan(start + length)));
    }

    /// <summary>
    /// The extension is opened by the last <c>.</c> of the whole line, so a dot in a trailing
    /// comment moves it — the directive cannot read such a line either, and the span stays the
    /// one it would have used.
    /// </summary>
    [Fact]
    public void Data_TakesTheLastDotOfTheLine() =>
        Assert.Equal("data.csv 'see note.txt", DataPath("#read M from data.csv 'see note.txt"));
}
