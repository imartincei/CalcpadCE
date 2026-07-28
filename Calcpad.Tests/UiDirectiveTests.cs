using System.Text.RegularExpressions;

namespace Calcpad.Tests
{
    public partial class UiDirectiveTests
    {
        [GeneratedRegex(@"(?m)^(\s*)#UI\s*(\{[^}]*\})?\s*")]
        private static partial Regex UiPrefix();

        private static string Render(string source, bool enableUi, Dictionary<string, string> overrides = null)
        {
            var parser = new ExpressionParser
            {
                Settings = new Settings(),
                EnableUi = enableUi,
                UiOverrides = overrides
            };
            parser.Parse(source, true, false);
            return parser.HtmlResult;
        }

        /// <summary>
        /// Renders the source in report mode and compares it against the same source with
        /// every #UI prefix removed. The two must be identical - in report mode the keyword
        /// is a no-op.
        /// </summary>
        private static void AssertReportMatchesPlainSource(string source)
        {
            var plain = UiPrefix().Replace(source, "$1");
            Assert.Equal(Render(plain, enableUi: false), Render(source, enableUi: false));
        }

        [Theory]
        [InlineData("#UI L = 10m\nA = L * 2m")]
        [InlineData("#UI {\"type\": \"checkbox\"} flag = 1\nflag * 2")]
        [InlineData("#UI {\"type\": \"dropdown\", \"keys\": [\"Low\", \"High\"], \"values\": [\"1\", \"3\"]} grade = 1")]
        [InlineData("#UI {\"type\": \"radio\", \"keys\": [\"Steel\", \"Concrete\"], \"values\": [\"200GPa\", \"25GPa\"]} E = 200GPa")]
        [InlineData("#UI v = [1; 2; 3]")]
        [InlineData("#UI M = [1; 2; 3 | 4; 5; 6]")]
        [InlineData("#UI Z = vector(5)")]
        [InlineData("#UI G = matrix(3; 4)")]
        [InlineData("#UI {\"type\": \"datagrid\", \"rows\": 3, \"columns\": 4} M = [1; 2; 3 | 4; 5; 6]")]
        public void ReportMode_IsIdenticalToSourceWithoutTheKeyword(string source) =>
            AssertReportMatchesPlainSource(source);

        [Fact]
        public void ReportMode_EmitsNoUiMarkup()
        {
            var html = Render("#UI {\"type\": \"checkbox\"} flag = 1\n#UI v = [1; 2; 3]", enableUi: false);
            Assert.DoesNotContain("calcpad-ui", html);
            Assert.DoesNotContain("data-ui-", html);
            Assert.DoesNotContain("<input", html);
        }

        [Fact]
        public void ReportStyle_AddsClass_AndIsTheOnlyDifference()
        {
            var styled = Render("#UI {\"reportStyle\": \"boxed\"} P = 25kN", enableUi: false);
            var plain = Render("P = 25kN", enableUi: false);
            Assert.Contains("class=\"boxed\"", styled);
            Assert.Equal(plain, styled.Replace(" class=\"boxed\"", ""));
        }

        [Fact]
        public void ReportStyle_IsNotEmittedInUiMode() =>
            Assert.DoesNotContain("boxed", Render("#UI {\"reportStyle\": \"boxed\"} P = 25kN", enableUi: true));

        [Fact]
        public void Entry_IsAutoDetected_AndKeepsTheUnitOutsideTheInput()
        {
            var html = Render("#UI L = 10m", enableUi: true);
            Assert.Contains("class=\"calcpad-ui-input\"", html);
            Assert.Contains("data-ui-var=\"L:1\"", html);
            Assert.Contains("value=\"10\"", html);
            Assert.DoesNotContain("value=\"10 m\"", html);
        }

