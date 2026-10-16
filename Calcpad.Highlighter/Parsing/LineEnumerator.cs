using System;

namespace Calcpad.Highlighter.Parsing
{
    /// <summary>
    /// Zero-allocation line enumerator handling \r\n, \r and \n line endings, replacing
    /// string.Split. Returns ReadOnlySpan&lt;char&gt; per line without allocating strings.
    /// </summary>
    public ref struct LineEnumerator(ReadOnlySpan<char> span)
    {
        private ReadOnlySpan<char> _span = span;
        private bool _hasMore = true;

        public readonly LineEnumerator GetEnumerator() => this;

        public bool MoveNext()
        {
            if (!_hasMore)
                return false;

            if (_span.IsEmpty)
            {
                // Final empty line after trailing newline
                Current = default;
                _hasMore = false;
                return true;
            }

            var i = _span.IndexOfAny('\r', '\n');
            if (i < 0)
            {
                // Last line, no trailing newline
                Current = _span;
                _span = [];
                _hasMore = false;
                return true;
            }

            Current = _span[..i];

            // Advance past the line ending
            if (i < _span.Length - 1 && _span[i] == '\r' && _span[i + 1] == '\n')
            {
                // \r\n
                _span = _span[(i + 2)..];
            }
            else
            {
                // \r or \n
                _span = _span[(i + 1)..];
            }

            return true;
        }

        public ReadOnlySpan<char> Current { get; private set; } = default;
    }

    /// <summary>
    /// ReadOnlyMemory&lt;char&gt; variant of LineEnumerator, for a caller that needs to store the
    /// per-line slice in a field across method boundaries (a ref-struct Span cannot escape a
    /// method). Same \r\n / \r / \n semantics.
    /// </summary>
    public struct LineMemoryEnumerator(ReadOnlyMemory<char> memory)
    {
        private ReadOnlyMemory<char> _memory = memory;
        private bool _hasMore = true;

        public readonly LineMemoryEnumerator GetEnumerator() => this;

        public bool MoveNext()
        {
            if (!_hasMore)
                return false;

            if (_memory.IsEmpty)
            {
                Current = default;
                _hasMore = false;
                return true;
            }

            var span = _memory.Span;
            var i = span.IndexOfAny('\r', '\n');
            if (i < 0)
            {
                Current = _memory;
                _memory = default;
                _hasMore = false;
                return true;
            }

            Current = _memory[..i];

            if (i < span.Length - 1 && span[i] == '\r' && span[i + 1] == '\n')
                _memory = _memory[(i + 2)..];
            else
                _memory = _memory[(i + 1)..];

            return true;
        }

        public ReadOnlyMemory<char> Current { get; private set; } = default;
    }
}
