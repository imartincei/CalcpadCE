namespace Calcpad.Tests;

public class CommandBlockErrorTests
{
    private static string[] Errors(string code, bool calculate = true)
    {
        var parser = new ExpressionParser { Debug = true };
        parser.Parse(code, calculate, false);
        return [.. parser.Errors.Select(e => e.SourceLine + ": " + e.Message)];
    }

    [Fact]
    public void SolverParseError_DoesNotBlockLaterFunctionDefinitions()
    {
        var error = Assert.Single(Errors("y = $Find{x^2 - 4 @ x = 1}\nf(x) = 2*x\nz = f(3)"));
        Assert.StartsWith("1: ", error);
    }

    [Fact]
    public void SolverRuntimeError_DoesNotBlockLaterFunctionDefinitions()
    {
        var error = Assert.Single(Errors("y = $Find{x + 1m @ x = 0 : 1}\nf(x) = 2*x\nz = f(3)"));
        Assert.StartsWith("1: ", error);
    }

    [Fact]
    public void PlotError_DoesNotHideLaterNoSolution()
    {
        var errors = Errors("$Plot{q @ x = 0 : 1}\ny = $Root{x^2 + 1 @ x = -1 : 1}");
        Assert.Equal(2, errors.Length);
        Assert.StartsWith("2: ", errors[1]);
    }

    [Fact]
    public void UnclosedSolver_IsNotReportedAsFormatString()
    {
        var error = Assert.Single(Errors("y = $Find{x^2 - 4 @ x = 0 : 3"));
        Assert.DoesNotContain("format string", error);
    }

    [Fact]
    public void UndefinedFunctionInSolver_WithoutCalculation_IsReported()
    {
        var error = Assert.Single(Errors("y = $Find{g(x) @ x = 0 : 1}", false));
        Assert.Contains("g", error);
    }

    [Theory]
    [InlineData("$Plot{x @ x = 0}", "Missing delimiter")]
    [InlineData("$Plot{ @ x = 0 : 1}", "Missing")]
    [InlineData("$Plot{x|x|x @ x = 0 : 1}", "More than one delimiter")]
    [InlineData("$Map{x + y @ x = 0 : 1}", "Missing delimiter")]
    public void MalformedPlot_IsReportedAsError(string code, string message)
    {
        var error = Assert.Single(Errors($"{code}\nz = 1"));
        Assert.StartsWith("1: " + message, error);
        Assert.DoesNotContain("<span", error);
    }
}
