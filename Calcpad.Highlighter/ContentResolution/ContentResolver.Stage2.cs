using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Calcpad.Core;
using Calcpad.Highlighter.Linter.Helpers;
using Calcpad.Highlighter.Parsing;
using Calcpad.Highlighter.Tokenizer;
using Calcpad.Highlighter.Tokenizer.Models;

namespace Calcpad.Highlighter.ContentResolution
{
    /// <summary>
    /// Stage 2: Include/read resolution and macro collection.
    /// </summary>
    public partial class ContentResolver
    {
        /// <summary>
        /// STAGE 2: Resolve includes/reads, then collect macros
        /// Pass 1: Replace #include/#read lines with file content
        /// Pass 2: Scan combined content for macro definitions
        /// </summary>
        private Stage2Result ProcessStage2(Stage1Result stage1, Dictionary<string, string> includeFiles, Dictionary<string, byte[]> clientFileCache, string sourceFilePath = null)
        {
            // Pass 1: Expand includes recursively - matching Core's MacroParser approach
            var expandedLines = new List<string>();
            var sourceMap = new Dictionary<int, int>();  // expanded line -> stage1 line
            var includeMap = new Dictionary<int, SourceInfo>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var includedFileHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var rootDir = !string.IsNullOrEmpty(sourceFilePath)
                ? Path.GetDirectoryName(sourceFilePath) : null;
            // Shared across the root and every included file, exactly like MacroParser's own
            // instance field — a {project}/{library} reference resolves against whichever
            // declaration came first in document order, wherever it was written.
            var pathRoots = new PathRoots();

            for (int i = 0; i < stage1.Lines.Count; i++)
            {
                var line = stage1.Lines[i];
                var trimmedSpan = line.AsSpan().Trim();

                if (PathRoots.IsDeclaration(trimmedSpan, out var isProject, out var declStart, out var declLength))
                {
                    if (declLength > 0)
                        pathRoots.TryDeclare(isProject, trimmedSpan.Slice(declStart, declLength).ToString(),
                            rootDir, out _);
                    // A malformed or duplicate declaration is reported when the document is
                    // actually parsed/converted (Calcpad.Core.MacroParser/ExpressionParser) — the
                    // editor's lint pass only needs the roots it can resolve, not to re-validate.
                }
                else if (IsIncludeDirective(trimmedSpan))
                {
                    var rawFileName = ExtractIncludeFilename(trimmedSpan);
                    pathRoots.TryExpand(rawFileName, out rawFileName, out _);
                    ExpandInclude(rawFileName, i, includeFiles, clientFileCache,
                        expandedLines, sourceMap, includeMap, visited, includedFileHashes, 0, rootDir, pathRoots);
                    continue;
                }

                // Note: #read directives are NOT file includes - they read data (CSV/Excel) at runtime
                // The linter just needs to recognize that #read defines a variable
                // The #read line is kept as-is and processed by CollectVariables

                // Regular line
                expandedLines.Add(line);
                sourceMap[expandedLines.Count - 1] = i;
                includeMap[expandedLines.Count - 1] = new SourceInfo { Source = "local" };
            }

            // Pass 2: Tokenize in Macro mode to collect macro definitions
            var tokenizer = new CalcpadTokenizer();
            tokenizer.SetIncludeMap(includeMap);
            var source = JoinLines(expandedLines);
            var tokenizerResult = tokenizer.Tokenize(source, TokenizerMode.Macro);

            var macroDefinitions = tokenizerResult.MacroDefinitions;
            var duplicateMacros = tokenizerResult.DuplicateMacros;

            // Compute comment parameters for all macros (with transitive closure)
            var (macroCommentParams, macroParamOrder) = ComputeMacroCommentParameters(macroDefinitions);

            // Build macro bodies for argument type resolution
            var macroBodies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var macro in macroDefinitions)
            {
                if (macro.Content != null && macro.Content.Count > 0 && !macroBodies.ContainsKey(macro.Name))
                {
                    macroBodies[macro.Name] = JoinLines(macro.Content);
                }
            }

            return new Stage2Result
            {
                Lines = expandedLines,
                SourceMap = sourceMap,
                IncludeMap = includeMap,
                MacroDefinitions = macroDefinitions,
                DuplicateMacros = duplicateMacros,
                MacroCommentParameters = macroCommentParams,
                MacroParameterOrder = macroParamOrder,
                MacroBodies = macroBodies,
                UserDefinedMacros = tokenizerResult.UserDefinedMacros,
                PathRoots = pathRoots,
                IncludedFileHashes = includedFileHashes
            };
        }

