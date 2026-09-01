using Calcpad.Server;
using Calcpad.Server.Services;
using System.Net;
using System.Runtime.InteropServices;

// Non-blocking stdout/stderr — see ConsoleRelay for why an auto-flushing writer wedges us.
FileLogger.InstallConsoleRelay();

// Hot endpoints are synchronous and block, so a burst outruns the ~1-2/sec injection rate.
ThreadPool.GetMinThreads(out _, out var minIoThreads);
ThreadPool.SetMinThreads(Math.Max(Environment.ProcessorCount * 4, 32), minIoThreads);

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

// Fires on Environment.Exit and clean Main return, but NOT on FailFast or StackOverflow.
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

    // Our own CLI flags, pulled out before the rest goes to ASP.NET.
    // --port-file <path>          Bound base URL, for hosts discovering a random-port server.
    // --exit-on-stdin-close       Exit on stdin EOF. Default when piped; CALCPAD_DETACHED=1 opts out.
    // --no-exit-on-stdin-close    Force off. Tauri uses --parent-pid instead.
    // --parent-pid <pid>          Poll that PID every 2s; exit when it disappears.
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
        FileLogger.LogVerbose("Parent PID watchdog enabled", watchedPid.ToString());
        _ = Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    // Disposed: undisposed, this leaks a native handle every 2s to the finalizer.
                    using var parent = System.Diagnostics.Process.GetProcessById(watchedPid);
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

    // EOF watchdog: on when stdin is piped, since that parent can die and orphan us.
    // CALCPAD_DETACHED=1 opts out — VS Code shares one server across windows.
    bool detached = Environment.GetEnvironmentVariable("CALCPAD_DETACHED") == "1";
    bool exitOnStdinClose = exitOnStdinCloseExplicit ?? (Console.IsInputRedirected && !detached);

    // Defaults next to the binary. Always wiped at startup, so no frontend reads a dead URL.
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
        // stdin stays open until the parent dies, so EOF is the death signal for any
        // stdio-piping parent (build scripts, raw shells).
        _ = Task.Run(async () =>
        {
            try
            {
                while (await Console.In.ReadLineAsync().ConfigureAwait(false) != null) { /* drain */ }
                FileLogger.LogInfo("stdin EOF; parent likely exited — shutting down");
            }
            catch (Exception ex)
            {
                FileLogger.LogWarning("watchdog error", ex.Message);
            }

            // Environment.Exit bypasses ApplicationStopping, so clear the port file here.
            if (!string.IsNullOrEmpty(portFile))
            {
                try { if (File.Exists(portFile)) File.Delete(portFile); } catch { /* best-effort */ }
            }
            Environment.Exit(0);
        });
    }

    // With neither --urls nor CALCPAD_PORT, take an OS-assigned port over the legacy 9420:
    // what sidecar launches want, and it kills the "address already in use" orphan case.
    // Callers passing either one keep their existing behavior.
    bool hasExplicitUrls = forwardedArgs.Any(a => a == "--urls");
    bool hasExplicitPort = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CALCPAD_PORT"));
    if (!hasExplicitUrls && !hasExplicitPort)
    {
        // Kestrel rejects "localhost:0" for dynamic binding — must be the loopback IP.
        forwardedArgs = forwardedArgs.Concat(new[] { "--urls", "http://127.0.0.1:0" }).ToArray();
        FileLogger.LogVerbose("No explicit URL or port set", "defaulting to http://127.0.0.1:0 (random free port)");
    }
    else if (!hasExplicitUrls)
    {
        // CALCPAD_PORT set — keep the legacy 9420 default through GetServerUrl.
        Environment.SetEnvironmentVariable("CALCPAD_PORT", Environment.GetEnvironmentVariable("CALCPAD_PORT") ?? "9420");
    }

    // Create and configure web application using shared service
    var (app, serverUrl) = CalcpadApiService.CreateConfiguredApp(forwardedArgs);

    // Server mode (non-localhost) is unfinished — the remote-include path is being reworked.
    // Crash early on any non-loopback URL.
    foreach (var u in serverUrl.Split(';', StringSplitOptions.RemoveEmptyEntries))
    {
        if (!Program.IsLoopbackUrl(u))
            throw new InvalidOperationException(
                $"Calcpad server is bound to '{u}' which is not localhost. " +
                "Server mode is in development and not yet supported. " +
                "Set CALCPAD_HOST=127.0.0.1 or pass --urls http://127.0.0.1:<port>.");
    }

    FileLogger.LogInfo("Starting console application", serverUrl);

    // Interactive runs only — piped into a host's Output panel this is just noise.
    if (!Console.IsOutputRedirected)
    {
        Console.WriteLine($"Calcpad Server starting at {serverUrl}");
        Console.WriteLine("Press Ctrl+C to stop the server.");
        Console.WriteLine($"API Documentation: {serverUrl}/swagger");
    }

    var cts = new CancellationTokenSource();

    // SIGINT and SIGTERM on all platforms; Console.CancelKeyPress only covers SIGINT. Nothing
    // may escape: these run on the signal thread, where an exception kills us with no log.
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
    lifetime.ApplicationStopping.Register(() => FileLogger.LogVerbose("ApplicationStopping"));
    lifetime.ApplicationStopped.Register(() => FileLogger.LogVerbose("ApplicationStopped"));

    // The check above validates intent (the UseUrls string); a Kestrel:Endpoints section
    // overrides it entirely. app.Urls is ground truth, re-checked here before the port-file
    // writer so `bindingRejected` is already set when that runs.
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

    // The bound URL, once Kestrel is listening. Tauri polls this to find a random-port server.
    if (!string.IsNullOrEmpty(portFile))
    {
        lifetime.ApplicationStarted.Register(() =>
        {
            // A rejected binding is being torn down; its URL would point at a dying server.
            if (bindingRejected) return;
            try
            {
                // First listening URL — wildcard/random bindings are concrete by now.
                var addresses = app.Urls.ToList();
                var bound = addresses.FirstOrDefault() ?? serverUrl;
                // Linux defaults this into the 1777 temp dir. Tighten while still empty, so
                // the URL is never briefly world-readable.
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

    // Shared across VS Code instances, so it outlives its spawner: exits only on
    // SIGINT/SIGTERM (`calcpad.stopServer`) or OS shutdown.

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
        if (!Console.IsOutputRedirected) Console.WriteLine("Shutting down...");
        await app.StopAsync();
    }

    FileLogger.LogInfo("Application shutdown complete");
}
catch (Exception ex)
{
    FileLogger.LogCrash(ex, "Console application");
    Console.WriteLine($"ERROR: {ex.Message}");
    Console.WriteLine($"Log file: {FileLogger.GetLogFilePath()}");
    FileLogger.Flush();
    throw;
}

// createdump vars (DOTNET_DbgEnableMiniDump etc.) must be in the child env before the runtime
// starts — too late from Main. server-manager.ts owns them at spawn time.

internal static partial class Program
{
    /// <summary>
    /// True if the URL's host is loopback. Gates server-mode bindings for now.
    /// </summary>
    internal static bool IsLoopbackUrl(string urlString)
    {
        if (!Uri.TryCreate(urlString, UriKind.Absolute, out var uri)) return false;
        return IsLoopbackHost(uri.Host);
    }

    /// <summary>
    /// True for a bare hostname denoting this machine — no scheme or port, as in a
    /// <c>Host</c> header.
    /// </summary>
    internal static bool IsLoopbackHost(string host)
    {
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)) return true;
        // Uri.Host keeps IPv6 literals in brackets; IPAddress.TryParse wants them bare.
        var bare = host.StartsWith('[') && host.EndsWith(']') ? host[1..^1] : host;
        return IPAddress.TryParse(bare, out var ip) && IPAddress.IsLoopback(ip);
    }
}
