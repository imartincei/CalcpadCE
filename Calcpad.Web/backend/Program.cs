using Calcpad.Server;
using Calcpad.Server.Services;
using System.Net;
using System.Runtime.InteropServices;

// Auto-flush stdout so the parent process (VS Code extension) sees logs in real time
// when stdio is piped (non-TTY), instead of waiting for buffer flush on exit.
Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });

// Set up global exception handling
AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
{
    FileLogger.LogCrash((Exception)e.ExceptionObject, "AppDomain.UnhandledException");
};

TaskScheduler.UnobservedTaskException += (sender, e) =>
{
    FileLogger.LogCrash(e.Exception, "TaskScheduler.UnobservedTaskException");
    e.SetObserved();
};

// ProcessExit fires on Environment.Exit and clean Main return, but NOT on
// FailFast or StackOverflow. Useful for catching the "graceful but unexpected"
// exit path — and for flushing the log on normal shutdown.
AppDomain.CurrentDomain.ProcessExit += (sender, e) =>
{
    FileLogger.LogInfo("ProcessExit fired", $"ExitCode={Environment.ExitCode}");
    FileLogger.Flush();
};

try
{
    FileLogger.LogInfo("Starting Calcpad Server");

    // Set default environment variables if not set
    Environment.SetEnvironmentVariable("CALCPAD_HOST", Environment.GetEnvironmentVariable("CALCPAD_HOST") ?? "127.0.0.1");

    // Pull our custom CLI flags out of args before handing them to ASP.NET.
    // --port-file <path>          Write the bound base URL to this file once
    //                             Kestrel is listening. Used by the Tauri
    //                             desktop host to discover the random-port
    //                             server without hard-coding 9420.
    // --exit-on-stdin-close       When stdin reaches EOF (parent process died
    //                             and dropped our stdin pipe), exit. On by
    //                             default whenever stdin is piped; opt out
    //                             via env CALCPAD_DETACHED=1 (VS Code does
    //                             this so the server outlives the spawning
    //                             window and is shared across instances).
    // --no-exit-on-stdin-close    Force-disable the watchdog (overrides the
    //                             default-on logic for stdin-piped launches).
    //                             Tauri sets this because it uses --parent-pid
    //                             for death detection instead.
    // --parent-pid <pid>          Poll the given PID every 2s; self-exit when
    //                             that process disappears. Defense-in-depth
    //                             for the Tauri host: if Rust panics or is
    //                             SIGKILL'd without a chance to run its
    //                             kill-on-exit hook, this reaps the server.
    string? portFile = null;
    bool? exitOnStdinCloseExplicit = null;
    int? parentPid = null;
    var passthroughArgs = new List<string>(args.Length);
    for (int i = 0; i < args.Length; i++)
    {
        if (args[i] == "--port-file" && i + 1 < args.Length)
        {
            portFile = args[++i];
        }
        else if (args[i] == "--exit-on-stdin-close")
        {
            exitOnStdinCloseExplicit = true;
        }
        else if (args[i] == "--no-exit-on-stdin-close")
        {
            exitOnStdinCloseExplicit = false;
        }
        else if (args[i] == "--parent-pid" && i + 1 < args.Length
                 && int.TryParse(args[++i], out var pidValue))
        {
            parentPid = pidValue;
        }
        else
        {
            passthroughArgs.Add(args[i]);
        }
    }
    var forwardedArgs = passthroughArgs.ToArray();

    if (parentPid is int watchedPid)
    {
        FileLogger.LogInfo("Parent PID watchdog enabled", watchedPid.ToString());
        _ = Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    _ = System.Diagnostics.Process.GetProcessById(watchedPid);
                }
                catch (ArgumentException)
                {
                    FileLogger.LogInfo("Parent process gone — shutting down", watchedPid.ToString());
                    if (!string.IsNullOrEmpty(portFile))
                    {
                        try { if (File.Exists(portFile)) File.Delete(portFile); } catch { /* best-effort */ }
                    }
                    Environment.Exit(0);
                    return;
                }
                catch { /* transient — retry next tick */ }
                await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            }
        });
    }

    // Decide whether the EOF watchdog runs. Default-on when stdin is piped
    // (we're a child of *something* that may die and orphan us), opt-out
    // via CALCPAD_DETACHED=1 (the VS Code extension's server-manager.ts
    // sets this because it shares one server across windows).
    bool detached = Environment.GetEnvironmentVariable("CALCPAD_DETACHED") == "1";
    bool exitOnStdinClose = exitOnStdinCloseExplicit ?? (Console.IsInputRedirected && !detached);

    // Default the port file to a fixed location next to the binary when no
    // path was given explicitly. Always wipe any stale file at startup so
    // the frontend never reads a URL pointing at a previous (dead) server.
    if (portFile == null && !detached)
    {
        portFile = Path.Combine(AppContext.BaseDirectory, ".calcpad-server.port");
    }
    if (!string.IsNullOrEmpty(portFile))
    {
        try { if (File.Exists(portFile)) File.Delete(portFile); } catch { /* best-effort */ }
    }

    if (exitOnStdinClose && Console.IsInputRedirected)
    {
        // Death-detection background task: stdin stays open until the parent
        // dies, so reading until EOF is the death signal for any parent that
        // pipes stdio (build scripts, raw shells). Tauri opts out via
        // --no-exit-on-stdin-close and uses --parent-pid instead.
        _ = Task.Run(async () =>
        {
            try
            {
                while (await Console.In.ReadLineAsync().ConfigureAwait(false) != null) { /* drain */ }
                FileLogger.LogInfo("stdin EOF; parent likely exited — shutting down");
            }
            catch (Exception ex)
            {
                FileLogger.LogInfo("watchdog error", ex.Message);
            }

            // Environment.Exit bypasses ApplicationStopping callbacks, so
            // clean up the port file ourselves before going down — otherwise
            // the next launch sees a stale URL pointing at a dead server.
            if (!string.IsNullOrEmpty(portFile))
            {
                try { if (File.Exists(portFile)) File.Delete(portFile); } catch { /* best-effort */ }
            }
            Environment.Exit(0);
        });
    }

    // When nothing was set explicitly (no --urls, no CALCPAD_PORT env), prefer
    // an OS-assigned random port over the legacy hard-coded 9420 — this is
    // what Tauri sidecar launches want, and it eliminates the "address already
    // in use" failure mode for orphan processes. Callers that need a fixed
    // port (`dotnet run` for dev, the VS Code extension explicitly passing
    // --urls) keep their existing behavior.
    bool hasExplicitUrls = forwardedArgs.Any(a => a == "--urls");
    bool hasExplicitPort = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CALCPAD_PORT"));
    if (!hasExplicitUrls && !hasExplicitPort)
    {
        // Kestrel rejects "localhost:0" with "Dynamic port binding is not
        // supported when binding to localhost" — must be the loopback IP.
        forwardedArgs = forwardedArgs.Concat(new[] { "--urls", "http://127.0.0.1:0" }).ToArray();
        FileLogger.LogInfo("No explicit URL or port set", "defaulting to http://127.0.0.1:0 (random free port)");
    }
    else if (!hasExplicitUrls)
    {
        // CALCPAD_PORT was set explicitly — preserve the legacy 9420 default
        // for that path through GetServerUrl.
        Environment.SetEnvironmentVariable("CALCPAD_PORT", Environment.GetEnvironmentVariable("CALCPAD_PORT") ?? "9420");
    }

    // Create and configure web application using shared service
    var (app, serverUrl) = CalcpadApiService.CreateConfiguredApp(forwardedArgs);

    // Server mode (non-localhost binding) is in development and not yet supported:
    // the include-resolution path that ships uploaded files from a remote browser
    // is being reworked. Crash early if anything resolves to a non-loopback URL.
    foreach (var u in serverUrl.Split(';', StringSplitOptions.RemoveEmptyEntries))
    {
        if (!Program.IsLoopbackUrl(u))
            throw new InvalidOperationException(
                $"Calcpad server is bound to '{u}' which is not localhost. " +
                "Server mode is in development and not yet supported. " +
                "Set CALCPAD_HOST=127.0.0.1 or pass --urls http://127.0.0.1:<port>.");
    }

    FileLogger.LogInfo("Starting console application", serverUrl);
    Console.WriteLine($"Calcpad Server starting at {serverUrl}");
    Console.WriteLine("Press Ctrl+C to stop the server.");
    Console.WriteLine($"API Documentation: {serverUrl}/swagger");
    Console.WriteLine($"Sample Client: Open sample-client.html in a browser");

    var cts = new CancellationTokenSource();

    // Handle SIGINT (Ctrl+C) and SIGTERM (graceful kill from parent) on all platforms.
    // Replaces Console.CancelKeyPress, which only covers SIGINT.
    // An exception escaping one of these runs on the runtime's signal thread, where it
    // kills the process with no managed handler and no log — so nothing may propagate.
    void RequestShutdown(PosixSignalContext ctx, string signal)
    {
        try
        {
            FileLogger.LogInfo($"Received {signal}, shutting down");
            ctx.Cancel = true;
            cts.Cancel();
        }
        catch (Exception ex)
        {
            FileLogger.LogError($"{signal} handler failed", ex);
        }
    }

    using var sigIntReg = PosixSignalRegistration.Create(PosixSignal.SIGINT, ctx => RequestShutdown(ctx, "SIGINT"));
    using var sigTermReg = PosixSignalRegistration.Create(PosixSignal.SIGTERM, ctx => RequestShutdown(ctx, "SIGTERM"));

    // Log ASP.NET Core lifetime transitions so graceful-shutdown progress is visible.
    var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
    lifetime.ApplicationStopping.Register(() => FileLogger.LogInfo("ApplicationStopping"));
    lifetime.ApplicationStopped.Register(() => FileLogger.LogInfo("ApplicationStopped"));

    // The startup check above validates the URL string handed to UseUrls, which is
    // the intent rather than the outcome — a Kestrel:Endpoints section in
    // appsettings.json overrides UseUrls entirely and would never appear in it.
    // app.Urls is the ground truth, so re-check it once Kestrel has actually bound
    // and stop the host if anything non-loopback got through. Registered before the
    // port-file writer so `bindingRejected` is already set when that runs.
    var bindingRejected = false;
    lifetime.ApplicationStarted.Register(() =>
    {
        foreach (var bound in app.Urls)
        {
            if (Program.IsLoopbackUrl(bound)) continue;
            bindingRejected = true;
            FileLogger.LogError(
                $"Bound to non-loopback address '{bound}' — shutting down",
                new InvalidOperationException("non-loopback binding"));
            Console.Error.WriteLine($"ERROR: refusing to serve on non-loopback address '{bound}'.");
            lifetime.StopApplication();
            return;
        }
    });

    // When --port-file was given, write the bound URL once Kestrel is
    // listening. The frontend (Tauri desktop) polls this file to discover
    // the random-port server without hard-coding 9420.
    if (!string.IsNullOrEmpty(portFile))
    {
        lifetime.ApplicationStarted.Register(() =>
        {
            // A rejected binding is being torn down; publishing its URL would point
            // the frontend at a server that is about to stop.
            if (bindingRejected) return;
            try
            {
                // Use the first listening URL — Kestrel resolves any
                // wildcard/random binding to a concrete one by this point.
                var addresses = app.Urls.ToList();
                var bound = addresses.FirstOrDefault() ?? serverUrl;
                // The default port-file location on Linux is the 1777 temp dir, where
                // any local user can read it. Tighten the mode while the file is still
                // empty, so the URL is never briefly world-readable.
                using (File.Create(portFile)) { }
                if (!OperatingSystem.IsWindows())
                    File.SetUnixFileMode(portFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                File.WriteAllText(portFile, bound);
                FileLogger.LogInfo("Wrote port file", $"{portFile} -> {bound}");
            }
            catch (Exception ex)
            {
                FileLogger.LogError($"Failed to write port file {portFile}", ex);
            }
        });
        lifetime.ApplicationStopping.Register(() =>
        {
            try { if (File.Exists(portFile)) File.Delete(portFile); } catch { /* best-effort */ }
        });
    }

    // Server is designed to be shared across multiple VS Code instances, so it
    // intentionally outlives the spawning process. It only exits on explicit
    // SIGINT/SIGTERM (via the `calcpad.stopServer` command) or OS shutdown.

    var runTask = Task.Run(async () =>
    {
        try
        {
            await app.RunAsync();
        }
        catch (Exception ex)
        {
            FileLogger.LogCrash(ex, "Web application");
        }
    });

    try
    {
        await Task.Delay(-1, cts.Token);
    }
    catch (OperationCanceledException)
    {
        Console.WriteLine("Shutting down...");
        await app.StopAsync();
    }

    FileLogger.LogInfo("Application shutdown complete");
}
catch (Exception ex)
{
    FileLogger.LogCrash(ex, "Console application");
    Console.WriteLine($"ERROR: {ex.Message}");
    Console.WriteLine($"Log file: {FileLogger.GetLogFilePath()}");
    throw;
}

