using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Text;

namespace Calcpad.Server
{
    /// <summary>
    /// Verbosity of a log entry. Ordered most-severe-first so filtering is a single
    /// <c>&gt;</c> test against <see cref="FileLogger.MinLevel"/>.
    /// </summary>
    public enum LogLevel
    {
        Error = 0,
        Warning = 1,
        Information = 2,
        Verbose = 3,
    }

    /// <summary>
    /// Process-wide log sink. Every request logs before doing any work, so a stall here wedges
    /// every endpoint at once. Both outputs are therefore decoupled from the caller and from each
    /// other — stdout via <see cref="ConsoleRelay"/>, the file via a bounded queue and its own
    /// drain thread — so neither a stalled pipe nor a stalled disk can park a request thread, and
    /// a stalled pipe still leaves us the log file. <see cref="LogCrash"/> is the exception: it
    /// writes synchronously and fsyncs, to survive a kill the drain thread would not.
    /// </summary>
    public static class FileLogger
    {
        private const int FileQueueCapacity = 4096;
        private const int FlushIntervalMs = 1000;

        private static readonly BlockingCollection<string> _fileQueue = new(FileQueueCapacity);
        private static readonly object _fileLock = new();
        private static readonly ConsoleRelay _stdoutRelay = new(Console.OpenStandardOutput());
        private static readonly ConsoleRelay _stderrRelay = new(Console.OpenStandardError());

        // A field initializer, not the static ctor body: this has to be set before the ctor
        // logs its own first entry.
        private static readonly string? _rawEnvLevel = Environment.GetEnvironmentVariable("CALCPAD_LOG_LEVEL");
        private static volatile LogLevel _minLevel = ParseLevel(_rawEnvLevel) ?? LogLevel.Warning;

        private static string? _logFilePath;
        private static FileStream? _stream;
        private static bool _dirty;
        private static int _fileDropped;
        private static int _reportedConsoleDrops;
        private static long _lastConsoleDropReportTicks;
        private const int ConsoleDropReportIntervalMs = 5000;

        /// <summary>
        /// Entries more verbose than this are dropped. Set from <c>CALCPAD_LOG_LEVEL</c> at
        /// startup and from the <c>/api/calcpad/log-level</c> endpoint at runtime.
        /// </summary>
        public static LogLevel MinLevel
        {
            get => _minLevel;
            set => _minLevel = value;
        }

        public static readonly string[] LevelNames = ["error", "warning", "information", "verbose"];

        /// <summary>
        /// Maps a level name to a <see cref="LogLevel"/>, accepting common aliases. Null when
        /// nothing matched, so callers choose their own default.
        /// </summary>
        public static LogLevel? ParseLevel(string? value) => value?.Trim().ToLowerInvariant() switch
        {
            "verbose" or "trace" or "debug" or "all" => LogLevel.Verbose,
            "information" or "info" => LogLevel.Information,
            "warning" or "warn" => LogLevel.Warning,
            "error" or "err" or "critical" or "fatal" or "none" or "off" => LogLevel.Error,
            _ => null,
        };

        private static string TagFor(LogLevel level) => level switch
        {
            LogLevel.Error => "ERROR",
            LogLevel.Warning => "WARN",
            LogLevel.Information => "INFO",
            _ => "TRACE",
        };

        static FileLogger()
        {
            try
            {
                // Host apps (Tauri AppImage, VS Code extension) set CALCPAD_LOG_DIR
                // when the executable dir is read-only (AppImage FUSE mount, MSI
                // Program Files, etc.). Fall back to executable-adjacent logs/ for
                // dev runs and other embedders that don't set it.
                var overrideDir = Environment.GetEnvironmentVariable("CALCPAD_LOG_DIR");
                string logsDir;
                if (!string.IsNullOrEmpty(overrideDir))
                {
                    logsDir = overrideDir;
                }
                else
                {
                    var executablePath = Assembly.GetExecutingAssembly().Location;
                    if (string.IsNullOrEmpty(executablePath))
                    {
                        executablePath = Environment.ProcessPath ?? AppContext.BaseDirectory;
                    }
                    var directory = Path.GetDirectoryName(executablePath) ?? AppContext.BaseDirectory;
                    logsDir = Path.Combine(directory, "logs");
                }
                Directory.CreateDirectory(logsDir);
                var timestamp = DateTime.Now.ToString("yyyyMMdd");
                _logFilePath = Path.Combine(logsDir, $"CalcpadServer-{timestamp}.log");
            }
            catch (Exception ex)
            {
                try
                {
                    _logFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                        $"CalcpadServer-{DateTime.Now:yyyyMMdd}.log");
                    QueueForFile(Format("WARN", "Logger fallback location", $"Error: {ex.Message}"));
                }
                catch
                {
                    _logFilePath = null;
                }
            }

