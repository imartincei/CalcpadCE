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
    /// Stage 2: Include/read resolution and macro collection
    /// Stage 3: Macro expansion and definition collection
    ///
    /// The caller (Calcpad.Server) pre-fetches include/read file contents
    /// and passes them as a dictionary. This class handles all processing internally.
    /// </summary>
    public partial class ContentResolver
    {
        /// <summary>
        /// Get staged content with all three stages.
        /// </summary>
        /// <param name="content">Raw Calcpad source code</param>
        /// <param name="includeFiles">Dictionary mapping filename to file content for #include/#read directives</param>
        /// <param name="clientFileCache">Dictionary mapping filename to raw file bytes from client cache</param>
        public StagedResolvedContent GetStagedContent(string content, Dictionary<string, string> includeFiles = null, Dictionary<string, byte[]> clientFileCache = null, string sourceFilePath = null)
        {
            if (string.IsNullOrEmpty(content))
                throw new ArgumentException("Content cannot be null or empty", nameof(content));

            includeFiles ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            clientFileCache ??= new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

            // Use LineEnumerator to avoid intermediate string[] allocation from Split
            var lines = new List<string>();
            foreach (var lineSpan in new LineEnumerator(content.AsSpan()))
            {
                lines.Add(lineSpan.ToString());
            }

            // Stage 1: Process line continuations only
            var stage1 = ProcessStage1(lines);

            // Stage 2: Resolve includes/reads, collect macros (but don't expand)
            var stage2 = ProcessStage2(stage1, includeFiles, clientFileCache, sourceFilePath);

            // Stage 3: Expand macros, collect all definitions
            var stage3 = ProcessStage3(stage2, stage1);

            return new StagedResolvedContent
            {
                Stage1 = stage1,
                Stage2 = stage2,
                Stage3 = stage3
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
