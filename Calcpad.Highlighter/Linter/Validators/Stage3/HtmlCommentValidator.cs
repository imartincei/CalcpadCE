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
        private const string MisplacedUiOverridesCode = "CPD-3416";
        private const string DuplicateUiOverridesCode = "CPD-3417";
        private const string MixedUiOverridesCode = "CPD-3418";

        /// <summary>Recognized top-level keys of a metadata comment.</summary>
        private static readonly HashSet<string> KnownKeys = new(StringComparer.OrdinalIgnoreCase)
            { "desc", "paramTypes", "paramDesc", "returnType", "LintIgnore", "EndLintIgnore", "pdf", "uiOverrides" };

        private readonly HtmlCommentParser _parser = new();

        public void Validate(Stage3Context stage3, LinterResult result, TokenizedLineProvider tokenProvider)
        {
            var sawUiOverrides = false;
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
                    reporter.WarnInvalidJson();
                    continue;
                }

                var root = block.Data.Value;
                if (!reporter.RequireObject(root))
                    continue;

                reporter.CheckKnownKeys(root, KnownKeys.Contains, "metadata property");
                ValidateTypes(block, root, stage3, tokenProvider, reporter);
                ValidateLintRegions(root, reporter);
                ValidateUiOverrides(block, root, stage3, reporter, ref sawUiOverrides);
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
        /// Checks the shape of the saved <c>#UI</c> values: an object mapping a control
        /// identity to the expression entered for it, both written as strings. The keys
        /// themselves are not checked - a key may deliberately be broader than any single
        /// control, and one left behind by an edit is not an error.
        /// <para>
        /// The host (readUiOverrides in ui-overrides.ts) only ever reads the first
        /// 'uiOverrides' comment it finds, and only when it sits on the file's first line -
        /// anywhere else, or any comment after the first, is silently inert. Both are flagged
        /// here rather than left to fail quietly, since either can happen from an edit or a
        /// merge as easily as from a file reached through #include.
        /// </para>
        /// <para>
        /// Sharing the comment with another key - a 'desc', a 'pdf' block - is flagged too:
        /// the Properties tab and the host's own save/restore round-trip both rewrite a
        /// comment in place by its position, so a comment that also carries other keys stops
        /// being something either side can predict the shape of after an edit.
        /// </para>
        /// <para>
        /// A comment reached through #include is skipped entirely: <c>CalcpadService</c>
        /// strips 'uiOverrides' out of included content before rendering regardless of where
        /// it sits, so it is dead weight rather than a mistake worth flagging.
        /// </para>
        /// </summary>
        private static void ValidateUiOverrides(HtmlCommentBlock block, JsonElement root, Stage3Context stage3, DirectiveJsonReporter reporter, ref bool sawUiOverrides)
        {
            if (!root.TryGetProperty("uiOverrides", out var overrides))
                return;

            if (stage3.IncludeMap.TryGetValue(block.StartLine, out var sourceInfo) && sourceInfo.Source == "include")
                return;

            if (HasKeyOtherThan(root, "uiOverrides"))
            {
                reporter.For(MixedUiOverridesCode, "metadata comment")
                    .Warn("'uiOverrides' should be the only key in its comment for predictable behavior");
            }

            var wasMisplacedOrDuplicate = false;
            if (block.StartLine != 0)
            {
                reporter.For(MisplacedUiOverridesCode, "metadata comment")
                    .Warn("'uiOverrides' has no effect unless its comment is on the first line of the file");
                wasMisplacedOrDuplicate = true;
            }

            if (sawUiOverrides)
            {
                reporter.For(DuplicateUiOverridesCode, "metadata comment")
                    .Warn("a second 'uiOverrides' comment is ignored - only the first one in the file is read");
                wasMisplacedOrDuplicate = true;
            }
            sawUiOverrides = true;

            if (wasMisplacedOrDuplicate)
                return;

            if (overrides.ValueKind != JsonValueKind.Object)
            {
                reporter.Warn("'uiOverrides' must be an object of control identities to values, e.g. {\"L:1\": \"8\"}");
                return;
            }

            foreach (var prop in overrides.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.String)
                    reporter.Warn($"'uiOverrides' value for '{prop.Name}' must be a string, e.g. \"8\"");
            }
        }

        private static bool HasKeyOtherThan(JsonElement root, string key)
        {
            foreach (var prop in root.EnumerateObject())
            {
                if (!prop.NameEquals(key))
                    return true;
            }
            return false;
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
