namespace Calcpad.Tests;

public class MacroParserErrorTests
{
    private static (MacroParser Parser, string[] Lines) Parse(string source)
    {
        var parser = new MacroParser();
        string expanded = null;
        // Dedicated thread so thread-pool starvation under full-suite load can't fake a hang.
        var task = Task.Factory.StartNew(
            () => parser.Parse(source, out expanded, null, 0, false),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
        Assert.True(task.Wait(TimeSpan.FromSeconds(30)), "MacroParser hung");
        return (parser, expanded.Split('\n', StringSplitOptions.TrimEntries));
    }

    private static string[] Messages(MacroParser parser) =>
        [.. parser.Errors.Select(e => e.Message)];

    [Theory]
    [InlineData("#def m$(a$")]
    [InlineData("#def m$(a$ ")]
    public void UnclosedParameterList_DoesNotHang(string def)
    {
        var (parser, _) = Parse($"{def}\nx = 1");
        Assert.Contains(Core.Messages.Brackets_not_closed, Messages(parser));
    }

    [Fact]
    public void DefinitionErrors_DoNotAbort()
    {
        var (parser, lines) = Parse("#def m$(a$; a$) = a$\n#def n$(a$ = 1; b$) = b$\n#def k$(x\n#end def\nx = 1");
        Assert.Equal(
        [
            string.Format(Core.Messages.Duplicate_macro_parameter_names_0_and_1, "a$", "a$"),
            string.Format(Core.Messages.Required_parameter_after_optional_0, "b$"),
            Core.Messages.Brackets_not_closed,
        ], Messages(parser));
        Assert.Contains("x = 1", lines);
    }

    [Theory]
    [InlineData("#include")]
    [InlineData("#include   ")]
    public void MissingIncludePath_DoesNotAbort(string include)
    {
        var (parser, lines) = Parse($"{include}\nx = 1");
        Assert.Equal([Core.Messages.Missing_source_file_for_include], Messages(parser));
        Assert.Contains("x = 1", lines);
    }

    [Theory]
    [InlineData("#def 1x$ = 5")]
    [InlineData("#def a-b$ = 1")]
    [InlineData("#def abc = 1")]
    public void InvalidMacroName_DoesNotSwallowDocument(string def)
    {
        var (parser, lines) = Parse($"{def}\nx = 1");
        Assert.Single(parser.Errors);
        Assert.Contains("x = 1", lines);
    }

    [Fact]
    public void StrayEndDef_DoesNotBreakLaterDefinitions()
    {
        var (parser, lines) = Parse("#end def\n#def m$ = 1\nx = m$");
        Assert.Equal([Core.Messages.There_is_no_matching_def], Messages(parser));
        Assert.Contains("x = 1", lines);
    }

    [Fact]
    public void InlineDefInsideBlock_DoesNotNest()
    {
        var (parser, lines) = Parse("#def a$\n#def b$ = 1\n#end def\nx = 1");
        Assert.Equal([Core.Messages.Invalid_inside_macro_definition], Messages(parser));
        Assert.Contains("x = 1", lines);
    }

    [Fact]
    public void ArgumentText_IsNotSubstitutedAgain()
    {
        var (_, lines) = Parse("#def f$(a; b) = a + b\nx = f$(1; a)");
        Assert.Contains("x = 1 + a", lines);
    }

    [Fact]
    public void LongerParameter_WinsOverPrefix()
    {
        var (_, lines) = Parse("#def f$(x; xa) = x + xa\ny = f$(1; 2)");
        Assert.Contains("y = 1 + 2", lines);
    }

    [Fact]
    public void UnclosedDef_IsReportedOnDefLine()
    {
        var (parser, _) = Parse("x = 1\n#def blk$\ny = 2");
        var error = Assert.Single(parser.Errors);
        Assert.Equal(2, error.SourceLine);
        Assert.Equal(Core.Messages.Macro_definition_block_not_closed_Missing_end_def, error.Message);
    }
}
