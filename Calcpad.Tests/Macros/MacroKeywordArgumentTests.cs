namespace Calcpad.Tests;

public class MacroKeywordArgumentTests
{
    private static string Expand(string source)
    {
        var macroParser = new MacroParser();
        var hasErrors = macroParser.Parse(source, out var expanded, null, 0, false);
        Assert.False(hasErrors, expanded);
        return expanded;
    }

    private static string ExpandWithErrors(string source)
    {
        var macroParser = new MacroParser();
        macroParser.Parse(source, out var expanded, null, 0, false);
        return expanded;
    }

    [Fact]
    public void OmittedOptionalArguments_UseDefaults()
    {
        var expanded = Expand("#def m$(a$; b$ = 10; c$ = 5) = a$|b$|c$\nm$(1)\n");
        Assert.Contains("1|10|5", expanded);
    }

    [Fact]
    public void PositionalArgumentsOverrideDefaults()
    {
        var expanded = Expand("#def m$(a$; b$ = 10; c$ = 5) = a$|b$|c$\nm$(1; 2; 3)\n");
        Assert.Contains("1|2|3", expanded);
    }

    [Fact]
    public void KeywordArguments_CanBeGivenInAnyOrder()
    {
        var expanded = Expand("#def m$(a$; b$ = 10; c$ = 5) = a$|b$|c$\nm$(a$ = 1; c$ = 7; b$ = 2)\n");
        Assert.Contains("1|2|7", expanded);
    }

    [Fact]
    public void PositionalFollowedByKeyword_IsAllowed()
    {
        var expanded = Expand("#def m$(a$; b$ = 10; c$ = 5) = a$|b$|c$\nm$(1; c$ = 7)\n");
        Assert.Contains("1|10|7", expanded);
    }

    [Fact]
    public void KeywordArgumentsWithoutSpaces_AreRecognized()
    {
        var expanded = Expand("#def m$(a$; b$ = 10) = a$|b$\nm$(a$=1;b$=2)\n");
        Assert.Contains("1|2", expanded);
    }

    [Fact]
    public void MultilineMacro_SupportsDefaults()
    {
        var expanded = Expand("#def m$(a$; b$ = 4)\n\ta$ + b$\n#end def\nm$(3)\n");
        Assert.Contains("3 + 4", expanded);
    }

    [Fact]
    public void DefaultContainingBrackets_IsPreserved()
    {
        var expanded = Expand("#def m$(a$ = f(1; 2)) = [a$]\nm$()\n");
        Assert.Contains("[f(1; 2)]", expanded);
    }

    [Fact]
    public void MultilineMacro_DefaultDoesNotEndTheDefinition()
    {
        var expanded = Expand("#def m$(v$; u$ = mm)\n\tv$|u$\n#end def\nm$(2.5m)\n");
        Assert.Contains("2.5m|mm", expanded);
    }

    [Fact]
    public void DefaultContainingAVector_IsPreserved()
    {
        var expanded = Expand("#def m$(a$ = [1; 2; 3]) = len(a$)\nm$()\n");
        Assert.Contains("len([1; 2; 3])", expanded);
    }

    [Fact]
    public void EmptyArgumentList_UsesDefaultsWhenMacroHasOptionalParameters()
    {
        var expanded = Expand("#def m$(a$ = 7; b$ = 8) = a$|b$\nm$()\n");
        Assert.Contains("7|8", expanded);
    }

    [Fact]
    public void EmptyArgument_StillMeansEmptyStringForRequiredParameters()
    {
        var expanded = Expand("#def m$(a$) = [a$]\nm$()\n");
        Assert.Contains("[]", expanded);
    }

    [Fact]
    public void AllRequiredParameters_StillWork()
    {
        var expanded = Expand("#def m$(a$; b$) = a$+b$\nm$(1; 2)\n");
        Assert.Contains("1+2", expanded);
    }

    [Fact]
    public void ParameterWithoutDollarSuffix_StillParses()
    {
        var expanded = Expand("#def m$(a) = [a]\nm$(1)\n");
        Assert.Contains("[1]", expanded);
    }

    [Fact]
    public void ArgumentNamingAnExistingMacro_StaysPositional()
    {
        var expanded = Expand("#def y$ = q\n#def emit$(lhs$) = [lhs$]\nemit$(y$ = 5)\n");
        Assert.Contains("[q = 5]", expanded);
    }

    [Fact]
    public void UnknownKeywordArgument_ReportsError()
    {
        var expanded = ExpandWithErrors("#def m$(a$; b$ = 1) = a$|b$\nm$(1; z$ = 2)\n");
        Assert.Contains("Unknown keyword argument", expanded);
    }

    [Fact]
    public void MissingRequiredArgument_ReportsError()
    {
        var expanded = ExpandWithErrors("#def m$(a$; b$; c$ = 1) = a$|b$|c$\nm$(1)\n");
        Assert.Contains("Invalid number of arguments", expanded);
    }

    [Fact]
    public void DuplicateArgument_ReportsError()
    {
        var expanded = ExpandWithErrors("#def m$(a$; b$ = 1) = a$|b$\nm$(1; a$ = 2)\n");
        Assert.Contains("Duplicate argument", expanded);
    }

    [Fact]
    public void RequiredParameterAfterOptional_ReportsError()
    {
        var expanded = ExpandWithErrors("#def m$(a$ = 1; b$) = a$|b$\nm$(1; 2)\n");
        Assert.Contains("cannot follow an optional parameter", expanded);
    }

    [Fact]
    public void PositionalAfterKeyword_ReportsError()
    {
        var expanded = ExpandWithErrors("#def m$(a$; b$ = 1) = a$|b$\nm$(a$ = 1; 2)\n");
        Assert.Contains("Invalid number of arguments", expanded);
    }
}
