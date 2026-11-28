using System.Globalization;
using Calcpad.Highlighter.ContentResolution;
using Xunit;

namespace Calcpad.Tests.Highlighter;

[CollectionDefinition("Culture", DisableParallelization = true)]
public sealed class CultureCollection;

[Collection("Culture")]
public class CoreCheckCultureTests
{
    [Fact]
    public void CachesMessagesPerUiCulture()
    {
        var results = new CoreCheckResults(_ =>
            [new CalcpadError { Message = CultureInfo.CurrentUICulture.Name }]);
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            Assert.Equal("en-US", Assert.Single(results.Get(false)).Message);

            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("zh-CN");
            Assert.Equal("zh-CN", Assert.Single(results.Get(false)).Message);

            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            Assert.Equal("en-US", Assert.Single(results.Get(false)).Message);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }
}
