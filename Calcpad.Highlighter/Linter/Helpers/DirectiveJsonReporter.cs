using System;
using System.Text.Json;
using Calcpad.Highlighter.Linter.Models;

namespace Calcpad.Highlighter.Linter.Helpers
{
    /// <summary>
    /// Reports diagnostics for a JSON payload carried by a directive, e.g. #settings or #UI.
    /// Binds the diagnostic code and the span of the payload once, so the checks common to
    /// every such directive - parsing it and rejecting unrecognized keys - read the same way
    /// in each validator and keep their wording consistent.
    /// </summary>
    public readonly struct DirectiveJsonReporter
    {
        private readonly LinterResult _result;
        private readonly int _lineIndex;
        private readonly int _from;
        private readonly int _to;
        private readonly string _code;
        private readonly string _directive;

        /// <param name="directive">The directive as written, e.g. "#UI". Used in messages.</param>
        public DirectiveJsonReporter(LinterResult result, int lineIndex, int from, int to, string code, string directive)
        {
            _result = result;
            _lineIndex = lineIndex;
            _from = from;
            _to = to;
            _code = code;
            _directive = directive;
        }

        /// <summary>
        /// The same span, reporting under a different code and label. Used for a payload
        /// nested inside another - the <c>pdf</c> object of a metadata comment - so its
        /// messages name it rather than borrowing the enclosing directive's wording.
        /// </summary>
        public DirectiveJsonReporter For(string code, string directive) =>
            new(_result, _lineIndex, _from, _to, code, directive);

        public void Warn(string message) =>
            _result.AddWarning(_lineIndex, _from, _to, _code, message);

        /// <summary>
        /// Parses the payload, returning null and reporting the failure when it is malformed.
        /// The caller owns the returned document.
        /// </summary>
        public JsonDocument TryParse(string json)
        {
            try
            {
                return JsonDocument.Parse(json);
            }
            catch (JsonException)
            {
                Warn($"malformed JSON in {_directive} directive");
                return null;
            }
        }

        public bool RequireObject(JsonElement root)
        {
            if (root.ValueKind == JsonValueKind.Object)
                return true;

            Warn($"{_directive} payload must be a JSON object");
            return false;
        }

        /// <param name="keyNoun">Completes "'x' is not a recognized ...", e.g. "setting".</param>
        public void CheckKnownKeys(JsonElement root, Func<string, bool> isKnown, string keyNoun)
        {
            foreach (var prop in root.EnumerateObject())
            {
                if (!isKnown(prop.Name))
                    Warn($"'{prop.Name}' is not a recognized {keyNoun}");
            }
        }
    }
}