        /// <summary>
        /// Computes which parameters are "comment parameters" for each macro.
        /// A parameter is a comment parameter if:
        /// 1. It appears directly in a comment section (text between ' or ") in the macro content
        /// 2. It is passed to another macro's comment parameter position (transitive)
        ///
        /// Uses fixed-point iteration to handle transitive dependencies.
        /// </summary>
        private static (Dictionary<string, HashSet<string>> CommentParams, Dictionary<string, List<string>> ParamOrder)
            ComputeMacroCommentParameters(List<MacroDefinition> macroDefinitions)
        {
            var commentParams = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            var paramOrder = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            // Build macro lookup and initialize structures
            var macrosByName = new Dictionary<string, MacroDefinition>(StringComparer.OrdinalIgnoreCase);
            foreach (var macro in macroDefinitions)
            {
                if (!macrosByName.ContainsKey(macro.Name))
                {
                    macrosByName[macro.Name] = macro;
                    paramOrder[macro.Name] = macro.Params ?? new List<string>();
                    commentParams[macro.Name] = new HashSet<string>(StringComparer.Ordinal);
                }
            }

            // First pass: find direct comment parameters (params that appear in comment text)
            foreach (var macro in macroDefinitions)
            {
                if (macro.Params == null || macro.Params.Count == 0 || macro.Content == null)
                    continue;

                foreach (var contentLine in macro.Content)
                {
                    var lineCommentParams = CalcpadTokenizer.FindCommentParamsInLine(contentLine, macro.Params);
                    foreach (var param in lineCommentParams)
                    {
                        commentParams[macro.Name].Add(param);
                    }
                }

                // Also update the MacroDefinition's CommentParameters
                macro.CommentParameters = new HashSet<string>(commentParams[macro.Name], StringComparer.Ordinal);
            }

            // Second pass: transitive closure - if a param is passed to another macro's comment param position
            // Repeat until no changes (fixed point)
            bool changed = true;
            int maxIterations = 100; // Safety limit
            int iteration = 0;

            while (changed && iteration < maxIterations)
            {
                changed = false;
                iteration++;

                foreach (var macro in macroDefinitions)
                {
                    if (macro.Params == null || macro.Params.Count == 0 || macro.Content == null)
                        continue;

                    // Scan macro content for calls to other macros
                    foreach (var contentLine in macro.Content)
                    {
                        // Find macro calls in this line (pattern: macroName$(args))
                        var calls = FindMacroCallsInLine(contentLine, macrosByName);

                        foreach (var (calledMacro, args) in calls)
                        {
                            if (!macrosByName.TryGetValue(calledMacro, out var calledMacroDef))
                                continue;

                            var calledParams = calledMacroDef.Params;
                            if (calledParams == null)
                                continue;

                            // Check each argument position
                            for (int argIdx = 0; argIdx < args.Count && argIdx < calledParams.Count; argIdx++)
                            {
                                var calledParamName = calledParams[argIdx];

                                // If this position in the called macro is a comment parameter
                                if (commentParams.TryGetValue(calledMacro, out var calledCommentParams) &&
                                    calledCommentParams.Contains(calledParamName))
                                {
                                    // Check if the argument contains any of our parameters
                                    var argText = args[argIdx];
                                    foreach (var ourParam in macro.Params)
                                    {
                                        if (argText.Contains(ourParam) && !commentParams[macro.Name].Contains(ourParam))
                                        {
                                            commentParams[macro.Name].Add(ourParam);
                                            changed = true;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            // Update MacroDefinition.CommentParameters with final results
            foreach (var macro in macroDefinitions)
            {
                if (commentParams.TryGetValue(macro.Name, out var cp))
                {
                    macro.CommentParameters = new HashSet<string>(cp, StringComparer.Ordinal);
                }
            }

            return (commentParams, paramOrder);
        }

        /// <summary>
        /// Finds macro calls in a line and returns the called macro name and arguments.
        /// </summary>
        private static List<(string MacroName, List<string> Args)> FindMacroCallsInLine(
            string line, Dictionary<string, MacroDefinition> knownMacros)
        {
            var results = new List<(string, List<string>)>();
            if (string.IsNullOrEmpty(line))
                return results;

            var lineSpan = line.AsSpan();

            // Look for pattern: macroName$(args) where macroName$ is a known macro
            int i = 0;
            while (i < lineSpan.Length)
            {
                // Find next $
                int dollarIdx = lineSpan[i..].IndexOf('$');
                if (dollarIdx < 0)
                    break;
                dollarIdx += i;

                // Try to extract macro name ending at this $
                // Work backwards to find the start of the identifier
                int nameStart = dollarIdx;
                while (nameStart > 0 && CalcpadCharacterHelpers.IsMacroLetter(lineSpan[nameStart - 1], dollarIdx - nameStart))
                {
                    nameStart--;
                }

                if (nameStart < dollarIdx)
                {
                    var macroName = lineSpan.Slice(nameStart, dollarIdx - nameStart + 1).ToString();

                    // Check if this is a known macro
                    if (knownMacros.ContainsKey(macroName))
                    {
                        // Look for opening parenthesis
                        int afterDollar = dollarIdx + 1;

                        // Skip whitespace
                        ParsingHelpers.SkipWhitespace(lineSpan, ref afterDollar);

                        if (afterDollar < lineSpan.Length && lineSpan[afterDollar] == '(')
                        {
                            // Find matching close paren and extract args
                            int parenStart = afterDollar;
                            var closePos = ParsingHelpers.FindMatchingClose(lineSpan, parenStart, '(', ')');

                            if (closePos >= 0)
                            {
                                var argsStr = lineSpan.Slice(parenStart + 1, closePos - parenStart - 1).ToString();
                                var args = ParameterParser.ParseMacroParameters(argsStr);
                                results.Add((macroName, args));
                                i = closePos + 1;
                                continue;
                            }
                        }
                    }
                }

                i = dollarIdx + 1;
            }

            return results;
        }

        /// <summary>
        /// Tests whether a trimmed line span is an #include directive.
        /// Matches Core: starts with "#include" followed by whitespace.
        /// </summary>
        private static bool IsIncludeDirective(ReadOnlySpan<char> trimmedSpan)
        {
            return trimmedSpan.StartsWith("#include", StringComparison.OrdinalIgnoreCase) &&
                   trimmedSpan.Length > 8 && char.IsWhiteSpace(trimmedSpan[8]);
        }

        /// <summary>
        /// Tests whether a trimmed line span is a #local or #global directive.
        /// Matches Core's Validator.IsKeyword(line, ...).
        /// </summary>
        private static bool IsLocalOrGlobalDirective(ReadOnlySpan<char> trimmedSpan)
        {
            return trimmedSpan.StartsWith("#local", StringComparison.OrdinalIgnoreCase) ||
                   trimmedSpan.StartsWith("#global", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Extracts the raw filename from an #include directive.
        /// Matches Core's MacroParser.ParseInclude: finds first comment marker ('/") or
        /// field parameter (#), takes text from position 8 to that marker, trims whitespace.
        /// </summary>
        private static string ExtractIncludeFilename(ReadOnlySpan<char> lineContent)
        {
            int n = lineContent.Length;
            var quoteIdx = lineContent[8..].IndexOfAny('\'', '"');
            if (quoteIdx >= 0) quoteIdx += 8;
            var hashIdx = lineContent.Slice(1).LastIndexOf('#');
            if (hashIdx >= 0) hashIdx += 1;

            if (quoteIdx >= 9)
                n = quoteIdx;
            if (hashIdx > 0 && (n == lineContent.Length || hashIdx < n))
                n = hashIdx;
            if (n < 9)
                n = lineContent.Length;

            return lineContent[8..n].Trim().ToString();
        }

        /// <summary>
        /// Recursively expands an #include directive by resolving the file content
        /// and processing any nested #include directives within it.
        /// Matches Core's recursive Parse() behavior.
        /// </summary>
        private void ExpandInclude(
            string rawFileName,
            int stage1Line,
            Dictionary<string, string> includeFiles,
            Dictionary<string, byte[]> clientFileCache,
            List<string> expandedLines,
            Dictionary<int, int> sourceMap,
            Dictionary<int, SourceInfo> includeMap,
            HashSet<string> visited,
            Dictionary<string, string> includedFileHashes,
            int depth,
            string sourceDir = null,
            PathRoots pathRoots = null)
        {
            pathRoots ??= new PathRoots();
            if (depth > 20 || string.IsNullOrEmpty(rawFileName) || !visited.Add(rawFileName))
            {
                expandedLines.Add("' Error: Include file not provided: " + rawFileName);
                sourceMap[expandedLines.Count - 1] = stage1Line;
                includeMap[expandedLines.Count - 1] = new SourceInfo { Source = "include", SourceFile = rawFileName };
                return;
            }

            var (fileContent, resolvedPath, fromFilesystem) = ResolveFileContent(rawFileName, sourceDir, includeFiles, clientFileCache);
            if (fileContent == null)
            {
                expandedLines.Add("' Error: Include file not provided: " + rawFileName);
                sourceMap[expandedLines.Count - 1] = stage1Line;
                includeMap[expandedLines.Count - 1] = new SourceInfo { Source = "include", SourceFile = rawFileName };
                return;
            }

            if (fromFilesystem)
                includedFileHashes[resolvedPath] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fileContent)));

            // Process included content through Stage1 (line continuations)
            var includedLines = new List<string>();
            foreach (var span in new LineEnumerator(fileContent.AsSpan()))
                includedLines.Add(span.ToString());
            var includedStage1 = ProcessStage1(includedLines);

            // Process each line, filtering #local sections and recursing for nested #include.
            // Matches Core's CalcpadReader.Include: #local sections are excluded from includes.
            var directives = new DirectiveState();

            for (int j = 0; j < includedStage1.Lines.Count; j++)
            {
                var line = includedStage1.Lines[j];
                var trimmedSpan = line.AsSpan().Trim();

                if (IsLocalOrGlobalDirective(trimmedSpan))
                {
                    directives.Apply(trimmedSpan);
                    continue;
                }

                if (directives.Scope == ScopeMode.Local)
                    continue;

                var nestedSourceDir = resolvedPath != null ? Path.GetDirectoryName(resolvedPath) : sourceDir;
                if (PathRoots.IsDeclaration(trimmedSpan, out var isProject, out var declStart, out var declLength))
                {
                    // A #local-scoped declaration never reaches here — the scope check above
                    // already excluded it, the same way the real include delegate
                    // (CalcpadService.ProcessIncludedContent) strips it before Core ever parses
                    // the includer's flattened text. One that does reach here is added to
                    // expandedLines below like any other line — it is genuinely part of the
                    // flattened document, same as the root's own declaration.
                    if (declLength > 0)
                        pathRoots.TryDeclare(isProject, trimmedSpan.Slice(declStart, declLength).ToString(),
                            nestedSourceDir, out _);
                }
                else if (IsIncludeDirective(trimmedSpan))
                {
                    var nestedFileName = ExtractIncludeFilename(trimmedSpan);
                    pathRoots.TryExpand(nestedFileName, out nestedFileName, out _);
                    ExpandInclude(nestedFileName, stage1Line, includeFiles, clientFileCache,
                        expandedLines, sourceMap, includeMap, visited, includedFileHashes, depth + 1, nestedSourceDir, pathRoots);
                    continue;
                }

                expandedLines.Add(line);
                sourceMap[expandedLines.Count - 1] = stage1Line;
                var originalLineInInclude = includedStage1.SourceMap.TryGetValue(j, out var origLine)
                    ? origLine : j;
                includeMap[expandedLines.Count - 1] = new SourceInfo
                {
                    Source = "include",
                    SourceFile = rawFileName,
                    OriginalLine = originalLineInInclude
                };
            }
        }

        /// <summary>
        /// Resolves file content matching Core's MacroParser lookup order:
        /// 1. Try filesystem (Path.GetFullPath + File.Exists)
        /// 2. Try includeFiles dictionary (plain text, client-provided)
        /// 3. Try clientFileCache dictionary (raw bytes, pre-fetched by Web layer)
        /// Returns the content, the resolved full path (for tracking source directory in nested
        /// includes), and whether it came from the filesystem (branch 1) as opposed to
        /// client-provided content — only a filesystem hit has independent on-disk state worth
        /// hashing for cache invalidation.
        /// </summary>
        private static (string content, string resolvedPath, bool fromFilesystem) ResolveFileContent(string rawFileName, string sourceDir,
            Dictionary<string, string> includeFiles, Dictionary<string, byte[]> clientFileCache)
        {
            string resolvedPath = null;

            // 1. Try filesystem (matching Core: expand env vars, get full path, check exists)
            try
            {
                var expandedPath = Environment.ExpandEnvironmentVariables(rawFileName);
                resolvedPath = sourceDir != null
                    ? Path.GetFullPath(expandedPath, sourceDir)
                    : Path.GetFullPath(expandedPath);
                if (File.Exists(resolvedPath))
                    return (File.ReadAllText(resolvedPath), resolvedPath, true);
            }
            catch { /* Not a valid filesystem path (URLs, API syntax, etc.) */ }

            // 2. Try includeFiles — resolved path first, then raw filename
            if (resolvedPath != null && includeFiles.TryGetValue(resolvedPath, out var content))
                return (content, resolvedPath, false);
            if (includeFiles.TryGetValue(rawFileName, out content))
                return (content, null, false);

            // 3. Try clientFileCache — resolved path first, then raw filename
            if (resolvedPath != null && clientFileCache.TryGetValue(resolvedPath, out var contentBytes))
                return (Encoding.UTF8.GetString(contentBytes), resolvedPath, false);
            if (clientFileCache.TryGetValue(rawFileName, out contentBytes))
                return (Encoding.UTF8.GetString(contentBytes), null, false);

            return (null, null, false);
        }

        private static List<string> ParseParameters(string paramsStr)
        {
            return ParameterParser.ParseParameters(paramsStr);
        }
    }
}
