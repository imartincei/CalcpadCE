using System;
using System.IO;
using Calcpad.Server.Services;

namespace Calcpad.Tests;

/// <summary>
/// An included file's line endings must not add blank lines to the flattened text.
/// </summary>
public class IncludeLineEndingTests
{
    [Theory]
    [InlineData("\r\n")]
    [InlineData("\n")]
    [InlineData("\r")]
    public void IncludeDelegate_PreservesLineCount(string eol)
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".cpd");
        File.WriteAllText(path, string.Join(eol, "#hide", "'a'", "", "'b'"));
        try
        {
            var result = CalcpadService.CreateIncludeDelegate()(path, new());
            var lines = result.Split(Environment.NewLine);

            Assert.Equal(new[] { "#hide", "'a'", "", "'b'" }, lines);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