            var drain = new Thread(FileDrainLoop)
            {
                IsBackground = true,
                Name = "calcpad-log-writer",
            };
            drain.Start();

            // Warning, so a typo in CALCPAD_LOG_LEVEL is still visible at the default level.
            if (!string.IsNullOrWhiteSpace(_rawEnvLevel) && ParseLevel(_rawEnvLevel) is null)
                LogWarning($"Unrecognized CALCPAD_LOG_LEVEL '{_rawEnvLevel}' — using {_minLevel}",
                    $"Expected one of: {string.Join(", ", LevelNames)}");

            LogVerbose("Logger initialized", $"Log file: {_logFilePath}, level: {_minLevel}");
        }

        /// <summary>
        /// Routes <see cref="Console"/> through the non-blocking relays. Call once at startup,
        /// before anything else writes to the console.
        /// </summary>
        public static void InstallConsoleRelay()
        {
            Console.SetOut(new ConsoleRelayWriter(_stdoutRelay));
            Console.SetError(new ConsoleRelayWriter(_stderrRelay));
        }

        public static void LogVerbose(string message, string? details = null)
        {
            WriteLog(LogLevel.Verbose, message, details);
        }

        public static void LogInfo(string message, string? details = null)
        {
            WriteLog(LogLevel.Information, message, details);
        }

        public static void LogWarning(string message, string? details = null)
        {
            WriteLog(LogLevel.Warning, message, details);
        }

        public static void LogError(string message, Exception? exception = null)
        {
            var details = exception != null ?
                $"Exception: {exception.GetType().Name}\nMessage: {exception.Message}\nStackTrace: {exception.StackTrace}" :
                null;
            WriteLog(LogLevel.Error, message, details);
        }

        public static void LogCrash(Exception exception, string context = "Application")
        {
            var sb = new StringBuilder();
            sb.AppendLine($"=== CRASH REPORT ===");
            sb.AppendLine($"Context: {context}");
            sb.AppendLine($"Exception Type: {exception.GetType().FullName}");
            sb.AppendLine($"Message: {exception.Message}");
            sb.AppendLine($"Stack Trace:");
            sb.AppendLine(exception.StackTrace);

            var innerEx = exception.InnerException;
            var level = 1;
            while (innerEx != null)
            {
                sb.AppendLine($"--- Inner Exception {level} ---");
                sb.AppendLine($"Type: {innerEx.GetType().FullName}");
                sb.AppendLine($"Message: {innerEx.Message}");
                sb.AppendLine($"Stack Trace:");
                sb.AppendLine(innerEx.StackTrace);
                innerEx = innerEx.InnerException;
                level++;
            }

            sb.AppendLine($"=== END CRASH REPORT ===");

            WriteDirect(Format("CRASH", "Application crashed", sb.ToString()));

            try { Console.WriteLine($"CRASH: {exception.Message} (details: {_logFilePath})"); }
            catch { /* Ignore console errors */ }
        }

        /// <summary>
        /// Writes <paramref name="body"/> straight to the log file, bypassing the queue, and
        /// fsyncs. For diagnostics that must survive a hard kill or a wedged drain thread.
        /// </summary>
        public static void WriteDirect(string body)
        {
            if (string.IsNullOrEmpty(_logFilePath)) return;
            try
            {
                lock (_fileLock)
                {
                    var stream = EnsureStream();
                    if (stream == null) return;
                    var bytes = Encoding.UTF8.GetBytes(body);
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(flushToDisk: true);
                    _dirty = false;
                }
            }
            catch { /* never throw from the logger */ }
        }

