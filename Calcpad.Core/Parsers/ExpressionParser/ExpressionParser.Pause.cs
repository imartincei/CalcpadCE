using System;
using System.Collections.Generic;

namespace Calcpad.Core
{
    public enum PauseBehavior
    {
        /// <summary>Runs past every #pause.</summary>
        Ignore,
        /// <summary>Stops at the first #pause reached after <see cref="ExpressionParser.PauseAfter"/>.</summary>
        Step
    }

    public partial class ExpressionParser
    {
        public const string PauseAtStart = "start";

        public PauseBehavior PauseMode { get; set; }

        /// <summary>
        /// Key of the last #pause the user moved past, for <see cref="PauseBehavior.Step"/>.
        /// Null or <see cref="PauseAtStart"/> stops at the first one.
        /// </summary>
        public string PauseAfter { get; set; }

        /// <summary>Key of the #pause the parse stopped at, or null if it reached the end.</summary>
        public string NextPauseKey { get; private set; }

        /// <summary>
        /// <see cref="PauseAfter"/> that steps back one #pause: <see cref="PauseAtStart"/> for the
        /// first step, or null when there is no step before this one.
        /// </summary>
        public string PreviousPauseKey { get; private set; }

        // Loop openers and #pause lines numbered in source order, so a key survives edits to other lines
        private int[] _pauseOrdinals;
        private int[] _pauseAfterKey;
        private int[] _lastPassedPause;

        private void InitializePauseSteps(ReadOnlySpan<char> code, List<int> lines)
        {
            NextPauseKey = null;
            PreviousPauseKey = null;
            _lastPassedPause = null;
            if (PauseMode != PauseBehavior.Step)
                return;

            _pauseAfterKey = ParsePauseKey(PauseAfter);
            var lineCount = lines.Count - 1;
            _pauseOrdinals = new int[lineCount];
            var ordinal = 0;
            for (int i = 0; i < lineCount; ++i)
            {
                var line = code[lines[i]..lines[i + 1]].Trim();
                if (line.Length > 0 && line[0] == '#' &&
                    GetKeyword(line) is Keyword.Pause or Keyword.Repeat or Keyword.For or Keyword.While)
                    _pauseOrdinals[i] = ++ordinal;
            }
        }

        private KeywordResult StepPause()
        {
            var key = GetPauseKey();
            if (_pauseAfterKey is null || ComparePauseKeys(key, _pauseAfterKey) > 0)
            {
                NextPauseKey = FormatPauseKey(key);
                return KeywordResult.Break;
            }
            PreviousPauseKey = _lastPassedPause is null ? PauseAtStart : FormatPauseKey(_lastPassedPause);
            _lastPassedPause = key;
            return KeywordResult.Continue;
        }

        // [loop ordinal, pass, ..., pause ordinal], outermost loop first
        private int[] GetPauseKey()
        {
            var key = new int[2 * _loops.Count + 1];
            var i = key.Length - 1;
            key[i] = _pauseOrdinals[_currentLine];
            foreach (var loop in _loops) // enumerates innermost first
            {
                key[--i] = loop.Pass;
                key[--i] = _pauseOrdinals[loop.StartLine];
            }
            return key;
        }

        private static int ComparePauseKeys(int[] a, int[] b)
        {
            var n = Math.Min(a.Length, b.Length);
            for (int i = 0; i < n; ++i)
                if (a[i] != b[i])
                    return a[i].CompareTo(b[i]);

            return a.Length.CompareTo(b.Length);
        }

        private static string FormatPauseKey(int[] key) => string.Join('.', key);

        private static int[] ParsePauseKey(string s)
        {
            if (string.IsNullOrEmpty(s))
                return null;

            var parts = s.Split('.');
            var key = new int[parts.Length];
            for (int i = 0; i < parts.Length; ++i)
                if (!int.TryParse(parts[i], out key[i]))
                    return null;

            return key;
        }
    }
}
