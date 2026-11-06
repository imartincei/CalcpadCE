using System;
using System.IO;
using Calcpad.Server.Services;

namespace Calcpad.Tests;

/// <summary>An included file's metadata footer only applies to that file, so it never reaches the host's output.</summary>
public class UiOverridesIncludeTests
{
    [Fact]
    public void CalcpadService_StripsAUiOverridesCommentFromAnInclude()
    {
        using var temp = new TempDir();
        temp.Write("sub/mod.cpd", "#UI L = 4\n'<!--CalcpadCE Metadata-->\n'<!--{\"uiOverrides\":{\"L:1\":\"8\"}}-->\n");

        var (html, _, errors, _) = new CalcpadService().Convert(
            "#include sub/mod.cpd\n", sourceFilePath: temp.At("main.cpd"), forceUnwrappedCode: true);

        Assert.Empty(errors);
        Assert.DoesNotContain("uiOverrides", html);
    }

    [Fact]
    public void CalcpadService_StripsAFileInfoCommentFromAnInclude()
    {
        using var temp = new TempDir();
        temp.Write("sub/mod.cpd", "x = 1\n'<!--{\"fileInfo\":{\"title\":\"Module\"}}-->\n");

        var (html, _, errors, _) = new CalcpadService().Convert(
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
