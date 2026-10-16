using System;
using System.Collections.Generic;
using Calcpad.Highlighter.Linter.Models;
using Calcpad.Highlighter.Tokenizer;

namespace Calcpad.Highlighter.ContentResolution
{
    /// <summary>A run of source lines, inclusive, in #html or #markdown mode.</summary>
    public readonly record struct ParseModeRange(int StartLine, int EndLine, ParseMode Mode);

    public static class SourceParseModes
    {
        /// <summary>
        /// Parse mode of each source line, read from the macro-expanded Stage 3 content so that a
        /// macro or #include switching mode counts, as it does for Core. A line takes the mode in
        /// effect before its own content; Calcpad lines are omitted from the result.
        /// </summary>
        public static List<ParseModeRange> Build(StagedResolvedContent staged, int sourceLineCount)
        {
            var before = new ParseMode?[sourceLineCount];
            var after = new ParseMode?[sourceLineCount];
            var tracker = new ParseModeTracker();
            var stage3 = staged.Stage3;
            for (int i = 0; i < stage3.Lines.Count; i++)
            {
                var modeBefore = tracker.Mode;
                tracker.Apply(stage3.Lines[i].AsSpan().Trim());
                var line = ToSourceLine(i, staged);
                if ((uint)line >= (uint)sourceLineCount)
                    continue;

                before[line] ??= modeBefore;
                after[line] = tracker.Mode;
            }

            // Lines missing from Stage 3, such as #def bodies, carry the mode left by the line above
            var ranges = new List<ParseModeRange>();
            var carry = ParseMode.Cpd;
            var start = 0;
            var current = ParseMode.Cpd;
            for (int line = 0; line < sourceLineCount; line++)
            {
                var mode = before[line] ?? carry;
                carry = after[line] ?? carry;
                if (mode == current)
                    continue;

                if (current != ParseMode.Cpd)
                    ranges.Add(new ParseModeRange(start, line - 1, current));

                start = line;
                current = mode;
            }
            if (current != ParseMode.Cpd)
                ranges.Add(new ParseModeRange(start, sourceLineCount - 1, current));

            return ranges;
        }

        private static int ToSourceLine(int stage3Line, StagedResolvedContent staged)
        {
            var stage2Line = staged.Stage3.SourceMap.GetValueOrDefault(stage3Line, stage3Line);
            var stage1Line = staged.Stage2.SourceMap.GetValueOrDefault(stage2Line, stage2Line);
            return staged.Stage1.SourceMap.GetValueOrDefault(stage1Line, stage1Line);
        }
    }
}
