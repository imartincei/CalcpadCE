using System.Linq;
using Calcpad.Server.Services;

namespace Calcpad.Tests;

/// <summary>Run errors carry Core's columns to the editors.</summary>
public class RunErrorColumnTests
{
    private static CalcpadError Error(string source) =>
        new CalcpadService().Convert(source, debug: true).Errors.Single();

    [Theory]
    [InlineData("#UI {\"type\": \"nope\"} x = 1", "{\"type\": \"nope\"}")]
    [InlineData("a = 1\n  b = 2 + * 3", "+ *")]
    [InlineData("q = 1\n\tw = 2*q + zzq*3", "zzq")]
    public void UnchangedLine_KeepsTheColumns(string source, string expected)
    {
        var error = Error(source);
        Assert.Equal(expected, source.Split('\n')[error.SourceLine - 1][error.StartColumn..error.EndColumn]);
    }

    [Fact]
    public void MacroExpandedLine_DropsTheColumns()
    {
        var error = Error("#def m$(x$) = x$ + zzq\nr = m$(2)");
        Assert.Equal((2, -1, -1), (error.SourceLine, error.StartColumn, error.EndColumn));
    }
}
