using Calcpad.Server.Services;

namespace Calcpad.Tests;

public class UiPauseServiceTests
{
    private const string Source = "aaa1 = 1\n#pause\nbbb2 = 2\n#pause\nccc3 = 3\n";

    [Fact]
    public void SteppedForm_StopsAndReportsPosition()
    {
        var (html, _, _, _, pause) = new CalcpadService().Convert(Source, enableUi: true, stepPauses: true, pauseAfter: "1");

        Assert.Contains("bbb2", html);
        Assert.DoesNotContain("ccc3", html);
        Assert.Contains("<p class=\"paused\">Paused: Click Next to Continue</p>", html);
        Assert.Equal(new PauseInfo("2", "start"), pause);
    }

    [Theory]
    [InlineData(false, true)]  // Report, preview and exports
    [InlineData(true, false)]  // Input-form export
    public void UnsteppedRenders_RunPastEveryPause(bool enableUi, bool stepPauses)
    {
        var (html, _, _, _, pause) = new CalcpadService().Convert(Source, enableUi: enableUi, stepPauses: stepPauses);

        Assert.Contains("ccc3", html);
        Assert.DoesNotContain("Paused:", html);
        Assert.Null(pause);
    }
}
