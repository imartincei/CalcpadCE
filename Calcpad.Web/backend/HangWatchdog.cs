using System.Collections.Concurrent;
using System.Text;

namespace Calcpad.Server
{
    /// <summary>
    /// Detects the process staying alive but no longer completing requests — a state that leaves
    /// no exception, no dump and no final log entry, only a log that stops mid-sentence. Uses a
    /// dedicated thread and <see cref="FileLogger.WriteDirect"/> because thread-pool starvation
    /// and a stalled log queue are both things it has to be able to report on.
    /// </summary>
    public static class HangWatchdog
    {
        private static readonly ConcurrentDictionary<long, InFlight> _inFlight = new();
        private static long _nextId;
        private static long _lastCompletedTicks = Environment.TickCount64;
        private static long _completedTotal;
        private static bool _reported;

        private readonly record struct InFlight(string Path, long StartTicks);

        private static int ThresholdSeconds =>
            int.TryParse(Environment.GetEnvironmentVariable("CALCPAD_HANG_THRESHOLD_SECONDS"), out var s) && s > 0
                ? s : 60;

        /// <summary>Starts the monitor thread. Safe to call once at startup.</summary>
        public static void Start()
        {
            var thread = new Thread(MonitorLoop)
            {
                IsBackground = true,
                Name = "calcpad-hang-watchdog",
            };
            thread.Start();
        }

        /// <summary>
        /// Registers a request as in flight. Dispose the result when it finishes.
        /// </summary>
        public static IDisposable Track(string path)
        {
            var id = Interlocked.Increment(ref _nextId);
            _inFlight[id] = new InFlight(path, Environment.TickCount64);
            return new Registration(id);
        }

        private sealed class Registration(long id) : IDisposable
        {
            private readonly long _id = id;
            private bool _disposed;

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _inFlight.TryRemove(_id, out _);
                Interlocked.Exchange(ref _lastCompletedTicks, Environment.TickCount64);
                Interlocked.Increment(ref _completedTotal);
                _reported = false;
            }
        }

        private static void MonitorLoop()
        {
            while (true)
            {
                try
                {
                    Thread.Sleep(5000);

                    var stalledFor = (Environment.TickCount64 - Interlocked.Read(ref _lastCompletedTicks)) / 1000;
                    if (_reported || _inFlight.IsEmpty || stalledFor < ThresholdSeconds)
                        continue;

                    _reported = true;
                    FileLogger.WriteDirect(BuildReport(stalledFor));
                    TryCaptureDump();
                }
                catch { /* the watchdog must never be the thing that dies */ }
            }
        }

        private static string BuildReport(long stalledForSeconds)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [HANG] Server stopped completing requests");
            sb.AppendLine($"No request has completed in {stalledForSeconds}s; {_inFlight.Count} still in flight.");
            sb.AppendLine($"Completed since start: {Interlocked.Read(ref _completedTotal)}");
            sb.AppendLine();

            sb.AppendLine("--- Thread pool ---");
            ThreadPool.GetMinThreads(out var minW, out var minIo);
            ThreadPool.GetMaxThreads(out var maxW, out var maxIo);
            ThreadPool.GetAvailableThreads(out var availW, out var availIo);
            sb.AppendLine($"ThreadCount={ThreadPool.ThreadCount} PendingWorkItems={ThreadPool.PendingWorkItemCount} CompletedWorkItems={ThreadPool.CompletedWorkItemCount}");
            sb.AppendLine($"Worker min/max/available = {minW}/{maxW}/{availW}");
            sb.AppendLine($"IO     min/max/available = {minIo}/{maxIo}/{availIo}");
            sb.AppendLine($"Process threads: {System.Diagnostics.Process.GetCurrentProcess().Threads.Count}");
            sb.AppendLine();

            sb.AppendLine("--- Memory ---");
            var info = GC.GetGCMemoryInfo();
            sb.AppendLine($"HeapSize={GC.GetTotalMemory(false) / (1024 * 1024)}MB Committed={info.TotalCommittedBytes / (1024 * 1024)}MB PauseTimePct={info.PauseTimePercentage}");
            sb.AppendLine($"Gen0={GC.CollectionCount(0)} Gen1={GC.CollectionCount(1)} Gen2={GC.CollectionCount(2)}");
            sb.AppendLine();

            sb.AppendLine("--- In-flight requests (oldest first) ---");
            var now = Environment.TickCount64;
            foreach (var entry in _inFlight.Values.OrderBy(v => v.StartTicks).Take(50))
                sb.AppendLine($"{(now - entry.StartTicks) / 1000,6}s  {entry.Path}");

            sb.AppendLine("=== END HANG REPORT ===");
            return sb.ToString();
        }

        /// <summary>
        /// Spawns the runtime's <c>createdump</c> against this process — the only way to get
        /// managed stacks for every thread, as .NET has no in-process equivalent. Off unless
        /// CALCPAD_HANG_DUMP=1, since it suspends the process while writing.
        /// </summary>
        private static void TryCaptureDump()
        {
            if (Environment.GetEnvironmentVariable("CALCPAD_HANG_DUMP") != "1") return;
            try
            {
                var exe = Path.Combine(AppContext.BaseDirectory,
                    OperatingSystem.IsWindows() ? "createdump.exe" : "createdump");
                if (!File.Exists(exe))
                {
                    FileLogger.WriteDirect($"[HANG] createdump not found at {exe}{Environment.NewLine}");
                    return;
                }

                var dir = Path.GetDirectoryName(FileLogger.GetLogFilePath()) ?? AppContext.BaseDirectory;
                var dumpPath = Path.Combine(dir, $"hang-{DateTime.Now:yyyyMMdd-HHmmss}.dmp");
                using var proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = exe,
                    ArgumentList = { "-f", dumpPath, "-u", Environment.ProcessId.ToString() },
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
                proc?.WaitForExit(60000);
                FileLogger.WriteDirect($"[HANG] wrote {dumpPath}{Environment.NewLine}");
            }
            catch (Exception ex)
            {
                FileLogger.WriteDirect($"[HANG] createdump failed: {ex.Message}{Environment.NewLine}");
            }
        }
    }
}
