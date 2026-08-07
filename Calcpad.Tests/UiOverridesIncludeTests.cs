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

        var (html, _, errors) = new CalcpadService().Convert(
            "#include sub/mod.cpd\n", sourceFilePath: temp.At("main.cpd"), forceUnwrappedCode: true);

        Assert.Empty(errors);
        Assert.DoesNotContain("uiOverrides", html);
    }

    [Fact]
    public void CalcpadService_KeepsAUiOverridesCommentDeclaredInTheMainFile()
    {
        // The strip only applies to content an #include brings in - the including document's
        // own comment is exactly the one the host is supposed to read back.
        using var temp = new TempDir();
        temp.Write("sub/mod.cpd", "#UI q = 1\n");

        var (html, _, errors) = new CalcpadService().Convert(
            "'<!--{\"uiOverrides\":{\"L:1\":\"8\"}}-->\n#include sub/mod.cpd\n",
            sourceFilePath: temp.At("main.cpd"), forceUnwrappedCode: true);

        Assert.Empty(errors);
        Assert.Contains("uiOverrides", html);
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
