namespace Calcpad.Tests;

public class EmptyMacroArgumentTests
{
    private static string Expand(string source)
    {
        var macroParser = new MacroParser();
        var hasErrors = macroParser.Parse(source, out var expanded, null, 0, false);
        Assert.False(hasErrors, expanded);
        return expanded;
    }

    [Fact]
    public void LeadingEmptyArguments_ExpandToEmptyStrings()
    {
        var expanded = Expand("#def m$(a$;b$;c$) = a$|b$|c$\nm$(;;2)\n");
        Assert.Contains("||2", expanded);
    }

    [Fact]
    public void TrailingEmptyArguments_ExpandToEmptyStrings()
    {
        var expanded = Expand("#def m$(a$;b$;c$) = a$|b$|c$\nm$(1;;)\n");
        Assert.Contains("1||", expanded);
    }

    [Fact]
    public void MiddleEmptyArgument_ExpandsToEmptyString()
    {
        var expanded = Expand("#def m$(a$;b$;c$) = a$|b$|c$\nm$(1;;3)\n");
        Assert.Contains("1||3", expanded);
    }

    [Fact]
    public void WhitespaceOnlyArgument_TreatedAsEmpty()
    {
        var expanded = Expand("#def m$(a$;b$;c$) = a$|b$|c$\nm$( ; ;2)\n");
        Assert.Contains("||2", expanded);
    }

    [Fact]
    public void AllEmptyArguments_ExpandToEmptyStrings()
    {
        var expanded = Expand("#def m$(a$;b$) = [a$b$]\nm$(;)\n");
        Assert.Contains("[]", expanded);
    }
}
