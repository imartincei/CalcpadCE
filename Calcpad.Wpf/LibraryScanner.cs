using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Calcpad.Wpf
{
    internal sealed record LibraryFunction(string Name, string Signature, int LineNumber);

    internal static partial class LibraryScanner
    {
        // #def name$(args) = ...   or   #def name$  (multi-line block ended by #end def)
        // Group 1: name (with trailing $ for macros)
        // Group 2: args (optional)
        [GeneratedRegex(@"^\s*#def\s+([A-Za-z_][A-Za-z0-9_]*\$?)\s*(?:\(([^)]*)\))?", RegexOptions.CultureInvariant)]
        private static partial Regex MacroDefRegex();

        // f(x) = expression   (single-line math function definition)
        // Group 1: name
        // Group 2: args
        [GeneratedRegex(@"^\s*([A-Za-z_][A-Za-z_0-9]*)\s*\(([^)]*)\)\s*=", RegexOptions.CultureInvariant)]
        private static partial Regex MathFuncRegex();

        public static IReadOnlyList<LibraryFunction> ScanFile(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return Array.Empty<LibraryFunction>();

            var results = new List<LibraryFunction>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            string[] lines;
            try { lines = File.ReadAllLines(path); }
            catch { return Array.Empty<LibraryFunction>(); }

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('\''))
                    continue;

                var m = MacroDefRegex().Match(line);
                if (m.Success)
                {
                    var name = m.Groups[1].Value;
                    var args = m.Groups[2].Success ? m.Groups[2].Value.Trim() : string.Empty;
                    var sig = $"{name}({args})";
                    if (seen.Add(sig))
                        results.Add(new LibraryFunction(name, args, i + 1));
                    continue;
                }

                m = MathFuncRegex().Match(line);
                if (m.Success)
                {
                    var name = m.Groups[1].Value;
                    var args = m.Groups[2].Value.Trim();
                    var sig = $"{name}({args})";
                    if (seen.Add(sig))
                        results.Add(new LibraryFunction(name, args, i + 1));
                }
            }

            return results;
        }
    }
}
