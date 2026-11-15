using System.Linq;
using Calcpad.Highlighter.ContentResolution;
using Calcpad.Highlighter.Linter;
using Calcpad.Highlighter.Linter.Models;
using Calcpad.Highlighter.Tokenizer;
using Calcpad.Highlighter.Tokenizer.Models;

namespace Calcpad.Tests.Highlighter
{
    /// <summary>
    /// A command's scope variable (the identifier after '@') may collide with a unit name,
    /// e.g. 's' (second) in $Integral{cos(s^2) @ s = 0 : t}. It must still tokenize as the
    /// loop variable, not a unit, so the command-syntax check (CPD-3406) doesn't misfire.
    /// $Inf/$Sup additionally define an implicit &lt;counter&gt;_inf / &lt;counter&gt;_sup variable.
    /// </summary>
    public class CommandScopeVariableTests
    {
        private static LinterResult Lint(string content)
        {
            var staged = new ContentResolver().GetStagedContent(content);
            var ignoreRegions = new LintIgnoreRegionParser().ExtractRegions(content);
            return new CalcpadLinter().Lint(staged, ignoreRegions);
        }

        private static int Count(LinterResult result, string code) =>
            result.Diagnostics.Count(d => d.Code == code);

        [Theory]
        [InlineData("#rad\nx(t) = $Integral{cos(s^2) @ s = 0 : t}")]
        [InlineData("$Sum{s^2 @ s = 1 : 5}")]
        [InlineData("q = $Integral{cos(m^2) @ m = 0 : 5}")]
        public void CommandScopeVariableCollidingWithUnit_NoSyntaxError(string src)
        {
            Assert.Equal(0, Count(Lint(src), "CPD-3406"));
        }

        private static TokenType[] TypesOf(string content, string text) =>
            new CalcpadTokenizer().Tokenize(content).Tokens.Where(t => t.Text == text).Select(t => t.Type).ToArray();

        [Theory]
        [InlineData("$Block{a = vector(5); $Repeat{a.i = i^2 @ i = 1 : 5}; a}", "a")]
        [InlineData("$Block{m = matrix(5; 5); $Repeat{$Repeat{m.(i; j) = i*j @ i = 1 : 5} @ j = 1 : 5}; m}", "m")]
        [InlineData("x = $Inline{s = 2; t = s^2; t}", "s")]
        [InlineData("x = $Inline{s = 2; t = s^2; t}", "t")]
        [InlineData("$While{g < 5; g = g + 1}", "g")]
        public void BlockAssignment_IsLocalVariable(string src, string name)
        {
            var types = TypesOf(src, name);
            Assert.NotEmpty(types);
            Assert.All(types, t => Assert.Equal(TokenType.LocalVariable, t));
        }

        [Theory]
        [InlineData("$Block{a == 5; a}")]
        [InlineData("$Block{f(a = 5); a}")]
        [InlineData("$Sum{a = 5 @ i = 1 : 2}")]
        public void NonAssignment_LeavesUnit(string src)
        {
            Assert.Contains(TokenType.Units, TypesOf(src, "a"));
        }

        [Fact]
        public void InfDefinesCounterInfVariable()
        {
            var src =
                "f(k) = k^2 - 3*k\n" +
                "A_inf = $Inf{f(k) @ k = 0 : 5}\n" +
                "k = k_inf\n";
            Assert.Equal(0, Count(Lint(src), "CPD-3301"));
        }

        [Fact]
        public void SupDefinesCounterSupVariable()
        {
            var src =
                "f(x) = -x^2 + 4*x\n" +
                "A_sup = $Sup{f(x) @ x = 0 : 5}\n" +
                "x_peak = x_sup\n";
            Assert.Equal(0, Count(Lint(src), "CPD-3301"));
        }

        [Fact]
        public void CounterInfWithUnrelatedName_StillUndefined()
        {
            var src =
                "f(k) = k^2\n" +
                "A_inf = $Inf{f(k) @ k = 0 : 5}\n" +
                "y = j_inf\n";
            Assert.True(Count(Lint(src), "CPD-3301") > 0);
        }

        [Fact]
        public void NestedInf_DefinesBothCounterVariables()
        {
            var src =
                "f(x; y) = (x^2 + y - 11)^2 + (x + y^2 - 7)^2\n" +
                "z_inf = $inf{$inf{f(x; y) @ x = -5 : 0} @ y = -5 : 0}\n" +
                "x_inf','y_inf\n";
            Assert.Equal(0, Count(Lint(src), "CPD-3301"));
        }

        [Fact]
        public void ImplicitSolverVariables_AppearInDefinitionsForAutocomplete()
        {
            var src =
                "f(x; y) = x^2 + y^2\n" +
                "z_inf = $inf{$inf{f(x; y) @ x = -5 : 0} @ y = -5 : 0}\n";
            var staged = new ContentResolver().GetStagedContent(src);
            var names = staged.Stage3.VariablesWithDefinitions.Select(v => v.Name).ToList();
            Assert.Contains("x_inf", names);
            Assert.Contains("y_inf", names);
        }

        [Fact]
        public void NonInfSolver_DoesNotDefineImplicitVariable()
        {
            var src =
                "f(x) = x^2\n" +
                "s = $Sum{f(k) @ k = 1 : 5}\n" +
                "y = k_inf\n";
            Assert.True(Count(Lint(src), "CPD-3301") > 0);
        }

        [Fact]
        public void LuDecomposition_DefinesIndPermutationVector()
        {
            var src =
                "A = [4; 12; -16|12; 37; -43|-16; -43; 98]\n" +
                "LU = lu(A)\n" +
                "p = ind\n";
            Assert.Equal(0, Count(Lint(src), "CPD-3301"));
        }

        [Fact]
        public void LuIndVariable_AppearsInDefinitionsForAutocomplete()
        {
            var src = "A = [1; 2 | 3; 4]\nLU = lu(A)\n";
            var staged = new ContentResolver().GetStagedContent(src);
            Assert.Contains("ind", staged.Stage3.VariablesWithDefinitions.Select(v => v.Name));
        }

        [Fact]
        public void IndWithoutLu_StillUndefined()
        {
            var src = "x = ind\n";
            Assert.True(Count(Lint(src), "CPD-3301") > 0);
        }
    }
}
