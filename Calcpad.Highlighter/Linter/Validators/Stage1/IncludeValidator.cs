using System;
using Calcpad.Highlighter.Linter.Constants;
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

                // Check if there's anything after #include
                if (trimmedSpan.Length <= 8 || trimmedSpan[8..].Trim().Length == 0)
                {
                    result.AddError(i, 0, line.Length, "CPD-1102",
                        "#include requires a file path", LineStage.Stage1);
                    continue;
                }

                // Use regex to extract the filename
                var match = CalcpadPatterns.IncludeStatement.Match(line);
                if (!match.Success)
                {
                    result.AddError(i, 0, line.Length, "CPD-1101",
                        "'" + line.AsSpan().Trim().ToString() + "'", LineStage.Stage1);
                    continue;
                }

                var filename = match.Groups[1].Value.AsSpan().Trim().ToString();

                // Check for empty filename
                if (string.IsNullOrWhiteSpace(filename))
                {
                    result.AddError(i, includeKeywordEndIndex, line.Length, "CPD-1102",
                        "#include requires a file path", LineStage.Stage1);
                    continue;
                }
            }
        }
    }
}
