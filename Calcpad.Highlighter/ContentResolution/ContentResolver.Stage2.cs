using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Calcpad.Core;
using Calcpad.Highlighter.Linter.Helpers;
using Calcpad.Highlighter.Linter.Models;
using Calcpad.Highlighter.Parsing;
using Calcpad.Highlighter.Tokenizer;

namespace Calcpad.Highlighter.ContentResolution
{
    /// <summary>
    /// Stage 2: includes and macro collection, resolved by Core's <see cref="MacroParser"/>.
    /// </summary>
    public partial class ContentResolver
    {
        private static readonly StringComparer PathComparer =
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

        /// <summary>A line MacroParser emitted, for Stage 3.</summary>
        private readonly record struct OutputLine(string Text, int Stage2Line, IReadOnlyList<string> ExpandedMacros, int ContentIndex, int ContentCount);

        private Stage2Result ProcessStage2(Stage1Result stage1, string sourceFilePath, out List<OutputLine> outputs)
        {
            var trace = new MacroTrace();
            var includes = new Dictionary<string, Stage1Result>(PathComparer);
            var includedFileHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var parser = new MacroParser
            {
                SourceFilePath = sourceFilePath,
                Trace = trace,
                Include = (path, _) =>
                {
                    string raw;
                    try
                    {
                        raw = File.ReadAllText(path);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        return string.Empty;
                    }
                    includedFileHashes[path] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
                    var included = ProcessStage1(SplitLines(MacroParser.FilterIncludedContent(raw, keepLineCount: true)));
                    includes[path] = included;
                    return JoinLines(included.Lines);
                }
            };
            parser.Parse(JoinLines(stage1.Lines), out _, null, 0, false);

            var includeNames = new Dictionary<string, string>(PathComparer);
            foreach (var include in trace.Includes)
                includeNames.TryAdd(include.Path, include.Name);

            var lines = new List<string>(trace.SourceLines.Count);
            var sourceMap = new Dictionary<int, int>(trace.SourceLines.Count);
            var includeMap = new Dictionary<int, SourceInfo>(trace.SourceLines.Count);
            var local = new SourceInfo { Source = "local" };
            foreach (var source in trace.SourceLines)
            {
                var i = lines.Count;
                lines.Add(source.Text);
                sourceMap[i] = source.RootLine - 1;
                includeMap[i] = source.File is null
                    ? local
                    : new SourceInfo
                    {
                        Source = "include",
                        SourceFile = includeNames.GetValueOrDefault(source.File, source.File),
                        OriginalLine = includes.TryGetValue(source.File, out var included)
                            ? included.SourceMap.GetValueOrDefault(source.FileLine, source.FileLine)
                            : source.FileLine
                    };
            }

            outputs = new List<OutputLine>(trace.OutputLines.Count);
            foreach (var output in trace.OutputLines)
            {
                var text = output.Text;
                if (output.FailedInclude is not null)
                {
                    text = "' Error: Include file not provided: " + output.FailedInclude;
                    lines[output.SourceIndex] = text;
                    includeMap[output.SourceIndex] = new SourceInfo { Source = "include", SourceFile = output.FailedInclude };
                }
                outputs.Add(new(text, output.SourceIndex, output.ExpandedMacros, output.ContentIndex, output.ContentCount));
            }

            // MacroParser stops at an unrecoverable error, so the rest is passed through unexpanded.
            if (!trace.IsComplete)
            {
                var next = 0;
                foreach (var source in trace.SourceLines)
                    next = Math.Max(next, source.RootLine);

                for (var j = next; j < stage1.Lines.Count; j++)
                {
                    var i = lines.Count;
                    lines.Add(stage1.Lines[j]);
                    sourceMap[i] = j;
                    includeMap[i] = local;
                    outputs.Add(new(stage1.Lines[j], i, null, 0, 1));
                }
            }

            var (macroDefinitions, duplicateMacros, userDefinedMacros) = CollectMacros(trace, lines, includeMap);
            var (macroCommentParams, macroParamOrder) = ComputeMacroCommentParameters(macroDefinitions);

            var macroBodies = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var macro in macroDefinitions)
            {
                if (macro.Content.Count > 0)
                    macroBodies.TryAdd(macro.Name, JoinLines(macro.Content));
            }

