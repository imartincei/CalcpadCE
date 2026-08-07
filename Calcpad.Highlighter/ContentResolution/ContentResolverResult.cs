using System;
using System.Collections.Generic;
using Calcpad.Core;
using Calcpad.Highlighter.Linter.Helpers;

namespace Calcpad.Highlighter.ContentResolution
{
    public class MacroDefinition
    {
        public string Name { get; set; }
        public List<string> Params { get; set; }
        public List<string> Content { get; set; }
        public int LineNumber { get; set; }
        public string Source { get; set; } // "local" | "include"
        public string SourceFile { get; set; }

        /// <summary>
        /// Parameters that are used in comments (directly or transitively through nested macro calls).
        /// These parameters should have their call-site arguments tokenized as Comment.
        /// </summary>
        public HashSet<string> CommentParameters { get; set; } = new(StringComparer.Ordinal);

        /// <summary>User-provided description from a metadata comment on the preceding line.</summary>
        public string Description { get; set; }

        /// <summary>User-provided type hints per parameter (e.g., "vector", "scalar").</summary>
        public List<string> ParamTypes { get; set; }

        /// <summary>User-provided descriptions per parameter.</summary>
        public List<string> ParamDescriptions { get; set; }
    }

    public class FunctionDefinition
    {
        public string Name { get; set; }
        public List<string> Params { get; set; }
        public int LineNumber { get; set; }
        public string Source { get; set; }
        public string SourceFile { get; set; }

        /// <summary>
        /// Whether this function was defined with #const (readonly)
        /// </summary>
        public bool IsConst { get; set; }

        /// <summary>
        /// The expression body of the function (right side of =).
        /// For command block functions, this is the full command block content.
        /// </summary>
        public string Expression { get; set; }

        /// <summary>
        /// If this function uses a command block ($Inline, $Block, $While),
        /// this contains the block content for validation at call sites.
        /// </summary>
        public CommandBlockInfo CommandBlock { get; set; }

        /// <summary>User-provided description from a metadata comment on the preceding line.</summary>
        public string Description { get; set; }

        /// <summary>User-provided type hints per parameter (e.g., "vector", "scalar").</summary>
        public List<string> ParamTypes { get; set; }

        /// <summary>User-provided descriptions per parameter.</summary>
        public List<string> ParamDescriptions { get; set; }

        /// <summary>User-declared return type from a metadata comment (value/vector/matrix/any).</summary>
        public string ReturnType { get; set; }
    }

    /// <summary>
    /// Information about a command block ($Inline, $Block, $While) in a function definition.
    /// Used to validate the block content when the function is called.
    /// </summary>
    public class CommandBlockInfo
    {
        /// <summary>The name of the function containing this command block</summary>
        public string FunctionName { get; set; }

        /// <summary>The parameter names of the function</summary>
        public List<string> Parameters { get; set; } = new();

        /// <summary>The type of command block (Inline, Block, or While)</summary>
        public string BlockType { get; set; }

        /// <summary>
        /// The statements inside the command block, split by ; (ignoring ; inside parentheses).
        /// Each statement is treated as a separate line for linting purposes.
        /// </summary>
        public List<string> Statements { get; set; } = new();

        /// <summary>The full line containing the function definition</summary>
        public string FullLine { get; set; }

        /// <summary>Zero-based line number where the command block function is defined</summary>
        public int LineNumber { get; set; }
    }

    public class VariableDefinition
    {
        public string Name { get; set; }
        public string Definition { get; set; }
        public int LineNumber { get; set; }
        public string Source { get; set; }
        public string SourceFile { get; set; }

        /// <summary>
        /// Whether this variable was defined with #const (readonly)
        /// </summary>
        public bool IsConst { get; set; }

        /// <summary>User-provided description from a metadata comment on the preceding line.</summary>
        public string Description { get; set; }
    }

    public class CustomUnitDefinition
    {
        public string Name { get; set; }
        public string Definition { get; set; }
        public int LineNumber { get; set; }
        public string Source { get; set; }
        public string SourceFile { get; set; }

        /// <summary>User-provided description from a metadata comment on the preceding line.</summary>
        public string Description { get; set; }
    }

    public class DuplicateMacro
    {
        public string Name { get; set; }
        public int DuplicateLineNumber { get; set; }
        public int OriginalLineNumber { get; set; }
    }

    public class MacroInfo
    {
        public int LineNumber { get; set; }
        public int ParamCount { get; set; }
        public List<string> ParamNames { get; set; } = new();
    }

    public class FunctionInfo
    {
        public int LineNumber { get; set; }
        public int ParamCount { get; set; }
        public List<string> ParamNames { get; set; } = new();
    }

