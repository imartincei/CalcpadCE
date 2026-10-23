using System;
using System.Text.Json;
using Calcpad.Core;
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
        private readonly LinterSeverity _severity;

        /// <param name="directive">The directive as written, e.g. "#UI". Used in messages.</param>
        /// <param name="coreRejects">Core stops on a bad payload, so <see cref="Report"/> gives errors rather than warnings.</param>
        public DirectiveJsonReporter(LinterResult result, int lineIndex, int from, int to, string code, string directive, bool coreRejects = false)
        {
            _result = result;
            _lineIndex = lineIndex;
            _from = from;
            _to = to;
            _code = code;
            _directive = directive;
            _severity = coreRejects ? LinterSeverity.Error : LinterSeverity.Warning;
        }

        /// <summary>
        /// The same span, reporting under a different code and label. Used for a payload
        /// nested inside another - the <c>pdf</c> object of a metadata comment - so its
        /// messages name it rather than borrowing the enclosing directive's wording.
        /// </summary>
        public DirectiveJsonReporter For(string code, string directive) =>
            new(_result, _lineIndex, _from, _to, code, directive, _severity == LinterSeverity.Error);

        /// <summary>A value the directive rejects.</summary>
        public void Report(string message) =>
            _result.AddDiagnostic(_lineIndex, _from, _to, _code, message, _severity);

        /// <summary>Something the directive ignores, such as an unknown key.</summary>
        public void Warn(string message) =>
            _result.AddWarning(_lineIndex, _from, _to, _code, message);

        /// <summary>
        /// Reports the payload as unparsable, in the same wording the engine uses, so a
        /// document rejected while typing reads the same way when it is calculated.
        /// </summary>
        public void ReportInvalidJson() =>
            Report(string.Format(Messages.Invalid_JSON_in_0, _directive));

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
                ReportInvalidJson();
                return null;
            }
        }

        public bool RequireObject(JsonElement root)
        {
            if (root.ValueKind == JsonValueKind.Object)
                return true;

            Report($"{_directive} payload must be a JSON object");
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
