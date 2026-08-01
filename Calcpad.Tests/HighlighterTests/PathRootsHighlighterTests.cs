using System.Collections.Generic;
using System.Linq;
using Calcpad.Highlighter.ContentResolution;
using Calcpad.Highlighter.Tokenizer;
using Calcpad.Highlighter.Tokenizer.Models;

namespace Calcpad.Tests.HighlighterTests
{
    public class PathRootsHighlighterTests
    {
        [Fact]
        public void Declaration_TokenizesValueAsSingleToken()
        {
            var result = new CalcpadTokenizer().Tokenize("#ProjectPath = C:/Jobs/1042");
            var pathRootTokens = result.Tokens.Where(t => t.Type == TokenType.PathRoot).ToList();
            var token = Assert.Single(pathRootTokens);
            Assert.Equal("= C:/Jobs/1042", token.Text);
        }

        [Fact]
        public void Declaration_IsRecognizedAsKeyword()
        {
            var result = new CalcpadTokenizer().Tokenize("#LibraryPath = C:/Lib");
            var keywordTokens = result.Tokens.Where(t => t.Type == TokenType.Keyword).ToList();
            var token = Assert.Single(keywordTokens);
            Assert.Equal("#LibraryPath", token.Text);
        }

        [Fact]
        public void Include_WithLibraryToken_ResolvesAgainstDeclaredRoot()
        {
            var resolvedLibraryFile = System.IO.Path.GetFullPath("lib/steel.cpd", "/project");
            var content = "#LibraryPath = /project/lib\n#include <library>/steel.cpd\nx = 1";
            var includeFiles = new Dictionary<string, string>
            {
                [resolvedLibraryFile] = "y = 2"
            };

            var staged = new ContentResolver().GetStagedContent(content, includeFiles, sourceFilePath: "/project/main.cpd");
            var joined = string.Join('\n', staged.Stage2.Lines);

            Assert.DoesNotContain("Error: Include file not provided", joined);
            Assert.Contains("y = 2", joined);
        }

        [Fact]
        public void Include_WithUndeclaredToken_FallsBackToNotFound()
        {
            var content = "#include <library>/steel.cpd\nx = 1";
            var staged = new ContentResolver().GetStagedContent(content, sourceFilePath: "/project/main.cpd");
            var joined = string.Join('\n', staged.Stage2.Lines);

            Assert.Contains("Error: Include file not provided", joined);
        }
    }
}
