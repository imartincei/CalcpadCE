namespace Calcpad.Tests;

public class TrailingNewlineRenderTests
{
    public static TheoryData<string> SingleTrailingLineBreakCases => new()
    {
        "5 = 5\n",
        "5 = 5\r\n",
        "5 = 5\r",
    };

    [Theory]
    [MemberData(nameof(SingleTrailingLineBreakCases))]
    public void TrailingLineBreak_DoesNotEmitBlankParagraph(string source)
    {
        var html = Render(source);

        Assert.Equal(0, CountOccurrences(html, "<p>&nbsp;</p>"));
        Assert.Equal(0, CountOccurrences(html, "<p></p>"));
    }

    [Fact]
    public void MidFileBlankLine_EmitsExactlyOneBlankParagraph()
    {
        var html = Render("5 = 5\n\n7 = 7\n");

        Assert.Equal(1, CountOccurrences(html, "<p>&nbsp;</p>"));
        Assert.Equal(0, CountOccurrences(html, "<p></p>"));
    }

    [Fact]
    public void AllLineEndings_ProduceEqualHtml()
    {
        var lf = Render("'note\n5 = 5\n");
        var crlf = Render("'note\r\n5 = 5\r\n");
        var cr = Render("'note\r5 = 5\r");

        Assert.Equal(lf, crlf);
        Assert.Equal(lf, cr);
    }

    [Fact]
    public void MultipleTrailingLineBreaks_AreSymmetric()
    {
        var lf = Render("5 = 5\n\n\n");
        var crlf = Render("5 = 5\r\n\r\n\r\n");
        var cr = Render("5 = 5\r\r\r");

        Assert.Equal(lf, crlf);
        Assert.Equal(lf, cr);
        Assert.Equal(2, CountOccurrences(lf, "<p>&nbsp;</p>"));
        Assert.Equal(0, CountOccurrences(lf, "<p></p>"));
    }

    private static string Render(string source)
    {
        var macroParser = new MacroParser();
        var hasMacroErrors = macroParser.Parse(source, out var expandedSource, null, 0, false);
        Assert.False(hasMacroErrors);

        var parser = new ExpressionParser();
        parser.Parse(expandedSource, true, false);

        return parser.HtmlResult;
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var start = 0;
        while ((start = text.IndexOf(value, start, StringComparison.Ordinal)) >= 0)
        {
            ++count;
            start += value.Length;
        }

        return count;
    }
}
