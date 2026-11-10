using System;
using System.Text.Json;
using Calcpad.Core;
using Calcpad.Highlighter.Linter.Helpers;
using Calcpad.Highlighter.Linter.Models;
using Calcpad.Highlighter.Tokenizer.Models;

namespace Calcpad.Highlighter.Linter.Validators.Stage3
{
    /// <summary>
    /// Unrecognized keys in the #settings JSON payload. Core's check pass reports the rest.
    /// </summary>
    public class SettingsValidator
    {
        private const string Code = "CPD-3413";

        public void Validate(Stage3Context stage3, LinterResult result, TokenizedLineProvider tokenProvider)
        {
            for (int i = 0; i < stage3.Lines.Count; i++)
            {
                if (!tokenProvider.IsCpdMode(i)) continue;

                // #UI shares the SettingsJson token but not the key set, so skip its payload.
                if (!stage3.Lines[i].AsSpan().TrimStart().StartsWith("#settings", StringComparison.OrdinalIgnoreCase))
                    continue;

                foreach (var token in tokenProvider.GetTokensForLine(i))
                {
                    if (token.Type == TokenType.SettingsJson)
                        ValidateSettingsJson(i, token, result);
                }
            }
        }

        private void ValidateSettingsJson(int lineIndex, Token token, LinterResult result)
        {
            var json = token.Text.Trim();
            if (json.Length == 0)
                return;

            var reporter = new DirectiveJsonReporter(
                result, lineIndex, token.Column, token.Column + token.Length, Code, "#settings");
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    reporter.CheckKnownKeys(doc.RootElement, SettingsDto.KnownKeys.Contains, "setting");
            }
            catch (JsonException)
            {
            }
        }
    }
}
