using Calcpad.Server.Services;

namespace Calcpad.Tests;

/// <summary>
/// The metadata footer never reaches the preview or exports.
/// </summary>
public class MetadataStripTests
{
    private const string Source =
        "a = 1\n" +
        "'<!--{\"note\":\"kept\"}-->\n" +
        "'<!--CalcpadCE Metadata-->\n" +
        "'<!--{\"uiOverrides\":{\"a:1\":\"2 > 1\"}}-->\n" +
        "'<!--{\"fileInfo\":{\"title\":\"T\",\"project\":\"\"}}-->\n";

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Convert_RemovesFooter(bool forPrint, bool unwrapped)
    {
        var html = new CalcpadService().Convert(Source, forPrint: forPrint, forceUnwrappedCode: unwrapped).Html;

        Assert.DoesNotContain("CalcpadCE Metadata", html);
        Assert.DoesNotContain("uiOverrides", html);
        Assert.DoesNotContain("fileInfo", html);
        Assert.Contains("kept", html);
    }
}
