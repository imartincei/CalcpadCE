using Calcpad.Core;

namespace Calcpad.Tests;

/// <summary>
/// <c>PathRoots</c> is the single implementation of the <c>&lt;project&gt;</c>/<c>&lt;library&gt;</c>
/// grammar, and these tests exercise it both directly and through the two Core parsers that
/// declare and expand it: <c>MacroParser</c>, which resolves <c>#include</c>, and
/// <c>ExpressionParser</c>, which resolves <c>#read</c>/<c>#write</c>.
/// </summary>
public class PathRootsTests
{
    [Fact]
    public void TryDeclare_WithAnEmptyValue_Fails()
    {
        var roots = new PathRoots();
        Assert.False(roots.TryDeclare(true, "", "/project", out var error));
        Assert.Contains("ProjectPath", error);
    }

    [Fact]
    public void TryDeclare_TwiceWithTheSameResolvedValue_IsANoOp()
    {
        using var temp = new TempDir();
        var roots = new PathRoots();
        Assert.True(roots.TryDeclare(true, temp.Path, "/project", out _));
        Assert.True(roots.TryDeclare(true, temp.Path, "/project", out _));
    }

    [Fact]
    public void TryDeclare_TwiceWithADifferentValue_Fails()
    {
        using var temp1 = new TempDir();
        using var temp2 = new TempDir();
        var roots = new PathRoots();
        Assert.True(roots.TryDeclare(true, temp1.Path, "/project", out _));
        Assert.False(roots.TryDeclare(true, temp2.Path, "/project", out var error));
        Assert.Contains("ProjectPath", error);
    }

    [Fact]
    public void TryDeclare_WithAFolderThatDoesNotExist_Fails()
    {
        var roots = new PathRoots();
        Assert.False(roots.TryDeclare(true, "no/such/folder", "/project", out var error));
        Assert.Contains("ProjectPath", error);
    }

    [Fact]
    public void TryExpand_WithAnUndeclaredRoot_FailsButLeavesTheTokenInPlace()
    {
        var roots = new PathRoots();
        Assert.False(roots.TryExpand("{library}/steel.cpd", out var expanded, out var error));
        Assert.Equal("{library}/steel.cpd", expanded);
        Assert.Contains("LibraryPath", error);
    }

    [Fact]
    public void TryExpand_WithADeclaredRoot_JoinsTheRemainder()
    {
        using var temp = new TempDir();
        var roots = new PathRoots();
        roots.TryDeclare(false, temp.Path, "/project", out _);
        Assert.True(roots.TryExpand("{library}/steel.cpd", out var expanded, out _));
        Assert.Equal(System.IO.Path.Combine(temp.Path, "steel.cpd"), expanded);
    }

    [Fact]
    public void TryExpand_ANonTokenPath_IsLeftUntouched()
    {
        var roots = new PathRoots();
        Assert.True(roots.TryExpand("./data/loads.csv", out var expanded, out var error));
        Assert.Equal("./data/loads.csv", expanded);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("#ProjectPath C:/Jobs/1042", true, "C:/Jobs/1042")]
    [InlineData("#LibraryPath C:/Lib", false, "C:/Lib")]
    [InlineData("#projectpath ./rel", true, "./rel")]
    public void IsDeclaration_LocatesTheValue(string line, bool expectProject, string expectValue)
    {
        Assert.True(PathRoots.IsDeclaration(line.AsSpan(), out var isProject, out var start, out var length));
        Assert.Equal(expectProject, isProject);
        Assert.Equal(expectValue, line.Substring(start, length));
    }

    [Fact]
    public void IsDeclaration_WithNoValue_ReportsAZeroLengthValue()
    {
        Assert.True(PathRoots.IsDeclaration("#ProjectPath".AsSpan(), out _, out _, out var length));
        Assert.Equal(0, length);
    }

    [Fact]
    public void IsDeclaration_WithATrailingComment_StopsBeforeIt()
    {
        Assert.True(PathRoots.IsDeclaration("#LibraryPath /lib 'the shared library".AsSpan(),
            out _, out var start, out var length));
        Assert.Equal("/lib", "#LibraryPath /lib 'the shared library"[start..(start + length)]);
    }

    [Fact]
    public void Include_WithATokenDeclaredAboveIt_Resolves()
    {
        using var temp = new TempDir();
        temp.Write("lib/steel.cpd", "leaf = 1\n");

        var macroParser = new MacroParser { Include = (f, _) => File.ReadAllText(f), SourceFilePath = temp.At("main.cpd") };
        macroParser.Parse($"#LibraryPath {temp.Path}/lib\n#include {{library}}/steel.cpd\n",
            out var expanded, null, 0, false);

        Assert.DoesNotContain("not found", expanded);
        Assert.Contains("leaf = 1", expanded);
    }

