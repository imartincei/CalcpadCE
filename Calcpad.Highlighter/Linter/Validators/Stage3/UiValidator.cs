using System;
using System.Text.Json;
using Calcpad.Core;
using Calcpad.Highlighter.Linter.Helpers;
using Calcpad.Highlighter.Linter.Models;

namespace Calcpad.Highlighter.Linter.Validators.Stage3
{
    /// <summary>
    /// Unknown keys in the JSON block of `#UI {...} name = value`. Core's check pass reports
    /// everything else about the directive.
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
                var start = line.Length - line.TrimStart().Length;
                if (line.AsSpan(start).StartsWith(Keyword, StringComparison.OrdinalIgnoreCase))
                    CheckKeys(i, line, start + Keyword.Length, result);
            }
        }

        private static void CheckKeys(int lineIndex, string line, int cursor, LinterResult result)
        {
            while (cursor < line.Length && line[cursor] == ' ')
                ++cursor;
            if (cursor >= line.Length || line[cursor] != '{')
                return;

            var braceEnd = line.IndexOf('}', cursor);
            if (braceEnd < 0)
                return;

            var reporter = new DirectiveJsonReporter(result, lineIndex, cursor, braceEnd + 1, Code, "#UI");
            try
            {
                using var doc = JsonDocument.Parse(line[cursor..(braceEnd + 1)]);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    reporter.CheckKnownKeys(doc.RootElement, UiDto.KnownKeys.Contains, "#UI property");
            }
            catch (JsonException)
            {
            }
        }
    }
}
