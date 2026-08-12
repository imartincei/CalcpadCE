using System.Collections.Generic;
using Calcpad.Highlighter.ContentResolution;

namespace Calcpad.Highlighter.Tokenizer.Models
{
    /// <summary>
    /// Result of tokenizing Calcpad source code
    /// </summary>
    public class TokenizerResult
    {
        /// <summary>All tokens from the source, in order of appearance</summary>
        public List<Token> Tokens { get; } = new();

        /// <summary>Tokens grouped by line number for efficient line-based access</summary>
        public Dictionary<int, List<Token>> TokensByLine { get; } = new();

        /// <summary>Variables defined in the source (name -> line number)</summary>
        public Dictionary<string, int> DefinedVariables { get; } = new();

        /// <summary>Functions defined in the source (name -> line number). Case-sensitive to match Core.</summary>
        public Dictionary<string, int> DefinedFunctions { get; } = new(System.StringComparer.Ordinal);

        /// <summary>Subsequent = assignments to already-defined variables/functions (name, line, column)</summary>
        public List<(string Name, int Line, int Column)> VariableReassignments { get; } = new();

        /// <summary>All ← assignments - must target existing variables (name, line, column)</summary>
        public List<(string Name, int Line, int Column)> OuterScopeAssignments { get; } = new();

        // --- Lint mode only: rich definition metadata ---

        /// <summary>
        /// Full variable definitions with expressions (only populated in Lint mode).
        /// </summary>
        public List<VariableDefinition> VariableDefinitions { get; } = new();

        /// <summary>
        /// Full function definitions with params and expressions (only populated in Lint mode).
        /// </summary>
        public List<FunctionDefinition> FunctionDefinitions { get; } = new();

        /// <summary>
        /// Custom unit definitions (only populated in Lint mode).
        /// </summary>
        public List<CustomUnitDefinition> CustomUnitDefinitions { get; } = new();

        /// <summary>
        /// Functions that use command blocks - $Inline, $Block, $While (only populated in Lint mode).
        /// Key is function name, value is the command block info.
        /// </summary>
        public Dictionary<string, CommandBlockInfo> CommandBlockFunctions { get; } = new(System.StringComparer.Ordinal);

        // --- Macro mode only: macro definition metadata ---

        /// <summary>
        /// Full macro definitions with name, params, content, line numbers (only populated in Macro mode).
        /// </summary>
        public List<MacroDefinition> MacroDefinitions { get; } = new();

        /// <summary>
        /// Duplicate macro definitions detected during collection (only populated in Macro mode).
        /// </summary>
        public List<DuplicateMacro> DuplicateMacros { get; } = new();

        /// <summary>
        /// User-defined macro info for linting (only populated in Macro mode).
        /// Key: macro name (case-insensitive), Value: MacroInfo with param counts.
        /// Built directly during tokenization for single source of truth.
        /// </summary>
        public Dictionary<string, MacroInfo> UserDefinedMacros { get; } = new(System.StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Get all tokens for a specific line
        /// </summary>
        public List<Token> GetTokensForLine(int line)
        {
            return TokensByLine.TryGetValue(line, out var tokens) ? tokens : new List<Token>();
        }

        internal void AddToken(Token token)
        {
            Tokens.Add(token);

            if (!TokensByLine.TryGetValue(token.Line, out var lineTokens))
            {
                lineTokens = new List<Token>();
                TokensByLine[token.Line] = lineTokens;
            }
            lineTokens.Add(token);
        }

        internal void AddVariableDefinition(string name, int line)
        {
            // Only track first definition
            if (!DefinedVariables.ContainsKey(name))
            {
                DefinedVariables[name] = line;
            }
        }

        internal void AddFunctionDefinition(string name, int line)
        {
            // Only track first definition
            if (!DefinedFunctions.ContainsKey(name))
            {
                DefinedFunctions[name] = line;
            }
        }
    }
}
