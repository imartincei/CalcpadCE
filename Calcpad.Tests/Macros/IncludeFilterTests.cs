namespace Calcpad.Tests;

public class IncludeFilterTests
{
    private static string[] Filter(string content, bool keepLineCount = false) =>
        MacroParser.FilterIncludedContent(content, keepLineCount)
            .Split(Environment.NewLine);

    [Fact]
    public void LocalSections_AreDropped()
    {
        Assert.Equal(["a = 1", "c = 3", ""], Filter("a = 1\n#local\nb = 2\n#global\nc = 3\n"));
    }

    [Fact]
    public void KeepLineCount_BlanksLocalSections()
    {
        Assert.Equal(["a = 1", "", "", "", "c = 3"], Filter("a = 1\n#local\nb = 2\n#global\nc = 3", keepLineCount: true));
    }

    [Fact]
    public void Footer_IsDropped()
    {
        Assert.Equal(["x = 1", ""], Filter("x = 1\n'<!--CalcpadCE Metadata-->"));
        Assert.Equal(["x = 1", ""], Filter("x = 1\n'<!--CalcpadCE Metadata-->", keepLineCount: true));
    }
}