        [Fact]
        public void InlineComment_IsNotMistakenForTheAssignment()
        {
            // A control is often labelled with an inline comment that contains its own
            // '=', as in Circle.cpd. Taking the first '=' made the label the variable
            // name, and that name became the control key written back to the document.
            var html = Render("#UI '2&middot;<i>r</i> ='d = 1", enableUi: true);
            Assert.Contains("data-ui-var=\"d:1\"", html);
            Assert.DoesNotContain("data-ui-var=\"'2", html);
        }

        [Theory]
        [InlineData("#UI 'label ='x = 1", "x:1")]
        [InlineData("#UI \"label =\"y = 2", "y:1")]
        [InlineData("#UI 'a ='  z  = 3", "z:1")]
        public void InlineComment_IsStrippedFromTheVariableName(string source, string key) =>
            Assert.Contains($"data-ui-var=\"{key}\"", Render(source, enableUi: true));

        [Fact]
        public void InlineComment_LeavesTheReportUnchanged() =>
            AssertReportMatchesPlainSource("#UI '2&middot;<i>r</i> ='d = 1\nA = d*2");

        [Fact]
        public void InlineComment_DoesNotBreakOverrides()
        {
            var overrides = new Dictionary<string, string> { ["d:1"] = "4" };
            var html = Render("#UI '2&middot;<i>r</i> ='d = 1\nA = d*2", enableUi: false, overrides);
            Assert.Contains("8", html);
        }

        [Fact]
        public void MultipleAssignments_EachGetTheirOwnControl()
        {
            var html = Render("#UI '2&middot;<i>r</i> ='d = 1','x = 2'", enableUi: true);
            Assert.Contains("data-ui-var=\"d:1\"", html);
            Assert.Contains("data-ui-var=\"x:1\"", html);
            Assert.Equal(2, Regex.Matches(html, "calcpad-ui-input").Count);
        }

        [Fact]
        public void MultipleAssignments_ShareTheJsonProperties()
        {
            var html = Render("#UI {\"type\": \"checkbox\", \"style\": \"sw\"} a = 1', 'b = 0", enableUi: true);
            Assert.Equal(2, Regex.Matches(html, "class=\"calcpad-ui-checkbox sw\"").Count);
        }

        [Fact]
        public void MultipleAssignments_AreOverriddenSeparately()
        {
            var overrides = new Dictionary<string, string> { ["x:1"] = "9" };
            var html = Render("#UI d = 1', 'x = 2\nS = d + x", enableUi: false, overrides);
            // d keeps its default, only x is replaced: 1 + 9.
            Assert.Contains("10", html);
        }

        [Fact]
        public void SameNameTwiceOnOneLine_GetsTwoKeys()
        {
            var html = Render("#UI d = 1', 'd = 2", enableUi: true);
            Assert.Contains("data-ui-var=\"d:1\"", html);
            Assert.Contains("data-ui-var=\"d:2\"", html);
        }

        [Fact]
        public void MultipleAssignments_LeaveTheReportUnchanged() =>
            AssertReportMatchesPlainSource("#UI '2&middot;<i>r</i> ='d = 1','x = 2'\nA = d*x");

        [Fact]
        public void MultipleAssignments_SkipSegmentsThatAssignNothing()
        {
            // The trailing 'd' is an output expression, not a declaration.
            var html = Render("#UI d = 1', 'd", enableUi: true);
            Assert.Single(Regex.Matches(html, "calcpad-ui-input"));
        }

        [Fact]
        public void MultipleDatagrids_EachEmitTheirOwnContainer()
        {
            var html = Render("#UI v = [1; 2]', 'w = [3; 4; 5]", enableUi: true);
            Assert.Contains("data-ui-values=\"1;2\"", html);
            Assert.Contains("data-ui-values=\"3;4;5\"", html);
            Assert.Equal(2, Regex.Matches(html, "class=\"calcpad-ui-datagrid\"").Count);
        }

        [Fact]
        public void ControlBinding_ReportsTheSourceLine()
        {
            // A host writes an edited value back to this line, so it has to be the
            // 1 based source line the rest of the markup uses, not the output index.
            var html = Render("'first\n'second\n#UI L = 10m", enableUi: true);
            Assert.Contains("data-ui-var=\"L:1\" data-ui-line=\"3\"", html);
        }

