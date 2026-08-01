using Calcpad.Server.Services;

namespace Calcpad.Tests;

/// <summary>
/// <c>PortableWorksheet.Build</c> is what a compiled <c>.cpdz</c> is made from: macros and
/// includes expanded, <c>#read</c> inlined, and — the option under test here — a <c>#write</c>/
/// <c>#append</c> target rewritten when it names an absolute path, so the compiled file's output
/// lands beside wherever it runs rather than a folder that may not exist there.
/// </summary>
public class PortableWorksheetTests
{
    [Fact]
    public void AnAbsoluteWriteTarget_IsLeftAsWrittenWithTheOptionOff()
    {
        using var dir = new WorksheetDir();
        var source = $"#write R to {dir.At("results.csv")}\n";
        var result = dir.Build(source, nextToWorksheet: false);

        Assert.Empty(result.Errors);
        Assert.Equal(source, Rewritten(result));
    }

    [Fact]
    public void AnAbsoluteWriteTarget_CollapsesToItsFilenameWithTheOptionOn()
    {
        using var dir = new WorksheetDir();
        var source = $"#write R to {dir.At("results.csv")}\n";
        var result = dir.Build(source, nextToWorksheet: true);

        Assert.Empty(result.Errors);
        Assert.Equal("#write R to results.csv\n", Rewritten(result));
    }

    [Fact]
    public void ARelativeWriteTarget_IsNeverRewritten()
    {
        using var dir = new WorksheetDir();
        var source = """
            #write R to ./out/results.csv
            #append S to results.csv
            """ + "\n";
        var result = dir.Build(source, nextToWorksheet: true);

        Assert.Empty(result.Errors);
        Assert.Equal(source, Rewritten(result));
    }

    [Fact]
    public void AppendBehavesAsWrite_ForTheCollapseOption()
    {
        using var dir = new WorksheetDir();
        var source = $"#append R to {dir.At("results.csv")}\n";
        var result = dir.Build(source, nextToWorksheet: true);

        Assert.Empty(result.Errors);
        Assert.Equal("#append R to results.csv\n", Rewritten(result));
    }

    [Fact]
    public void TwoAbsoluteWriteTargetsSharingAFilename_AreRefused()
    {
        using var dir = new WorksheetDir();
        var a = dir.At("a/results.csv");
        var b = dir.At("b/results.csv");
        var result = dir.Build($"""
            #write A to {a}
            #write B to {b}
            """ + "\n", nextToWorksheet: true);

        var message = Assert.Single(result.Errors);
        Assert.Contains("results.csv", message);
        Assert.Contains(a, message);
        Assert.Contains(b, message);
    }

    [Fact]
    public void WriteThenAppendToTheSameAbsoluteFile_IsNotACollision()
    {
        using var dir = new WorksheetDir();
        var path = dir.At("results.csv");
        var result = dir.Build($"""
            #write R to {path}
            #append R to {path}
            """ + "\n", nextToWorksheet: true);

        Assert.Empty(result.Errors);
        Assert.Equal("""
            #write R to results.csv
            #append R to results.csv
            """ + "\n", Rewritten(result));
    }

    [Fact]
    public void AWriteInsideAnInclude_IsRewrittenAfterExpansion()
    {
        using var dir = new WorksheetDir();
        var absolute = dir.At("results.csv");
        dir.Write("lib.cpd", $"#write R to {absolute}\n");
        var result = dir.Build("#include ./lib.cpd\n", nextToWorksheet: true);

        Assert.Empty(result.Errors);
        Assert.Equal("#write R to results.csv\n", Rewritten(result));
    }

    [Fact]
    public void ATokenWriteTarget_AlwaysResolves_RegardlessOfNextToWorksheet()
    {
        using var dir = new WorksheetDir();
        var source = $"""
            #ProjectPath = {dir.At("out")}
            #write R to <project>/results.csv
            """ + "\n";
        var result = dir.Build(source, nextToWorksheet: false);

        Assert.Empty(result.Errors);
        Assert.Equal($"#write R to {dir.At("out/results.csv")}\n", Rewritten(result));
    }

    [Fact]
    public void ATokenWriteTarget_CollapsesToItsFilename_WithNextToWorksheetOn()
    {
        using var dir = new WorksheetDir();
        var source = $"""
            #ProjectPath = {dir.At("out")}
            #write R to <project>/results.csv
            """ + "\n";
        var result = dir.Build(source, nextToWorksheet: true);

        Assert.Empty(result.Errors);
        Assert.Equal("#write R to results.csv\n", Rewritten(result));
    }

    [Fact]
    public void ATokenImageSource_ResolvesToTheAuthorsLocalPath()
    {
        using var dir = new WorksheetDir();
        var source = $"""
            #LibraryPath = {dir.At("lib")}
            '<img src="<library>/logo.png">
            """ + "\n";
        var result = dir.Build(source, nextToWorksheet: false);

        Assert.Empty(result.Errors);
        var expectedSrc = "src=\"" + dir.At("lib/logo.png").Replace('\\', '/') + "\"";
        Assert.Contains(expectedSrc, Rewritten(result));
    }

    [Fact]
    public void AnUndeclaredTokenWriteTarget_Errors()
    {
        using var dir = new WorksheetDir();
        var result = dir.Build("#write R to <project>/results.csv\n", nextToWorksheet: false);

        var message = Assert.Single(result.Errors);
        Assert.Contains("ProjectPath", message);
    }

    // PortableWorksheet.Build always adds one trailing blank line when the source ends with a
    // newline — a pre-existing quirk of its line splitting, unrelated to what these tests check.
    private static string Rewritten(PortableWorksheet.Result result) => result.Content.TrimEnd('\n') + "\n";

    /// <summary>A folder to build a worksheet against, with a path to resolve includes from.</summary>
    private sealed class WorksheetDir : IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        public WorksheetDir() => Directory.CreateDirectory(_path);

        public string At(string relative) => Path.Combine(_path, relative.Replace('/', Path.DirectorySeparatorChar));

        public void Write(string relative, string text) => File.WriteAllText(At(relative), text);

        public PortableWorksheet.Result Build(string content, bool nextToWorksheet) =>
            PortableWorksheet.Build(content, At("worksheet.cpd"), nextToWorksheet);

        public void Dispose() => Directory.Delete(_path, true);
    }
}
