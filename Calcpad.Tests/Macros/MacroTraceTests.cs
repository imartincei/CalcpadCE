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
                Debug = true,
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
        public void UnclosedDef_IsReportedOnDefLine()
        {
            var trace = new MacroTrace();
            var parser = new MacroParser { Trace = trace };
            Assert.True(parser.Parse("x = 1\n#def blk$\ny = 2", out _, null, 0, false));
            Assert.Equal("#def blk$", trace.SourceLines[trace.Errors.Single().SourceIndex].Text);
            Assert.Equal(2, parser.Errors.Single().SourceLine);
        }

        // Source line of each character, '·' where none; every located character must match its source.
        private static string SourceLines(MacroTrace trace, MacroTraceOutputLine output) =>
            string.Concat(output.SourceOffsets.Select((offset, i) =>
            {
                if (!trace.TryLocate(offset, out var line, out var column))
                    return '·';

                Assert.Equal(output.Text[i], trace.SourceLines[line].Text[column]);
                return (char)('0' + line);
            }));

        [Theory]
        [InlineData("#def m$(a$) = a$ + 1", "x = m$(ab)", "x = ab + 1", "1111110000")]
        [InlineData("#def m$(a$; b$) = b$*a$", "y = m$(1; 2) - 3", "y = 2*1 - 3", "11111011111")]
        [InlineData("#def m$(a$; b$ = 9) = a$ + b$", "m$(b$ = 4; a$ = q)", "q + 4", "10001")]
        [InlineData("#def m$(a$; b$ = 9) = a$ + b$", "m$(5)", "5 + 9", "1000·")]
        [InlineData("#def in$(p$) = (p$)\n#def out$(q$) = in$(q$)*2", "y = out$(z) + 1", "y = (z)*2 + 1", "2222020112222")]
        [InlineData("#def m$(a$) = a$ + a$", "'note m$(t)", "'note t + t", "11111110001")]
        public void SourceOffsets_LocateEveryCharacter(string defs, string line, string expected, string sources)
        {
            var trace = Trace(defs + "\n" + line);
            var output = trace.OutputLines.Last();
            Assert.Equal(expected, output.Text);
            Assert.Equal(sources, SourceLines(trace, output));
        }

        [Fact]
        public void SourceOffsets_SplitAcrossBodyLines()
        {
            var trace = Trace("#def blk$(n$; x$)\nn$ = x$\nr = n$*2\n#end def\n  blk$(v; 7)");
            var outputs = trace.OutputLines;
            Assert.Equal(["  v = 7", "r = v*2"], outputs.Select(o => o.Text));
            Assert.Equal("4441114", SourceLines(trace, outputs[0]));
            Assert.Equal("2222422", SourceLines(trace, outputs[1]));
        }

        [Fact]
        public void SourceOffsets_NullWithoutDebug()
        {
            var trace = new MacroTrace();
            new MacroParser { Trace = trace }.Parse("#def m$(a$) = a$ + 1\nx = m$(2)", out _, null, 0, false);
            Assert.Equal("x = 2 + 1", trace.OutputLines.Single().Text);
            Assert.Null(trace.OutputLines.Single().SourceOffsets);
        }

        [Fact]
        public void SourceOffsets_NullWithoutExpansion()
        {
            var outputs = Trace("#def m$ = 1\nx = 2\n'cost $").OutputLines;
            Assert.All(outputs, o => Assert.Null(o.SourceOffsets));
        }

        [Fact]
        public void ArgumentText_IsNotSubstitutedAgain()
        {
            var trace = Trace("#def f$(a; b) = a + b\nx = f$(1; a)");
            Assert.Equal("x = 1 + a", trace.OutputLines.Single().Text);
        }

        [Fact]
        public void DefinitionErrors_DoNotAbort()
        {
            var trace = Trace("#def m$(a$; a$) = a$\n#def n$(a$ = 1; b$) = b$\n#def k$(x$\n#end def\nx = 1");
            Assert.True(trace.IsComplete);
            Assert.Equal(["CPD-2212", "CPD-2213", "CPD-2205"], trace.Errors.Select(e => e.Code));
            Assert.Equal("x = 1", trace.OutputLines[^1].Text);
        }

        [Fact]
        public void Abort_IsNotComplete()
        {
            var path = Path.GetTempFileName();
            try
            {
                var trace = new MacroTrace();
                var parser = new MacroParser { Trace = trace, Include = (_, _) => throw new IOException() };
                parser.Parse($"#include {path}\nx = 1", out _, null, 0, false);
                Assert.False(trace.IsComplete);
                Assert.Equal("CPD-2200", Assert.Single(trace.Errors).Code);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void CallErrors_LocateTheCall()
        {
            var trace = Trace("#def m$(a$) = a$\nx = 1 + m$(1; 2) + n$");
            Assert.Equal(["CPD-3304"], trace.Errors.Select(e => e.Code));
            var error = trace.Errors[0];
            Assert.True(trace.TryLocate(error.Start, out var line, out var start));
            Assert.True(trace.TryLocate(error.End, out _, out var end));
            Assert.Equal((1, 8, 15), (line, start, end));
        }

        [Fact]
        public void ErrorsFromMacroBodies_LocateTheBody()
        {
            var trace = Trace("#def m$ = n$\nx = m$");
            var error = Assert.Single(trace.Errors);
            Assert.Equal("CPD-3303", error.Code);
            Assert.Equal(1, error.SourceIndex);
            Assert.True(trace.TryLocate(error.Start, out var line, out _));
            Assert.Equal(0, line);
        }

        [Fact]
        public void UnclosedParameterList_DoesNotHang()
        {
            var trace = Trace("#def m$(a$ \nx = 1");
            Assert.Contains(trace.Errors, e => e.Code == "CPD-2205");
        }

        [Fact]
        public void StrayEndDef_DoesNotBreakLaterDefinitions()
        {
            var trace = Trace("#end def\n#def m$ = 1\nx = m$");
            Assert.Equal(["CPD-2206"], trace.Errors.Select(e => e.Code));
            Assert.Equal("x = 1", trace.OutputLines[^1].Text);
        }
    }
}
