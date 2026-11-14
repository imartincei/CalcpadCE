using System.IO;
using System.Linq;
using Calcpad.Server.Services;

namespace Calcpad.Tests;

/// <summary>Run errors carry Core's columns to the editors, mapped to the source line.</summary>
public class RunErrorColumnTests
{
    private static CalcpadError Error(string source, string sourceFilePath = null) =>
        new CalcpadService().Convert(source, debug: true, sourceFilePath: sourceFilePath).Errors.Single();

    private static string Span(string source, int line, int start, int end) =>
        source.Split('\n')[line - 1][start..end];

    [Theory]
    [InlineData("#UI {\"type\": \"nope\"} x = 1", "{\"type\": \"nope\"}")]
    [InlineData("a = 1\n  b = 2 + * 3", "+ *")]
    [InlineData("q = 1\n\tw = 2*q + zzq*3", "zzq")]
    [InlineData("#def m$ = 1\nw = 2*zzq", "zzq")]
    public void UnchangedLine_KeepsTheColumns(string source, string expected)
    {
        var error = Error(source);
        Assert.Equal(expected, Span(source, error.SourceLine, error.StartColumn, error.EndColumn));
    }

    [Theory]
    [InlineData("#def m$(x$) = x$ + 1\nr = 3 + m$(2*zzq)", "zzq")]
    [InlineData("#def m$(x$) = x$ + zzq\nr = 1 + m$(2)", "m$(2)")]
    [InlineData("#def m$(x$) = x$\n#def n$(x$) = x$ + zzq\nr = m$(1) + n$(2)", "n$(2)")]
    [InlineData("#def m$ = zzq\nr = 1 + m$", "m$")]
    public void MacroLine_MapsTheColumns(string source, string expected)
    {
        var error = Error(source);
        Assert.Equal(source.Split('\n').Length, error.SourceLine);
        Assert.Equal(expected, Span(source, error.SourceLine, error.StartColumn, error.EndColumn));
    }

    [Fact]
    public void ContinuedLine_SplitsTheSpanAtEachLine()
    {
        const string source = "a = 2 + _\n  * 3";
        var error = Error(source);
        Assert.Equal("+", Span(source, error.SourceLine, error.StartColumn, error.EndColumn));
        var more = Assert.Single(error.AdditionalRanges);
        Assert.Equal("*", Span(source, more.SourceLine, more.StartColumn, more.EndColumn));
    }

    [Fact]
    public void ContinuedLine_PointsAtTheLineTheErrorIsOn()
    {
        const string source = "a = 1 + _\n  2*zzq";
        var error = Error(source);
        Assert.Equal((2, "zzq"), (error.SourceLine, Span(source, 2, error.StartColumn, error.EndColumn)));
        Assert.Null(error.AdditionalRanges);
    }

    [Fact]
    public void IncludedLine_CoversTheWholeLine()
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "lib.cpd"), "w = 2*zzq\n");
            var error = Error("a = 1\n#include ./lib.cpd\n", Path.Combine(dir, "main.cpd"));
            Assert.Equal((2, -1, -1), (error.SourceLine, error.StartColumn, error.EndColumn));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
