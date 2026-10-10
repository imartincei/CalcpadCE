using System.IO;
using System.Linq;

namespace Calcpad.Tests
{
    public class MacroTraceTests
    {
        private static MacroTrace Trace(string source, string sourceFilePath = null)
        {
            var trace = new MacroTrace();
            var parser = new MacroParser
            {
                SourceFilePath = sourceFilePath,
                Trace = trace,
                Include = (path, _) => MacroParser.FilterIncludedContent(File.ReadAllText(path), keepLineCount: true)
            };
            parser.Parse(source, out _, null, 0, false);
            return trace;
        }

        [Fact]
        public void SourceAndOutputLines_MapThroughExpansion()
        {
            var trace = Trace("#def m$(a) = a + 1\nx = m$(2)\n#def blk$\ny = 1\nz = 2\n#end def\nblk$");
            Assert.True(trace.IsComplete);
            Assert.Equal(7, trace.SourceLines.Count);
            Assert.Equal("#def blk$", trace.SourceLines[2].Text);
            Assert.Equal(4, trace.SourceLines[3].RootLine);

            var outputs = trace.OutputLines;
            Assert.Equal(3, outputs.Count);
            Assert.Equal("x = 2 + 1", outputs[0].Text);
            Assert.Equal(1, outputs[0].SourceIndex);
            Assert.Equal(["m$"], outputs[0].ExpandedMacros);
            Assert.Equal("z = 2", outputs[2].Text);
            Assert.Equal(6, outputs[2].SourceIndex);
            Assert.Equal(1, outputs[2].ContentIndex);
            Assert.Equal(2, outputs[2].ContentCount);

            Assert.Equal(2, trace.Macros.Count);
            Assert.True(trace.Macros[0].IsInline);
            Assert.Equal(["a"], trace.Macros[0].Parameters);
            Assert.False(trace.Macros[1].IsInline);
            Assert.Equal(2, trace.Macros[1].SourceIndex);
            Assert.Equal(["y = 1", "z = 2"], trace.Macros[1].Content);
        }

        [Fact]
        public void Includes_CarryFileLinesAndBlankLocalSections()
        {
            var dir = Directory.CreateTempSubdirectory("calcpad-trace-");
            try
            {
                var inc = Path.Combine(dir.FullName, "inc.cpd");
                File.WriteAllText(inc, "#local\nhidden = 1\n#global\nv = 3");
                var main = Path.Combine(dir.FullName, "main.cpd");
                var trace = Trace("a = 1\n#include inc.cpd\nb = 2", main);

                Assert.Equal([inc], trace.Includes.Select(i => i.Path));
                Assert.DoesNotContain(trace.SourceLines, l => l.Text.StartsWith("#include"));
                var v = trace.SourceLines.Single(l => l.Text == "v = 3");
                Assert.Equal(inc, v.File);
                Assert.Equal(3, v.FileLine);
                Assert.Equal(2, v.RootLine);
                Assert.DoesNotContain(trace.SourceLines, l => l.Text.Contains("hidden"));
                Assert.Equal(3, trace.SourceLines.Single(l => l.Text == "b = 2").RootLine);
            }
            finally
            {
                dir.Delete(true);
            }
        }

        [Fact]
        public void FailedInclude_IsFlagged()
        {
            var trace = Trace("#include missing-file.cpd\nx = 1", Path.Combine(Path.GetTempPath(), "main.cpd"));
            var failed = trace.OutputLines[0];
            Assert.Equal("missing-file.cpd", failed.FailedInclude);
            Assert.Equal("#include missing-file.cpd", trace.SourceLines[failed.SourceIndex].Text);
            Assert.Equal("x = 1", trace.OutputLines[1].Text);
        }

        [Fact]
        public void Duplicates_AreFlagged()
        {
            var trace = Trace("#def m$ = 1\n#def m$ = 2");
            Assert.False(trace.Macros[0].IsDuplicate);
            Assert.True(trace.Macros[1].IsDuplicate);
            Assert.Equal(1, trace.Macros[1].SourceIndex);
        }

        [Fact]
        public void ExpansionError_KeepsOriginalText()
        {
            var trace = Trace("#def m$ = 1\nx = undefined$");
            Assert.Equal("x = undefined$", trace.OutputLines.Single().Text);
            Assert.Equal(1, trace.Errors.Single().SourceIndex);
        }

        [Fact]
        public void Abort_IsNotComplete()
        {
            var trace = Trace("#def m$(a; a) = a\nx = 1");
            Assert.False(trace.IsComplete);
        }
    }
}
