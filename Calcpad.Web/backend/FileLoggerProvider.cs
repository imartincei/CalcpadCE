using Microsoft.Extensions.Logging;
using MsLogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Calcpad.Server
{
    /// <summary>
    /// Routes Microsoft.Extensions.Logging through <see cref="FileLogger"/>, so framework entries
    /// obey the same level as ours and reach the same file. A provider rather than a log filter
    /// because filters are cached per (provider, category) when the host is built, which froze the
    /// framework at whatever level startup happened to have; <see cref="FileLogger"/> re-reads its
    /// own on every entry, so <c>/api/calcpad/log-level</c> now applies without a restart.
    /// </summary>
    public sealed class FileLoggerProvider : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new CategoryLogger(categoryName);

        public void Dispose() { }

        private sealed class CategoryLogger(string category) : ILogger
        {
            // Kestrel's "Now listening on: <url>", which the Tauri host sniffs out of stdout when
            // the port file is unreadable (extract_listening_url in src-tauri/src/lib.rs). It is
            // one line per launch, and without it a random-port server cannot be found at all.
            private const string LifetimeCategory = "Microsoft.Hosting.Lifetime";
            private const int ListeningOnAddressEventId = 14;

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            // The lifetime category is always enabled so the listening line below reaches Log at
            // any level — the framework skips Log entirely when IsEnabled says no. It emits only a
            // handful of entries per process, and Log still filters the rest of them by level.
            public bool IsEnabled(MsLogLevel logLevel) =>
                category == LifetimeCategory || Enabled(logLevel);

            public void Log<TState>(
                MsLogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (category == LifetimeCategory && eventId.Id == ListeningOnAddressEventId)
                {
                    FileLogger.LogAlways(formatter(state, exception));
                    return;
                }

                if (!Enabled(logLevel)) return;

                var message = $"{category}: {formatter(state, exception)}";
                switch (LevelFor(logLevel))
                {
                    case LogLevel.Error: FileLogger.LogError(message, exception); break;
                    case LogLevel.Warning: FileLogger.LogWarning(message, exception?.ToString()); break;
                    default: FileLogger.LogVerbose(message, exception?.ToString()); break;
                }
            }

            private static bool Enabled(MsLogLevel logLevel) =>
                LevelFor(logLevel) is { } level && level <= FileLogger.MinLevel;

            /// <summary>
            /// The framework is far chattier than we are at the same nominal level — its
            /// Information is several lines per request and its Trace is a hundred — so its
            /// Information and Debug both land on Verbose, the level documented as "a line per
            /// request". Trace is dropped outright: nothing maps to it.
            /// </summary>
            private static LogLevel? LevelFor(MsLogLevel level) => level switch
            {
                MsLogLevel.Critical or MsLogLevel.Error => LogLevel.Error,
                MsLogLevel.Warning => LogLevel.Warning,
                MsLogLevel.Information or MsLogLevel.Debug => LogLevel.Verbose,
                _ => null,
            };
        }
    }
}
