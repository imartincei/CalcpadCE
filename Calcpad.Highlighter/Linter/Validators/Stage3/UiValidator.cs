using System;
using System.Collections.Generic;
using System.Text.Json;
using Calcpad.Highlighter.Linter.Helpers;
using Calcpad.Highlighter.Linter.Models;

namespace Calcpad.Highlighter.Linter.Validators.Stage3
{
    /// <summary>
    /// Validates the optional JSON block of the #UI directive (`#UI {...} name = value`).
    /// Reports an unclosed or malformed block, unrecognized keys, dropdown/radio option
    /// lists that are missing or of unequal length, and the string mode that
    /// ExpressionParser rejects. Mirrors the checks in ExpressionParser.ParseKeywordUi.
    /// </summary>
    public class UiValidator
    {
        private const string Code = "CPD-3415";
        private const string Keyword = "#ui";

        private static readonly HashSet<string> KnownKeys =
        [
            "type", "mode", "style", "reportStyle", "rows", "columns",
            "columnHeaders", "rowHeaders", "keys", "values"
        ];

        private static readonly string[] KnownTypes =
            ["entry", "datagrid", "dropdown", "radio", "checkbox"];

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
                Reporter(result, lineIndex, afterKeyword, end).Warn("#UI requires a variable assignment");
                return;
            }

            var jsonEnd = cursor;
            if (line[cursor] == '{')
            {
                var braceEnd = line.IndexOf('}', cursor);
                if (braceEnd < 0)
                {
                    Reporter(result, lineIndex, cursor, end).Warn("#UI has an unclosed JSON block, missing '}'");
                    return;
                }
                if (!ValidateJson(line[cursor..(braceEnd + 1)], Reporter(result, lineIndex, cursor, braceEnd + 1)))
                    return;

                jsonEnd = braceEnd + 1;
            }

            var assignment = line.AsSpan(jsonEnd);
            var eqIndex = assignment.IndexOf('=');
            if (eqIndex < 0 || assignment[..eqIndex].IsWhiteSpace())
                Reporter(result, lineIndex, jsonEnd, end).Warn("#UI requires a variable assignment");
            else if (assignment[..eqIndex].TrimEnd().EndsWith("$", StringComparison.Ordinal))
                Reporter(result, lineIndex, jsonEnd, end).Warn("#UI does not support string variables");
        }

        private static bool ValidateJson(string json, DirectiveJsonReporter reporter)
        {
            using var doc = reporter.TryParse(json);
            if (doc is null)
                return false;

            var root = doc.RootElement;
            reporter.CheckKnownKeys(root, KnownKeys.Contains, "#UI property");

            var type = GetString(root, "type");
            if (type is not null && Array.IndexOf(KnownTypes, type) < 0)
                reporter.Warn($"'{type}' is not a recognized #UI type, expected one of {string.Join(", ", KnownTypes)}");

            var mode = GetString(root, "mode");
            if (mode is not null && !string.Equals(mode, "number", StringComparison.OrdinalIgnoreCase))
                reporter.Warn("#UI does not support string variables");

            if (type is "dropdown" or "radio")
            {
                var keys = ArrayLength(root, "keys");
                var values = ArrayLength(root, "values");
                if (keys < 0 || values < 0)
                    reporter.Warn($"#UI {type} requires both 'keys' and 'values' arrays");
                else if (keys != values)
                    reporter.Warn($"#UI {type} has {keys} keys but {values} values, the arrays must be the same length");
            }
            return true;
        }

        private static string GetString(JsonElement root, string name) =>
            root.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ?
            p.GetString() :
            null;

        private static int ArrayLength(JsonElement root, string name) =>
            root.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Array ?
            p.GetArrayLength() :
            -1;

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
