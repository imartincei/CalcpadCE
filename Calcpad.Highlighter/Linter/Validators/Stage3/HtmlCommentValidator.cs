using System;
using System.Text.Json;
using Calcpad.Highlighter.Linter.Helpers;
using Calcpad.Highlighter.Linter.Models;
using Calcpad.Highlighter.Tokenizer.Models;

namespace Calcpad.Highlighter.Linter.Validators.Stage3
{
    /// <summary>
    /// Validates metadata comments ('<!--{...}-->) in HTML comment tokens.
    /// Checks JSON validity and validates paramTypes values against allowed sets
    /// depending on whether the next definition is a function or macro.
    /// </summary>
    public class HtmlCommentValidator
    {
        public void Validate(Stage3Context stage3, LinterResult result, TokenizedLineProvider tokenProvider)
        {
            for (int i = 0; i < stage3.Lines.Count; i++)
            {
                if (!tokenProvider.IsCpdMode(i)) continue;

                var tokens = tokenProvider.GetTokensForLine(i);

                foreach (var token in tokens)
                {
                    if (token.Type != TokenType.HtmlComment)
                        continue;

                    var json = DefinitionMetadata.ExtractJsonFromComment(token.Text);
                    if (json == null)
                        continue;

                    ValidateMetadataJson(i, token, json, stage3, tokenProvider, result);
                }
            }
        }

        private void ValidateMetadataJson(
            int lineIndex, Token token, string json,
            Stage3Context stage3, TokenizedLineProvider tokenProvider,
            LinterResult result)
        {
            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(json);
            }
            catch (JsonException)
            {
                result.AddWarning(lineIndex, token.Column, token.Column + token.Length, "CPD-3412",
                    "malformed JSON in metadata comment");
                return;
            }

            using (doc)
            {
                var root = doc.RootElement;

                // returnType applies only to custom functions (value/vector/matrix/any).
                if (root.TryGetProperty("returnType", out var returnProp) && returnProp.ValueKind == JsonValueKind.String)
                {
                    var value = returnProp.GetString();
                    if (!string.IsNullOrEmpty(value) && !DefinitionMetadata.ValidFunctionParamTypes.Contains(value))
                    {
                        result.AddWarning(lineIndex, token.Column, token.Column + token.Length, "CPD-3411",
                            "'" + value + "' is not a valid returnType. Expected value, vector, matrix, or any");
                    }
                }

                if (!root.TryGetProperty("paramTypes", out var typesProp) || typesProp.ValueKind != JsonValueKind.Array)
                    return;

                // Determine if next definition is a function or macro
                bool isMacro = IsNextDefinitionMacro(lineIndex, stage3, tokenProvider);
                var validTypes = isMacro
                    ? DefinitionMetadata.ValidMacroParamTypes
                    : DefinitionMetadata.ValidFunctionParamTypes;

                foreach (var item in typesProp.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String)
                        continue;

                    var value = item.GetString();
                    if (!string.IsNullOrEmpty(value) && !validTypes.Contains(value))
                    {
                        var validList = isMacro ? "a valid TokenType name" : "value, vector, matrix, or any";
                        result.AddWarning(lineIndex, token.Column, token.Column + token.Length, "CPD-3411",
                            "'" + value + "' is not valid. Expected " + validList);
                    }
                }
            }
        }

        /// <summary>
        /// Looks ahead from the metadata comment line to determine if the next definition is a macro.
        /// Checks the next non-blank line for a #def keyword token.
        /// </summary>
        private static bool IsNextDefinitionMacro(int lineIndex, Stage3Context stage3, TokenizedLineProvider tokenProvider)
        {
            for (int i = lineIndex + 1; i < stage3.Lines.Count; i++)
            {
                var line = stage3.Lines[i];
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var tokens = tokenProvider.GetTokensForLine(i);
                if (tokens.Count == 0)
                    continue;

                // Check if first meaningful token is a #def keyword
                foreach (var t in tokens)
                {
                    if (t.Type == TokenType.None)
                        continue;

                    return t.Type == TokenType.Keyword &&
                           t.Text.TrimEnd().Equals("#def", StringComparison.OrdinalIgnoreCase);
                }

                // Non-blank line with tokens but first non-whitespace isn't #def
                return false;
            }

            return false;
        }
    }
}
