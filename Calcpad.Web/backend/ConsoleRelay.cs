using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace Calcpad.Server
{
    /// <summary>
    /// Bounded, drop-on-full relay to a stream that can block forever — our stdout, which hosts
    /// attach as a pipe. A parent that stops draining wedges the next write, and since
    /// <see cref="Console.SetOut"/> wraps its writer in <c>TextWriter.Synchronized</c> that
    /// parked thread takes every other logger with it. One dedicated thread owns the blocking
    /// write so callers never can.
    /// </summary>
    internal sealed class ConsoleRelay
    {
        private const int Capacity = 4096;

        private readonly BlockingCollection<string> _queue = new(Capacity);
        private readonly Stream _target;
        private int _dropped;
        private int _droppedTotal;
        private int _pending;

        /// <summary>
        /// Writes dropped since startup. Surfaced in the log file by <see cref="FileLogger"/>,
        /// since the in-band notice below goes to the console nobody is reading.
        /// </summary>
        internal int DroppedTotal => Volatile.Read(ref _droppedTotal);

        internal ConsoleRelay(Stream target)
        {
            _target = target;
            new Thread(DrainLoop)
            {
                IsBackground = true,
                Name = "calcpad-stdout-relay",
            }.Start();
        }

        /// <summary>Queues text for the relay thread. Never blocks; false if it was dropped.</summary>
        internal bool Enqueue(string text)
        {
            if (string.IsNullOrEmpty(text)) return true;
            Interlocked.Increment(ref _pending);
            if (_queue.TryAdd(text)) return true;
            Interlocked.Decrement(ref _pending);
            Interlocked.Increment(ref _dropped);
            Interlocked.Increment(ref _droppedTotal);
            return false;
        }

        /// <summary>
        /// Waits up to <paramref name="timeoutMs"/> for the relay thread to catch up. The thread
        /// is a background one, so without this the final crash lines die with the process.
        /// </summary>
        internal void Drain(int timeoutMs)
        {
            var sw = Stopwatch.StartNew();
            while (Volatile.Read(ref _pending) > 0 && sw.ElapsedMilliseconds < timeoutMs)
                Thread.Sleep(5);
        }

        private void DrainLoop()
        {
            foreach (var entry in _queue.GetConsumingEnumerable())
            {
                // Decremented after the write, not on dequeue, so Drain covers this entry too.
                TryWrite(entry);
                Interlocked.Decrement(ref _pending);
                var dropped = Interlocked.Exchange(ref _dropped, 0);
                if (dropped > 0)
                    TryWrite($"[calcpad] {dropped} console write(s) dropped{Environment.NewLine}");
            }
        }

        private void TryWrite(string text)
        {
            try
            {
                var bytes = Encoding.UTF8.GetBytes(text);
                _target.Write(bytes, 0, bytes.Length);
                _target.Flush();
            }
            catch { /* console closed or pipe broken; the log file is the source of truth */ }
        }
    }

    /// <summary>
    /// <see cref="TextWriter"/> face of <see cref="ConsoleRelay"/>, installed as
    /// <see cref="Console.Out"/> so stray <c>Console.Write</c> calls are non-blocking too. The
    /// synchronized decorator still wraps this, but its lock now spans only a queue insert.
    /// </summary>
    internal sealed class ConsoleRelayWriter(ConsoleRelay relay) : TextWriter
    {
        private readonly ConsoleRelay _relay = relay;

        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value) => _relay.Enqueue(value.ToString());

        public override void Write(string? value)
        {
            if (value != null) _relay.Enqueue(value);
        }

        // Required: ASP.NET's console provider writes spans, and TextWriter's base overloads
        // fan those out to one Write(char) — one queue slot, one flushed write — per character.
        public override void Write(char[] buffer, int index, int count) =>
            _relay.Enqueue(new string(buffer, index, count));

        public override void Write(ReadOnlySpan<char> buffer) => _relay.Enqueue(new string(buffer));

        public override void WriteLine(string? value) => _relay.Enqueue((value ?? string.Empty) + Environment.NewLine);

        public override void WriteLine(char[] buffer, int index, int count) =>
            _relay.Enqueue(new string(buffer, index, count) + Environment.NewLine);

        public override void WriteLine(ReadOnlySpan<char> buffer) =>
            _relay.Enqueue(string.Concat(buffer, Environment.NewLine));

        public override void WriteLine() => _relay.Enqueue(Environment.NewLine);
    }
}
