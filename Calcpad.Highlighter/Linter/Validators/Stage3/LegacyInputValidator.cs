using System;
using Calcpad.Core;
using Calcpad.Highlighter.Linter.Helpers;
using Calcpad.Highlighter.Linter.Models;

namespace Calcpad.Highlighter.Linter.Validators.Stage3
{
    /// <summary>
    /// Warns on '?{value}' input fields, the legacy way of storing an input value in the document
    /// that #UI replaces. They still parse, so this is a warning that gives existing documents time
    /// to migrate.
    /// </summary>
    public class LegacyInputValidator
    {
        private const string Code = "CPD-3419";

        public void Validate(Stage3Context stage3, LinterResult result, TokenizedLineProvider tokenProvider)
        {
            for (var i = 0; i < stage3.Lines.Count; i++)
            {
                if (!tokenProvider.IsCpdMode(i))
                    continue;

                var offset = 0;
                foreach (var segment in stage3.Lines[i].AsSpan().EnumerateComments())
                {
                    if (UiSyntax.IsCode(segment))
                        ReportInputFields(i, segment, offset, result);

                    offset += segment.Length;
                }
            }
        }

        private static void ReportInputFields(
            int lineIndex,
            ReadOnlySpan<char> segment,
            int offset,
            LinterResult result)
        {
            for (var i = 0; i < segment.Length; i++)
            {
                if (segment[i] != '?')
                    continue;

                var brace = i + 1;
                while (brace < segment.Length && segment[brace] == ' ')
                    ++brace;

                if (brace >= segment.Length || segment[brace] != '{')
                    continue;

                var close = segment[brace..].IndexOf('}');
                var end = close < 0 ? segment.Length : brace + close + 1;
                result.AddWarning(lineIndex, offset + i, offset + end, Code,
                    $"'{segment[i..end]}' - declare the input with #UI instead");

                i = end - 1;
            }
        }
    }
}
