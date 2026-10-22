using System;
using Calcpad.Highlighter.Linter.Helpers;
using Calcpad.Highlighter.Linter.Models;

namespace Calcpad.Highlighter.Linter.Validators.Stage1
{
    public class IncludeValidator
    {
        public void Validate(Stage1Context context, LinterResult result)
        {
            for (int i = 0; i < context.Lines.Count; i++)
            {
                var line = context.Lines[i];
                ReadOnlySpan<char> trimmedSpan = line.AsSpan().TrimStart();

                if (!trimmedSpan.StartsWith("#include", StringComparison.OrdinalIgnoreCase))
                    continue;

                var includeKeywordEndIndex = line.AsSpan().IndexOf("#include".AsSpan(), StringComparison.OrdinalIgnoreCase) + 8;
                ValidateInputFields(i, line, includeKeywordEndIndex, result);
            }
        }

        /// <summary>
        /// Warns on the '#{v1; v2}' field list that fed input values into the included file, the
        /// legacy input storage that #UI replaces. It still parses, so this is a warning that gives
        /// existing documents time to migrate.
        /// </summary>
        private static void ValidateInputFields(int lineIndex, string line, int afterKeyword, LinterResult result)
        {
            var span = line.AsSpan();
            var marker = span[afterKeyword..].LastIndexOf("#{");
            if (marker < 0)
                return;

            marker += afterKeyword;
            var close = span[marker..].IndexOf('}');
            var end = close < 0 ? span.Length : marker + close + 1;
            result.AddWarning(lineIndex, marker, end, "CPD-1103",
                $"'{span[marker..end]}' - declare the inputs with #UI instead",
                LineStage.Stage1);
        }
    }
}
