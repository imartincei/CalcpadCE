using System;
using System.IO;
using System.Linq;
using Calcpad.Highlighter.Linter.Models;

namespace Calcpad.Tests.Highlighter
{
    /// <summary>An error from an included file is on its #include line and records its place in that file.</summary>
    public class IncludeOriginTests : IClassFixture<HighlighterLinterFixture>, IDisposable
    {
        private readonly HighlighterLinterFixture _fixture;
        private readonly string _dir = Path.Combine(Path.GetTempPath(), $"include_origin_{Guid.NewGuid():N}");

        public IncludeOriginTests(HighlighterLinterFixture fixture)
        {
            _fixture = fixture;
            Directory.CreateDirectory(_dir);
        }

        public void Dispose() => Directory.Delete(_dir, true);

        private (LinterDiagnostic[] Diagnostics, string LibPath) Lint(string lib, string main)
        {
            var libPath = Path.Combine(_dir, "lib.cpd");
            var mainPath = Path.Combine(_dir, "main.cpd");
            File.WriteAllText(libPath, lib);
            File.WriteAllText(mainPath, main);
            return (_fixture.LintFile(mainPath).Diagnostics.Where(d => d.Code == "CPD-3301").ToArray(), libPath);
        }

        [Fact]
        public void IncludedError_RecordsFileLineAndColumns()
        {
            var lib = new[] { "a = 1", "b = libMissing + 1" };
            var (diagnostics, libPath) = Lint(string.Join('\n', lib), "x = 0\n#include lib.cpd");

            var d = Assert.Single(diagnostics);
            Assert.Equal(1, d.Line);
            Assert.StartsWith("lib.cpd line 2: ", d.Message);
            var origin = Assert.Single(d.IncludeOrigins);
            Assert.Equal(libPath, origin.FilePath);
            Assert.Equal(1, origin.Line);
            Assert.Equal("libMissing", lib[1][origin.Column..origin.EndColumn]);
            Assert.Equal(d.Message["lib.cpd line 2: ".Length..], origin.Message);
        }

        [Fact]
        public void IncludedContinuedLine_RecordsThePhysicalLine()
        {
            var lib = new[] { "c = 1 + _", "libMissing" };
            var (diagnostics, _) = Lint(string.Join('\n', lib), "#include lib.cpd");

            var origin = Assert.Single(Assert.Single(diagnostics).IncludeOrigins);
            Assert.Equal(1, origin.Line);
            Assert.Equal("libMissing", lib[1][origin.Column..origin.EndColumn]);
        }

        [Fact]
        public void ErrorsMergedOnTheIncludeLine_KeepEachOrigin()
        {
            var (diagnostics, _) = Lint("p = qa\nr = qb", "#include lib.cpd");

            var d = Assert.Single(diagnostics);
            Assert.Equal(new[] { 0, 1 }, d.IncludeOrigins.Select(o => o.Line).Order());
        }

        [Fact]
        public void IncludedFunctionBody_RecordsTheBody()
        {
            var lib = new[] { "' Library", "libBody(x) = x + libMissing" };
            var (diagnostics, libPath) = Lint(string.Join('\n', lib), "#include lib.cpd\na = libBody(1)");

            var body = Assert.Single(diagnostics, d => d.Line == 0);
            var origin = Assert.Single(body.IncludeOrigins);
            Assert.Equal(libPath, origin.FilePath);
            Assert.Equal(1, origin.Line);
            Assert.Equal("libMissing", lib[1][origin.Column..origin.EndColumn]);
            Assert.EndsWith("At the first call to libBody on line 2.", origin.Message);
            Assert.DoesNotContain("lib.cpd line", origin.Message);

            var call = Assert.Single(diagnostics, d => d.Line == 1);
            Assert.Null(call.IncludeOrigins);
        }

        [Fact]
        public void IncludedMacroBody_RecordsTheBodyAtTheCall()
        {
            var (diagnostics, libPath) = Lint("#def test$ = error", "#include lib.cpd\ntest$");

            var call = Assert.Single(diagnostics);
            Assert.Equal(1, call.Line);
            Assert.StartsWith("lib.cpd line 1: ", call.Message);
            var origin = Assert.Single(call.IncludeOrigins);
            Assert.Equal(libPath, origin.FilePath);
            Assert.Equal((0, 13, 18), (origin.Line, origin.Column, origin.EndColumn));
        }

        // The run shows it at the call, so it is kept only for the include
        [Fact]
        public void IncludedMacroBodyTheRunReported_IsIncludeOnly()
        {
            File.WriteAllText(Path.Combine(_dir, "lib.cpd"), "#def test$ = error");
            var mainPath = Path.Combine(_dir, "main.cpd");
            File.WriteAllText(mainPath, "#include lib.cpd\ntest$");
            var result = _fixture.LintFileAfterRun(mainPath).Result;

            Assert.DoesNotContain(result.Diagnostics, d => d.Code == "CPD-3301");
            var call = Assert.Single(result.IncludeOnlyDiagnostics, d => d.Code == "CPD-3301");
            Assert.Equal(1, call.Line);
            Assert.Equal((0, 13, 18), (call.IncludeOrigins[0].Line, call.IncludeOrigins[0].Column, call.IncludeOrigins[0].EndColumn));
        }
    }
}
