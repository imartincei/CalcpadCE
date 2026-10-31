namespace Calcpad.Tests;

public class BlockAndLoopErrorTests
{
    private static string[] Errors(string code)
    {
        var parser = new ExpressionParser { Debug = true };
        parser.Parse(code, true, false);
        return [.. parser.Errors.Select(e => e.SourceLine + ": " + e.Message)];
    }

    private static int[] UnclosedLines(string code)
    {
        new MacroParser().Parse(code, out var expanded, null, 0, true);
        var parser = new ExpressionParser { Debug = true };
        parser.Parse(expanded, true, false);
        return [.. parser.Errors
            .Where(e => e.Message == Core.Messages.if_block_not_closed_Missing_end_if ||
                        e.Message == Core.Messages.Iteration_block_not_closed_Missing_loop)
            .Select(e => e.SourceLine)];
    }

    [Theory]
    [InlineData("x = 1\n#if x > 0\ny = 2", 2)]
    [InlineData("#if 1\n#end if\n#for i = 1 : 2\ny = i\n#loop\n#if i\nz = 1", 6)]
    [InlineData("#repeat 2\ny = 1\nz = 2", 1)]
    [InlineData("#def m$ = 1\ny = 1\n#while y < 3\ny = y + m$", 3)]
    public void UnclosedBlock_IsReportedOnItsOpeningLine(string code, int line)
    {
        Assert.Equal([line], UnclosedLines(code));
    }

    [Fact]
    public void UnclosedIfAndLoop_AreBothReported()
    {
        Assert.Equal([2, 3], UnclosedLines("x = 1\n#if x > 0\n#while x < 3\nx = x + 1").Order());
    }

    [Fact]
    public void LoopPasses_ReportALineOnce()
    {
        var errors = Errors("#for i = 1 : 100\nx = 1m + 1s\n#loop\ny = 1 + * 2");
        Assert.Equal(2, errors.Length);
        Assert.StartsWith("4: ", errors[1]);
    }

    [Fact]
    public void InvalidFormat_NamesTheFormat()
    {
        var error = Assert.Single(Errors("#format q9z\nx = 1"));
        Assert.Contains("q9z", error);
        Assert.DoesNotContain("{0}", error);
    }

    [Fact]
    public void BadUiJson_ReportsOneError()
    {
        Assert.Single(Errors("#UI {\"type\": bad} x = 5\ny = x + 1"));
    }

    [Fact]
    public void OversizedDatagrid_DoesNotStopTheRun()
    {
        var errors = Errors("#UI {\"type\": \"datagrid\", \"rows\": 100000, \"columns\": 100000} M = [1]\ny = 1 + * 2");
        Assert.Equal(2, errors.Length);
        Assert.StartsWith("2: ", errors[1]);
    }
}
