using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Calcpad.Highlighter.Tokenizer.Models;

namespace Calcpad.Highlighter.Linter.Models
{
    /// <summary>
    /// Metadata parsed from a JSON comment on the line preceding a definition.
    /// Syntax: '<!--{"desc":"...","paramTypes":["vector","scalar"],"paramDesc":["desc1","desc2"]}-->
    /// </summary>
    public class DefinitionMetadata
    {
        public string Description { get; set; }
        public List<string> ParamTypes { get; set; }
        public List<string> ParamDescriptions { get; set; }

        /// <summary>User-declared return type for custom functions (value/vector/matrix/any).</summary>
        public string ReturnType { get; set; }

        /// <summary>Valid paramType and returnType values for custom functions (f(x;y) = ...)</summary>
        public static readonly HashSet<string> ValidFunctionParamTypes = new(StringComparer.OrdinalIgnoreCase)
            { "value", "vector", "matrix", "any" };

        /// <summary>Valid paramType values for macros (#def) — all TokenType enum names</summary>
        public static readonly HashSet<string> ValidMacroParamTypes =
            new(Enum.GetNames(typeof(TokenType)), StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Maps a metadata returnType string to a <see cref="CalcpadType"/>, or null when
        /// the value isn't a recognized function return type. "any" maps to Various.
        /// </summary>
        public static CalcpadType? ParseFunctionReturnType(string value)
        {
            if (string.IsNullOrEmpty(value))
                return null;
            return value.ToLowerInvariant() switch
            {
                "value" => CalcpadType.Value,
                "vector" => CalcpadType.Vector,
                "matrix" => CalcpadType.Matrix,
                "any" => CalcpadType.Various,
                _ => null
            };
        }

        /// <summary>
        /// Attempts to parse a metadata comment from a line of text.
        /// Looks for the pattern '<!--{...}--> (Calcpad comment containing HTML-comment-wrapped JSON).
        /// Returns true if a valid metadata comment was found and parsed.
        /// Malformed JSON is silently ignored (returns false).
        /// </summary>
        public static bool TryParse(ReadOnlySpan<char> line, out DefinitionMetadata metadata)
        {
            metadata = null;
            if (line.IsEmpty)
                return false;

            var span = line;

            // Find the comment start (first ' or ")
            int commentStart = -1;
            for (int i = 0; i < span.Length; i++)
            {
                if (span[i] == '\'' || span[i] == '"')
                {
                    commentStart = i;
                    break;
                }
            }
            if (commentStart < 0)
                return false;

            // Look for <!-- after the comment delimiter
            var afterComment = span[(commentStart + 1)..];
            int markerStart = afterComment.IndexOf("<!--".AsSpan());
            if (markerStart < 0)
                return false;

            int jsonStart = markerStart + 4; // skip "<!--"
            if (jsonStart >= afterComment.Length || afterComment[jsonStart] != '{')
                return false;

            // Find closing -->
            int markerEnd = afterComment.IndexOf("-->".AsSpan());
            if (markerEnd < 0 || markerEnd <= jsonStart)
                return false;

            // Extract JSON between <!-- and -->
            var jsonSpan = afterComment[jsonStart..markerEnd];
            if (jsonSpan.IsEmpty)
                return false;

            try
            {
                using var doc = JsonDocument.Parse(jsonSpan.ToString());
                var root = doc.RootElement;

                var result = new DefinitionMetadata();
                bool hasAny = false;

                if (root.TryGetProperty("desc", out var descProp) && descProp.ValueKind == JsonValueKind.String)
                {
                    result.Description = descProp.GetString();
                    hasAny = true;
                }

                if (root.TryGetProperty("paramTypes", out var typesProp) && typesProp.ValueKind == JsonValueKind.Array)
                {
                    result.ParamTypes = new List<string>();
                    foreach (var item in typesProp.EnumerateArray())
                        result.ParamTypes.Add(item.GetString() ?? string.Empty);
                    hasAny = true;
                }

                if (root.TryGetProperty("paramDesc", out var descsProp) && descsProp.ValueKind == JsonValueKind.Array)
                {
                    result.ParamDescriptions = new List<string>();
                    foreach (var item in descsProp.EnumerateArray())
                        result.ParamDescriptions.Add(item.GetString() ?? string.Empty);
                    hasAny = true;
                }

                if (root.TryGetProperty("returnType", out var returnProp) && returnProp.ValueKind == JsonValueKind.String)
                {
                    result.ReturnType = returnProp.GetString();
                    hasAny = true;
                }

                if (hasAny)
                {
                    metadata = result;
                    return true;
                }
            }
            catch (JsonException)
            {
                // Malformed JSON — silently ignore
            }

            return false;
        }

        /// <summary>
        /// Extracts JSON string from an HtmlComment token text.
        /// Strips comment quotes (' or ") and HTML comment markers (&lt;-- and --&gt;).
        /// Returns null if the token doesn't contain valid JSON markers.
        /// </summary>
        public static string ExtractJsonFromComment(string tokenText)
        {
            if (string.IsNullOrEmpty(tokenText))
                return null;

            var text = tokenText.AsSpan();

            // Strip leading comment quote
            if (text.Length > 0 && (text[0] == '\'' || text[0] == '"'))
                text = text[1..];

            // Strip trailing comment quote
            if (text.Length > 0 && (text[^1] == '\'' || text[^1] == '"'))
                text = text[..^1];

            // Find <!-- marker
            int openIdx = text.IndexOf("<!--".AsSpan());
            if (openIdx < 0) return null;

            var afterOpen = text[(openIdx + 4)..];
            if (afterOpen.IsEmpty || afterOpen[0] != '{')
                return null;

            // Find --> marker
            int closeIdx = afterOpen.IndexOf("-->".AsSpan());
            if (closeIdx < 0) return null;

            var json = afterOpen[..closeIdx];
            return json.IsEmpty ? null : json.ToString();
        }
    }
}
