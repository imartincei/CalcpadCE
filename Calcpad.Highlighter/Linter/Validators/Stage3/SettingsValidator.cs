using System.Text.Json;
using Calcpad.Core;
using Calcpad.Highlighter.Linter.Helpers;
using Calcpad.Highlighter.Linter.Models;
using Calcpad.Highlighter.Tokenizer.Models;

namespace Calcpad.Highlighter.Linter.Validators.Stage3
{
    /// <summary>
    /// Validates the JSON payload of the #settings directive (`#settings {...}`), reporting
    /// malformed JSON, non-object payloads, unrecognized keys, wrong value types and out-of-range
    /// values. Recognized keys, types and ranges come from <see cref="SettingsDto"/> so the linter
    /// and the runtime parser stay in sync.
    /// </summary>
    public class SettingsValidator
    {
        private const string Code = "CPD-3413";

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

            var reporter = new DirectiveJsonReporter(
                result, lineIndex, token.Column, token.Column + token.Length, Code, "#settings");

            using (var doc = reporter.TryParse(json))
            {
                if (doc is null)
                    return;

                if (!reporter.RequireObject(doc.RootElement))
                    return;

                reporter.CheckKnownKeys(doc.RootElement, SettingsDto.KnownKeys.Contains, "setting");
            }

            SettingsDto dto;
            try
            {
                dto = SettingsDto.Parse(json);
            }
            catch (JsonException)
            {
                reporter.Warn("a #settings value has the wrong type");
                return;
            }

            foreach (var error in dto.Validate())
                reporter.Warn(error.Message);
        }
    }
}
