using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using Calcpad.Highlighter.Snippets;

namespace Calcpad.Highlighter.Linter.Constants
{
    public enum ControlBlockType
    {
        None,
        If,
        ElseIf,
        Else,
        EndIf,
        Repeat,
        For,
        While,
        Loop,
        Def,
        EndDef,
        Break,
        Continue
    }

    /// <summary>
    /// Provides access to built-in Calcpad language elements.
    /// Basic lookups (functions, keywords, units, etc.) are derived from SnippetRegistry.
    /// Semantic groupings (control blocks, standalone keywords, etc.) are hardcoded.
    /// </summary>
    public static class CalcpadBuiltIns
    {
        #region Derived from SnippetRegistry

        /// <summary>
        /// All built-in function names (case-insensitive).
        /// </summary>
        public static FrozenSet<string> Functions => SnippetRegistry.GetFunctionNames();

        /// <summary>
        /// All keyword names with # prefix (case-insensitive).
        /// </summary>
        public static FrozenSet<string> Keywords => SnippetRegistry.GetKeywordNames();

        /// <summary>
        /// All command names with $ prefix (case-insensitive).
        /// </summary>
        public static FrozenSet<string> Commands => SnippetRegistry.GetCommandNames();

        /// <summary>
        /// All unit names (case-sensitive).
        /// </summary>
        public static FrozenSet<string> Units => SnippetRegistry.GetUnitNames();

        /// <summary>
        /// All constant names (case-sensitive).
        /// </summary>
        public static FrozenSet<string> CommonConstants => SnippetRegistry.GetConstantNames();

        /// <summary>
        /// All operator characters.
        /// </summary>
        public static FrozenSet<char> Operators => SnippetRegistry.GetOperators();

        /// <summary>
        /// All control block keyword names with # prefix (case-insensitive).
        /// Includes: #if, #repeat, #for, #while, #def, #else, #else if
        /// </summary>
        public static FrozenSet<string> ControlBlockKeywords => SnippetRegistry.GetControlBlockKeywordNames();

        /// <summary>
        /// All end keyword names with # prefix (case-insensitive).
        /// Includes: #end if, #end def, #loop
        /// </summary>
        public static FrozenSet<string> EndKeywords => SnippetRegistry.GetEndKeywordNames();

        #endregion

        #region Derived Groupings

        private static FrozenSet<string> _validHashKeywords;
        private static FrozenSet<string> _controlBlockStarters;
        private static FrozenSet<string> _controlBlockEnders;
        private static FrozenSet<string> _commandsExcludingCommandBlocks;
        private static (string Name, string NameWithBrace)[] _commandsWithBrace;

        /// <summary>
        /// Valid hash keywords without the # prefix (case-insensitive).
        /// Derived from SnippetRegistry by removing # prefix from all keywords.
        /// </summary>
        public static FrozenSet<string> ValidHashKeywords
        {
            get
            {
                if (_validHashKeywords != null) return _validHashKeywords;

                // Combine all keyword types and remove # prefix
                var allKeywords = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

                // Add regular keywords
                foreach (var keyword in Keywords)
                {
                    var withoutHash = keyword.TrimStart('#');
                    if (!string.IsNullOrEmpty(withoutHash))
                        allKeywords.Add(withoutHash);
                }

                // Add control block keywords
                foreach (var keyword in ControlBlockKeywords)
                {
                    var withoutHash = keyword.TrimStart('#');
                    if (!string.IsNullOrEmpty(withoutHash))
                        allKeywords.Add(withoutHash);
                }

                // Add end keywords
                foreach (var keyword in EndKeywords)
                {
                    var withoutHash = keyword.TrimStart('#');
                    if (!string.IsNullOrEmpty(withoutHash))
                        allKeywords.Add(withoutHash);
                }

                _validHashKeywords = allKeywords.ToFrozenSet(System.StringComparer.OrdinalIgnoreCase);
                return _validHashKeywords;
            }
        }

        /// <summary>
        /// Keywords that start control blocks.
        /// Derived from SnippetRegistry ControlBlockKeywords, filtering for starters only.
        /// </summary>
        public static FrozenSet<string> ControlBlockStarters
        {
            get
            {
                if (_controlBlockStarters != null) return _controlBlockStarters;

                // Filter control block keywords to only starters (not #else or #else if)
                var starters = ControlBlockKeywords
                    .Where(k => !k.Equals("#else", System.StringComparison.OrdinalIgnoreCase) &&
                                !k.Equals("#else if", System.StringComparison.OrdinalIgnoreCase))
                    .ToHashSet(System.StringComparer.OrdinalIgnoreCase);

                _controlBlockStarters = starters.ToFrozenSet(System.StringComparer.OrdinalIgnoreCase);
                return _controlBlockStarters;
            }
        }