        [Fact]
        public void Style_IsAppendedToTheControlClass() =>
            Assert.Contains("class=\"calcpad-ui-input highlight\"",
                Render("#UI {\"style\": \"highlight\"} d = 2m", enableUi: true));

        [Fact]
        public void Checkbox_IsCheckedWhenTheValueIsOne()
        {
            Assert.Contains("type=\"checkbox\" class=\"calcpad-ui-checkbox\" data-ui-var=\"flag:1\" data-ui-line=\"1\" checked",
                Render("#UI {\"type\": \"checkbox\"} flag = 1", enableUi: true));
            Assert.DoesNotContain("checked",
                Render("#UI {\"type\": \"checkbox\"} flag = 0", enableUi: true));
        }

        [Fact]
        public void Dropdown_MarksTheMatchingOptionSelected()
        {
            var html = Render("#UI {\"type\": \"dropdown\", \"keys\": [\"Low\", \"Med\"], \"values\": [\"1\", \"2\"]} grade = 2", enableUi: true);
            Assert.Contains("<option value=\"1\">Low</option>", html);
            Assert.Contains("<option value=\"2\" selected>Med</option>", html);
        }

        [Fact]
        public void Radio_GroupsByControlKey()
        {
            var html = Render("#UI {\"type\": \"radio\", \"keys\": [\"A\", \"B\"], \"values\": [\"1\", \"2\"]} r = 1", enableUi: true);
            Assert.Contains("name=\"ui-radio-r:1\" value=\"1\" checked", html);
            Assert.Contains("name=\"ui-radio-r:1\" value=\"2\"", html);
        }

        [Theory]
        [InlineData("#UI v = [1; 2; 3]", 1, 3, "1;2;3")]
        [InlineData("#UI M = [1; 2; 3 | 4; 5; 6]", 2, 3, "1;2;3|4;5;6")]
        [InlineData("#UI Z = vector(5)", 1, 5, "0;0;0;0;0")]
        [InlineData("#UI G = matrix(3; 4)", 3, 4, "0;0;0;0|0;0;0;0|0;0;0;0")]
        public void Datagrid_AutoDetectsShapeAndValues(string source, int rows, int columns, string values)
        {
            var html = Render(source, enableUi: true);
            Assert.Contains($"data-ui-rows=\"{rows}\" data-ui-columns=\"{columns}\"", html);
            Assert.Contains($"data-ui-values=\"{values}\"", html);
        }

        [Fact]
        public void Datagrid_EmitsTheContainerOutsideTheParagraph()
        {
            var html = Render("#UI v = [1; 2; 3]", enableUi: true);
            Assert.Matches(@"</p>\s*<div class=""calcpad-ui-datagrid""", html);
        }

        [Theory]
        // Grows: the written values stay put and the new cells come in as zeros.
        [InlineData(2, 3, "[1; 2; 3 | 4; 5; 6]", "1;2;3|4;5;6")]
        [InlineData(3, 4, "[1; 2; 3 | 4; 5; 6]", "1;2;3;0|4;5;6;0|0;0;0;0")]
        [InlineData(2, 2, "[1; 2; 3]", "1;2|0;0")]
        // Shrinks: the cells outside the declared shape are dropped.
        [InlineData(1, 2, "[1; 2; 3 | 4; 5; 6]", "1;2")]
        [InlineData(2, 2, "[1; 2; 3 | 4; 5; 6]", "1;2|4;5")]
        public void Datagrid_DeclaredShape_PadsAndTruncatesTheLiteral(int rows, int columns, string literal, string values)
        {
            var html = Render(
                $"#UI {{\"type\": \"datagrid\", \"rows\": {rows}, \"columns\": {columns}}} T = {literal}",
                enableUi: true);
            Assert.Contains($"data-ui-rows=\"{rows}\" data-ui-columns=\"{columns}\"", html);
            Assert.Contains($"data-ui-values=\"{values}\"", html);
        }

