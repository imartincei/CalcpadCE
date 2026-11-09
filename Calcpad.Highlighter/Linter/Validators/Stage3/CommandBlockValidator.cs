using System;
using Calcpad.Highlighter.Linter.Helpers;
using Calcpad.Highlighter.Linter.Models;

namespace Calcpad.Highlighter.Linter.Validators.Stage3
{
    /// <summary>Checks the bodies of functions that use command blocks ($Inline, $Block, $While).</summary>
    public class CommandBlockValidator
    {
        public void Validate(Stage3Context stage3, LinterResult result, TokenizedLineProvider tokenProvider)
        {
            if (stage3.CommandBlockFunctions.Count == 0)
                return;

            ValidateNoHashDirectivesInBlocks(stage3, result);
        }

        /// <summary>
        /// Validates that command block statements don't contain a # symbol — inside $Inline,
        /// $Block and $While blocks, # directives are not allowed. Use if(), $Repeat, etc. instead.
        /// </summary>
        private void ValidateNoHashDirectivesInBlocks(Stage3Context stage3, LinterResult result)
        {
            foreach (var kvp in stage3.CommandBlockFunctions)
            {
                var blockInfo = kvp.Value;

                // Check each statement in the block for # symbol
                foreach (var statement in blockInfo.Statements)
                {
                    var hashIdx = statement.AsSpan().IndexOf('#');
                    if (hashIdx >= 0)
                    {
                        // Find the position of this statement in the full line
                        var fullLine = blockInfo.FullLine;
                        var statementIdx = fullLine.AsSpan().IndexOf(statement.AsSpan(), StringComparison.Ordinal);
                        var col = statementIdx >= 0 ? statementIdx + hashIdx : hashIdx;

                        result.AddError(blockInfo.LineNumber, col, col + 1, "CPD-3405",
                            "'#' directives are not allowed inside command blocks. Use if(), $Repeat, etc. instead");
                    }
                }
            }
        }
    }
}
