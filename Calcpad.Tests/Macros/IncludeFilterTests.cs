namespace Calcpad.Tests;

public class IncludeFilterTests
{
    private const string Footer =
        "'<!--CalcpadCE Metadata-->\n" +
        "'<!--{\"uiOverrides\":{\"L:1\":\"8\"}}-->\n" +
        "'<!--{\"fileInfo\":{\"title\":\"Module\"}}-->\n";

    private static string[] Filter(string content, bool keepLineCount = false) =>
        MacroParser.FilterIncludedContent(content, keepLineCount)
            .Split(Environment.NewLine);

    [Fact]
    public void Footer_IsDropped()
    {
        Assert.Equal(["x = 1", ""], Filter("x = 1\n\n" + Footer));
    }

    [Fact]
    public void GeneralMetadataComments_AreKept()
    {
        const string desc = "'<!--{\"desc\":\"Area\"}-->";
        const string pdf = "'<!--{\"pdf\":{\"format\":\"A4\"}}-->";
        Assert.Equal([pdf, desc, "A(b; h) = b*h", ""], Filter($"{pdf}\n{desc}\nA(b; h) = b*h\n\n" + Footer));
    }

    [Fact]
    public void FooterKeys_OutsideTheFooter_AreKept()
    {
        const string stray = "'<!--{\"uiOverrides\":{\"L:1\":\"8\"}}-->";
        Assert.Equal([stray, "x = 1", ""], Filter($"{stray}\nx = 1\n"));
    }

    [Fact]
    public void FooterParts_AreOptional()
    {
        Assert.Equal(["x = 1"], Filter("x = 1\n'<!--{\"fileInfo\":{\"title\":\"Module\"}}-->"));
        Assert.Equal(["x = 1"], Filter("x = 1\n'<!--CalcpadCE Metadata-->"));
        Assert.Equal(["x = 1"], Filter("x = 1\n'<!--{\"uiOverrides\":{\"L:1\":\"8\"}}-->"));
    }

    [Fact]
    public void MultiLineFooterComment_IsDropped()
    {
        Assert.Equal(["x = 1"], Filter("x = 1\n'<!--{\"fileInfo\": _\n'{\"title\":\"Module\"}}-->"));
    }

    [Fact]
    public void KeepLineCount_BlanksTheFooter()
    {
        Assert.Equal(["x = 1", "", "", "", ""], Filter("x = 1\n" + Footer, keepLineCount: true));
    }

    [Fact]
    public void LocalSections_AreStillDropped()
    {
        Assert.Equal(["a = 1", "c = 3", ""], Filter("a = 1\n#local\nb = 2\n#global\nc = 3\n"));
    }
}
