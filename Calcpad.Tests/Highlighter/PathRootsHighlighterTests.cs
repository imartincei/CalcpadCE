using System.Linq;
using Calcpad.Highlighter.ContentResolution;
using Calcpad.Highlighter.Tokenizer;
using Calcpad.Highlighter.Tokenizer.Models;

namespace Calcpad.Tests.Highlighter
{
    public class PathRootsHighlighterTests
    {
        // Include resolution runs the source file's folder through Path.GetFullPath, which
        // rejects a base directory that is not fully qualified — "/project" is rooted on Linux
        // but drive-less, so it throws on Windows and every lookup falls back to the raw name.
        private static readonly string SourceFilePath =
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "project", "main.cpd");

        [Fact]
        public void Declaration_TokenizesValueAsSingleToken()
        {
            var result = new CalcpadTokenizer().Tokenize("#ProjectPath C:/Jobs/1042");
            var pathRootTokens = result.Tokens.Where(t => t.Type == TokenType.FilePath).ToList();
            var token = Assert.Single(pathRootTokens);
            Assert.Equal("C:/Jobs/1042", token.Text);
        }

        [Fact]
        public void Declaration_IsRecognizedAsKeyword()
        {
            var result = new CalcpadTokenizer().Tokenize("#LibraryPath C:/Lib");
            var keywordTokens = result.Tokens.Where(t => t.Type == TokenType.Keyword).ToList();
            var token = Assert.Single(keywordTokens);
            Assert.Equal("#LibraryPath", token.Text);
        }

        [Fact]
        public void Include_WithLibraryToken_ResolvesAgainstDeclaredRoot()
        {
            var temp = new System.IO.DirectoryInfo(System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName()));
            temp.Create();
            try
            {
                var libDir = System.IO.Directory.CreateDirectory(System.IO.Path.Combine(temp.FullName, "lib"));
                System.IO.File.WriteAllText(System.IO.Path.Combine(libDir.FullName, "steel.cpd"), "y = 2");
                var content = $"#LibraryPath {libDir.FullName}\n#include {{library}}/steel.cpd\nx = 1";

                var staged = new ContentResolver().GetStagedContent(content,
                    sourceFilePath: System.IO.Path.Combine(temp.FullName, "main.cpd"));
                var joined = string.Join('\n', staged.Stage2.Lines);

                Assert.DoesNotContain("Error: Include file not provided", joined);
                Assert.Contains("y = 2", joined);
            }
            finally
            {
                temp.Delete(recursive: true);
            }
        }

        [Fact]
        public void Include_WithUndeclaredToken_FallsBackToNotFound()
        {
            var content = "#include {library}/steel.cpd\nx = 1";
            var staged = new ContentResolver().GetStagedContent(content, sourceFilePath: SourceFilePath);
            var joined = string.Join('\n', staged.Stage2.Lines);

            Assert.Contains("Error: Include file not provided", joined);
        }

        [Fact]
        public void Stage2PathRoots_DeclaredInsideInclude_IsPopulatedAndUsableByParent()
        {
            var temp = new System.IO.DirectoryInfo(System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName()));
            temp.Create();
            try
            {
                var libDir = System.IO.Directory.CreateDirectory(System.IO.Path.Combine(temp.FullName, "lib"));
                System.IO.File.WriteAllText(System.IO.Path.Combine(temp.FullName, "decl.cpd"), $"#LibraryPath {libDir.FullName}");
                System.IO.File.WriteAllText(System.IO.Path.Combine(libDir.FullName, "steel.cpd"), "y = 2");
                var content = "#include decl.cpd\n#include {library}/steel.cpd\nx = 1";

                var staged = new ContentResolver().GetStagedContent(content,
                    sourceFilePath: System.IO.Path.Combine(temp.FullName, "main.cpd"));
                var joined = string.Join('\n', staged.Stage2.Lines);

                Assert.DoesNotContain("Error: Include file not provided", joined);
                Assert.Contains("y = 2", joined);
                Assert.Equal(libDir.FullName, staged.Stage2.PathRoots?.Library);
            }
            finally
            {
                temp.Delete(recursive: true);
            }
        }

        // Asserts the {user} expansion without writing into the real profile folder.
        [Fact]
        public void Include_WithUserToken_ExpandsWithNoDeclaration()
        {
            var home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
            var content = "#include {user}/calcpad-test-missing/steel.cpd\nx = 1";

            var staged = new ContentResolver().GetStagedContent(content, sourceFilePath: SourceFilePath);
            var placeholder = staged.Stage2.Lines[0];

            Assert.Contains("Error: Include file not provided", placeholder);
            Assert.Contains(home, placeholder);
            Assert.DoesNotContain("{user}", placeholder);
        }
    }
}
