using System;
using System.Collections.Generic;
using System.Linq;
using Calcpad.Core;

namespace Calcpad.Highlighter.Linter
{
    /// <summary>The expression errors of a document's last run, and the content they came from.</summary>
    public sealed class RunErrors
    {
        private readonly string[] _lines;
        private readonly List<CalcpadError> _errors;

        public RunErrors(string content, IEnumerable<CalcpadError> errors)
        {
            _lines = SplitLines(content);
            _errors = errors.Where(e => e.Source == CalcpadErrorSource.Expression).ToList();
        }

        public static RunErrors Empty { get; } = new(string.Empty, []);

        /// <summary>
        /// The errors moved onto <paramref name="content"/>'s lines. After an edit only Values
        /// errors on unchanged lines are kept, without columns: the static checks cover names.
        /// </summary>
        public IReadOnlyList<CalcpadError> For(string content)
        {
            var lines = SplitLines(content);
            if (lines.AsSpan().SequenceEqual(_lines))
                return _errors;

            var max = Math.Min(lines.Length, _lines.Length);
            var prefix = 0;
            while (prefix < max && lines[prefix] == _lines[prefix])
                ++prefix;
            var suffix = 0;
            while (suffix < max - prefix && lines[^(suffix + 1)] == _lines[^(suffix + 1)])
                ++suffix;

            var shift = lines.Length - _lines.Length;
            var moved = new List<CalcpadError>();
            foreach (var e in _errors)
            {
                if (e.Kind != CalcpadErrorKind.Values)
                    continue;

                var line = e.SourceLine - 1;
                int target;
                if (line < prefix)
                    target = line;
                else if (line >= _lines.Length - suffix)
                    target = line + shift;
                else if (!TryFindUnique(lines, line, out target))
                    continue;

                moved.Add(new CalcpadError
                {
                    SourceLine = target + 1,
                    OutputLine = e.OutputLine,
                    Message = e.Message,
                    Source = e.Source,
                    Code = e.Code,
                    Kind = e.Kind
                });
            }
            return moved;
        }

        // Between two edits, a line that occurs once before and after the edit is the same line
        private bool TryFindUnique(string[] lines, int line, out int target)
        {
            target = -1;
            var text = _lines[line];
            if (Array.IndexOf(_lines, text) != Array.LastIndexOf(_lines, text))
                return false;

            target = Array.IndexOf(lines, text);
            return target >= 0 && target == Array.LastIndexOf(lines, text);
        }

        private static string[] SplitLines(string content) =>
            content.Length == 0 ? [] : content.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
    }
}
