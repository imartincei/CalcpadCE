using System.Reflection;
using System.Text;

namespace Calcpad.Server
{
    public static class FileLogger
    {
        private static readonly object _lock = new object();
        private static string? _logFilePath;
        
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
                
                // Write initial log entry
                WriteLog("INFO", "Logger initialized", $"Log file: {_logFilePath}");
            }
            catch (Exception ex)
            {
                // If we can't initialize logging, try a fallback location
                try
                {
                    _logFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), 
                        $"CalcpadServer-{DateTime.Now:yyyyMMdd}.log");
                    WriteLog("WARN", "Logger fallback location", $"Using desktop: {_logFilePath}, Error: {ex.Message}");
                }
                catch
                {
                    // If all else fails, we'll just skip logging
                    _logFilePath = null;
                }
            }
        }
        
        public static void LogInfo(string message, string? details = null)
        {
            WriteLog("INFO", message, details);
        }
        
        public static void LogWarning(string message, string? details = null)
        {
            WriteLog("WARN", message, details);
        }
        
        public static void LogError(string message, Exception? exception = null)
        {
            var details = exception != null ? 
                $"Exception: {exception.GetType().Name}\nMessage: {exception.Message}\nStackTrace: {exception.StackTrace}" : 
                null;
            WriteLog("ERROR", message, details);
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
            
            // Include inner exceptions
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
            
            WriteLog("CRASH", "Application crashed", sb.ToString());

            // Console line is captured by the VS Code extension's stdout pipe
            // and surfaced in the server debug channel. The full report is
            // already in the file via WriteLog above.
            try { Console.WriteLine($"CRASH: {exception.Message} (details: {_logFilePath})"); }
            catch { /* Ignore console errors */ }
        }

        private static void WriteLog(string level, string message, string? details = null)
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var header = $"[{timestamp}] [{level}] {message}";
            var logEntry = string.IsNullOrEmpty(details)
                ? header + Environment.NewLine
                : header + Environment.NewLine + details + Environment.NewLine;

            // Echo to stdout so the VS Code extension's stdout pipe surfaces every log entry in
            // the server debug channel; the console is auto-flushed (see Program.cs), so entries
            // appear in real time. Errors here are ignored — the file write below is the source
            // of truth.
            try { Console.Write(logEntry); } catch { /* console may be closed */ }

            if (string.IsNullOrEmpty(_logFilePath))
                return;

            try
            {
                lock (_lock)
                {
                    var bytes = Encoding.UTF8.GetBytes(logEntry);

                    // WriteThrough bypasses the OS write cache and Flush(true) calls
                    // FlushFileBuffers, so entries survive even a hard process kill
                    // (StackOverflow, FailFast). Cost: ~1ms per entry.
                    using var fs = new FileStream(
                        _logFilePath,
                        FileMode.Append,
                        FileAccess.Write,
                        FileShare.Read,
                        bufferSize: 4096,
                        FileOptions.WriteThrough);
                    fs.Write(bytes, 0, bytes.Length);
                    fs.Flush(flushToDisk: true);
                }
            }
            catch
            {
                // If logging fails, we can't do much about it
                // Don't throw exceptions from the logger
            }
        }

        /// <summary>
        /// Forces all buffered log writes to disk. Call from shutdown handlers
        /// (ProcessExit, etc.) to ensure final entries survive process termination.
        /// </summary>
        public static void Flush()
        {
            // WriteLog already opens/writes/flushes/closes per entry, so nothing
            // is buffered between calls. This method exists so callers don't have
            // to know that — and so behavior stays correct if we ever switch to a
            // long-lived stream.
        }

        public static string? GetLogFilePath() => _logFilePath;
    }
}