        /// <summary>
        /// The single funnel for level-bearing entries, and so the only place the filter lives.
        /// <see cref="LogCrash"/> and <see cref="WriteDirect"/> bypass it deliberately.
        /// </summary>
        private static void WriteLog(LogLevel level, string message, string? details = null)
        {
            if (level > _minLevel) return;
            var entry = Format(TagFor(level), message, details);
            _stdoutRelay.Enqueue(entry);
            QueueForFile(entry);
        }

        private static string Format(string level, string message, string? details)
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var header = $"[{timestamp}] [{level}] {message}";
            return string.IsNullOrEmpty(details)
                ? header + Environment.NewLine
                : header + Environment.NewLine + details + Environment.NewLine;
        }

        private static void QueueForFile(string entry)
        {
            if (string.IsNullOrEmpty(_logFilePath)) return;
            if (!_fileQueue.TryAdd(entry))
                Interlocked.Increment(ref _fileDropped);
        }

        private static void FileDrainLoop()
        {
            while (true)
            {
                try
                {
                    if (_fileQueue.TryTake(out var entry, FlushIntervalMs))
                    {
                        AppendToFile(entry);

                        var dropped = Interlocked.Exchange(ref _fileDropped, 0);
                        if (dropped > 0)
                            AppendToFile(Format("WARN", $"{dropped} log entries dropped — writer was not keeping up", null));

                        ReportConsoleDrops();

                        // Flush at the end of each burst rather than only when idle, so a hard
                        // kill loses at most the entries still queued behind this one.
                        if (_fileQueue.Count == 0) FlushFile();
                    }
                    else
                    {
                        ReportConsoleDrops();
                        FlushFile();
                    }
                }
                catch { /* a dead drain thread silently loses all logging */ }
            }
        }

        /// <summary>
        /// Mirrors stdout-relay drops into the log file. A stalled pipe reader is exactly when
        /// the relay's own in-band notice cannot be seen.
        /// </summary>
        private static void ReportConsoleDrops()
        {
            var total = _stdoutRelay.DroppedTotal;
            if (total == _reportedConsoleDrops) return;
            // Throttled: drops arrive in floods, and one warning per drain pass would itself
            // become the bulk of the log.
            if (Environment.TickCount64 - _lastConsoleDropReportTicks < ConsoleDropReportIntervalMs) return;
            _lastConsoleDropReportTicks = Environment.TickCount64;
            _reportedConsoleDrops = total;
            AppendToFile(Format("WARN", $"{total} console line(s) dropped since start — stdout reader is not draining", null));
        }

        private static void AppendToFile(string entry)
        {
            lock (_fileLock)
            {
                var stream = EnsureStream();
                if (stream == null) return;
                var bytes = Encoding.UTF8.GetBytes(entry);
                stream.Write(bytes, 0, bytes.Length);
                _dirty = true;
            }
        }

        private static void FlushFile()
        {
            lock (_fileLock)
            {
                if (!_dirty || _stream == null) return;
                try
                {
                    _stream.Flush();
                    _dirty = false;
                }
                catch { /* retried on the next tick */ }
            }
        }

        private static FileStream? EnsureStream()
        {
            if (_stream != null) return _stream;
            if (string.IsNullOrEmpty(_logFilePath)) return null;
            try
            {
                // ReadWrite share so a second instance can hold an append handle the same day,
                // matching the interleaving the old open-per-entry write allowed.
                _stream = new FileStream(
                    _logFilePath,
                    FileMode.Append,
                    FileAccess.Write,
                    FileShare.ReadWrite,
                    bufferSize: 16384,
                    FileOptions.None);
            }
            catch
            {
                _stream = null;
            }
            return _stream;
        }

        /// <summary>
        /// Drains anything still queued and forces it to disk. Call from shutdown handlers
        /// (ProcessExit, etc.) so final entries survive process termination.
        /// </summary>
        public static void Flush()
        {
            var deadline = Stopwatch.StartNew();
            while (_fileQueue.Count > 0 && deadline.ElapsedMilliseconds < 2000)
                Thread.Sleep(10);

            lock (_fileLock)
            {
                if (_stream == null) return;
                try
                {
                    _stream.Flush(flushToDisk: true);
                    _dirty = false;
                }
                catch { /* best-effort */ }
            }
        }

        public static string? GetLogFilePath() => _logFilePath;
    }
}
