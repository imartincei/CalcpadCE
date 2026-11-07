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
    }
}
