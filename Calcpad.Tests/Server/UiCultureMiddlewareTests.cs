using Calcpad.Server.Services;

namespace Calcpad.Tests;

/// <summary>
/// The server follows the same locale rules as the frontend: exact tags and language
/// variants select a catalog; Chinese script/region selects Simplified or Traditional;
/// bare "pt" and "pt-PT" remain unsupported.
/// </summary>
public class UiCultureMiddlewareTests
{
    [Theory]
    [InlineData("pt-BR", "pt-BR")]
    [InlineData("pt_BR", "pt-BR")]
    [InlineData("pt-br", "pt-BR")]
    [InlineData("pt-BR-x", "pt-BR")]
    [InlineData("zh-CN", "zh")]
    [InlineData("zh_Hans", "zh")]
    [InlineData("zh-TW", "zh-Hant")]
    [InlineData("zh_HK", "zh-Hant")]
    [InlineData("zh-Hant", "zh-Hant")]
    [InlineData("en-US", "en")]
    [InlineData("bg-BG", "bg")]
    [InlineData("cs-CZ", "cs")]
    [InlineData("de-AT", "de")]
    [InlineData("es-419", "es")]
    [InlineData("fr-CA", "fr")]
    [InlineData("it-IT", "it")]
    [InlineData("ja-JP", "ja")]
    [InlineData("ko-KR", "ko")]
    [InlineData("pl-PL", "pl")]
    [InlineData("ru-RU", "ru")]
    [InlineData("tr-TR", "tr")]
    [InlineData("pt-PT, pt-BR;q=0.9", "pt-BR")]
    [InlineData("fr-FR, pt-BR;q=0.9", "fr")]
    public void Match_SelectsSupportedLocale(string header, string expected)
    {
        Assert.Equal(expected, UiCultureMiddleware.Match(header)?.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("*")]
    [InlineData("pt")]
    [InlineData("pt-PT")]
    [InlineData("qps-ploc")]
    [InlineData("pt-PT, qps-ploc")]
    public void Match_LeavesAmbiguousOrUnsupportedTagsUnset(string header)
    {
        Assert.Null(UiCultureMiddleware.Match(header));
    }
}