    /// <summary>
    /// Tracks how a macro expansion maps back to the original call site.
    /// Used by the linter to trace errors inside expanded macro content back to
    /// the macro call that produced them.
    /// </summary>
    public class MacroExpansionInfo
    {
        /// <summary>The macro names that were expanded on this line (outermost first)</summary>
        public List<string> MacroNames { get; set; } = new();

        /// <summary>The original line text before macro expansion (the call site)</summary>
        public string CallSiteLine { get; set; }

        /// <summary>The stage2 line index of the call site (for mapping back through the pipeline)</summary>
        public int CallSiteStage2Line { get; set; }

        /// <summary>Which line within the expanded content this output line corresponds to (0-based)</summary>
        public int ContentLineIndex { get; set; }

        /// <summary>Total number of lines in the expanded result for this call site</summary>
        public int TotalContentLines { get; set; }
    }

    public class SourceInfo
    {
        public string Source { get; set; } // "local" | "include"
        public string SourceFile { get; set; }

        /// <summary>
        /// For included files: the original line number (0-based) within the included file.
        /// Used by Go To Definition to navigate to the correct line in the included file.
        /// -1 means not applicable (local source).
        /// </summary>
        public int OriginalLine { get; set; } = -1;
    }

    /// <summary>
    /// A single occurrence of a symbol (variable, function, macro) in the source code.
    /// Positions are mapped back to the original source lines (pre-pipeline).
    /// </summary>
    public class SymbolLocation
    {
        /// <summary>Original source line (0-based, mapped back through all pipeline stages)</summary>
        public int Line { get; set; }

        /// <summary>Column in the source line (0-based)</summary>
        public int Column { get; set; }

        /// <summary>Token length in characters</summary>
        public int Length { get; set; }

        /// <summary>"local" or "include"</summary>
        public string Source { get; set; }

        /// <summary>File path if from an #include, null otherwise</summary>
        public string SourceFile { get; set; }

        /// <summary>True for definitions and reassignments, false for read-only usages</summary>
        public bool IsAssignment { get; set; }
    }

    public class ResolvedContent
    {
        public List<string> ExpandedLines { get; set; }
        public Dictionary<int, int> SourceMap { get; set; }
        public Dictionary<int, MacroExpansionInfo> MacroExpansions { get; set; }
        public Dictionary<int, List<int>> LineContinuationMap { get; set; }
        public Dictionary<string, FunctionInfo> UserDefinedFunctions { get; set; }
        public List<FunctionDefinition> FunctionsWithParams { get; set; }
        public Dictionary<string, MacroInfo> UserDefinedMacros { get; set; }
        public HashSet<string> DefinedVariables { get; set; }
        public List<VariableDefinition> VariablesWithDefinitions { get; set; }
        public List<CustomUnitDefinition> CustomUnits { get; set; }
        public List<MacroDefinition> AllMacros { get; set; }
        public List<DuplicateMacro> DuplicateMacros { get; set; }
    }

    /// <summary>
    /// Represents a segment from a line continuation merge.
    /// When lines are merged via continuation (_), this tracks where each original line's content
    /// starts in the merged result.
    /// </summary>
    public class LineContinuationSegment
    {
        /// <summary>The original line number (0-based)</summary>
        public int OriginalLine { get; set; }

        /// <summary>Starting column in the merged line where this segment begins</summary>
        public int StartColumn { get; set; }

        /// <summary>Length of content from this original line (excluding the continuation character)</summary>
        public int Length { get; set; }
    }

    public class Stage1Result
    {
        public List<string> Lines { get; set; }
        public Dictionary<int, int> SourceMap { get; set; }
        public Dictionary<int, List<int>> LineContinuationMap { get; set; }

        /// <summary>
        /// Maps Stage1 line index to segment information for line continuation merges.
        /// Each entry contains the list of segments showing where each original line's content
        /// starts and ends within the merged line.
        /// </summary>
        public Dictionary<int, List<LineContinuationSegment>> LineContinuationSegments { get; set; }
    }

    public class Stage2Result
    {
        public List<string> Lines { get; set; }
        public Dictionary<int, int> SourceMap { get; set; }
        public Dictionary<int, SourceInfo> IncludeMap { get; set; }
        public List<MacroDefinition> MacroDefinitions { get; set; }
        public List<DuplicateMacro> DuplicateMacros { get; set; }

        /// <summary>
        /// The `#ProjectPath`/`#LibraryPath` roots declared anywhere in the root file or its
        /// `#include` chain, in document order — the same instance <see cref="ContentResolver"/>
        /// builds while expanding includes.
        /// </summary>
        public PathRoots PathRoots { get; set; }

