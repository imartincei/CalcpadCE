using System.Linq;
using Calcpad.Highlighter.ContentResolution;
using Calcpad.Highlighter.Tokenizer;
using Calcpad.Highlighter.Tokenizer.Models;
using Calcpad.Server.Services;

namespace Calcpad.Tests.Highlighter
{
    /// <summary>
    /// A unit-named identifier outside a definition is a variable only if the last run defined it
    /// before use, so a definition in an untaken branch leaves the unit, as in Core.
    /// </summary>
    public class RuntimeVariableNamesTests
    {
        private const string Branch = "x = 1\n#if x > 5\na = 5\n#end if\nc = a\nb = 2*a\n";

        private static RuntimeVariableNames Run(string content) =>
            RuntimeVariableNames.From(
                new CalcpadService().Convert(content, debug: true).Trace!,
                new ContentResolver().GetStagedContent(content));

        private static TokenType TypeOf(string content, RuntimeVariableNames names, int line, string text)
        {
            var tokenizer = new CalcpadTokenizer();
            tokenizer.SetResolvedContent(new ContentResolver().GetStagedContent(content), names);
            return tokenizer.Tokenize(content).Tokens.Last(t => t.Line == line && t.Text == text).Type;
        }

        [Fact]
        public void UntakenBranchDefinition_IsUnitAfterRun()
        {
            var names = Run(Branch);
            Assert.Equal(TokenType.Units, TypeOf(Branch, names, 4, "a"));
            Assert.Equal(TokenType.Units, TypeOf(Branch, names, 5, "a"));
        }

        [Fact]
        public void UntakenBranchDefinition_IsVariableWithoutRun()
        {
            Assert.Equal(TokenType.Variable, TypeOf(Branch, null, 4, "a"));
        }

        [Fact]
        public void TakenBranchDefinition_IsVariable()
        {
            var content = Branch.Replace("x = 1", "x = 10");
            Assert.Equal(TokenType.Variable, TypeOf(content, Run(content), 4, "a"));
        }

        [Fact]
        public void RunSurvivesEdits()
        {
            Assert.Equal(TokenType.Units, TypeOf(Branch + "d = a\n", Run(Branch), 6, "a"));
        }

        [Fact]
        public void DefinitionAddedSinceRun_IsVariable()
        {
            Assert.Equal(TokenType.Variable, TypeOf(Branch + "m = 3\nd = m\n", Run(Branch), 7, "m"));
        }

        [Fact]
        public void DefinitionDeletedSinceRun_IsUnit()
        {
            Assert.Equal(TokenType.Units, TypeOf("c = a\n", Run("a = 5\nc = a\n"), 0, "a"));
        }

        [Fact]
        public void NameOnlyTheRunAssigned_IsVariable()
        {
            Assert.Equal(TokenType.Variable, TypeOf("c = a\n", new RuntimeVariableNames(["a"], [], true), 0, "a"));
        }

        [Fact]
        public void IncompleteRun_FallsBackToStatic()
        {
            Assert.Equal(TokenType.Variable, TypeOf(Branch, new RuntimeVariableNames([], ["a"], false), 4, "a"));
        }

        [Fact]
        public void NameAfterNumber_IgnoresRun()
        {
            Assert.Equal(TokenType.Units, TypeOf("a = 5\nc = 2a\n", Run("a = 5\nc = 2a\n"), 1, "a"));
        }

        [Fact]
        public void FunctionParameter_IgnoresRun()
        {
            var content = Branch + "f(a) = a*2\n";
            Assert.Equal(TokenType.LocalVariable, TypeOf(content, Run(content), 6, "a"));
        }
    }
}
