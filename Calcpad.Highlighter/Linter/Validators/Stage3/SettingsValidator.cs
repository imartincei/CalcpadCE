using System.Text.Json;
using Calcpad.Core;
using Calcpad.Highlighter.Linter.Helpers;
using Calcpad.Highlighter.Linter.Models;
using Calcpad.Highlighter.Tokenizer.Models;

namespace Calcpad.Highlighter.Linter.Validators.Stage3
{
    /// <summary>
    /// Validates the JSON payload of the #settings directive (`#settings {...}`).
    /// Reports malformed JSON, non-object payloads, unrecognized keys, wrong value
    /// types, and out-of-range values. Recognized keys, types, and ranges come from
    /// <see cref="SettingsDto"/> so the linter and the runtime parser stay in sync.
    /// </summary>
    public class SettingsValidator
    {
        public void Validate(Stage3Context stage3, LinterResult result, TokenizedLineProvider tokenProvider)
        {
            for (int i = 0; i < stage3.Lines.Count; i++)
            {
                if (!tokenProvider.IsCpdMode(i)) continue;

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

            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(json);
            }
            catch (JsonException)
            {
                result.AddWarning(lineIndex, token.Column, token.Column + token.Length, "CPD-3413",
                    "malformed JSON in #settings directive");
                return;
            }

            using (doc)
            {
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    result.AddWarning(lineIndex, token.Column, token.Column + token.Length, "CPD-3413",
                        "#settings payload must be a JSON object");
                    return;
                }

                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (!SettingsDto.KnownKeys.Contains(prop.Name))
                        result.AddWarning(lineIndex, token.Column, token.Column + token.Length, "CPD-3413",
                            "'" + prop.Name + "' is not a recognized setting");
                }
            }

            SettingsDto dto;
            try
            {
                dto = SettingsDto.Parse(json);
            }
            catch (JsonException)
            {
                result.AddWarning(lineIndex, token.Column, token.Column + token.Length, "CPD-3413",
                    "a #settings value has the wrong type");
                return;
            }

            foreach (var error in dto.Validate())
                result.AddWarning(lineIndex, token.Column, token.Column + token.Length, "CPD-3413", error.Message);
        }
    }
}
