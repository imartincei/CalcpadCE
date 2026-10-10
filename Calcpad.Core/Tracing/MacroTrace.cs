using System.Collections.Generic;

namespace Calcpad.Core
{
    /// <summary>
    /// What <see cref="MacroParser"/> read and emitted, for tooling that needs Core's own
    /// include and macro resolution. Only filled when assigned to <see cref="MacroParser.Trace"/>.
    /// </summary>
    public sealed class MacroTrace
    {
        /// <summary>Every line read, with includes inlined. #include directives are omitted.</summary>
        public List<MacroTraceSourceLine> SourceLines { get; } = [];

        /// <summary>Every line emitted, with macros expanded and definitions removed.</summary>
        public List<MacroTraceOutputLine> OutputLines { get; } = [];

        public List<MacroTraceDefinition> Macros { get; } = [];
        public List<MacroTraceInclude> Includes { get; } = [];
        public List<MacroTraceError> Errors { get; } = [];

        /// <summary>False when parsing stopped before the end of the document.</summary>
        public bool IsComplete { get; internal set; }

        internal int AddSource(string text, int rootLine, string file, int fileLine)
        {
            SourceLines.Add(new(text, rootLine, file, fileLine));
            return SourceLines.Count - 1;
        }
    }

    /// <param name="RootLine">1-based line in the root document; included lines carry their #include's line.</param>
    /// <param name="File">Included file path, or null for the root document.</param>
    /// <param name="FileLine">0-based line within the content the include delegate returned.</param>
    public readonly record struct MacroTraceSourceLine(string Text, int RootLine, string File, int FileLine);

    public sealed class MacroTraceOutputLine
    {
        public string Text { get; init; }
        public int SourceIndex { get; init; }

        /// <summary>Macros expanded to produce this line, outermost first; null if none.</summary>
        public IReadOnlyList<string> ExpandedMacros { get; init; }

        public int ContentIndex { get; init; }
        public int ContentCount { get; init; } = 1;

        /// <summary>Raw path of an #include that could not be resolved; null otherwise.</summary>
        public string FailedInclude { get; init; }
    }

    public sealed class MacroTraceDefinition
    {
        public string Name { get; init; }
        public IReadOnlyList<string> Parameters { get; init; }
        public IReadOnlyList<string> Content { get; init; }
        public int SourceIndex { get; init; }

        /// <summary>Source index of the closing #end def; equals <see cref="SourceIndex"/> for inline macros.</summary>
        public int EndSourceIndex { get; init; }

        public bool IsInline { get; init; }
        public bool IsDuplicate { get; init; }
    }

    /// <param name="Name">The path as written, with path tokens expanded.</param>
    public readonly record struct MacroTraceInclude(string Path, string Name, int RootLine);

    public readonly record struct MacroTraceError(int SourceIndex, string Message);
}