        [Fact]
        public void Datagrid_DeclaredShape_DoesNotReshapeTheReport()
        {
            // The shape describes the grid the form draws, not the document. A report
            // renders the literal exactly as written, same as any other mode.
            AssertReportMatchesPlainSource(
                "#UI {\"type\": \"datagrid\", \"rows\": 3, \"columns\": 4} M = [1; 2; 3 | 4; 5; 6]\nM");
        }

        [Fact]
        public void Datagrid_EmitsHeaders()
        {
            var html = Render(
                "#UI {\"type\": \"datagrid\", \"columnHeaders\": [\"a\", \"b\"], \"rowHeaders\": [\"r1\"]} T = [1; 2]",
                enableUi: true);
            Assert.Contains("data-ui-col-headers=\"a,b\"", html);
            Assert.Contains("data-ui-row-headers=\"r1\"", html);
        }

        [Fact]
        public void Override_ReplacesTheValueAndPropagatesToDependents()
        {
            var overrides = new Dictionary<string, string> { ["L"] = "12" };
            var html = Render("#UI L = 10m\nA = L * 2m", enableUi: true, overrides);
            Assert.Contains("value=\"12\"", html);
            Assert.Contains("24", html);
        }

        [Fact]
        public void Override_PreservesTheUnit() =>
            Assert.DoesNotContain("10",
                Render("#UI L = 10m\nL", enableUi: true, new Dictionary<string, string> { ["L"] = "12" }));

        [Fact]
        public void Override_AppliesInReportModeToo() =>
            Assert.Contains("24",
                Render("#UI L = 10m\nA = L * 2m", enableUi: false, new Dictionary<string, string> { ["L"] = "12" }));

        [Fact]
        public void Override_ReplacesTheWholeDatagridLiteral()
        {
            var overrides = new Dictionary<string, string> { ["v"] = "[7; 8; 9]" };
            Assert.Contains("data-ui-values=\"7;8;9\"", Render("#UI v = [1; 2; 3]", enableUi: true, overrides));
        }

        [Theory]
        [InlineData("#UI {\"type\": \"dropdown\", \"keys\": [\"A\"]} x = 1")]
        [InlineData("#UI {\"type\": \"radio\", \"values\": [\"1\"]} x = 1")]
        [InlineData("#UI {\"type\": \"dropdown\", \"keys\": [\"A\", \"B\"], \"values\": [\"1\"]} x = 1")]
        [InlineData("#UI {\"type\": \"entry\" x = 1")]
        [InlineData("#UI {not json} x = 1")]
        [InlineData("#UI {\"mode\": \"string\"} x = 1")]
        [InlineData("#UI name$ = 1")]
        [InlineData("#UI x")]
        public void InvalidDirective_ReportsAnError(string source) =>
            Assert.Contains("err", Render(source, enableUi: true));

        [Fact]
        public void InactiveConditionalBranch_RendersNoControl()
        {
            var html = Render("#if 0\n#UI L = 10m\n#end if", enableUi: true);
            Assert.DoesNotContain("calcpad-ui-input", html);
        }

        [Fact]
        public void ActiveConditionalBranch_RendersTheControl() =>
            Assert.Contains("calcpad-ui-input", Render("#if 1\n#UI L = 10m\n#end if", enableUi: true));

        [Fact]
        public void SkippedUiLine_DoesNotAttachItsControlToTheNextLine()
        {
            var html = Render("#if 0\n#UI L = 10m\n#end if\nW = 5m", enableUi: true);
            Assert.DoesNotContain("data-ui-", html);
            Assert.DoesNotContain("<input", html);
        }

        [Fact]
        public void RedefinedVariable_GetsADistinctKeyPerOccurrence()
        {
            var html = Render("#UI x = 1\nx = x + 1\n#UI x = 5", enableUi: true);
            Assert.Contains("data-ui-var=\"x:1\"", html);
            Assert.Contains("data-ui-var=\"x:2\"", html);
        }