            return new Stage2Result
            {
                Lines = lines,
                SourceMap = sourceMap,
                IncludeMap = includeMap,
                MacroDefinitions = macroDefinitions,
                DuplicateMacros = duplicateMacros,
                MacroCommentParameters = macroCommentParams,
                MacroParameterOrder = macroParamOrder,
                MacroBodies = macroBodies,
                UserDefinedMacros = userDefinedMacros,
                PathRoots = parser.PathRoots,
                IncludedFileHashes = includedFileHashes
            };
        }

        private static (List<MacroDefinition>, List<DuplicateMacro>, Dictionary<string, MacroInfo>) CollectMacros(
            MacroTrace trace, List<string> lines, Dictionary<int, SourceInfo> includeMap)
        {
            var definitions = new List<MacroDefinition>(trace.Macros.Count);
            var duplicates = new List<DuplicateMacro>();
            var userDefined = new Dictionary<string, MacroInfo>(StringComparer.Ordinal);
            foreach (var macro in trace.Macros)
            {
                var line = macro.SourceIndex;
                var info = includeMap.GetValueOrDefault(line);
                var parameters = new List<string>(macro.Parameters);
                // Body lines are taken from the source so blank lines stay in place.
                var content = macro.IsInline
                    ? new List<string>(macro.Content)
                    : lines.GetRange(line + 1, Math.Max(0, macro.EndSourceIndex - line - 1));
                var metadata = FindDefinitionMetadata(lines, line);
                definitions.Add(new MacroDefinition
                {
                    Name = macro.Name,
                    Params = parameters,
                    Content = content,
                    LineNumber = line,
                    Source = info?.Source ?? "local",
                    SourceFile = info?.SourceFile,
                    Description = metadata?.Description,
                    ParamTypes = metadata?.ParamTypes,
                    ParamDescriptions = metadata?.ParamDescriptions
                });

                if (userDefined.TryGetValue(macro.Name, out var first))
                    duplicates.Add(new DuplicateMacro
                    {
                        Name = macro.Name,
                        DuplicateLineNumber = line,
                        OriginalLineNumber = first.LineNumber
                    });
                else
                    userDefined[macro.Name] = new MacroInfo
                    {
                        LineNumber = line,
                        ParamCount = parameters.Count,
                        ParamNames = parameters
                    };
            }
            return (definitions, duplicates, userDefined);
        }

        // A metadata comment applies across blank lines but not past any other line.
        private static DefinitionMetadata FindDefinitionMetadata(List<string> lines, int definitionLine)
        {
            for (var i = definitionLine - 1; i >= 0; i--)
            {
                var text = lines[i].AsSpan();
                if (text.IsWhiteSpace())
                    continue;

                return DefinitionMetadata.TryParse(text, out var metadata) ? metadata : null;
            }
            return null;
        }

        private static List<string> SplitLines(string content)
        {
            var lines = new List<string>();
            foreach (var line in new LineEnumerator(content.AsSpan()))
                lines.Add(line.ToString());

            return lines;
        }

        /// <summary>
        /// Computes which parameters are "comment parameters" for each macro — those appearing
        /// directly in a comment section of the macro content, or passed to another macro's
        /// comment parameter position. Uses fixed-point iteration to handle transitive
        /// dependencies.
        /// </summary>
        private static (Dictionary<string, HashSet<string>> CommentParams, Dictionary<string, List<string>> ParamOrder)
            ComputeMacroCommentParameters(List<MacroDefinition> macroDefinitions)
        {
            var commentParams = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            var paramOrder = new Dictionary<string, List<string>>(StringComparer.Ordinal);

            // Build macro lookup and initialize structures
            var macrosByName = new Dictionary<string, MacroDefinition>(StringComparer.Ordinal);
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
    }
}