    [Fact]
    public void Include_WithATokenUsedBeforeItsDeclaration_Errors()
    {
        using var temp = new TempDir();
        temp.Write("lib/steel.cpd", "leaf = 1\n");

        var macroParser = new MacroParser { Include = (f, _) => File.ReadAllText(f), SourceFilePath = temp.At("main.cpd") };
        macroParser.Parse($"#include {{library}}/steel.cpd\n#LibraryPath {temp.Path}/lib\n",
            out var expanded, null, 0, false);

        Assert.DoesNotContain("leaf = 1", expanded);
        Assert.Contains("LibraryPath", expanded);
    }

    [Fact]
    public void Include_WithASecondConflictingDeclaration_Errors()
    {
        using var temp = new TempDir();
        temp.Write("a/.keep", "");
        temp.Write("b/.keep", "");

        var macroParser = new MacroParser { Include = (f, _) => File.ReadAllText(f), SourceFilePath = temp.At("main.cpd") };
        macroParser.Parse("#LibraryPath ./a\n#LibraryPath ./b\n", out var expanded, null, 0, false);

        Assert.Contains("LibraryPath", expanded);
    }

    [Fact]
    public void Declaration_WithNoValue_Errors()
    {
        using var temp = new TempDir();

        var macroParser = new MacroParser { Include = (f, _) => File.ReadAllText(f), SourceFilePath = temp.At("main.cpd") };
        macroParser.Parse("#LibraryPath\n", out var expanded, null, 0, false);

        Assert.Contains("LibraryPath", expanded);
    }

    [Fact]
    public void Declaration_WithAFolderThatDoesNotExist_Errors()
    {
        using var temp = new TempDir();

        var macroParser = new MacroParser { Include = (f, _) => File.ReadAllText(f), SourceFilePath = temp.At("main.cpd") };
        macroParser.Parse("#LibraryPath ./no-such-folder\n", out var expanded, null, 0, false);

        Assert.Contains("LibraryPath", expanded);
    }

    [Fact]
    public void InlineReadDirective_WithADeclaredToken_ReadsTheFile()
    {
        using var temp = new TempDir();
        temp.Write("data/loads.csv", "1,2\n");
        var roots = new PathRoots();
        roots.TryDeclare(true, temp.Path + "/data", temp.Path, out _);

        var assignment = ExpressionParser.InlineReadDirective(
            "#read M from {project}/loads.csv", temp.At("main.cpd"), roots);

        Assert.Equal("M = [1; 2]", assignment);
    }

    [Fact]
    public void InlineReadDirective_WithAnUndeclaredToken_Throws()
    {
        using var temp = new TempDir();
        Assert.Throws<MathParserException>(() =>
            ExpressionParser.InlineReadDirective("#read M from {project}/loads.csv", temp.At("main.cpd"), new PathRoots()));
    }

    [Fact]
    public void TryExpand_UserToken_JoinsTheHomeDirectory()
    {
        var roots = new PathRoots();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.True(roots.TryExpand("{user}/lib/steel.cpd", out var expanded, out var error));
        Assert.Null(error);
        Assert.Equal(System.IO.Path.Combine(home, "lib", "steel.cpd"), expanded);
    }

    [Fact]
    public void TryExpand_UserTokenAlone_ResolvesToTheHomeDirectory()
    {
        var roots = new PathRoots();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.True(roots.TryExpand("{user}", out var expanded, out _));
        Assert.Equal(home, expanded);
    }

    [Fact]
    public void TryExpand_UserToken_NeedsNoDeclaration()
    {
        // Unlike {project}/{library}, a fresh PathRoots instance with nothing declared still
        // resolves {user} — it is never "undeclared".
        var roots = new PathRoots();
        Assert.True(roots.TryExpand("{user}/a.cpd", out _, out var error));
        Assert.Null(error);
    }

    [Fact]
    public void TryDeclare_WithAUserTokenValue_ExpandsItFirst()
    {
        var roots = new PathRoots();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.True(roots.TryDeclare(true, "{user}", "/project", out _));
        Assert.Equal(home, roots.Project);
    }


    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName());

        public TempDir() => Directory.CreateDirectory(Path);

        public string At(string relative) => System.IO.Path.Combine(Path, relative);

        public string Write(string relative, string content)
        {
            var p = System.IO.Path.Combine(Path, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(p)!);
            File.WriteAllText(p, content);
            return p;
        }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
