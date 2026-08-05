using Calcpad.Core;
using Calcpad.Server.Services;

namespace Calcpad.Tests;

/// <summary>
/// <c>PathRoots</c> is the single implementation of the <c>&lt;project&gt;</c>/<c>&lt;library&gt;</c>
/// grammar, and these tests exercise it both directly and through the two Core parsers that
/// declare and expand it: <c>MacroParser</c>, which resolves <c>#include</c>, and
/// <c>ExpressionParser</c>, which resolves <c>#read</c>/<c>#write</c>.
/// </summary>
public class PathRootsTests
{
    // TryDeclare feeds this to Path.GetFullPath, which rejects a base directory that is not
    // fully qualified — "/project" is rooted on Linux but drive-less, so it throws on Windows.
    private static readonly string DeclaringDirectory = System.IO.Path.GetTempPath();

    [Fact]
    public void TryDeclare_WithAnEmptyValue_Fails()
    {
        var roots = new PathRoots();
        Assert.False(roots.TryDeclare(true, "", DeclaringDirectory, out var error));
        Assert.Contains("ProjectPath", error);
    }

    [Fact]
    public void TryDeclare_TwiceWithTheSameResolvedValue_IsANoOp()
    {
        using var temp = new TempDir();
        var roots = new PathRoots();
        Assert.True(roots.TryDeclare(true, temp.Path, DeclaringDirectory, out _));
        Assert.True(roots.TryDeclare(true, temp.Path, DeclaringDirectory, out _));
    }

    [Fact]
    public void TryDeclare_TwiceWithADifferentValue_Fails()
    {
        using var temp1 = new TempDir();
        using var temp2 = new TempDir();
        var roots = new PathRoots();
        Assert.True(roots.TryDeclare(true, temp1.Path, DeclaringDirectory, out _));
        Assert.False(roots.TryDeclare(true, temp2.Path, DeclaringDirectory, out var error));
        Assert.Contains("ProjectPath", error);
    }

    [Fact]
    public void TryDeclare_WithAFolderThatDoesNotExist_Fails()
    {
        var roots = new PathRoots();
        Assert.False(roots.TryDeclare(true, "no/such/folder", DeclaringDirectory, out var error));
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
        roots.TryDeclare(false, temp.Path, DeclaringDirectory, out _);
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
        Assert.Equal(System.IO.Path.Combine(home, "lib/steel.cpd"), expanded);
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
        Assert.True(roots.TryDeclare(true, "{user}", DeclaringDirectory, out _));
        Assert.Equal(home, roots.Project);
    }

    /// <summary>
    /// Renders <paramref name="source"/> the way every host does — flatten with
    /// <c>MacroParser</c>, then hand its roots to <c>ExpressionParser</c> — so an image source is
    /// resolved against the roots the file that declared them was read at.
    /// </summary>
    private static (string Html, IReadOnlyList<CalcpadError> Errors) Render(string source, string sourceFilePath)
    {
        var macroParser = new MacroParser
        {
            Include = (f, _) => File.ReadAllText(f),
            SourceFilePath = sourceFilePath,
        };
        Assert.False(macroParser.Parse(source, out var expanded, null, 0, false));

        var parser = new ExpressionParser
        {
            Settings = new Settings(),
            SourceFilePath = sourceFilePath,
            PathRoots = macroParser.PathRoots,
            Debug = true,
        };
        parser.Parse(expanded, true, false);
        return (parser.HtmlResult, parser.Errors);
    }

    private static string Img(string src) => $"'<img src=\"{src}\">";

    [Fact]
    public void ImageSource_WithADeclaredToken_IsMadeAbsolute()
    {
        using var temp = new TempDir();
        temp.Write("lib/.keep", "");

        var (html, errors) = Render(
            $"#LibraryPath {temp.Path}/lib\n{Img("{library}/logo.png")}\n", temp.At("main.cpd"));

        Assert.Empty(errors);
        Assert.Contains($"src=\"{temp.Path.Replace('\\', '/')}/lib/logo.png\"", html);
    }

    [Fact]
    public void ImageSource_WithARelativeDeclarationAndDotDotSegments_IsStillAbsoluteAndNormalized()
    {
        using var temp = new TempDir();
        temp.Write("lib/.keep", "");
        temp.Write("shared/.keep", "");

        var (html, errors) = Render(
            $"#LibraryPath ./lib\n{Img("{library}/../shared/logo.png")}\n", temp.At("main.cpd"));

        Assert.Empty(errors);
        Assert.Contains($"src=\"{temp.Path.Replace('\\', '/')}/shared/logo.png\"", html);
    }

