namespace Calcpad.Tests
{
    public class SubstitutionDirectiveTests
    {
        private static string Render(string source)
        {
            var parser = new ExpressionParser { Settings = new Settings() };
            parser.Parse(source, true, false);
            return parser.HtmlResult;
        }

        // "aaa1 = 2·xxx = 2·5 = 10" under #varsub loses the "2·5" half under #nosub and the
        // "aaa1 = 2·xxx" half under #novar. U+2006 is what HtmlWriter pads '*' with.
        private const string Substituted = "2\u2006·\u20065";
        private const string Preamble = "xxx = 5\n";

        [Fact]
        public void EndNoSub_RestoresPriorState()
        {
            Assert.DoesNotContain(Substituted, Render(Preamble + "#nosub\naaa1 = 2*xxx"));
            Assert.Contains(Substituted, Render(Preamble + "#nosub\n#end nosub\naaa1 = 2*xxx"));
        }

        [Fact]
        public void EndNoVar_Nested_RestoresOuterMode_NotDefault()
        {
            var html = Render(Preamble + "#nosub\n#novar\n#end novar\naaa1 = 2*xxx");
            Assert.Contains("aaa1", html); // #novar is closed, so the variable names are back
            Assert.DoesNotContain(Substituted, html); // but into #nosub, not the #varsub default
        }

        [Fact]
        public void EndSub_WithEmptyStack_FallsBackToVarSub()
        {
            var html = Render(Preamble + "#nosub\n#end nosub\n#end nosub\naaa1 = 2*xxx");
            Assert.Contains(Substituted, html);
        }

        [Fact]
        public void NoVarCondition_False_LeavesStateUntouched()
        {
            // Nested inside #nosub so a false condition on #novar must leave the
            // (names only) state alone rather than dropping the names.
            var html = Render(Preamble + "#nosub\n#novar 1 ≡ 0\naaa1 = 2*xxx");
            Assert.Contains("aaa1", html);
            Assert.DoesNotContain(Substituted, html);
        }

        [Fact]
        public void NoVarCondition_True_Applies_WhenNestedInsideNoSub()
        {
            var html = Render(Preamble + "#nosub\n#novar 1 ≡ 1\naaa1 = 2*xxx");
            Assert.DoesNotContain("aaa1", html);
            Assert.Contains(Substituted, html);
        }

        [Fact]
        public void NoSub_InsideFalseIfBranch_DoesNotTakeEffect()
        {
            var html = Render(Preamble + "#if 0\n#nosub\n#end if\naaa1 = 2*xxx");
            Assert.Contains(Substituted, html);
        }

        [Fact]
        public void NoSubCondition_BadExpression_RecordsErrorAndLeavesStateUnchanged()
        {
            var parser = new ExpressionParser { Settings = new Settings() };
            parser.Parse(Preamble + "#nosub 1/0\naaa1 = 2*xxx", true, false);
            Assert.Contains("err", parser.HtmlResult);
            Assert.Contains(Substituted, parser.HtmlResult);
        }
    }
}
