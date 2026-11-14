using System.Linq;
using Calcpad.Highlighter.ContentResolution;
using Calcpad.Highlighter.Linter;

namespace Calcpad.Tests;

public class MacroCallsSettingTests
{
    private static (MacroParser Parser, string[] Lines) Parse(string source, MacroCallMode defaultMode = MacroCallMode.Auto)
    {
        var parser = new MacroParser { DefaultMacroCalls = defaultMode };
        parser.Parse(source, out var expanded, null, 0, false);
        return (parser, expanded.Split('\n', StringSplitOptions.TrimEntries));
    }

    private static string Settings(string mode) => $"#settings {{\"macroCalls\": \"{mode}\"}}";

    [Fact]
    public void Auto_IgnoresDollarBeforeAnyDef()
    {
        var (parser, lines) = Parse("'Cost a$\n#def m$ = 1\nx = m$");
        Assert.Empty(parser.Errors);
        Assert.Contains("'Cost a$", lines);
        Assert.Contains("x = 1", lines);
    }

    [Fact]
    public void Off_LeavesDollarLiteralAfterDef()
    {
        var (parser, lines) = Parse($"#def m$ = 1\n{Settings("off")}\n'Cost a$ and m$");
        Assert.Empty(parser.Errors);
        Assert.Contains("'Cost a$ and m$", lines);
    }

    [Fact]
    public void On_ReportsUndefinedMacroWithoutDef()
    {
        var (parser, _) = Parse($"{Settings("on")}\nx = a$");
        Assert.Single(parser.Errors);
    }

    [Fact]
    public void ChangesMidFile()
    {
        var (parser, lines) = Parse($"#def m$ = 1\n{Settings("off")}\ny = m$\n{Settings("auto")}\nz = m$");
        Assert.Empty(parser.Errors);
        Assert.Contains("y = m$", lines);
        Assert.Contains("z = 1", lines);
    }

    [Fact]
    public void EndSettings_RestoresPriorMode()
    {
        var (parser, lines) = Parse($"#def m$ = 1\n{Settings("off")}\ny = m$\n#end settings\nz = m$");
        Assert.Empty(parser.Errors);
        Assert.Contains("y = m$", lines);
        Assert.Contains("z = 1", lines);
    }

    [Fact]
    public void SettingsLine_IsKeptForExpressionParser()
    {
        var (_, lines) = Parse(Settings("off"));
        Assert.Contains(Settings("off"), lines);
    }

    [Fact]
    public void SettingsInsideDefBody_DoesNotChangeMode()
    {
        var (parser, lines) = Parse($"#def m$\n{Settings("off")}\nx = 1\n#end def\ny = n$");
        Assert.Single(parser.Errors);
        Assert.DoesNotContain("y = n$", lines);
    }

    [Fact]
    public void AppDefault_Off_LeavesDollarLiteral()
    {
        var (parser, lines) = Parse("#def m$ = 1\ny = m$", MacroCallMode.Off);
        Assert.Empty(parser.Errors);
        Assert.Contains("y = m$", lines);
    }

    [Fact]
    public void EndSettings_RestoresAppDefault()
    {
        var (parser, lines) = Parse($"#def m$ = 1\n{Settings("auto")}\ny = m$\n#end settings\nz = m$", MacroCallMode.Off);
        Assert.Empty(parser.Errors);
        Assert.Contains("y = 1", lines);
        Assert.Contains("z = m$", lines);
    }

    [Fact]
    public void Linter_HonorsAppDefault()
    {
        const string source = "#def m$ = 1\n'Cost a$";
        Assert.NotEmpty(new ContentResolver().GetStagedContent(source).Stage2.MacroErrors);
        Assert.Empty(new ContentResolver().GetStagedContent(source, null, MacroCallMode.Off).Stage2.MacroErrors);
    }

    [Fact]
    public void ContentCache_KeysByMode()
    {
        using var memoryCache = new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());
        var cache = new Calcpad.Server.Services.ContentResolutionCache(memoryCache, new Calcpad.Server.Services.RuntimeTraceCache());
        const string source = "#def m$ = 1\n'Cost a$";
        Assert.NotEmpty(cache.GetOrResolve(source, null).Stage2.MacroErrors);
        Assert.Empty(cache.GetOrResolve(source, null, macroCalls: MacroCallMode.Off).Stage2.MacroErrors);
        Assert.NotEmpty(cache.GetOrResolve(source, null).Stage2.MacroErrors);
    }

    [Theory]
    [InlineData("on")]
    [InlineData("OFF")]
    [InlineData("auto")]
    public void Linter_AcceptsValidValues(string mode)
    {
        var diagnostics = Lint($"{Settings(mode)}\nx = 1");
        Assert.DoesNotContain(diagnostics, d => d.Code == "CPD-3413");
    }

    [Theory]
    [InlineData("\"maybe\"")]
    [InlineData("\"1\"")]
    [InlineData("true")]
    public void Linter_RejectsInvalidValues(string value)
    {
        var diagnostics = Lint($"#settings {{\"macroCalls\": {value}}}\nx = 1");
        Assert.Contains(diagnostics, d => d.Code == "CPD-3413");
    }

    private static System.Collections.Generic.List<Calcpad.Highlighter.Linter.Models.LinterDiagnostic> Lint(string content)
    {
        var staged = new ContentResolver().GetStagedContent(content);
        var ignore = new LintIgnoreRegionParser().ExtractRegions(content);
        return new CalcpadLinter().Lint(staged, ignore).Diagnostics.ToList();
    }
}