        /// <summary>
        /// Maps macro name to its comment parameters (parameters used in comments, directly or transitively).
        /// Computed after all macros are collected, with transitive closure for nested macro calls.
        /// Key: macro name (case-insensitive), Value: set of parameter names that are comment parameters.
        /// </summary>
        public Dictionary<string, HashSet<string>> MacroCommentParameters { get; set; } = new(System.StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Maps macro name to ordered list of parameter names.
        /// Used for matching call-site argument positions to parameter names.
        /// </summary>
        public Dictionary<string, List<string>> MacroParameterOrder { get; set; } = new(System.StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Maps macro name to inline body text (everything after = in #def for inline macros,
        /// or joined content lines for multiline macros).
        /// Used for argument type resolution via substitution + tokenization.
        /// </summary>
        public Dictionary<string, string> MacroBodies { get; set; } = new(System.StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// User-defined macro info for linting, built by the tokenizer during Macro mode.
        /// Key: macro name (case-insensitive), Value: MacroInfo with param counts.
        /// </summary>
        public Dictionary<string, MacroInfo> UserDefinedMacros { get; set; } = new(System.StringComparer.OrdinalIgnoreCase);
    }

    public class Stage3Result
    {
        public List<string> Lines { get; set; }
        public Dictionary<int, int> SourceMap { get; set; }
        public Dictionary<int, MacroExpansionInfo> MacroExpansions { get; set; }
        public Dictionary<string, FunctionInfo> UserDefinedFunctions { get; set; }
        public List<FunctionDefinition> FunctionsWithParams { get; set; }
        public Dictionary<string, MacroInfo> UserDefinedMacros { get; set; }
        public HashSet<string> DefinedVariables { get; set; }
        public List<VariableDefinition> VariablesWithDefinitions { get; set; }
        public List<CustomUnitDefinition> CustomUnits { get; set; }

        /// <summary>
        /// Type tracker with full type information for all definitions
        /// </summary>
        public TypeTracker TypeTracker { get; set; } = new();

        /// <summary>
        /// Functions that use command blocks ($Inline, $Block, $While).
        /// Key is function name, value is the command block info.
        /// </summary>
        public Dictionary<string, CommandBlockInfo> CommandBlockFunctions { get; set; } = new(System.StringComparer.Ordinal);

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
        /// Used by the linter for unused variable detection.
        /// </summary>
        public List<(string Name, int Line, int Column, int Length)> VariableAssignments { get; set; } = new();

        /// <summary>
        /// All variable usage positions (non-assignment Variable tokens) from Lint-mode tokenization.
        /// Each entry is (Name, Line, Column) identifying where a variable is read.
        /// Used by the linter for unused variable detection.
        /// </summary>
        public List<(string Name, int Line, int Column)> VariableUsages { get; set; } = new();

        /// <summary>
        /// Index of all variable occurrences (assignments and usages) grouped by name.
        /// Positions are mapped to original source lines with include file info.
        /// Used for go-to-definition and find-all-occurrences features.
        /// </summary>
        public Dictionary<string, List<SymbolLocation>> VariableIndex { get; set; } = new(System.StringComparer.Ordinal);

        /// <summary>
        /// Index of all user-defined function occurrences (definitions and calls) grouped by name.
        /// Positions are mapped to original source lines with include file info.
        /// Used for go-to-definition and find-all-occurrences features.
        /// </summary>
        public Dictionary<string, List<SymbolLocation>> FunctionIndex { get; set; } = new(System.StringComparer.Ordinal);

        /// <summary>
        /// Index of all macro occurrences (definitions and call sites) grouped by name.
        /// Definitions come from Stage 2 macro collection; call sites from macro expansion tracking.
        /// Positions are mapped to original source lines with include file info.
        /// Used for go-to-definition and find-all-occurrences features.
        /// </summary>
        public Dictionary<string, List<SymbolLocation>> MacroIndex { get; set; } = new(System.StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Maps macro name to its comment parameters (parameters used in comments, directly or transitively).
        /// Key: macro name (case-insensitive), Value: set of parameter names that are comment parameters.
        /// </summary>
        public Dictionary<string, HashSet<string>> MacroCommentParameters { get; set; } = new(System.StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Maps macro name to ordered list of parameter names.
        /// Used for matching call-site argument positions to parameter names.
        /// </summary>
        public Dictionary<string, List<string>> MacroParameterOrder { get; set; } = new(System.StringComparer.OrdinalIgnoreCase);
    }

    public class StagedResolvedContent
    {
        public Stage1Result Stage1 { get; set; }
        public Stage2Result Stage2 { get; set; }
        public Stage3Result Stage3 { get; set; }
    }
}
