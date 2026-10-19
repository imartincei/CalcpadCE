using System;
using System.Text.RegularExpressions;
using Calcpad.Highlighter.Linter.Helpers;
using Calcpad.Highlighter.Linter.Models;
using Calcpad.Highlighter.Tokenizer.Models;

namespace Calcpad.Highlighter.Linter.Validators.Stage3
{
    public class FormatValidator
    {
        // Matches the Core regex from Calcpad.Core/Validator.cs
        // Standard specifiers: F, C, E, G, N, D followed by 0-2 digits
        // Custom patterns: combinations of 0, #, comma, dot, e/E, +, -
        private static readonly Regex FormatRegex = new(
            @"^[FCEGND]\d{0,2}$|^[0#]+(,[0#]+)?(\.[0#]+)?([eE][+-]?0+)?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public void Validate(Stage3Context stage3, LinterResult result, TokenizedLineProvider tokenProvider)
        {
            for (int i = 0; i < stage3.Lines.Count; i++)
            {
                if (!tokenProvider.IsCpdMode(i)) continue;

                var line = stage3.Lines[i];

                if (LineParser.ShouldSkipLine(line))
                    continue;

                var tokens = tokenProvider.GetTokensForLine(i);
                if (tokens == null || tokens.Count == 0)
                    continue;

                // Check if this is a #format keyword line
                bool isFormatKeywordLine = tokens.Count > 0 &&
                    tokens[0].Type == TokenType.Keyword &&
                    tokens[0].Text.Equals("#format", StringComparison.OrdinalIgnoreCase);

                for (int t = 0; t < tokens.Count; t++)
                {
                    var token = tokens[t];

                    if (token.Type != TokenType.Format)
                        continue;

                    var formatText = token.Text;

                    // "default" is valid only for #format keyword lines
                    if (formatText.Equals("default", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!isFormatKeywordLine)
                        {
                            result.AddWarning(i, token.Column, token.EndColumn, "CPD-3601",
                                $"Invalid format specifier: '{formatText}' - 'default' is only valid with #format");
                        }
                        continue;
                    }

                    // Validate against the format regex
                    if (!FormatRegex.IsMatch(formatText))
                    {
                        result.AddWarning(i, token.Column, token.EndColumn, "CPD-3601",
                            $"Invalid format specifier: '{formatText}'");
                    }
                }
            }
        }
    }
}