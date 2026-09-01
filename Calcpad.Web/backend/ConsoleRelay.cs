using System.Collections.Concurrent;
using System.Text;

namespace Calcpad.Server
{
    /// <summary>
    /// Bounded, drop-on-full relay to a stream that can block forever — our stdout, which hosts
    /// attach as a pipe. When a parent stops draining it the OS buffer fills and the next write
    /// blocks indefinitely without throwing; since <see cref="Console.SetOut"/> wraps its writer
    /// in <c>TextWriter.Synchronized</c>, that parked thread holds a process-wide monitor and
    /// every other thread that logs stops behind it. One dedicated thread owns the blocking
    /// write so callers never can.
    /// </summary>
    internal sealed class ConsoleRelay
    {
        private const int Capacity = 4096;

        private readonly BlockingCollection<string> _queue = new(Capacity);
        private readonly Stream _target;
        private int _dropped;
        private int _droppedTotal;

        /// <summary>
        /// Lines dropped since startup. Surfaced in the log file by <see cref="FileLogger"/>,
        /// because the in-band notice below goes to the console nobody is reading.
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
            if (_queue.TryAdd(text)) return true;
            Interlocked.Increment(ref _dropped);
            Interlocked.Increment(ref _droppedTotal);
            return false;
        }

        private void DrainLoop()
        {
            foreach (var entry in _queue.GetConsumingEnumerable())
            {
                TryWrite(entry);
                var dropped = Interlocked.Exchange(ref _dropped, 0);
                if (dropped > 0)
                    TryWrite($"[calcpad] {dropped} console line(s) dropped{Environment.NewLine}");
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
    /// <see cref="Console.Out"/> so stray <c>Console.Write</c> calls anywhere in the process are
    /// non-blocking too. The synchronized decorator still wraps this, but its lock is now only
    /// held across a queue insert.
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

        public override void WriteLine(string? value) => _relay.Enqueue((value ?? string.Empty) + Environment.NewLine);

        public override void WriteLine() => _relay.Enqueue(Environment.NewLine);
    }
}
