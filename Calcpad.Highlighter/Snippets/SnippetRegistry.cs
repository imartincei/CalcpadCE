#nullable enable

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using Calcpad.Highlighter.Linter.Models;
using Calcpad.Highlighter.Snippets.Data;
using Calcpad.Highlighter.Snippets.Models;

namespace Calcpad.Highlighter.Snippets
{
    /// <summary>
    /// Central registry that aggregates all snippet definitions from category-specific classes.
    /// Provides read-only access to the complete collection of snippets for the frontend,
    /// as well as cached lookups for use by CalcpadBuiltIns and FunctionSignatures.
    /// </summary>
    public static class SnippetRegistry
    {
        /// <summary>
        /// All available snippets combined from all categories.
        /// </summary>
        public static readonly FrozenSet<SnippetItem> AllSnippets;

        /// <summary>
        /// Snippets grouped by their top-level category.
        /// </summary>
        public static readonly FrozenDictionary<string, SnippetItem[]> ByCategory;

        // Cached lookups for CalcpadBuiltIns - lazily initialized
        private static FrozenSet<string>? _functionNames;
        private static FrozenSet<string>? _keywordNames;
        private static FrozenSet<string>? _commandNames;
        private static FrozenSet<string>? _unitNames;
        private static FrozenSet<string>? _constantNames;
        private static FrozenSet<string>? _settingNames;
        private static FrozenSet<string>? _controlBlockKeywordNames;
        private static FrozenSet<string>? _endKeywordNames;
        private static FrozenSet<char>? _operators;
        private static FrozenDictionary<string, SnippetItem>? _functionSnippets;
        private static FrozenDictionary<string, SnippetItem[]>? _functionOverloads;
        private static FrozenSet<string>? _vectorReturningFunctions;
        private static FrozenSet<string>? _matrixReturningFunctions;

        static SnippetRegistry()
        {
            // Combine all snippet arrays
            var allItems = new List<SnippetItem>();
            allItems.AddRange(ConstantSnippets.Items);
            allItems.AddRange(OperatorSnippets.Items);
            allItems.AddRange(FunctionSnippets.Items);
            allItems.AddRange(VectorFunctionSnippets.Items);
            allItems.AddRange(MatrixFunctionSnippets.Items);
            allItems.AddRange(KeywordSnippets.Items);
            allItems.AddRange(CommandSnippets.Items);
            allItems.AddRange(SettingSnippets.Items);
            allItems.AddRange(UnitSnippets.Items);
            allItems.AddRange(FormatStringSnippets.Items);
            allItems.AddRange(HtmlSnippets.Items);
            allItems.AddRange(MarkdownSnippets.Items);
            allItems.AddRange(SymbolSnippets.Items);

            AllSnippets = allItems.ToFrozenSet();

            // Group by top-level category
            ByCategory = allItems
                .GroupBy(s => GetTopLevelCategory(s.Category))
                .ToFrozenDictionary(g => g.Key, g => g.ToArray());
        }

        /// <summary>
        /// Gets the top-level category from a hierarchical category path.
        /// </summary>
        private static string GetTopLevelCategory(string category)
        {
            var slashIndex = category.IndexOf('/');
            return slashIndex >= 0 ? category[..slashIndex] : category;
        }

        /// <summary>
        /// Gets all snippets as an array for serialization. Excludes InternalOnly
        /// snippets, which exist for the linter's built-in sets only.
        /// </summary>
        public static SnippetItem[] GetAllSnippetsArray() =>
            AllSnippets.Where(s => !s.InternalOnly).ToArray();

