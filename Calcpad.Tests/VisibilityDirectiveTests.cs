namespace Calcpad.Tests
{
    public class VisibilityDirectiveTests
    {
        private static string Render(string source, bool forPrint = false)
        {
            var parser = new ExpressionParser { Settings = new Settings(), ForPrint = forPrint };
            parser.Parse(source, true, false);
            return parser.HtmlResult;
        }

        [Fact]
        public void EndHide_RestoresPriorState()
        {
            var html = Render("aaa1 = 1\n#hide\nbbb2 = 2\n#end hide\nccc3 = 3");
            Assert.Contains("aaa1", html);
            Assert.DoesNotContain("bbb2", html);
            Assert.Contains("ccc3", html);
        }

        [Fact]
        public void EndPre_Nested_RestoresPreHiddenState_NotDefault()
        {
            // #pre (hidden under ForPrint) -> #hide -> #end hide must restore to
            // #pre's hidden state, not to the document default (visible).
            var html = Render(
                "aaa1 = 1\n#pre\nbbb2 = 2\n#hide\nccc3 = 3\n#end hide\nddd4 = 4\n#end pre\neee5 = 5",
                forPrint: true);

            Assert.Contains("aaa1", html);
            Assert.DoesNotContain("bbb2", html);
            Assert.DoesNotContain("ccc3", html);
            Assert.DoesNotContain("ddd4", html); // still hidden: #end hide restored #pre's state
            Assert.Contains("eee5", html);
        }

        [Fact]
        public void EndHide_WithEmptyStack_FallsBackToVisible()
        {
            var html = Render("#hide\naaa1 = 1\n#end hide\n#end hide\nbbb2 = 2");
            Assert.DoesNotContain("aaa1", html);
            Assert.Contains("bbb2", html);
        }

        [Fact]
        public void HideCondition_True_Hides()
        {
            var html = Render("x = 5\n#hide x ≡ 5\naaa1 = 1\n#end hide\nbbb2 = 2");
            Assert.DoesNotContain("aaa1", html);
            Assert.Contains("bbb2", html);
        }

        [Fact]
        public void HideCondition_False_LeavesStateUntouched()
        {
            // Nested inside #hide so a false condition on #show must leave the
            // (hidden) state alone rather than making it visible.
            var html = Render("x = 5\n#hide\n#show x ≡ 3\naaa1 = 1\n#end show\n#end hide\nbbb2 = 2");
            Assert.DoesNotContain("aaa1", html);
            Assert.Contains("bbb2", html);
        }

        [Fact]
        public void ShowCondition_True_MakesVisible_WhenNestedInsideHide()
        {
            var html = Render("x = 5\n#hide\n#show x ≡ 5\naaa1 = 1\n#end show\n#end hide\nbbb2 = 2");
            Assert.Contains("aaa1", html);
            Assert.Contains("bbb2", html);
        }

        [Fact]
        public void Pre_HidesOnPrint_ShowsOnScreen()
        {
            // #post stays visible in the preview regardless of ForPrint (hiding it
            // there is deferred to a future #UI mode); only #pre reacts to ForPrint.
            const string source = "#pre\naaa1 = 1\n#end pre\n#post\nbbb2 = 2\n#end post";

            var onScreen = Render(source, forPrint: false);
            Assert.Contains("aaa1", onScreen);
            Assert.Contains("bbb2", onScreen);

            var forPrint = Render(source, forPrint: true);
            Assert.DoesNotContain("aaa1", forPrint);
            Assert.Contains("bbb2", forPrint);
        }

        [Fact]
        public void Hide_InsideFalseIfBranch_DoesNotTakeEffect()
        {
            var html = Render("#if 0\n#hide\n#end if\naaa1 = 1");
            Assert.Contains("aaa1", html);
        }

        [Fact]
        public void HideCondition_BadExpression_RecordsErrorAndLeavesStateUnchanged()
        {
            var parser = new ExpressionParser { Settings = new Settings() };
            parser.Parse("#hide 1/0\naaa1 = 1", true, false);
            Assert.Contains("err", parser.HtmlResult);
            Assert.Contains("aaa1", parser.HtmlResult);
        }
    }
}
