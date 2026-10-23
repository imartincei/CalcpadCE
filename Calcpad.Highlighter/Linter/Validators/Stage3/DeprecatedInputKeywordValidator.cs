using System;
using Calcpad.Highlighter.Linter.Helpers;
using Calcpad.Highlighter.Linter.Models;

namespace Calcpad.Highlighter.Linter.Validators.Stage3
{
    /// <summary>
    /// Warns on #input, which #pause and #UI replace. It is now a no-op, so this is a warning
    /// that gives existing documents time to migrate before it is removed.
    /// </summary>
    public class DeprecatedInputKeywordValidator
    {
        private const string Code = "CPD-3420";
        private const string Keyword = "#input";

        public void Validate(Stage3Context stage3, LinterResult result, TokenizedLineProvider tokenProvider)
        {
            for (var i = 0; i < stage3.Lines.Count; i++)
            {
                if (!tokenProvider.IsCpdMode(i))
                    continue;

                var line = stage3.Lines[i].AsSpan();
                var start = line.Length - line.TrimStart().Length;
                var rest = line[start..];
                if (!rest.StartsWith(Keyword, StringComparison.OrdinalIgnoreCase) ||
                    rest.Length > Keyword.Length && !char.IsWhiteSpace(rest[Keyword.Length]))
                    continue;

                result.AddWarning(i, start, start + Keyword.Length, Code,
                    "#input is deprecated and has no effect - use #pause with #UI controls instead");
            }
        }
    }
}
