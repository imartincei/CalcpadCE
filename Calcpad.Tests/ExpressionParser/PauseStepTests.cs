namespace Calcpad.Tests
{
    public class PauseStepTests
    {
        private static ExpressionParser Render(string source, string pauseAfter = null,
            PauseBehavior mode = PauseBehavior.Step)
        {
            var parser = new ExpressionParser { EnableUi = true, PauseMode = mode, PauseAfter = pauseAfter };
            parser.Parse(source, true, false);
            return parser;
        }

        private const string ThreeSections = "aaa1 = 1\n#pause\nbbb2 = 2\n#pause\nccc3 = 3";

        [Fact]
        public void FirstRender_StopsAtFirstPause()
        {
            var p = Render(ThreeSections);
            Assert.Contains("aaa1", p.HtmlResult);
            Assert.DoesNotContain("bbb2", p.HtmlResult);
            Assert.Contains("Paused:", p.HtmlResult);
            Assert.Equal("1", p.NextPauseKey);
            Assert.Null(p.PreviousPauseKey);
        }

        [Fact]
        public void Next_IsCumulative()
        {
            var p = Render(ThreeSections, "1");
            Assert.Contains("aaa1", p.HtmlResult);
            Assert.Contains("bbb2", p.HtmlResult);
            Assert.DoesNotContain("ccc3", p.HtmlResult);
            Assert.Equal("2", p.NextPauseKey);
            Assert.Equal(ExpressionParser.PauseAtStart, p.PreviousPauseKey);

            var back = Render(ThreeSections, p.PreviousPauseKey);
            Assert.Equal("1", back.NextPauseKey);
            Assert.Null(back.PreviousPauseKey);
        }

        [Fact]
        public void LastStep_ReachesEnd_AndPreviousStepsBack()
        {
            var p = Render(ThreeSections, "2");
            Assert.Contains("ccc3", p.HtmlResult);
            Assert.DoesNotContain("Paused:", p.HtmlResult);
            Assert.Null(p.NextPauseKey);
            Assert.Equal("1", p.PreviousPauseKey);

            var back = Render(ThreeSections, p.PreviousPauseKey);
            Assert.Equal("2", back.NextPauseKey);
        }

        [Fact]
        public void AtEnd_PauseAddedAfterPosition_BecomesNextStep()
        {
            const string loop = "#repeat n\nlp = 1\n#pause\n#loop\nend6 = 6";
            var atEnd = Render("n = 2\n" + loop, "1.2.2");
            Assert.Contains("end6", atEnd.HtmlResult);
            Assert.Null(atEnd.NextPauseKey);

            var grown = Render("n = 3\n" + loop, "1.2.2");
            Assert.DoesNotContain("end6", grown.HtmlResult);
            Assert.Equal("1.3.2", grown.NextPauseKey);
        }

        [Fact]
        public void InvalidKey_StopsAtFirstPause()
        {
            Assert.Equal("1", Render(ThreeSections, "x.y").NextPauseKey);
        }

        [Fact]
        public void Loop_StopsOncePerPass()
        {
            const string source = "#repeat 3\nlp = 1\n#pause\n#loop\nzzz9 = 9";
            var keys = new List<string>();
            string after = null;
            while (true)
            {
                var p = Render(source, after);
                if (p.NextPauseKey is null)
                {
                    Assert.Contains("zzz9", p.HtmlResult);
                    break;
                }
                keys.Add(p.NextPauseKey);
                after = p.NextPauseKey;
            }
            Assert.Equal(["1.1.2", "1.2.2", "1.3.2"], keys);
        }

        [Fact]
        public void ShrunkLoop_StopsAtFirstPauseAfterIt()
        {
            const string source = "n = 2\n#repeat n\nlp = 1\n#pause\n#loop\nmid5 = 5\n#pause\nend6 = 6";
            var p = Render(source, "1.3.2");
            Assert.Contains("mid5", p.HtmlResult);
            Assert.DoesNotContain("end6", p.HtmlResult);
            Assert.Equal("3", p.NextPauseKey);
        }

        private const string Branches =
            "c = 0\n#if c ≡ 1\n#pause\naaa1 = 1\n#else\n#pause\nbbb2 = 2\n#end if\n#pause\nccc3 = 3";

        [Fact]
        public void UntakenBranch_PauseIsSkipped()
        {
            Assert.Equal("2", Render(Branches).NextPauseKey);
        }

        [Fact]
        public void FlippedBranch_StopsAtTheOtherBranch()
        {
            var p = Render(Branches, "1");
            Assert.Equal("2", p.NextPauseKey);
            Assert.DoesNotContain("bbb2", p.HtmlResult);
        }

        [Fact]
        public void EditingOtherLines_KeepsKeys()
        {
            var edited = "'Heading\naaa1 = 1\nxxx7 = 7\n\n#pause\nbbb2 = 2\n#pause\nccc3 = 3";
            var p = Render(edited, "1");
            Assert.Contains("bbb2", p.HtmlResult);
            Assert.Equal("2", p.NextPauseKey);
        }

        [Fact]
        public void Default_RunsPastEveryPause()
        {
            var parser = new ExpressionParser();
            parser.Parse(ThreeSections, true, false);
            Assert.Equal(PauseBehavior.Ignore, parser.PauseMode);
            Assert.Contains("ccc3", parser.HtmlResult);
            Assert.DoesNotContain("Paused:", parser.HtmlResult);
            Assert.Null(parser.NextPauseKey);
        }

        [Fact]
        public void Input_IsNoOp()
        {
            var p = Render("aaa1 = 1\n#input\nbbb2 = 2", mode: PauseBehavior.Ignore);
            Assert.Contains("bbb2", p.HtmlResult);
            Assert.Empty(p.Errors);
        }
    }
}
