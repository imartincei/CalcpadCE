using System;
using System.Collections.Generic;
using System.Text.Json;
using Calcpad.Highlighter.HtmlComment;
using Calcpad.Highlighter.Linter.Constants;
using Calcpad.Highlighter.Linter.Helpers;
using Calcpad.Highlighter.Linter.Models;
using Calcpad.Highlighter.Tokenizer.Models;

namespace Calcpad.Highlighter.Linter.Validators.Stage3
{
    /// <summary>
    /// Validates metadata comments ('<!--{...}-->): the JSON itself, its recognized
    /// keys, the paramTypes/returnType vocabularies (which depend on whether the next
    /// definition is a function or a macro), the LintIgnore region markers, and the
    /// nested <c>pdf</c> object of PDF export settings.
    /// <para>
    /// Blocks come from <see cref="HtmlCommentParser"/> rather than from individual
    /// tokens, so a comment continued across lines with <c>_</c> is validated as one
    /// payload instead of being skipped.
    /// </para>
    /// </summary>
    public class HtmlCommentValidator
    {
        private const string Code = "CPD-3412";
        private const string TypeCode = "CPD-3411";
        private const string PdfCode = "CPD-3414";

        /// <summary>Recognized top-level keys of a metadata comment.</summary>
        private static readonly HashSet<string> KnownKeys = new(StringComparer.OrdinalIgnoreCase)
            { "desc", "paramTypes", "paramDesc", "returnType", "LintIgnore", "EndLintIgnore", "pdf" };

        private readonly HtmlCommentParser _parser = new();

        public void Validate(Stage3Context stage3, LinterResult result, TokenizedLineProvider tokenProvider)
        {
            foreach (var block in _parser.Parse(tokenProvider.AllTokens))
            {
                if (block.StartLine >= stage3.Lines.Count || !tokenProvider.IsCpdMode(block.StartLine))
                    continue;

                // A plain prose comment ('<!-- a note -->) is an HTML comment too, and
                // carries no metadata. Only a JSON object is ours to judge.
                if (block.RawJson is null || !block.RawJson.StartsWith("{", StringComparison.Ordinal))
                    continue;

                var reporter = new DirectiveJsonReporter(
                    result, block.StartLine, block.StartColumn, block.EndColumn, Code, "metadata comment");

                if (block.Status != HtmlCommentParseStatus.Success || block.Data is null)
                {
                    reporter.Warn("malformed JSON in metadata comment");
                    continue;
                }

                var root = block.Data.Value;
                if (!reporter.RequireObject(root))
                    continue;

                reporter.CheckKnownKeys(root, KnownKeys.Contains, "metadata property");
                ValidateTypes(block, root, stage3, tokenProvider, reporter);
                ValidateLintRegions(root, reporter);
                ValidatePdf(root, reporter);
            }
        }

        /// <summary>
        /// Checks returnType and paramTypes against the vocabulary that applies here:
        /// custom functions take value/vector/matrix/any, macros take TokenType names.
        /// </summary>
        private static void ValidateTypes(HtmlCommentBlock block, JsonElement root,
            Stage3Context stage3, TokenizedLineProvider tokenProvider, DirectiveJsonReporter reporter)
        {
            var typeReporter = reporter.For(TypeCode, "metadata comment");

            if (root.TryGetProperty("returnType", out var returnProp) && returnProp.ValueKind == JsonValueKind.String)
            {
                var value = returnProp.GetString();
                if (!string.IsNullOrEmpty(value) && !DefinitionMetadata.ValidFunctionParamTypes.Contains(value))
                    typeReporter.Warn($"'{value}' is not a valid returnType. Expected value, vector, matrix, or any");
            }

            if (!root.TryGetProperty("paramTypes", out var typesProp) || typesProp.ValueKind != JsonValueKind.Array)
                return;

            bool isMacro = IsNextDefinitionMacro(block.EndLine, stage3, tokenProvider);
            var validTypes = isMacro
                ? DefinitionMetadata.ValidMacroParamTypes
                : DefinitionMetadata.ValidFunctionParamTypes;
            var validList = isMacro ? "a valid TokenType name" : "value, vector, matrix, or any";

            foreach (var item in typesProp.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                    continue;

                var value = item.GetString();
                if (!string.IsNullOrEmpty(value) && !validTypes.Contains(value))
                    typeReporter.Warn($"'{value}' is not valid. Expected {validList}");
            }
        }

        /// <summary>
        /// Checks that the region markers list diagnostic codes. A typo here silently
        /// suppresses nothing, so it is worth reporting even though the region parser
        /// itself tolerates it.
        /// </summary>
        private static void ValidateLintRegions(JsonElement root, DirectiveJsonReporter reporter)
        {
            foreach (var name in new[] { "LintIgnore", "EndLintIgnore" })
            {
                if (!root.TryGetProperty(name, out var prop))
                    continue;

                if (prop.ValueKind != JsonValueKind.Array)
                {
                    reporter.Warn($"'{name}' must be an array of diagnostic codes (empty for all)");
                    continue;
                }

                foreach (var item in prop.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String)
                    {
                        reporter.Warn($"'{name}' must contain diagnostic codes, e.g. \"CPD-3301\"");
                        continue;
                    }

                    var code = item.GetString();
                    if (!ErrorCodes.Descriptions.ContainsKey(code))
                        reporter.Warn($"'{code}' is not a known diagnostic code");
                }
            }
        }

        /// <summary>
        /// Defers to <see cref="PdfSettingsDto"/> for the PDF export settings, so the
        /// linter and the export path judge the payload by the same rules.
        /// </summary>
        private static void ValidatePdf(JsonElement root, DirectiveJsonReporter reporter)
        {
            if (!root.TryGetProperty("pdf", out var pdf))
                return;

            var pdfReporter = reporter.For(PdfCode, "pdf settings");
            if (!pdfReporter.RequireObject(pdf))
                return;

            pdfReporter.CheckKnownKeys(pdf, PdfSettingsDto.KnownKeys.Contains, "PDF setting");

            PdfSettingsDto dto;
            try
            {
                dto = PdfSettingsDto.Parse(pdf.GetRawText());
            }
            catch (JsonException)
            {
                pdfReporter.Warn("a pdf value has the wrong type");
                return;
            }

            foreach (var error in dto.Validate())
                pdfReporter.Warn(error.Message);
        }

        /// <summary>
        /// Looks ahead from the metadata comment to determine if the next definition is
        /// a macro. Checks the next non-blank line for a #def keyword token.
        /// </summary>
        private static bool IsNextDefinitionMacro(int commentEndLine, Stage3Context stage3, TokenizedLineProvider tokenProvider)
        {
            for (int i = commentEndLine + 1; i < stage3.Lines.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(stage3.Lines[i]))
                    continue;

                var tokens = tokenProvider.GetTokensForLine(i);
                if (tokens.Count == 0)
                    continue;

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
