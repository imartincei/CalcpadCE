using System;
using System.Collections.Generic;
using Calcpad.Highlighter.ContentResolution;
using Calcpad.Highlighter.Linter.Helpers;

namespace Calcpad.Highlighter.Linter.Models
{
    public class Stage3Context : StageContext
    {
        // Fully expanded code (includes processed, macros expanded)
        public Dictionary<int, int> Stage3ToStage2Map { get; set; } = new();

        /// <summary>Whether each Stage3 line came from the local file or an #include, keyed by Stage3 line index.</summary>
        public Dictionary<int, SourceInfo> IncludeMap { get; set; } = new();
        public HashSet<string> DefinedVariables { get; set; } = new();
        public Dictionary<string, FunctionInfo> DefinedFunctions { get; set; } = new(StringComparer.Ordinal);
        public Dictionary<string, MacroInfo> DefinedMacros { get; set; } = new(StringComparer.OrdinalIgnoreCase); // name -> macro info
        public HashSet<string> CustomUnits { get; set; } = new();

        /// <summary>
        /// Stage3 lines produced by macro expansion, keyed by Stage3 line index.
        /// Columns on these lines refer to the expanded text, which has no correspondence
        /// to the original call site, so diagnostics on them are widened to the whole line.
        /// </summary>
        public Dictionary<int, MacroExpansionInfo> MacroExpansions { get; set; } = new();

        /// <summary>
        /// Type tracker with full type information for all definitions
        /// </summary>
        public TypeTracker TypeTracker { get; set; } = new();

        /// <summary>
        /// Functions that use command blocks ($Inline, $Block, $While).
        /// Key is function name, value is the command block info.
        /// </summary>
        public Dictionary<string, CommandBlockInfo> CommandBlockFunctions { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Subsequent = assignments to already-defined variables/functions (name, line, column)
        /// </summary>
        public List<(string Name, int Line, int Column)> VariableReassignments { get; set; } = new();

        /// <summary>
        /// All ← assignments - must target existing variables (name, line, column)
        /// </summary>
        public List<(string Name, int Line, int Column)> OuterScopeAssignments { get; set; } = new();

        /// <summary>
        /// All variable assignment positions (definitions and reassignments) from Lint-mode tokenization.
        /// Each entry is (Name, Line, Column, Length) identifying where a variable is assigned.
        /// </summary>
        public List<(string Name, int Line, int Column, int Length)> VariableAssignments { get; set; } = new();

        /// <summary>
        /// All variable usage positions (non-assignment Variable tokens) from Lint-mode tokenization.
        /// Each entry is (Name, Line, Column) identifying where a variable is read.
        /// </summary>
        public List<(string Name, int Line, int Column)> VariableUsages { get; set; } = new();
    }
}
