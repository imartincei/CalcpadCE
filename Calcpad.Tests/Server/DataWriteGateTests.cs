using System;
using System.IO;
using Calcpad.Server.Services;

namespace Calcpad.Tests;

/// <summary>
/// The server decides per request whether a render may run <c>#write</c>/<c>#append</c>, and
/// defaults to not. The case worth pinning is the forced-unwrapped path: it runs a second,
/// silent parser to collect error lines, which used to repeat every write the document does.
/// </summary>
public class DataWriteGateTests
{
    private const string Document = "M = [1; 2|3; 4]\n#write M to out.csv\n";

    [Fact]
    public void Convert_DoesNotWriteUnlessAsked()
    {
        using var temp = new TempDir();
        new CalcpadService().Convert(Document, sourceFilePath: temp.At("main.cpd"));

        Assert.False(File.Exists(temp.At("out.csv")));
    }

    [Fact]
    public void Convert_WritesWhenAsked()
    {
        using var temp = new TempDir();
        new CalcpadService().Convert(Document, sourceFilePath: temp.At("main.cpd"), write: true);

        Assert.True(File.Exists(temp.At("out.csv")));
    }

    /// <summary>
    /// The silent error-collection pass is hard-coded not to write, whatever the request asked
    /// for: running the document's side effects a second time is never wanted.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnwrappedConvert_NeverWritesTwice(bool write)
    {
        using var temp = new TempDir();
        new CalcpadService().Convert(
            Document, sourceFilePath: temp.At("main.cpd"), forceUnwrappedCode: true, write: write);

        // The unwrapped view is a code listing, so the only parse that runs is the silent one.
        Assert.False(File.Exists(temp.At("out.csv")));
    }

    private sealed class TempDir : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        public TempDir() => Directory.CreateDirectory(_dir);

        public string At(string name) => Path.Combine(_dir, name);

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch { /* best-effort cleanup */ }
        }
    }
}
