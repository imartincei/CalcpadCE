namespace Calcpad.Tests;

public class TestCalcTests
{
    [Fact]
    public void CompareWithTolerance_UsesAbsoluteTolerance_NearZero()
    {
        var calc = new TestCalc(new());
        var result = calc.Run([
            "c = 10^-8",
            "c_hp = c + 5*10^-13",
            TestCalc.CompareWithTolerance("c", "c_hp", "10^-12")
        ]);

        Assert.Equal(1, result);
    }

    [Fact]
    public void CompareWithTolerance_UsesRelativeTolerance_ForLargerValues()
    {
        var calc = new TestCalc(new());
        var result = calc.Run([
            "c = 10^6",
            "c_hp = c + 5*10^-7",
            TestCalc.CompareWithTolerance("c", "c_hp", "10^-12")
        ]);

        Assert.Equal(1, result);
    }

    [Fact]
    public void CompareWithTolerance_Fails_WhenAbsoluteToleranceIsExceeded_NearZero()
    {
        var calc = new TestCalc(new());
        var result = calc.Run([
            "c = 10^-8",
            "c_hp = c + 2*10^-12",
            TestCalc.CompareWithTolerance("c", "c_hp", "10^-12")
        ]);

        Assert.Equal(0, result);
    }

    [Fact]
    public void CompareWithTolerance_Fails_WhenRelativeToleranceIsExceeded_ForLargerValues()
    {
        var calc = new TestCalc(new());
        var result = calc.Run([
            "c = 10^6",
            "c_hp = c + 2*10^-6",
            TestCalc.CompareWithTolerance("c", "c_hp", "10^-12")
        ]);

        Assert.Equal(0, result);
    }
}