        [Fact]
        public void KeyedOverride_AppliesToThatOccurrenceOnly()
        {
            var overrides = new Dictionary<string, string> { ["x:2"] = "50" };
            var html = Render("#UI x = 1\n#UI x = 5", enableUi: true, overrides);
            Assert.Contains("value=\"1\"", html);
            Assert.Contains("value=\"50\"", html);
            Assert.DoesNotContain("value=\"5\"", html);
        }

        [Fact]
        public void BareOverride_AppliesToEveryOccurrenceOfThatName()
        {
            var overrides = new Dictionary<string, string> { ["x"] = "7" };
            var html = Render("#UI x = 1\n#UI x = 5", enableUi: true, overrides);
            Assert.Equal(2, Regex.Matches(html, "value=\"7\"").Count);
        }

        [Fact]
        public void LoopedControl_GetsOneKeyPerPass()
        {
            var html = Render("#repeat 3\n#UI x = 1\n#loop", enableUi: true);
            Assert.Contains("data-ui-var=\"x:1:1\"", html);
            Assert.Contains("data-ui-var=\"x:1:2\"", html);
            Assert.Contains("data-ui-var=\"x:1:3\"", html);
        }

        [Fact]
        public void NestedLoops_JoinThePassNumbers()
        {
            var html = Render("#repeat 2\n#repeat 2\n#UI x = 1\n#loop\n#loop", enableUi: true);
            Assert.Contains("data-ui-var=\"x:1:1.1\"", html);
            Assert.Contains("data-ui-var=\"x:1:1.2\"", html);
            Assert.Contains("data-ui-var=\"x:1:2.1\"", html);
            Assert.Contains("data-ui-var=\"x:1:2.2\"", html);
        }

        [Fact]
        public void ControlNumbering_IsUnaffectedByLoopIterationCount()
        {
            // The declaration ordinal is keyed on the source line, so re-running the line
            // must not advance the count for whatever follows the loop.
            Assert.Contains("data-ui-var=\"b:1\"", Render("#repeat 2\n#UI a = 1\n#loop\n#UI b = 2", enableUi: true));
            Assert.Contains("data-ui-var=\"b:1\"", Render("#repeat 9\n#UI a = 1\n#loop\n#UI b = 2", enableUi: true));
        }

        [Fact]
        public void DeclarationOverride_CoversEveryPassOfALoop()
        {
            var overrides = new Dictionary<string, string> { ["x:1"] = "4" };
            var html = Render("#repeat 3\n#UI x = 1\n#loop", enableUi: true, overrides);
            Assert.Equal(3, Regex.Matches(html, "value=\"4\"").Count);
        }

        [Fact]
        public void PassOverride_AppliesToThatIterationOnly()
        {
            var overrides = new Dictionary<string, string> { ["x:1:2"] = "4" };
            var html = Render("#repeat 3\n#UI x = 1\n#loop", enableUi: true, overrides);
            Assert.Single(Regex.Matches(html, "value=\"4\""));
            Assert.Equal(2, Regex.Matches(html, "value=\"1\"").Count);
        }

        [Fact]
        public void ControlNumbering_IsStableAcrossConditionalBranches()
        {
            const string source = "#if {0}\n#UI a = 1\n#end if\n#UI b = 2";
            Assert.Contains("data-ui-var=\"b:1\"", Render(string.Format(source, "1"), enableUi: true));
            Assert.Contains("data-ui-var=\"b:1\"", Render(string.Format(source, "0"), enableUi: true));
        }

        [Fact]
        public void UiStateDoesNotLeakToTheFollowingLine()
        {
            var html = Render("#UI L = 10m\nW = 5m", enableUi: true);
            Assert.Contains("data-ui-var=\"L:1\"", html);
            Assert.DoesNotContain("data-ui-var=\"W:1\"", html);
            Assert.Single(Regex.Matches(html, "calcpad-ui-input"));
        }
    }
}
