using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Calcpad.Highlighter.Parsing;

namespace Calcpad.Highlighter.ContentResolution
{
    /// <summary>
    /// Resolves Calcpad content through three stages:
    /// Stage 1: Line continuations
    /// Stage 2: Includes and macro collection (Core's MacroParser)
    /// Stage 3: Macro expansion (from the same MacroParser run) and definition collection
    /// </summary>
    public partial class ContentResolver
    {
        /// <param name="content">Raw Calcpad source code</param>
        /// <param name="sourceFilePath">Path of the document, used to resolve #include</param>
        public StagedResolvedContent GetStagedContent(string content, string sourceFilePath = null)
        {
            if (string.IsNullOrEmpty(content))
                throw new ArgumentException("Content cannot be null or empty", nameof(content));

            var lines = new List<string>();
            foreach (var lineSpan in new LineEnumerator(content.AsSpan()))
            {
                lines.Add(lineSpan.ToString());
            }

            var stage1 = ProcessStage1(lines);
            var stage2 = ProcessStage2(stage1, sourceFilePath, out var outputs);
            var stage3 = ProcessStage3(stage2, stage1, outputs);

            return new StagedResolvedContent
            {
                Stage1 = stage1,
                Stage2 = stage2,
                Stage3 = stage3
            };
        }

        /// <summary>
        /// Returns a copy of <paramref name="staged"/> whose types and line facts come from a
        /// Core run of the same content. The original is left untouched, as it may be cached.
        /// </summary>
        public static StagedResolvedContent ApplyRuntimeTrace(StagedResolvedContent staged, Calcpad.Core.RuntimeTrace trace)
        {
            var overlay = new RuntimeOverlay(trace, staged);
            var stage3 = staged.Stage3.ShallowCopy();
            stage3.TypeTracker = staged.Stage3.TypeTracker.WithRuntime(overlay);
            return new StagedResolvedContent
            {
                Stage1 = staged.Stage1,
                Stage2 = staged.Stage2,
                Stage3 = stage3,
                Runtime = overlay
            };
        }

        /// <summary>
        /// Joins a list of strings with '\n' separator using a pre-sized StringBuilder.
        /// Avoids the intermediate string[] allocation from string.Join.
        /// </summary>
        private static string JoinLines(List<string> lines)
        {
            if (lines.Count == 0) return string.Empty;
            if (lines.Count == 1) return lines[0];

            int totalLength = lines.Count - 1; // for '\n' separators
            for (int i = 0; i < lines.Count; i++)
                totalLength += lines[i].Length;

            var sb = new StringBuilder(totalLength);
            AppendJoinedLines(sb, lines);
            return sb.ToString();
        }

        /// <summary>
        /// Appends the lines joined by '\n' into an existing StringBuilder.
        /// Used when the caller needs to keep mutating the buffer (e.g. parameter
        /// substitution in macro expansion).
        /// </summary>
        private static void AppendJoinedLines(StringBuilder sb, List<string> lines)
        {
            if (lines.Count == 0) return;
            sb.Append(lines[0]);
            for (int i = 1; i < lines.Count; i++)
            {
                sb.Append('\n');
                sb.Append(lines[i]);
            }
        }
    }
}
