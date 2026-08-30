using System;
using System.Text.Json;
using Calcpad.Core;
using Calcpad.Highlighter.Linter.Helpers;
using Calcpad.Highlighter.Linter.Models;

namespace Calcpad.Highlighter.Linter.Validators.Stage3
{
    /// <summary>
    /// Validates the optional JSON block of the #UI directive (`#UI {...} name = value`), reporting
    /// an unclosed or malformed block and unrecognized keys before deferring to <see cref="UiDto"/>
    /// for the property rules, so the linter and ExpressionParser reject the same payloads with the
    /// same wording. What stays here is what the payload alone cannot decide: that the line assigns
    /// something, and that it does not assign a string variable.
    /// </summary>
    public class UiValidator
    {
        private const string Code = "CPD-3415";
        private const string Keyword = "#ui";

        public void Validate(Stage3Context stage3, LinterResult result, TokenizedLineProvider tokenProvider)
        {
            for (var i = 0; i < stage3.Lines.Count; i++)
            {
                if (!tokenProvider.IsCpdMode(i))
                    continue;

                var line = stage3.Lines[i];
                var start = FirstNonSpace(line);
                if (!line.AsSpan(start).StartsWith(Keyword, StringComparison.OrdinalIgnoreCase))
                    continue;

                ValidateUiLine(i, line, start + Keyword.Length, result);
            }
        }

        private void ValidateUiLine(int lineIndex, string line, int afterKeyword, LinterResult result)
        {
            var cursor = afterKeyword;
            while (cursor < line.Length && line[cursor] == ' ')
                ++cursor;

            var end = line.Length;
            if (cursor >= end)
            {
                Reporter(result, lineIndex, afterKeyword, end).Warn(Messages.The_UI_keyword_requires_a_variable_assignment);
                return;
            }

            var jsonEnd = cursor;
            if (line[cursor] == '{')
            {
                var braceEnd = line.IndexOf('}', cursor);
                if (braceEnd < 0)
                {
                    Reporter(result, lineIndex, cursor, end).Warn(Messages.Improper_format_for_UI_keyword_Missing_closing_brace);
                    return;
                }
                if (!ValidateJson(line[cursor..(braceEnd + 1)], Reporter(result, lineIndex, cursor, braceEnd + 1)))
                    return;

                jsonEnd = braceEnd + 1;
            }

            var reporter = Reporter(result, lineIndex, jsonEnd, end);
            var assignments = UiSyntax.EnumerateAssignments(line.AsSpan(jsonEnd));
            if (assignments.Count == 0)
            {
                reporter.Warn(Messages.The_UI_keyword_requires_a_variable_assignment);
                return;
            }
            foreach (var (name, rhs) in assignments)
            {
                if (name.EndsWith('$'))
                {
                    reporter.Warn(Messages.Only_numbers_are_supported_by_the_UI_keyword);
                    return;
                }
                if (!UiSyntax.IsValue(rhs))
                {
                    reporter.Warn(Messages.UI_directives_do_not_support_expressions);
                    return;
                }
            }
        }

        private static bool ValidateJson(string json, DirectiveJsonReporter reporter)
        {
            using (var doc = reporter.TryParse(json))
            {
                if (doc is null)
                    return false;

                reporter.CheckKnownKeys(doc.RootElement, UiDto.KnownKeys.Contains, "#UI property");
            }

            UiDto properties;
            try
            {
                properties = UiDto.Parse(json);
            }
            catch (JsonException)
            {
                // A wrong value type stops deserialization but not the checks below, which
                // only need to know where the block ends.
                reporter.Warn(Messages.A_UI_value_has_the_wrong_type);
                return true;
            }

            foreach (var error in properties.Validate())
                reporter.Warn(error.Message);

            return true;
        }

        private static int FirstNonSpace(string line)
        {
            var i = 0;
            while (i < line.Length && char.IsWhiteSpace(line[i]))
                ++i;
            return i;
        }

        private static DirectiveJsonReporter Reporter(LinterResult result, int lineIndex, int from, int to) =>
            new(result, lineIndex, from, to, Code, "#UI");
    }
}