        /// <summary>
        /// Keywords that end control blocks.
        /// Derived from SnippetRegistry EndKeywords.
        /// </summary>
        public static FrozenSet<string> ControlBlockEnders
        {
            get
            {
                if (_controlBlockEnders != null) return _controlBlockEnders;
                _controlBlockEnders = EndKeywords;
                return _controlBlockEnders;
            }
        }

        /// <summary>
        /// All commands except command blocks ($While, $Block, $Inline).
        /// These commands use the @ separator pattern: $Command{expr @ var = start : end}
        /// </summary>
        public static FrozenSet<string> CommandsExcludingCommandBlocks
        {
            get
            {
                if (_commandsExcludingCommandBlocks != null) return _commandsExcludingCommandBlocks;

                var commands = Commands
                    .Where(c => !CommandBlocks.Contains(c))
                    .ToHashSet(System.StringComparer.OrdinalIgnoreCase);

                _commandsExcludingCommandBlocks = commands.ToFrozenSet(System.StringComparer.OrdinalIgnoreCase);
                return _commandsExcludingCommandBlocks;
            }
        }

        /// <summary>
        /// Same set as <see cref="CommandsExcludingCommandBlocks"/>, but as an array of
        /// (Name, NameWithBrace) pairs. NameWithBrace is the name + "{" precomputed,
        /// avoiding per-line string concatenation when searching for command tokens.
        /// </summary>
        public static (string Name, string NameWithBrace)[] CommandsWithBrace
        {
            get
            {
                if (_commandsWithBrace != null) return _commandsWithBrace;

                _commandsWithBrace = CommandsExcludingCommandBlocks
                    .Select(c => (c, c + "{"))
                    .ToArray();
                return _commandsWithBrace;
            }
        }

        #endregion

        #region Semantic Groupings (hardcoded)

        /// <summary>
        /// Comparison operators used in conditions (while loops, if statements, etc.)
        /// Note: &lt;= and &gt;= are NOT valid in Calcpad - must use ≤ and ≥
        /// </summary>
        public static readonly FrozenSet<char> ComparisonOperators = new HashSet<char>
        {
            '<', '>', '≤', '≥', '≡', '≠'
        }.ToFrozenSet();

