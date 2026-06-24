namespace Calcpad.Tests;

public class RecursiveMacroTests
{
    [Fact]
    public void DirectSelfReference_ReportsCircularError_DoesNotOverflow()
    {
        var macroParser = new MacroParser();
        var source = "#def selfref$ = selfref$\nselfref$\n";

        var hasErrors = macroParser.Parse(source, out var expanded, null, 0, false);

        Assert.True(hasErrors);
        Assert.Contains("ircular", expanded);
    }

    [Fact]
    public void MutualRecursion_ReportsCircularError_DoesNotOverflow()
    {
        var macroParser = new MacroParser();
        var source = "#def alpha$ = beta$\n#def beta$ = alpha$\nalpha$\n";

        var hasErrors = macroParser.Parse(source, out var expanded, null, 0, false);

        Assert.True(hasErrors);
        Assert.Contains("ircular", expanded);
    }

    [Fact]
    public void ParameterizedSelfReference_ReportsCircularError_DoesNotOverflow()
    {
        var macroParser = new MacroParser();
        var source = "#def grow$(x$) = grow$(x$)\ngrow$(1)\n";

        var hasErrors = macroParser.Parse(source, out var expanded, null, 0, false);

        Assert.True(hasErrors);
        Assert.Contains("ircular", expanded);
    }

    [Fact]
    public void NonCyclicNesting_StillExpandsFully()
    {
        var macroParser = new MacroParser();
        var source = "#def one$ = 1\n#def two$ = one$ + one$\n#def three$ = two$ + one$\nthree$\n";

        var hasErrors = macroParser.Parse(source, out var expanded, null, 0, false);

        Assert.False(hasErrors);
        Assert.Contains("1 + 1 + 1", expanded);
    }
}
