using Calcpad.Server.Services;

namespace Calcpad.Tests;

/// <summary>
/// <c>PortableWorksheet.Build</c> is what a compiled <c>.cpdz</c> is made from: macros and
/// includes expanded, <c>#read</c> inlined, and — what is under test here — a <c>#write</c>/
/// <c>#append</c> target rewritten when it names an absolute path, so the compiled file's output
/// lands beside wherever it runs rather than a folder that may not exist there.
/// </summary>
public class PortableWorksheetTests
{
    [Fact]
    public void AnAbsoluteWriteTarget_CollapsesToItsFilename()
    {
        using var dir = new WorksheetDir();
        var source = $"#write R to {dir.At("results.csv")}\n";
        var result = dir.Build(source);

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
        var result = dir.Build(source);

        Assert.Empty(result.Errors);
        Assert.Equal(source, Rewritten(result));
    }

    [Fact]
    public void AppendBehavesAsWrite_ForTheCollapse()
    {
        using var dir = new WorksheetDir();
        var source = $"#append R to {dir.At("results.csv")}\n";
        var result = dir.Build(source);

        Assert.Empty(result.Errors);
        Assert.Equal("#append R to results.csv\n", Rewritten(result));
    }

    /// <summary>
    /// Both are absolute and neither can keep the bare name over the other, so both are renamed
    /// — the same <c>name-1.ext</c>/<c>name-2.ext</c> scheme <c>PortablePackage</c> uses for two
    /// bundled dependency files sharing a basename.
    /// </summary>
    [Fact]
    public void TwoAbsoluteWriteTargetsSharingAFilename_AreBothRenamed()
    {
        using var dir = new WorksheetDir();
        var a = dir.At("a/results.csv");
        var b = dir.At("b/results.csv");
        var result = dir.Build($"""
            #write A to {a}
            #write B to {b}
            """ + "\n");

        Assert.Empty(result.Errors);
        Assert.Equal("""
            #write A to results-1.csv
            #write B to results-2.csv
            """ + "\n", Rewritten(result));
    }

    /// <summary>
    /// A relative target is never renamed — it stays exactly as written — so the colliding
    /// absolute one is the one renamed away from it instead.
    /// </summary>
    [Fact]
    public void AnAbsoluteWriteTargetCollidingWithARelativeOne_IsRenamedAwayFromIt()
    {
        using var dir = new WorksheetDir();
        var absolute = dir.At("out/results.csv");
        var result = dir.Build($"""
            #write A to {absolute}
            #write B to results.csv
            """ + "\n");

        Assert.Empty(result.Errors);
        Assert.Equal("""
            #write A to results-1.csv
            #write B to results.csv
            """ + "\n", Rewritten(result));
    }

    [Fact]
    public void WriteThenAppendToTheSameAbsoluteFile_IsNotACollision()
    {
        using var dir = new WorksheetDir();
        var path = dir.At("results.csv");
        var result = dir.Build($"""
            #write R to {path}
            #append R to {path}
            """ + "\n");

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
        var result = dir.Build("#include ./lib.cpd\n");

        Assert.Empty(result.Errors);
        Assert.Equal("#write R to results.csv\n", Rewritten(result));
    }

    /// <summary>
    /// A token target resolves against this machine's own root first, then collapses like any
    /// other absolute target — a compiled worksheet's source is locked, so there is no way for
    /// whoever opens it to add a <c>#ProjectPath</c>/<c>#LibraryPath</c> of their own.
    /// </summary>
    [Fact]
    public void ATokenWriteTarget_ResolvesAndCollapsesToItsFilename()
    {
        using var dir = new WorksheetDir();
        Directory.CreateDirectory(dir.At("out"));
        var source = $$"""
            #ProjectPath {{dir.At("out")}}
            #write R to {project}/results.csv
            """ + "\n";
        var result = dir.Build(source);

        Assert.Empty(result.Errors);
        Assert.Equal("#write R to results.csv\n", Rewritten(result));
    }

    [Fact]
    public void ATokenImageSource_ResolvesToTheAuthorsLocalPath()
    {
        using var dir = new WorksheetDir();
        Directory.CreateDirectory(dir.At("lib"));
        var source = $$"""
            #LibraryPath {{dir.At("lib")}}
            '<img src="{library}/logo.png">
            """ + "\n";
        var result = dir.Build(source);

        Assert.Empty(result.Errors);
        var expectedSrc = "src=\"" + dir.At("lib/logo.png").Replace('\\', '/') + "\"";
        Assert.Contains(expectedSrc, Rewritten(result));
    }

    [Fact]
    public void AUserTokenImageSource_ResolvesToTheHomeDirectory()
    {
        using var dir = new WorksheetDir();
        var source = "'<img src=\"{user}/logo.png\">\n";
        var result = dir.Build(source);

        Assert.Empty(result.Errors);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var expectedSrc = "src=\"" + Path.Combine(home, "logo.png").Replace('\\', '/') + "\"";
        Assert.Contains(expectedSrc, Rewritten(result));
    }

    /// <summary>
    /// <c>&lt;user&gt;</c> needs no declaration and always resolves, so it reaches the collapse
    /// the same way a declared <c>&lt;project&gt;</c>/<c>&lt;library&gt;</c> target does.
    /// </summary>
    [Fact]
    public void AUserTokenWriteTarget_ResolvesAndCollapses()
    {
        using var dir = new WorksheetDir();
        var result = dir.Build("#write R to {user}/results.csv\n");

        Assert.Empty(result.Errors);
        Assert.Equal("#write R to results.csv\n", Rewritten(result));
    }

    [Fact]
    public void AnUndeclaredTokenWriteTarget_Errors()
    {
        using var dir = new WorksheetDir();
        var result = dir.Build("#write R to {project}/results.csv\n");

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

        public PortableWorksheet.Result Build(string content) =>
            PortableWorksheet.Build(content, At("worksheet.cpd"));

        public void Dispose() => Directory.Delete(_path, true);
    }
}
