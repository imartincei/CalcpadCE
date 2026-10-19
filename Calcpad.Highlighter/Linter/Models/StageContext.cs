using System.Collections.Generic;

namespace Calcpad.Highlighter.Linter.Models
{
    public abstract class StageContext
    {
        public List<string> Lines { get; set; } = new();
        public Dictionary<int, int> SourceMap { get; set; } = new(); // Stage line -> Original line
    }
}