// Note: createdump (DOTNET_DbgEnableMiniDump and friends) must be set in the
// child process's environment BEFORE the runtime starts up — setting them from
// inside Main is too late. The VS Code extension's spawn-time env in
// calcpad-frontend/server-manager.ts owns this configuration.

internal static partial class Program
{
    /// <summary>
    /// True if the URL's host resolves to a loopback address (localhost, 127.0.0.0/8, ::1).
    /// Used to gate server-mode bindings until the remote-include path is finished.
    /// </summary>
    internal static bool IsLoopbackUrl(string urlString)
    {
        if (!Uri.TryCreate(urlString, UriKind.Absolute, out var uri)) return false;
        return IsLoopbackHost(uri.Host);
    }

    /// <summary>
    /// True for a bare hostname that denotes this machine (localhost, 127.0.0.0/8, ::1).
    /// Takes a hostname without scheme or port, as found in a <c>Host</c> header.
    /// </summary>
    internal static bool IsLoopbackHost(string host)
    {
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)) return true;
        // Uri.Host keeps IPv6 literals in brackets; IPAddress.TryParse wants them bare.
        var bare = host.StartsWith('[') && host.EndsWith(']') ? host[1..^1] : host;
        return IPAddress.TryParse(bare, out var ip) && IPAddress.IsLoopback(ip);
    }
}
