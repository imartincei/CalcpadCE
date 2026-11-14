using System.Collections.Generic;
using Calcpad.Highlighter.ContentResolution;

namespace Calcpad.Highlighter.Linter.Models
{
    public class Stage2Context : StageContext
    {
        // Post-include, pre-macro expansion
        public Dictionary<int, int> Stage2ToStage1Map { get; set; } = new();
        public Dictionary<int, SourceInfo> IncludeMap { get; set; } = new();
        public List<MacroDefinition> MacroDefinitions { get; set; } = new();
    }

    public class MacroDefinition
    {
        public string Name { get; set; }
        public int ParameterCount { get; set; }
        public int LineNumber { get; set; }
    }
}
