using System.Collections.Generic;

namespace Calcpad.Highlighter.ContentResolution
{
    public enum SymbolKind
    {
        Variable,
        Function,
        Macro,
    }

    /// <summary>
    /// Result of resolving a cursor position to a symbol. Contains the symbol's
    /// name, kind, and every location where it appears (definitions and usages).
    /// </summary>
    public class SymbolHit
    {
        public string Name { get; set; }
        public SymbolKind Kind { get; set; }
        public List<SymbolLocation> Locations { get; set; }
    }

    /// <summary>
    /// Server-side resolver for "what symbol is at this cursor position?" — the
    /// single source of truth shared by every editor integration (Monaco web,
    /// Monaco desktop, VS Code). Lookups go against the Stage3 indices, which
    /// have already been mapped back to original source line/column.
    /// </summary>
    public static class SymbolResolver
    {
        /// <summary>
        /// Find the symbol at the given original-source position, or null when no user-defined
        /// variable/function/macro covers the cursor. Macros are checked first because their
        /// `$`-suffixed names cannot collide with variable or function names, and function tokens
        /// take the next priority for the same reason.
        /// </summary>
        public static SymbolHit ResolveSymbolAt(Stage3Result stage3, int line, int column)
        {
            if (stage3 == null) return null;

            var hit = FindInIndex(stage3.MacroIndex, line, column);
            if (hit.Name != null)
                return new SymbolHit { Name = hit.Name, Kind = SymbolKind.Macro, Locations = stage3.MacroIndex[hit.Name] };

            hit = FindInIndex(stage3.FunctionIndex, line, column);
            if (hit.Name != null)
                return new SymbolHit { Name = hit.Name, Kind = SymbolKind.Function, Locations = stage3.FunctionIndex[hit.Name] };

            hit = FindInIndex(stage3.VariableIndex, line, column);
            if (hit.Name != null)
                return new SymbolHit { Name = hit.Name, Kind = SymbolKind.Variable, Locations = stage3.VariableIndex[hit.Name] };

            return null;
        }

        /// <summary>
        /// Scan one index for any local-source location that covers the cursor, inclusive on both
        /// edges so an editor reporting the cursor one-past the clicked character still resolves to
        /// the token it is visually attached to. When multiple locations match at edges, prefer an
        /// interior match.
        /// </summary>
        private static (string Name, SymbolLocation Loc) FindInIndex(
            Dictionary<string, List<SymbolLocation>> index, int line, int column)
        {
            string bestName = null;
            SymbolLocation bestLoc = null;
            bool bestIsInterior = false;

            foreach (var pair in index)
            {
                foreach (var loc in pair.Value)
                {
                    if (loc.Source != "local") continue;
                    if (loc.Line != line) continue;

                    int start = loc.Column;
                    int end = loc.Column + loc.Length;
                    if (column < start || column > end) continue;

                    bool isInterior = column > start && column < end;
                    if (bestName == null || (isInterior && !bestIsInterior))
                    {
                        bestName = pair.Key;
                        bestLoc = loc;
                        bestIsInterior = isInterior;
                    }
                }
            }

            return (bestName, bestLoc);
        }
    }
}
