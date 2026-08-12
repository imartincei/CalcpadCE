using System;
using System.Collections.Generic;
using Calcpad.Highlighter.ContentResolution;
using Calcpad.Highlighter.Linter.Models;
using Calcpad.Highlighter.Tokenizer.Models;

namespace Calcpad.Highlighter.Tokenizer
{
    /// <summary>
    /// Macro collection mode: captures full MacroDefinition objects during tokenization.
    /// Called when TokenizerMode is Macro (Stage 2 of content resolution).
    /// Follows the same hook pattern as CalcpadTokenizer.Definitions.cs (Lint mode).
    /// </summary>
    public partial class CalcpadTokenizer
    {
        // Macro collection state
        private string _macroCurrName;
        private List<string> _macroCurrParams;
        private int _macroCurrStartLine;
        private bool _macroCurrIsInline;
        private string _macroCurrInlineContent;
        private List<string> _macroCurrContentLines;
        private bool _macroCurrCollectingBody;
        private Dictionary<string, int> _macroFirstDefinitionLines;

        // Pending metadata from a '<!--{...}--> comment on the preceding line
        private DefinitionMetadata _macroPendingMetadata;

        private void InitMacroCollectionState()
        {
            _macroCurrName = null;
            _macroCurrParams = null;
            _macroCurrStartLine = -1;
            _macroCurrIsInline = false;
            _macroCurrInlineContent = null;
            _macroCurrContentLines = new List<string>();
            _macroCurrCollectingBody = false;
            _macroFirstDefinitionLines = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            _macroPendingMetadata = null;
        }

        /// <summary>
        /// Called from TrackDefinitions for each token in Macro mode.
        /// Captures macro name when inside a #def line, and detects inline content via '='.
        /// </summary>
        private void TrackDefinitionsMacro(TokenType type, string text)
        {
            // Don't process tokens from body lines — those are accumulated as raw text
            if (_macroCurrCollectingBody)
                return;

            switch (type)
            {
                case TokenType.Macro:
                    // Inside a #def line, capture the macro name.
                    // Use _state.IsMacro (not HasMacro) because Append() is called BEFORE
                    // HasMacro is set in ParseMacro(). IsMacro is still true at this point.
                    if (_state.IsMacro && _macroCurrName == null)
                    {
                        _macroCurrName = text;
                        _macroCurrStartLine = _state.Line;
                    }
                    break;
            }

            // Note: inline '=' detection and body storage happens in ParseMacroContent (CalcpadTokenizer.Macros.cs).
        }

        /// <summary>
        /// Called at end of each line in Macro mode.
        /// Handles multiline macro body accumulation and macro definition emission.
        /// </summary>
        private void FinalizeLineMacroCollection()
        {
            // Handle multiline macro body collection
            if (_macroCurrCollectingBody)
            {
                ReadOnlySpan<char> trimmedSpan = _state.Text.Span.Trim();
                if (trimmedSpan.Equals("#end def", StringComparison.OrdinalIgnoreCase))
                {
                    EmitMacroDefinition(isMultiline: true);
                    _macroCurrCollectingBody = false;
                    _macroCurrName = null;
                    _macroCurrParams = null;
                    _macroCurrContentLines = new List<string>();
                }
                else
                {
                    // Accumulate body line (string materialization required — stored across lines)
                    _macroCurrContentLines.Add(_state.Text.ToString());
                }
                return;
            }

            bool emittedMacro = false;

            // If we saw an inline macro this line (#def name$(params) = content), emit it
            if (_macroCurrIsInline && _macroCurrName != null)
            {
                EmitMacroDefinition(isMultiline: false);
                _macroCurrIsInline = false;
                _macroCurrName = null;
                _macroCurrParams = null;
                _macroCurrInlineContent = null;
                emittedMacro = true;
            }

            // If we saw #def this line but no '=' (multiline start), begin body collection
            if (!emittedMacro && _state.HasMacro && _macroCurrName != null && !_macroCurrIsInline)
            {
                _macroCurrParams = ExtractMacroParams(_state.Text.Span);
                _macroCurrCollectingBody = true;
                // Don't clear metadata — it will be consumed when the macro is emitted
                return;
            }

            // Metadata comment detection for next definition
            if (emittedMacro)
            {
                _macroPendingMetadata = null;
            }
            else
            {
                var textSpan = _state.Text.Span;
                if (DefinitionMetadata.TryParse(textSpan, out var metadata))
                    _macroPendingMetadata = metadata;
                else if (!textSpan.IsWhiteSpace())
                    _macroPendingMetadata = null; // Non-blank, non-metadata line clears pending
            }
        }

        /// <summary>
        /// Creates a MacroDefinition and adds it to the result.
        /// Tracks duplicate macro definitions.
        /// </summary>
        private void EmitMacroDefinition(bool isMultiline)
        {
            var sourceInfo = GetSourceInfo(_macroCurrStartLine);

            var macroDef = new ContentResolution.MacroDefinition
            {
                Name = _macroCurrName,
                Params = _macroCurrParams ?? new List<string>(),
                Content = isMultiline
                    ? new List<string>(_macroCurrContentLines)
                    : new List<string> { _macroCurrInlineContent ?? string.Empty },
                LineNumber = _macroCurrStartLine,
                Source = sourceInfo.Source,
                SourceFile = sourceInfo.SourceFile,
                Description = _macroPendingMetadata?.Description,
                ParamTypes = _macroPendingMetadata?.ParamTypes,
                ParamDescriptions = _macroPendingMetadata?.ParamDescriptions
            };

            // Track duplicates
            if (_macroFirstDefinitionLines.TryGetValue(_macroCurrName, out var origLine))
            {
                _result.DuplicateMacros.Add(new DuplicateMacro
                {
                    Name = _macroCurrName,
                    DuplicateLineNumber = _macroCurrStartLine,
                    OriginalLineNumber = origLine
                });
            }
            else
            {
                _macroFirstDefinitionLines[_macroCurrName] = _macroCurrStartLine;
            }

            _result.MacroDefinitions.Add(macroDef);

            // Build MacroInfo directly from the parsed params (single source of truth)
            if (!_result.UserDefinedMacros.ContainsKey(_macroCurrName))
            {
                var paramList = _macroCurrParams ?? new List<string>();
                _result.UserDefinedMacros[_macroCurrName] = new ContentResolution.MacroInfo
                {
                    LineNumber = _macroCurrStartLine,
                    ParamCount = paramList.Count,
                    ParamNames = paramList
                };
            }

            _macroPendingMetadata = null;
        }
    }
}
