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
        /// <param name="Origins">Stage 2 line and column each character was read from, or -1; null without expansion.</param>
        private readonly record struct OutputLine(string Text, int Stage2Line, IReadOnlyList<string> ExpandedMacros, int ContentIndex, int ContentCount, (int Line, int Column)[] Origins);

        private Stage2Result ProcessStage2(Stage1Result stage1, string sourceFilePath, out List<OutputLine> outputs)
        {
            var trace = new MacroTrace();
            var includes = new Dictionary<string, Stage1Result>(PathComparer);
            var includedFileHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var parser = new MacroParser
            {
                SourceFilePath = sourceFilePath,
                Trace = trace,
                Debug = true,
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
                outputs.Add(new(text, output.SourceIndex, output.ExpandedMacros, output.ContentIndex, output.ContentCount, Locate(trace, output.SourceOffsets)));
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
                    outputs.Add(new(stage1.Lines[j], i, null, 0, 1, null));
                }
            }

            var (macroDefinitions, duplicateMacros, userDefinedMacros) = CollectMacros(trace, lines, includeMap);
            var macroParamOrder = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var macro in macroDefinitions)
                macroParamOrder.TryAdd(macro.Name, macro.Params);

            return new Stage2Result
            {
                Lines = lines,
                SourceMap = sourceMap,
                IncludeMap = includeMap,
                MacroDefinitions = macroDefinitions,
                DuplicateMacros = duplicateMacros,
                MacroParameterOrder = macroParamOrder,
                UserDefinedMacros = userDefinedMacros,
                PathRoots = parser.PathRoots,
                IncludedFileHashes = includedFileHashes
            };
        }

        private static (int, int)[] Locate(MacroTrace trace, IReadOnlyList<int> offsets)
        {
            if (offsets is null)
                return null;

            var origins = new (int, int)[offsets.Count];
            for (var i = 0; i < origins.Length; i++)
                origins[i] = trace.TryLocate(offsets[i], out var line, out var column) ? (line, column) : (-1, -1);

            return origins;
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
                var defaults = new List<string>(macro.Defaults);
                // Body lines are taken from the source so blank lines stay in place.
                var content = macro.IsInline
                    ? new List<string>(macro.Content)
                    : lines.GetRange(line + 1, Math.Max(0, macro.EndSourceIndex - line - 1));
                var metadata = FindDefinitionMetadata(lines, line);
                definitions.Add(new MacroDefinition
                {
                    Name = macro.Name,
                    Params = parameters,
                    Defaults = defaults,
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
                        RequiredParamCount = ParsingHelpers.CountRequiredParams(parameters.Count, defaults),
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
    }
}
