using System;
using System.Collections.Generic;
using System.Text.Json;
using Calcpad.Highlighter.HtmlComment;
using Calcpad.Highlighter.Linter.Models;
using Calcpad.Highlighter.Tokenizer;

namespace Calcpad.Highlighter.Linter
{
    /// <summary>
    /// Extracts <see cref="LintIgnoreRegion"/> list from HTML comment blocks embedded in
    /// raw Calcpad source. Regions are expressed in original source line numbers (0-based)
    /// and are passed directly to <c>CalcpadLinter.Lint()</c>.
    ///
    /// Supported syntax:
    /// <code>
    ///   '&lt;!--{"LintIgnore": ["CPD-3301", "CPD-3302"]}--&gt;
    ///   code
    ///   '&lt;!--{"EndLintIgnore": ["CPD-3301"]}--&gt;
    /// </code>
    /// Rules:
    /// - Codes are case-insensitive.
    /// - <c>LintIgnore</c> with a non-empty array opens an independent suppression for each
    ///   listed code. Opening a code that is already open is a no-op.
    /// - <c>LintIgnore</c> with an empty array opens a "suppress all" region (matches any code).
    /// - <c>EndLintIgnore</c> with a non-empty array closes only the listed codes' open regions.
    ///   Codes not currently open are ignored. Other open codes continue to be suppressed.
    /// - <c>EndLintIgnore</c> with an empty array closes every currently-open region.
    /// - Any region still open at end of file suppresses through end of file.
    /// </summary>
    public sealed class LintIgnoreRegionParser
    {
        /// <summary>
        /// Tokenizes <paramref name="content"/> and returns all lint-ignore regions
        /// found in HTML comment blocks.
        /// </summary>
        public IReadOnlyList<LintIgnoreRegion> ExtractRegions(string content)
        {
            if (string.IsNullOrEmpty(content))
                return Array.Empty<LintIgnoreRegion>();

            var tokens = new CalcpadTokenizer().Tokenize(content);
            var blocks = new HtmlCommentParser().Parse(tokens);

            var openByCode = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int openAllStart = -1;
            var result = new List<LintIgnoreRegion>();

            foreach (var block in blocks)
            {
                if (block.Status != HtmlCommentParseStatus.Success || !block.Data.HasValue)
                    continue;

                var data = block.Data.Value;
                if (data.ValueKind != JsonValueKind.Object)
                    continue;

                if (data.TryGetProperty("LintIgnore", out var openEl)
                    && openEl.ValueKind == JsonValueKind.Array)
                {
                    var codes = ParseCodes(openEl);
                    int regionStart = block.EndLine + 1;

                    if (codes.Count == 0)
                    {
                        if (openAllStart < 0)
                            openAllStart = regionStart;
                    }
                    else
                    {
                        foreach (var code in codes)
                            openByCode.TryAdd(code, regionStart);
                    }
                }
                else if (data.TryGetProperty("EndLintIgnore", out var endEl)
                         && endEl.ValueKind == JsonValueKind.Array)
                {
                    var codes = ParseCodes(endEl);
                    int regionEnd = block.StartLine - 1;

                    if (codes.Count == 0)
                    {
                        if (openAllStart >= 0)
                        {
                            EmitIfNonEmpty(result, openAllStart, regionEnd, EmptyCodeSet);
                            openAllStart = -1;
                        }
                        foreach (var (code, start) in openByCode)
                            EmitIfNonEmpty(result, start, regionEnd, SingleCodeSet(code));
                        openByCode.Clear();
                    }
                    else
                    {
                        foreach (var code in codes)
                        {
                            if (openByCode.TryGetValue(code, out var start))
                            {
                                EmitIfNonEmpty(result, start, regionEnd, SingleCodeSet(code));
                                openByCode.Remove(code);
                            }
                        }
                    }
                }
            }

            // Unmatched opens → suppress to EOF
            if (openAllStart >= 0)
                result.Add(new LintIgnoreRegion(openAllStart, int.MaxValue, EmptyCodeSet));
            foreach (var (code, start) in openByCode)
                result.Add(new LintIgnoreRegion(start, int.MaxValue, SingleCodeSet(code)));

            return result;
        }

        private static readonly IReadOnlySet<string> EmptyCodeSet =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static IReadOnlySet<string> SingleCodeSet(string code) =>
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { code };

        private static void EmitIfNonEmpty(
            List<LintIgnoreRegion> result, int startLine, int endLine, IReadOnlySet<string> codes)
        {
            if (startLine <= endLine)
                result.Add(new LintIgnoreRegion(startLine, endLine, codes));
        }

        private static IReadOnlySet<string> ParseCodes(JsonElement arrayElement)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in arrayElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var code = item.GetString();
                    if (!string.IsNullOrWhiteSpace(code))
                        set.Add(code.Trim());
                }
            }
            return set;
        }
    }
}
