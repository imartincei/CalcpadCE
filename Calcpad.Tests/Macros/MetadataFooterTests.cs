namespace Calcpad.Tests;

public class MetadataFooterTests
{
    private const string Footer =
        "'<!--CalcpadCE Metadata-->\n" +
        "'<!--{\"uiOverrides\":{\"L:1\":\"8\"}}-->\n" +
        "'<!--{\"fileInfo\":{\"title\":\"Module\"}}-->\n";

    private const string CrLfFooter =
        "'<!--CalcpadCE Metadata-->\r\n" +
        "'<!--{\"uiOverrides\":{\"a:1\":\"2\"}}-->\r\n" +
        "'<!--{\"fileInfo\": _\r\n" +
        "'{\"title\":\"T\"}}-->\r\n\r\n";

    [Theory]
    [InlineData("x = 1\n\n" + Footer, "x = 1\n\n")]
    [InlineData("a = 1\n" + CrLfFooter, "a = 1\n")]
    [InlineData(CrLfFooter, "")]
    [InlineData("x = 1\n'<!--{\"fileInfo\": _\n'{\"title\":\"Module\"}}-->", "x = 1\n")]
    public void Remove_CutsTheTrailingFooter(string source, string expected) =>
        Assert.Equal(expected, MetadataFooter.Remove(source));

    [Theory]
    [InlineData("x = 1\n'<!--{\"fileInfo\":{\"title\":\"Module\"}}-->")]
    [InlineData("x = 1\n'<!--CalcpadCE Metadata-->")]
    [InlineData("x = 1\n'<!--{\"uiOverrides\":{\"L:1\":\"8\"}}-->")]
    public void FooterParts_AreOptional(string source) =>
        Assert.Equal("x = 1\n", MetadataFooter.Remove(source));

    [Theory]
    [InlineData("a = 1\n'<!--{\"desc\":\"x\"}-->\n")]
    [InlineData("'<!--{\"fileInfo\":{}}-->\na = 1\n")]
    [InlineData("'<!--{\"uiOverrides\":{\"L:1\":\"8\"}}-->\nx = 1\n")]
    public void CommentsOutsideTheFooter_AreKept(string source) =>
        Assert.Equal(source, MetadataFooter.Remove(source));

    [Fact]
    public void GeneralMetadataComments_AboveTheFooter_AreKept()
    {
        const string body = "'<!--{\"pdf\":{\"format\":\"A4\"}}-->\n'<!--{\"desc\":\"Area\"}-->\nA(b; h) = b*h\n\n";
        Assert.Equal(body, MetadataFooter.Remove(body + Footer));
    }

    [Fact]
    public void KeepLineCount_BlanksTheFooter()
    {
        Assert.Equal("x = 1\n\n\n\n", MetadataFooter.Remove("x = 1\n" + Footer, keepLineCount: true));
        Assert.Equal("a = 1\n\r\n\r\n\r\n\r\n\r\n", MetadataFooter.Remove("a = 1\n" + CrLfFooter, keepLineCount: true));
    }
}
