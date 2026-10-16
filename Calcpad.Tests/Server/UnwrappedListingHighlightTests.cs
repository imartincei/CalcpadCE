using Calcpad.Server.Services;

namespace Calcpad.Tests;

/// <summary>
/// The unwrapped listing is highlighted by the real <c>CalcpadTokenizer</c>, emitting the
/// canonical token names as span classes so the listing, the editor and the exported HTML
/// share one palette. It used to use a hand-rolled scanner with its own class names.
/// </summary>
public class UnwrappedListingHighlightTests
{
    private const string Document =
        "#def half$(x$)\n" +
        "x$/2\n" +
        "#end def\n" +
        "'A plain comment\n" +
        "'<b>markup</b>\n" +
        "'<!-- a note -->\n" +
        "#settings {\"decimals\": 4}\n" +
        "#UI {\"type\": \"entry\"} d = 10m\n" +
        "L = half$(4)*1m\n" +
        "$Plot{L @ d = 0 : 1}\n";

    private static string Unwrapped() =>
        new CalcpadService().Convert(Document, forceUnwrappedCode: true).Html;

    [Theory]
    [InlineData("comment")]
    [InlineData("htmlComment")]
    [InlineData("tag")]
    [InlineData("htmlContent")]
    [InlineData("keyword")]
    [InlineData("command")]
    [InlineData("variable")]
    [InlineData("localVariable")]
    [InlineData("units")]
    [InlineData("const")]
    [InlineData("operator")]
    [InlineData("bracket")]
    [InlineData("settingsJson")]
    public void CanonicalTokenClasses_AreEmitted(string tokenClass) =>
        Assert.Contains($"class=\"{tokenClass}\"", Unwrapped());

    /// <summary>The old scanner's names, which no theme has a rule for.</summary>
    [Theory]
    [InlineData("number")]
    [InlineData("htmltag")]
    public void LegacyTokenClasses_AreGone(string tokenClass) =>
        Assert.DoesNotContain($"class=\"{tokenClass}\"", Unwrapped());

    /// <summary>#UI's block ends at the brace, so the assignment after it still colours.</summary>
    [Fact]
    public void UiJsonBlock_IsOneSpanAndTheAssignmentFollows()
    {
        var html = Unwrapped();
        Assert.Contains("<span class=\"settingsJson\">{&quot;type&quot;: &quot;entry&quot;}</span>", html);
        Assert.Contains("<span class=\"variable\">d</span>", html);
    }

    /// <summary>The macro is expanded before the listing is built, so its call is gone.</summary>
    [Fact]
    public void MacroCalls_AreAlreadyExpanded()
    {
        var html = Unwrapped();
        Assert.DoesNotContain("half$", html);
        Assert.Contains("<span class=\"const\">4</span><span class=\"operator\">/</span>", html);
    }
}
