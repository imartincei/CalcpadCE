using System;
using System.Collections.Generic;
using System.IO;
using Calcpad.Highlighter.ContentResolution;
using Calcpad.Highlighter.Linter;
using Calcpad.Highlighter.Linter.Models;


namespace Calcpad.Tests.Highlighter
{
    public class HighlighterLinterFixture
    {
        public string BaseDir { get; }
        public string ValidDir { get; }
        public string ErrorsDir { get; }

        public HighlighterLinterFixture()
        {
            var assemblyDir = Path.GetDirectoryName(typeof(HighlighterLinterFixture).Assembly.Location)!;
            BaseDir = Path.Combine(assemblyDir, "Highlighter");
            ValidDir = Path.Combine(BaseDir, "valid");
            ErrorsDir = Path.Combine(BaseDir, "errors");

            if (!Directory.Exists(ValidDir))
                throw new DirectoryNotFoundException(
                    $"Test data folder not found at '{ValidDir}'.");
        }

        public LinterResult LintFile(string fullPath)
        {
            var content = File.ReadAllText(fullPath);
            var resolver = new ContentResolver();
            var staged = resolver.GetStagedContent(content, sourceFilePath: fullPath);
            var ignoreRegions = new LintIgnoreRegionParser().ExtractRegions(content);
            return new CalcpadLinter().Lint(staged, ignoreRegions);
        }

        /// <summary>
        /// Lints with the trace of a debug run attached, as the editor does after a run.
        /// Also returns the run's own errors and the source lines that have a line the run didn't execute.
        /// </summary>
        public (LinterResult Result, List<Calcpad.Core.CalcpadError> RunErrors, HashSet<int> NotRunLines) LintFileAfterRun(string fullPath)
        {
            var content = File.ReadAllText(fullPath);
            var macroParser = new Calcpad.Core.MacroParser
            {
                Include = (path, _) => Calcpad.Core.MacroParser.FilterIncludedContent(File.ReadAllText(path)),
                SourceFilePath = fullPath
            };
            macroParser.Parse(content, out var code, null, 0, true);
            var parser = new Calcpad.Core.ExpressionParser
            {
                Settings = new Calcpad.Core.Settings(),
                SourceFilePath = fullPath,
                PathRoots = macroParser.PathRoots,
                Debug = true,
                AllowDataWrite = false
            };
            parser.Parse(code, true, false);

            var staged = ContentResolver.ApplyRuntimeTrace(
                new ContentResolver().GetStagedContent(content, sourceFilePath: fullPath), parser.Trace);
            var ignoreRegions = new LintIgnoreRegionParser().ExtractRegions(content);
            var lineCount = content.Split('\n').Length;
            return (new CalcpadLinter().Lint(staged, ignoreRegions), [.. macroParser.Errors, .. parser.Errors],
                ErrorExpectations.NotRunLines(staged, lineCount));
        }
    }
}