    [Fact]
    public void ImageSource_WithAUserToken_JoinsTheHomeDirectory()
    {
        using var temp = new TempDir();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).Replace('\\', '/');

        var (html, errors) = Render($"{Img("{user}/logo.png")}\n", temp.At("main.cpd"));

        Assert.Empty(errors);
        Assert.Contains($"src=\"{home}/logo.png\"", html);
    }

    [Fact]
    public void ImageSource_WithAnEnvironmentVariable_IsExpanded()
    {
        using var temp = new TempDir();
        var appData = Environment.GetEnvironmentVariable("APPDATA")
            ?? Environment.GetEnvironmentVariable("HOME");
        Assert.NotNull(appData);

        var (html, errors) = Render($"{Img("%APPDATA%/logo.png")}\n", temp.At("main.cpd"));

        Assert.Empty(errors);
        Assert.Contains($"src=\"{appData.Replace('\\', '/')}/logo.png\"", html);
    }

    [Theory]
    [InlineData("images/plan.png")]
    [InlineData("./images/plan.png")]
    [InlineData("https://example.com/logo.png")]
    [InlineData("data:image/png;base64,iVBORw0KGgo=")]
    public void ImageSource_WithNoToken_IsLeftAsAuthored(string src)
    {
        using var temp = new TempDir();

        var (html, errors) = Render($"{Img(src)}\n", temp.At("main.cpd"));

        Assert.Empty(errors);
        Assert.Contains($"src=\"{src}\"", html);
    }

    [Fact]
    public void ImageSource_WithAnUndeclaredRoot_IsLeftAsWrittenAndReportsAnError()
    {
        using var temp = new TempDir();

        var (html, errors) = Render($"{Img("{library}/logo.png")}\n", temp.At("main.cpd"));

        Assert.Contains("src=\"{library}/logo.png\"", html);
        Assert.Contains("LibraryPath", Assert.Single(errors).Message);
    }

    [Fact]
    public void ImageSource_WithARootDeclaredInsideAnInclude_ResolvesAgainstTheIncludingFile()
    {
        // The regression this guards: ExpressionParser sees the flattened text, which no longer
        // says that "#ProjectPath ." was written in sub/mod.cpd, so re-declaring it there would
        // resolve the value against the root document and point the image at the wrong folder.
        using var temp = new TempDir();
        temp.Write("sub/mod.cpd", $"#ProjectPath .\n{Img("{project}/logo.png")}\n");

        var (html, errors) = Render("#include sub/mod.cpd\n", temp.At("main.cpd"));

        Assert.Empty(errors);
        Assert.Contains($"src=\"{temp.Path.Replace('\\', '/')}/sub/logo.png\"", html);
    }

    [Fact]
    public void ImageSource_InMarkdown_IsExpandedToo()
    {
        using var temp = new TempDir();
        temp.Write("lib/.keep", "");

        var (html, errors) = Render(
            $"#LibraryPath {temp.Path}/lib\n#md on\n'![plan]({{library}}/logo.png)\n", temp.At("main.cpd"));

        Assert.Empty(errors);
        Assert.Contains($"src=\"{temp.Path.Replace('\\', '/')}/lib/logo.png\"", html);
    }

    [Fact]
    public void ImageSource_WithoutAMacroParser_IsStillExpandedFromTheParsersOwnDeclaration()
    {
        // Calcpad.Api's Parser.Parse(code) runs ExpressionParser alone, so it has to keep
        // declaring the directives itself.
        using var temp = new TempDir();
        temp.Write("lib/.keep", "");

        var parser = new ExpressionParser { Settings = new Settings(), SourceFilePath = temp.At("main.cpd") };
        parser.Parse($"#LibraryPath {temp.Path}/lib\n{Img("{library}/logo.png")}\n", true, false);

        Assert.Contains($"src=\"{temp.Path.Replace('\\', '/')}/lib/logo.png\"", parser.HtmlResult);
    }

    [Fact]
    public void CalcpadService_ResolvesAnImageTokenDeclaredInsideAnInclude()
    {
        // The same guard as above, through the web pipeline: CalcpadService has to hand
        // MacroParser's roots to ExpressionParser or the include's "#ProjectPath ." is re-resolved
        // against the root document. The frontend no longer gets the roots to fix it up itself.
        using var temp = new TempDir();
        temp.Write("sub/mod.cpd", $"#ProjectPath .\n{Img("{project}/logo.png")}\n");

        var (html, _, errors) = new CalcpadService().Convert(
            "#include sub/mod.cpd\n", sourceFilePath: temp.At("main.cpd"));

        Assert.Empty(errors);
        Assert.Contains($"src=\"{temp.Path.Replace('\\', '/')}/sub/logo.png\"", html);
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