        /// <summary>
        /// Gets snippets filtered by category prefix. Excludes InternalOnly snippets.
        /// </summary>
        public static SnippetItem[] GetSnippetsByCategory(string categoryPrefix)
        {
            return AllSnippets
                .Where(s => !s.InternalOnly && s.Category.StartsWith(categoryPrefix, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        #region Cached Lookups for CalcpadBuiltIns

        /// <summary>
        /// Gets all function names (cached). Used by CalcpadBuiltIns.
        /// Includes only snippets with KeywordType = "Function".
        /// </summary>
        public static FrozenSet<string> GetFunctionNames()
        {
            if (_functionNames != null) return _functionNames;

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Extract from function snippet files directly
            foreach (var snippet in FunctionSnippets.Items)
            {
                var name = ExtractFunctionName(snippet.Insert);
                if (!string.IsNullOrEmpty(name))
                    names.Add(name);
            }
            foreach (var snippet in VectorFunctionSnippets.Items)
            {
                var name = ExtractFunctionName(snippet.Insert);
                if (!string.IsNullOrEmpty(name))
                    names.Add(name);
            }
            foreach (var snippet in MatrixFunctionSnippets.Items)
            {
                var name = ExtractFunctionName(snippet.Insert);
                if (!string.IsNullOrEmpty(name))
                    names.Add(name);
            }

            _functionNames = names.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
            return _functionNames;
        }

        /// <summary>
        /// Gets all keyword names with # prefix (cached). Used by CalcpadBuiltIns.
        /// Includes only snippets with KeywordType = "Keyword".
        /// </summary>
        public static FrozenSet<string> GetKeywordNames()
        {
            if (_keywordNames != null) return _keywordNames;

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Extract from KeywordSnippets directly (only file with KeywordType = "Keyword")
            foreach (var snippet in KeywordSnippets.Items)
            {
                if (string.Equals(snippet.KeywordType, "Keyword", StringComparison.OrdinalIgnoreCase))
                {
                    var name = ExtractKeywordName(snippet.Insert);
                    if (!string.IsNullOrEmpty(name))
                        names.Add(name);
                }
            }

            _keywordNames = names.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
            return _keywordNames;
        }

        /// <summary>
        /// Gets all command names with $ prefix (cached). Used by CalcpadBuiltIns.
        /// Includes only snippets with KeywordType = "Command".
        /// </summary>
        public static FrozenSet<string> GetCommandNames()
        {
            if (_commandNames != null) return _commandNames;

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Extract from CommandSnippets directly (only file with KeywordType = "Command")
            foreach (var snippet in CommandSnippets.Items)
            {
                var name = ExtractCommandName(snippet.Insert);
                if (!string.IsNullOrEmpty(name))
                    names.Add(name);
            }

            _commandNames = names.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
            return _commandNames;
        }

        /// <summary>
        /// Gets all unit names (cached). Used by CalcpadBuiltIns.
        /// Includes only snippets with KeywordType = "Unit".
        /// </summary>
        public static FrozenSet<string> GetUnitNames()
        {
            if (_unitNames != null) return _unitNames;

            var names = new HashSet<string>(); // Units are case-sensitive

            // Extract from UnitSnippets directly (only file with KeywordType = "Unit")
            foreach (var snippet in UnitSnippets.Items)
            {
                var name = snippet.Insert.Trim();
                if (!string.IsNullOrEmpty(name))
                    names.Add(name);
            }

            _unitNames = names.ToFrozenSet();
            return _unitNames;
        }

        /// <summary>
        /// Gets all constant names (cached). Used by CalcpadBuiltIns.
        /// Includes only snippets with KeywordType = "Constant".
        /// </summary>
        public static FrozenSet<string> GetConstantNames()
        {
            if (_constantNames != null) return _constantNames;

            var names = new HashSet<string>(StringComparer.Ordinal);

            // Extract from ConstantSnippets directly (only file with KeywordType = "Constant")
            // Only π and e have KeywordType = "Constant" (others are UI-only assignment snippets)
            foreach (var snippet in ConstantSnippets.Items)
            {
                if (string.Equals(snippet.KeywordType, "Constant", StringComparison.OrdinalIgnoreCase))
                {
                    var name = snippet.Insert.Trim();
                    if (!string.IsNullOrEmpty(name))
                        names.Add(name);
                }
            }

            _constantNames = names.ToFrozenSet(StringComparer.Ordinal);
            return _constantNames;
        }

        /// <summary>
        /// Gets all setting names (cached). Used by CalcpadBuiltIns.
        /// Includes only snippets with KeywordType = "Setting".
        /// Settings are special backend variables like PlotHeight, PlotWidth, Precision, etc.
        /// </summary>
        public static FrozenSet<string> GetSettingNames()
        {
            if (_settingNames != null) return _settingNames;

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Extract from SettingSnippets directly (only file with KeywordType = "Setting")
            foreach (var snippet in SettingSnippets.Items)
            {
                var name = ExtractSettingName(snippet.Insert);
                if (!string.IsNullOrEmpty(name))
                    names.Add(name);
            }

            _settingNames = names.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
            return _settingNames;
        }

        /// <summary>
        /// Gets all control block keyword names (cached). Used by CalcpadBuiltIns.
        /// Includes only snippets with KeywordType = "ControlBlockKeyword".
        /// Control block keywords include: #if, #repeat, #for, #while, #def, #else, #else if
        /// </summary>
        public static FrozenSet<string> GetControlBlockKeywordNames()
        {
            if (_controlBlockKeywordNames != null) return _controlBlockKeywordNames;

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Extract from KeywordSnippets directly (only file with KeywordType = "ControlBlockKeyword")
            foreach (var snippet in KeywordSnippets.Items)
            {
                if (string.Equals(snippet.KeywordType, "ControlBlockKeyword", StringComparison.OrdinalIgnoreCase))
                {
                    var name = ExtractKeywordName(snippet.Insert);
                    if (!string.IsNullOrEmpty(name))
                        names.Add(name);
                }
            }

            _controlBlockKeywordNames = names.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
            return _controlBlockKeywordNames;
        }

        /// <summary>
        /// Gets all end keyword names (cached). Used by CalcpadBuiltIns.
        /// Includes only snippets with KeywordType = "EndKeyword".
        /// End keywords include: #end if, #end def, #loop
        /// </summary>
        public static FrozenSet<string> GetEndKeywordNames()
        {
            if (_endKeywordNames != null) return _endKeywordNames;

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Extract from KeywordSnippets directly (only file with KeywordType = "EndKeyword")
            foreach (var snippet in KeywordSnippets.Items)
            {
                if (string.Equals(snippet.KeywordType, "EndKeyword", StringComparison.OrdinalIgnoreCase))
                {
                    var name = ExtractKeywordName(snippet.Insert);
                    if (!string.IsNullOrEmpty(name))
                        names.Add(name);
                }
            }

            _endKeywordNames = names.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
            return _endKeywordNames;
        }

        /// <summary>
        /// Gets all operator characters (cached). Used by CalcpadBuiltIns.
        /// Includes only snippets with KeywordType = "Operator".
        /// </summary>
        public static FrozenSet<char> GetOperators()
        {
            if (_operators != null) return _operators;

            var ops = new HashSet<char>();

            // Extract from OperatorSnippets directly (only file with KeywordType = "Operator")
            foreach (var snippet in OperatorSnippets.Items)
            {
                var trimmed = snippet.Insert.Trim();
                if (trimmed.Length == 1)
                    ops.Add(trimmed[0]);
            }

            _operators = ops.ToFrozenSet();
            return _operators;
        }

        /// <summary>
        /// Gets a dictionary of function snippets by name (cached). Used by FunctionSignatures.
        /// When multiple signatures exist for the same function (overloads), selects the most
        /// permissive one (lowest MinParams, highest MaxParams) for linting purposes.
        /// Includes only snippets with KeywordType = "Function".
        /// </summary>
        public static FrozenDictionary<string, SnippetItem> GetFunctionSnippetsByName()
        {
            if (_functionSnippets != null) return _functionSnippets;

            var allFunctions = new List<SnippetItem>();
            allFunctions.AddRange(FunctionSnippets.Items);
            allFunctions.AddRange(VectorFunctionSnippets.Items);
            allFunctions.AddRange(MatrixFunctionSnippets.Items);

            var snippets = allFunctions
                .GroupBy(s => ExtractFunctionName(s.Insert), StringComparer.OrdinalIgnoreCase)
                .Where(g => !string.IsNullOrEmpty(g.Key))
                .ToDictionary(g => g.Key, g => SelectMostPermissiveSignature(g), StringComparer.OrdinalIgnoreCase);

            _functionSnippets = snippets.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
            return _functionSnippets;
        }

        /// <summary>
        /// Selects the most permissive signature from a group of function overloads.
        /// Prefers: AcceptsAnyCount, then lowest MinParams, then highest MaxParams (-1 = unlimited).
        /// </summary>
        private static SnippetItem SelectMostPermissiveSignature(IEnumerable<SnippetItem> overloads)
        {
            return overloads
                .OrderByDescending(s => s.AcceptsAnyCount) // AcceptsAnyCount first
                .ThenBy(s => s.MinParams)                   // Then lowest min params
                .ThenByDescending(s => s.MaxParams == -1 ? int.MaxValue : s.MaxParams) // Then highest max (-1 = unlimited)
                .First();
        }

        /// <summary>
        /// Gets all function overloads grouped by name (cached).
        /// Returns all signatures for functions that have multiple overloads (like take, line, spline).
        /// Includes only snippets with KeywordType = "Function".
        /// </summary>
        public static FrozenDictionary<string, SnippetItem[]> GetFunctionOverloads()
        {
            if (_functionOverloads != null) return _functionOverloads;

            var allFunctions = new List<SnippetItem>();
            allFunctions.AddRange(FunctionSnippets.Items);
            allFunctions.AddRange(VectorFunctionSnippets.Items);
            allFunctions.AddRange(MatrixFunctionSnippets.Items);

            var overloads = allFunctions
                .GroupBy(s => ExtractFunctionName(s.Insert), StringComparer.OrdinalIgnoreCase)
                .Where(g => !string.IsNullOrEmpty(g.Key))
                .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.OrdinalIgnoreCase);

            _functionOverloads = overloads.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
            return _functionOverloads;
        }

        /// <summary>
        /// Gets all overloads for a specific function by name.
        /// Returns empty array if function is not found.
        /// </summary>
        public static SnippetItem[] GetOverloadsForFunction(string functionName)
        {
            var overloads = GetFunctionOverloads();
            return overloads.TryGetValue(functionName, out var items) ? items : [];
        }

        /// <summary>
        /// Gets all function names that return vectors (cached). Used by TypeTracker.
        /// Includes functions with ReturnType = CalcpadType.Vector.
        /// </summary>
        public static FrozenSet<string> GetVectorReturningFunctions()
        {
            if (_vectorReturningFunctions != null) return _vectorReturningFunctions;

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var snippet in VectorFunctionSnippets.Items)
            {
                if (snippet.ReturnType == CalcpadType.Vector)
                {
                    var name = ExtractFunctionName(snippet.Insert);
                    if (!string.IsNullOrEmpty(name))
                        names.Add(name);
                }
            }

            // Also check FunctionSnippets for any vector-returning functions
            foreach (var snippet in FunctionSnippets.Items)
            {
                if (snippet.ReturnType == CalcpadType.Vector)
                {
                    var name = ExtractFunctionName(snippet.Insert);
                    if (!string.IsNullOrEmpty(name))
                        names.Add(name);
                }
            }

            _vectorReturningFunctions = names.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
            return _vectorReturningFunctions;
        }

        /// <summary>
        /// Gets all function names that return matrices (cached). Used by TypeTracker.
        /// Includes functions with ReturnType = CalcpadType.Matrix.
        /// </summary>
        public static FrozenSet<string> GetMatrixReturningFunctions()
        {
            if (_matrixReturningFunctions != null) return _matrixReturningFunctions;

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var snippet in MatrixFunctionSnippets.Items)
            {
                if (snippet.ReturnType == CalcpadType.Matrix)
                {
                    var name = ExtractFunctionName(snippet.Insert);
                    if (!string.IsNullOrEmpty(name))
                        names.Add(name);
                }
            }

            // Also check FunctionSnippets for any matrix-returning functions
            foreach (var snippet in FunctionSnippets.Items)
            {
                if (snippet.ReturnType == CalcpadType.Matrix)
                {
                    var name = ExtractFunctionName(snippet.Insert);
                    if (!string.IsNullOrEmpty(name))
                        names.Add(name);
                }
            }

            _matrixReturningFunctions = names.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
            return _matrixReturningFunctions;
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Extracts function name from insert text (e.g., "sin(§)" -> "sin").
        /// </summary>
        private static string ExtractFunctionName(string insert)
        {
            var openParen = insert.IndexOf('(');
            if (openParen > 0)
            {
                return insert[..openParen].Trim();
            }
            return string.Empty;
        }

        /// <summary>
        /// Extracts keyword name from insert text (e.g., "#if §\n\t§\n#end if" -> "#if").
        /// Handles multi-word keywords like "#end if", "#else if", "#end def".
        /// </summary>
        private static string ExtractKeywordName(string insert)
        {
            if (!insert.StartsWith('#')) return string.Empty;

            // Find end of first word
            var firstEndIndex = insert.IndexOfAny([' ', '\n', '\t', '§']);
            if (firstEndIndex <= 0)
            {
                return insert.Trim();
            }

            var firstWord = insert[..firstEndIndex].Trim();
            var firstWordWithoutHash = firstWord.TrimStart('#').ToLowerInvariant();

            // Check if this is a multi-word keyword (e.g., #end, #else, #md)
            if (firstWordWithoutHash == "end" || firstWordWithoutHash == "else" || firstWordWithoutHash == "md")
            {
                // Look for the second word
                var remaining = insert[firstEndIndex..].TrimStart();
                if (remaining.Length > 0 && remaining[0] != '§' && remaining[0] != '\n')
                {
                    var secondEndIndex = remaining.IndexOfAny([' ', '\n', '\t', '§']);
                    if (secondEndIndex > 0)
                    {
                        var secondWord = remaining[..secondEndIndex].Trim().ToLowerInvariant();
                        // Return multi-word keyword (e.g., "#end if", "#else if")
                        return firstWord + " " + secondWord;
                    }
                    else if (remaining.Length > 0)
                    {
                        // Second word goes to end of string
                        var secondWord = remaining.Trim().ToLowerInvariant();
                        if (!string.IsNullOrEmpty(secondWord))
                        {
                            return firstWord + " " + secondWord;
                        }
                    }
                }
            }

            return firstWord;
        }

        /// <summary>
        /// Extracts command name from insert text (e.g., "$Root{§ @ § = § : §}" -> "$Root").
        /// </summary>
        private static string ExtractCommandName(string insert)
        {
            if (!insert.StartsWith('$')) return string.Empty;

            // Find end of command name (brace or space)
            var endIndex = insert.IndexOfAny(['{', ' ', '=']);
            if (endIndex > 0)
            {
                return insert[..endIndex].Trim();
            }
            return insert.Trim();
        }

        /// <summary>
        /// Extracts setting name from insert text (e.g., "PlotHeight = §" -> "PlotHeight").
        /// </summary>
        private static string ExtractSettingName(string insert)
        {
            // Find the equals sign
            var equalsIndex = insert.IndexOf('=');
            if (equalsIndex > 0)
            {
                return insert[..equalsIndex].Trim();
            }
            return insert.Trim();
        }

        #endregion
    }
}