        /// <summary>
        /// Keywords that are standalone (no content required after).
        /// </summary>
        public static readonly FrozenSet<string> StandaloneKeywords = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
        {
            "#else", "#end if", "#end def", "#loop", "#break", "#continue",
            "#local", "#global", "#pause", "#input",
            "#end hide", "#end show", "#end pre", "#end post",
            "#nosub", "#novar", "#varsub",
            "#end val", "#end equ", "#end noc",
            "#split", "#wrap", "#phasor", "#complex",
            "#rad", "#deg", "#gra",
            "#html", "#cpd", "#markdown"
        }.ToFrozenSet(System.StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Special variable names that configure the engine when assigned (plot dimensions,
        /// solver precision, etc.). Core reads them via GetSettingsVariable with a case-sensitive
        /// lookup, so assigning one has an effect even when it is never referenced afterwards.
        /// </summary>
        public static readonly FrozenSet<string> SettingsVariables = new HashSet<string>(System.StringComparer.Ordinal)
        {
            "PlotWidth", "PlotHeight", "PlotStep", "PlotSVG", "PlotAdaptive",
            "PlotSmooth", "PlotShadows", "PlotLightDir", "PlotPalette",
            "Precision", "Tol", "ReturnAngleUnits", "Units"
        }.ToFrozenSet(System.StringComparer.Ordinal);

        /// <summary>
        /// First words of multi-word keywords (else, end).
        /// </summary>
        public static readonly FrozenSet<string> MultiWordFirstWords = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
        {
            "else", "end", "md"
        }.ToFrozenSet(System.StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Command block names that do NOT use the @ separator syntax.
        /// All other commands use the @ separator pattern: $Command{expr @ var = start : end}
        /// </summary>
        public static readonly FrozenSet<string> CommandBlocks = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "$While", "$Block", "$Inline"
        }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

        #endregion

        #region Control Block Helpers

        // Keyword string to type mapping
        private static readonly FrozenDictionary<string, ControlBlockType> KeywordToType =
            new Dictionary<string, ControlBlockType>(StringComparer.OrdinalIgnoreCase)
            {
                ["#if"] = ControlBlockType.If,
                ["#else if"] = ControlBlockType.ElseIf,
                ["#else"] = ControlBlockType.Else,
                ["#end if"] = ControlBlockType.EndIf,
                ["#repeat"] = ControlBlockType.Repeat,
                ["#for"] = ControlBlockType.For,
                ["#while"] = ControlBlockType.While,
                ["#loop"] = ControlBlockType.Loop,
                ["#def"] = ControlBlockType.Def,
                ["#end def"] = ControlBlockType.EndDef,
                ["#break"] = ControlBlockType.Break,
                ["#continue"] = ControlBlockType.Continue
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

        // Reverse lookup from type to keyword
        private static readonly FrozenDictionary<ControlBlockType, string> TypeToKeyword =
            KeywordToType.ToFrozenDictionary(kvp => kvp.Value, kvp => kvp.Key);

        // Expected enders for block starters
        private static readonly FrozenDictionary<ControlBlockType, ControlBlockType> StarterToEnder =
            new Dictionary<ControlBlockType, ControlBlockType>
            {
                [ControlBlockType.If] = ControlBlockType.EndIf,
                [ControlBlockType.Repeat] = ControlBlockType.Loop,
                [ControlBlockType.For] = ControlBlockType.Loop,
                [ControlBlockType.While] = ControlBlockType.Loop,
                [ControlBlockType.Def] = ControlBlockType.EndDef
            }.ToFrozenDictionary();

        private static readonly FrozenSet<ControlBlockType> BlockStarterTypes = new HashSet<ControlBlockType>
        {
            ControlBlockType.If,
            ControlBlockType.Repeat,
            ControlBlockType.For,
            ControlBlockType.While,
            ControlBlockType.Def
        }.ToFrozenSet();

        private static readonly FrozenSet<ControlBlockType> LoopBlockStarterTypes = new HashSet<ControlBlockType>
        {
            ControlBlockType.Repeat,
            ControlBlockType.For,
            ControlBlockType.While
        }.ToFrozenSet();

        public static string NormalizeLine(string line) =>
            line.Trim().ToLowerInvariant();

        public static ControlBlockType GetBlockType(string line)
        {
            var normalized = NormalizeLine(line);

            // First try exact match
            if (KeywordToType.TryGetValue(normalized, out var exactType))
                return exactType;

            // Then try prefix match for keywords with expressions (e.g., "#if x > 0")
            foreach (var kvp in KeywordToType)
            {
                if (normalized.StartsWith(kvp.Key + " ", StringComparison.Ordinal))
                    return kvp.Value;
            }

            return ControlBlockType.None;
        }

        public static bool IsBlockStarter(ControlBlockType type) =>
            BlockStarterTypes.Contains(type);

        public static bool IsLoopBlockStarter(ControlBlockType type) =>
            LoopBlockStarterTypes.Contains(type);

        public static string GetKeywordString(ControlBlockType type) =>
            TypeToKeyword.TryGetValue(type, out var keyword) ? keyword : "";

        public static ControlBlockType GetExpectedEnderType(ControlBlockType starterType) =>
            StarterToEnder.TryGetValue(starterType, out var enderType) ? enderType : ControlBlockType.None;

        public static string GetExpectedEnderKeyword(ControlBlockType starterType) =>
            GetKeywordString(GetExpectedEnderType(starterType));

        public static bool MatchesEnder(ControlBlockType starterType, ControlBlockType enderType) =>
            StarterToEnder.TryGetValue(starterType, out var expected) && expected == enderType;

        public static void TrackControlBlocks(string line, Stack<(ControlBlockType type, int lineNumber)> stack, int lineNumber)
        {
            var blockType = GetBlockType(line);

            if (IsBlockStarter(blockType))
            {
                stack.Push((blockType, lineNumber));
            }
            else if (blockType == ControlBlockType.EndIf ||
                     blockType == ControlBlockType.Loop ||
                     blockType == ControlBlockType.EndDef)
            {
                if (stack.Count > 0 && MatchesEnder(stack.Peek().type, blockType))
                {
                    stack.Pop();
                }
            }
        }

        #endregion
    }
}
