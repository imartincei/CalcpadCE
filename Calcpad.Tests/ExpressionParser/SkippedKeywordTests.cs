using System.Text.RegularExpressions;

namespace Calcpad.Tests;

public class SkippedKeywordTests
{
    private static ExpressionParser Run(string code)
    {
        var parser = new ExpressionParser { Settings = new Settings(), Debug = true };
        parser.Parse(code, true, false);
        return parser;
    }

    private static string LastValue(string code)
    {
        var matches = Regex.Matches(Run(code).HtmlResult, @"= ([^=<]+)</span>");
        Assert.NotEmpty(matches);
        return matches[^1].Groups[1].Value.Trim();
    }

    [Theory]
    [InlineData("#if 0\n{0}\n#end if")]
    [InlineData("#if 1\n#else\n{0}\n#end if")]
    [InlineData("#repeat 0\n{0}\n#loop")]
    [InlineData("#repeat 2\n#break\n{0}\n#loop")]
    [InlineData("#repeat 0\n#repeat 2\n{0}\n#loop\n#loop")]
    [InlineData("#while 0\n{0}\n#loop")]
    public void AngleKeyword_InSkippedLines_IsIgnored(string block)
    {
        foreach (var keyword in new[] { "#rad", "#gra" })
            Assert.Equal("1", LastValue("#deg\n" + string.Format(block, keyword) + "\nx = sin(90)"));
    }

    [Fact]
    public void AngleKeyword_InRunningLoop_Applies()
    {
        Assert.Equal("0.894", LastValue("#deg\n#repeat 2\n#rad\n#loop\nx = sin(90)"));
    }

    [Fact]
    public void Deg_InSkippedBranch_IsIgnored()
    {
        Assert.Equal("0.894", LastValue("#rad\n#if 0\n#deg\n#end if\nx = sin(90)"));
    }

    [Fact]
    public void Md_InSkippedBranch_IsIgnored()
    {
        Assert.DoesNotContain("<strong>", Run("#md off\n#if 0\n#md on\n#end if\n'**b**").HtmlResult);
    }

    [Theory]
    [InlineData("#round 1")]
    [InlineData("#format F1")]
    [InlineData("#settings {\"decimals\": 1}")]
    public void FormattingKeyword_InSkippedBranch_IsIgnored(string keyword)
    {
        Assert.Equal("1.23", LastValue($"#if 0\n{keyword}\n#end if\nx = 1.23456"));
    }

    [Fact]
    public void Settings_InSkippedBranch_StillPairsWithEndSettings()
    {
        var code = "#settings {\"decimals\": 4}\n#if 0\n#settings {\"decimals\": 1}\n#end settings\n#end if\ny = 1.23456\n#end settings\nx = 1.23456";
        var html = Run(code).HtmlResult;
        Assert.Contains("<var>y</var> = 1.2346</span>", html);
        Assert.Contains("<var>x</var> = 1.23</span>", html);
    }

    [Theory]
    [InlineData("#repeat 2")]
    [InlineData("#for j = 1 : 2")]
    [InlineData("#while k < 2")]
    public void NestedLoop_InLoopThatNeverRuns_DoesNotRun(string header)
    {
        Assert.Equal("0", LastValue($"k = 0\n#repeat 0\n{header}\nk = k + 1\n#loop\n#loop\nk"));
    }

    [Fact]
    public void ForCounter_InLoopThatNeverRuns_IsUnchanged()
    {
        Assert.Equal("5", LastValue("i = 5\n#repeat 0\n#for i = 1 : 3\n#loop\n#loop\ni"));
    }

    [Fact]
    public void Continue_AfterBreak_DoesNotAdvanceCounter()
    {
        var withContinue = LastValue("#for i = 1 : 5\n#if i ≡ 2\n#break\n#end if\n#continue\n#loop\ni");
        var withoutContinue = LastValue("#for i = 1 : 5\n#if i ≡ 2\n#break\n#end if\n#loop\ni");
        Assert.Equal(withoutContinue, withContinue);
    }

    [Fact]
    public void BrokenWhile_InsideLoop_ClosesItsCondition()
    {
        var parser = Run("k = 0\n#for i = 1 : 30\n#while 1\n#break\n#loop\nk = k + 1\n#loop\nk");
        Assert.Empty(parser.Errors);
        Assert.Contains("<var>k</var> = 30</span>", parser.HtmlResult);
    }

    [Fact]
    public void Continue_EndingWhile_ClosesTheLoop()
    {
        var parser = Run("k = 0\n#while k < 3\nk = k + 1\n#continue\n#loop\nq = 7");
        Assert.Empty(parser.Errors);
        Assert.Contains("<var>q</var> = 7</span>", parser.HtmlResult);
    }

    [Fact]
    public void NestedLoop_InFalseWhile_DoesNotCloseTheWhile()
    {
        var parser = Run("k = 0\n#repeat 2\n#while 0\n#repeat 2\n#loop\n#loop\nk = k + 1\n#loop\nk");
        Assert.Empty(parser.Errors);
        Assert.Contains("<var>k</var> = 2</span>", parser.HtmlResult);
    }

    [Fact]
    public void UnclosedLoop_InSkippedBranch_DoesNotCaptureLaterLoop()
    {
        var html = Run("k = 0\n#if 0\n#repeat 2\n#end if\n#if 1\n#repeat 3\nk = k + 1\n#loop\n#end if\nk").HtmlResult;
        Assert.Contains("<var>k</var> = 3</span>", html);
    }
}
