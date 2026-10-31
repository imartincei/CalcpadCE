using System;
using System.IO;
using Calcpad.Server.Services;

namespace Calcpad.Tests;

/// <summary>
/// A saved 'uiOverrides' metadata comment only has any effect on the file that carries it, so
/// <c>CalcpadService</c> strips one out of an included file's content the same way it strips
/// <c>#local</c>...<c>#global</c> blocks - it should never sit inertly in the flattened text
/// just because the file happened to be reached through <c>#include</c>.
/// </summary>
public class UiOverridesIncludeTests
{
    [Fact]
    public void CalcpadService_StripsAUiOverridesCommentFromAnInclude()
    {
        using var temp = new TempDir();
        temp.Write("sub/mod.cpd", "'<!--{\"uiOverrides\":{\"L:1\":\"8\"}}-->\n#UI L = 4\n");

        var (html, _, errors, _, _) = new CalcpadService().Convert(
            "#include sub/mod.cpd\n", sourceFilePath: temp.At("main.cpd"), forceUnwrappedCode: true);

        Assert.Empty(errors);
        Assert.DoesNotContain("uiOverrides", html);
    }

    [Fact]
    public void CalcpadService_StripsAFileInfoCommentFromAnInclude()
    {
        using var temp = new TempDir();
        temp.Write("sub/mod.cpd", "'<!--{\"fileInfo\":{\"title\":\"Module\"}}-->\nx = 1\n");

        var (html, _, errors, _, _) = new CalcpadService().Convert(
            "#include sub/mod.cpd\n", sourceFilePath: temp.At("main.cpd"), forceUnwrappedCode: true);

        Assert.Empty(errors);
        Assert.DoesNotContain("fileInfo", html);
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
