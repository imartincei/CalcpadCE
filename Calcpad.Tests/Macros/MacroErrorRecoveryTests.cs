using System.Linq;
using Calcpad.Highlighter.ContentResolution;
using Calcpad.Highlighter.Linter;

namespace Calcpad.Tests;

public class MacroErrorRecoveryTests
{
    private static (MacroParser Macros, ExpressionParser Parser) Run(string source)
    {
        var macros = new MacroParser();
        Assert.True(macros.Parse(source, out var expanded, null, 0, true));
        var parser = new ExpressionParser { Debug = true };
        parser.Parse(expanded, true, false);
        return (macros, parser);
    }

    [Theory]
    [InlineData("#def m$(a$) = a$ + 1\nx = 1\ny = m$(1; 2)\nz = 3*x", 3, "CPD-3304")]
    [InlineData("#def m$ = 1\ny = undefined$(1)\nz = 3", 2, "CPD-3303")]
    [InlineData("x = 1\n#include no-such-file.cpd\nz = 3*x", 2, "CPD-1104")]
    public void FailedLine_IsAnErrorAndTheRunContinues(string source, int line, string code)
    {
        var (macros, parser) = Run(source);
        var error = Assert.Single(macros.Errors);
        Assert.Equal((line, code), (error.SourceLine, error.Code));
        Assert.Contains("<p class=\"err\"", parser.HtmlResult);
        Assert.Contains(error.Message, parser.HtmlResult);
        Assert.Contains("<var>z</var>", parser.HtmlResult);
        Assert.Empty(parser.Errors);
    }

    [Fact]
    public void FailedLine_IsInTheErrorSummary()
    {
        var (_, parser) = Run("#def m$ = 1\ny = undefined$(1)\nz = 3");
        Assert.Contains("class=\"roundBox\" data-line=\"1\"", parser.HtmlResult);
    }

    [Fact]
    public void FailedLine_HidesInHiddenBlocks()
    {
        var (_, parser) = Run("#def m$ = 1\n#hide\ny = undefined$(1)\n#show\nz = 3");
        Assert.DoesNotContain("<p class=\"err\"", parser.HtmlResult);
    }

    [Fact]
    public void ErrorLine_RoundTrips()
    {
        var macros = new MacroParser();
        macros.Parse("#def m$ = 1\ny = undefined$(1)", out var expanded, null, 0, false);
        Assert.True(MacroParser.TryParseErrorLine(expanded.Trim(), out var code, out var line, out var message));
        Assert.Equal(("CPD-3303", "y = undefined$(1)", macros.Errors[0].Message), (code, line, message));
    }

    [Fact]
    public void CheckPass_SkipsFailedLines()
    {
        new MacroParser().Parse("#def m$ = 1\ny = undefined$(1)\nz = 1 + * 2", out var expanded, null, 0, true);
        var errors = new ExpressionParser().Check(expanded);
        Assert.Equal([(3, "CPD-3401")], errors.Select(e => (e.SourceLine, e.Code)));
    }

    [Fact]
    public void Lint_ChecksTheRestOfADocumentWithMacroErrors()
    {
        var staged = new ContentResolver().GetStagedContent("#def m$ = 1\ny = undefined$(1)\nz = 1 + * 2");
        var diagnostics = new CalcpadLinter().Lint(staged).Diagnostics;
        Assert.Contains(diagnostics, d => d.Line == 2 && d.Code == "CPD-3401");
        Assert.DoesNotContain(diagnostics, d => d.Line == 1 && d.Code.StartsWith("CPD-5"));
    }
}
