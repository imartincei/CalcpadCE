import * as net from 'net';
import * as path from 'path';
import * as fs from 'fs';
import * as crypto from 'crypto';
import { spawn, execSync, ChildProcess } from 'child_process';
import type { ILogger } from 'calcpad-frontend';
import { decodeExitCode, buildCrashRecord, API_TOKEN_HEADER } from 'calcpad-frontend';

interface LockFileContents {
    pid: number;
    port: number;
    startedAt: number;
    /** Per-launch token the server requires on `/api` routes. Absent in a pre-token lock. */
    token?: string;
}

/** Lock files carry the API token, so nobody but this user may read one. */
const LOCK_FILE_MODE = 0o600;

/**
 * True for a plausible OS process id.
 *
 * A poisoned lock file reaching `process.kill` unchecked is the problem: on POSIX
 * a negative pid signals a whole process group, and `kill(-1, ...)` signals every
 * process the user owns.
 */
function isValidPid(pid: unknown): pid is number {
    return typeof pid === 'number' && Number.isInteger(pid) && pid > 0 && pid <= 0xffffffff;
}

/**
 * Manages the lifecycle of the bundled CalcPad server process, designed for cross-instance
 * reuse: multiple VS Code windows share one server discovered via a lock file at
 * `{basePath}/bin/.calcpad-server.lock`, and only the first instance to start spawns it.
 * The server is spawned detached, so it exits only via `calcpad.stopServer` or an OS signal.
 */
export class BaseServerManager {
    private static readonly MAX_RESTARTS = 3;

    private serverProcess: ChildProcess | null = null;
    private port: number = 0;
    private authToken: string | null = null;
    private logger: ILogger;
    private mainLogger: ILogger;
    private basePath: string;
    private dotnetPath: string;
    private _isRunning: boolean = false;
    private _owned: boolean = false;
    private _disposed: boolean = false;
    private _startingUp: boolean = false;
    private _restartCount: number = 0;
    private _lastCrashOutput: string[] = [];
    private _processClosed: boolean = false;
    /** Set when the spawn itself failed (EACCES, EPERM, ENOENT etc.). Distinguishes
     *  "Windows blocked the exe" from "process started but crashed". */
    private _spawnFailed: boolean = false;
    private _spawnFailedCode: string | null = null;
    private lockFilePath: string;
    /** Last-seen mtime of last-crash.dmp.crashreport.json (ms). Seeded at boot
     *  so an old dump from a previous extension run doesn't re-fire the watcher. */
    private _lastSeenDumpMtimeMs: number = 0;
    private _crashWatchInterval: ReturnType<typeof setInterval> | null = null;

    /** Called when auto-restart retries are exhausted. Receives the last stderr output. */
    public onCrashExhausted?: (crashOutput: string) => void;

    /**
     * @param logger    Server debug channel — receives stdout (verbose server output).
     * @param mainLogger Main extension log — receives stderr only. Falls back to `logger` if omitted.
     */
    constructor(basePath: string, logger: ILogger, dotnetPath: string = 'dotnet', mainLogger?: ILogger) {
        this.basePath = basePath;
        this.logger = logger;
        this.mainLogger = mainLogger ?? logger;
        this.dotnetPath = dotnetPath;
        this.lockFilePath = path.join(basePath, 'bin', '.calcpad-server.lock');
        this.startCrashWatcher();
    }

    /**
     * Full path to the bundled server executable (the apphost). Surfaced in
     * "Windows blocked the exe" messages so the user can locate the exact file
     * to Unblock in Explorer.
     */
    public getExecutablePath(): string {
        const exeName = process.platform === 'win32' ? 'Calcpad.Server.exe' : 'Calcpad.Server';
        return path.join(this.basePath, 'bin', exeName);
    }

    /**
     * Check if the bundled server DLL exists.
     */
    public static dllExists(basePath: string): boolean {
        const dllPath = path.join(basePath, 'bin', 'Calcpad.Server.dll');
        return fs.existsSync(dllPath);
    }

    /**
     * Check if the bundled native apphost binary exists. When present,
     * the server can be spawned directly without a system `dotnet` —
     * the apphost is a self-contained .NET host (ships libcoreclr +
     * libhostfxr alongside it on Linux/macOS, calcpad-server.exe on
     * Windows).
     */
    public static appHostExists(basePath: string): boolean {
        const exeName = process.platform === 'win32' ? 'Calcpad.Server.exe' : 'Calcpad.Server';
        return fs.existsSync(path.join(basePath, 'bin', exeName));
    }

    /**
     * Read the lock file and verify the recorded server is alive and healthy.
     * Returns the lock contents if reusable, or null if the lock is missing/stale.
     */
    private async tryReuseExistingServer(): Promise<LockFileContents | null> {
        const lock = this.readLockFile();
        if (!lock) {
            if (fs.existsSync(this.lockFilePath)) this.removeLockFile();
            return null;
        }

        try {
            process.kill(lock.pid, 0);
        } catch {
            this.log(`Lock file references dead PID ${lock.pid} — ignoring`);
            this.removeLockFile();
            return null;
        }

        try {
            const response = await fetch(`http://localhost:${lock.port}/api/calcpad/snippets`, {
                headers: lock.token ? { [API_TOKEN_HEADER]: lock.token } : {},
                signal: AbortSignal.timeout(2000)
            });
            if (!response.ok) {
                this.log(`Existing server at port ${lock.port} unhealthy (HTTP ${response.status}) — ignoring`);
                return null;
            }
        } catch (err) {
            this.log(`Existing server at port ${lock.port} unreachable: ${err instanceof Error ? err.message : String(err)}`);
            return null;
        }

        return lock;
    }

    /**
     * Start the bundled server. Cleans up any stale process, allocates a free port,
     * spawns the dotnet process, and waits for the server to become ready.
     */
    public async start(): Promise<void> {
        if (this._isRunning) {
            this.log('Server is already running');
            return;
        }

        // Reuse an existing server from another VS Code window if one is alive.
        const existing = await this.tryReuseExistingServer();
        if (existing) {
            this.port = existing.port;
            this.authToken = existing.token ?? null;
            this._owned = false;
            this._isRunning = true;
            this.log(`Reusing existing server (PID ${existing.pid}) at port ${existing.port}`);
            return;
        }

        const dllPath = path.join(this.basePath, 'bin', 'Calcpad.Server.dll');
        if (!fs.existsSync(dllPath)) {
            throw new Error(`Calcpad.Server.dll not found at ${dllPath}`);
        }

        const candidatePort = await this.findFreePort();
        // Per-launch bearer for the server's /api routes, without which any program on the box
        // has arbitrary file read as the user: loopback binding keeps remote machines out but not
        // other local processes, and `#include` resolution reads any path it is handed. Published
        // only in the 0600 lock file.
        const token = crypto.randomBytes(32).toString('hex');

        // Race guard: atomically claim the lock file before spawning. If another
        // window claimed it in the window between our reuse-check and this line,
        // the `wx` flag makes this throw EEXIST — we then wait for that peer's
        // server to come up and adopt it instead of spawning a duplicate.
        const placeholderLock: LockFileContents = {
            pid: process.pid,  // extension host PID — used by peers to detect if spawner died
            port: candidatePort,
            startedAt: Date.now()
        };
        if (!this.tryClaimLockExclusive(placeholderLock)) {
            this.log('Another window is spawning the server — waiting to adopt it...');
            const adopted = await this.waitForPeerServer(20000);
            if (adopted) {
                this.port = adopted.port;
                this.authToken = adopted.token ?? null;
                this._owned = false;
                this._isRunning = true;
                this.log(`Adopted peer-spawned server (PID ${adopted.pid}) at port ${adopted.port}`);
                return;
            }
            this.log('Timed out waiting for peer server — reclaiming lock and spawning our own');
            this.removeLockFile();
            this.tryClaimLockExclusive(placeholderLock);
        }

        this.port = candidatePort;
        this.authToken = token;
        this.log(`Starting server on port ${this.port}...`);

        const serverUrl = `http://localhost:${this.port}`;

        // Prefer the native apphost exe when available — it shows as
        // "Calcpad.Server" in Task Manager instead of ".NET Host".
        // Falls back to `dotnet Calcpad.Server.dll` for compatibility.
        const exeName = process.platform === 'win32' ? 'Calcpad.Server.exe' : 'Calcpad.Server';
        const exePath = path.join(this.basePath, 'bin', exeName);
        const useAppHost = fs.existsSync(exePath);

        // VSIX packaging strips the executable bit on POSIX, so the bundled apphost can sit
        // on disk while spawn fails silently with EACCES. Re-set the bit (and that of the
        // libraries the apphost dlopens) before every spawn so this self-heals.
        if (useAppHost && process.platform !== 'win32') {
            try {
                fs.chmodSync(exePath, 0o755);
            } catch (err) {
                this.log(`Warning: could not chmod ${exeName}: ${err instanceof Error ? err.message : String(err)}`);
            }
            // createdump is invoked by the .NET runtime on crash; without +x
            // the runtime aborts startup on some distros.
            const createdump = path.join(this.basePath, 'bin', 'createdump');
            if (fs.existsSync(createdump)) {
                try { fs.chmodSync(createdump, 0o755); } catch { /* best-effort */ }
            }
        }

        // On POSIX, `detached: true` starts the child in its own process group so it
        // survives this window exiting; stdio is still piped while we are alive. On Windows
        // we deliberately do NOT detach: DETACHED_PROCESS leaves the server with no console,
        // forcing every console-subsystem browser helper it spawns during PDF generation to
        // allocate its own visible console window, and orphaned children survive anyway.
        //
        // DOTNET_DbgEnableMiniDump writes a minidump on unrecoverable crashes
        // (StackOverflow, FailFast) that bypass the in-process FileLogger. The fixed
        // filename means each crash overwrites the previous dump, and the lock file keeps
        // one server per project so there is no race between concurrent dumps.
        const dumpDir = path.join(this.basePath, 'bin', 'logs');
        try { fs.mkdirSync(dumpDir, { recursive: true }); } catch { /* best-effort */ }
        const childEnv: NodeJS.ProcessEnv = {
            ...process.env,
            DOTNET_DbgEnableMiniDump: '1',
            DOTNET_DbgMiniDumpType: '2',
            DOTNET_DbgMiniDumpName: path.join(dumpDir, 'last-crash.dmp'),
            DOTNET_EnableCrashReport: '1',
            // The server defaults to "exit when stdin EOFs" so the
            // Tauri desktop doesn't leak orphan processes. The VS Code
            // extension shares one server across multiple windows via the
            // lock file, so it must explicitly opt out — without this,
            // closing the spawning window would kill the server even if
            // other VS Code windows are still using it.
            CALCPAD_DETACHED: '1',
            // Env, not argv: argv is world-readable through /proc/{pid}/cmdline on
            // Linux and through WMI on Windows, which would hand the token to
            // exactly the local processes it exists to keep out.
            CALCPAD_API_TOKEN: token,
            // The child inherits our whole environment, so a developer with
            // ASPNETCORE_ENVIRONMENT=Development exported would otherwise get
            // Swagger and the debug-crash endpoint on a shipped extension.
            ASPNETCORE_ENVIRONMENT: 'Production',
        };

        // The apphost probes the standard install locations and PATH, so it cannot find a
        // runtime the extension downloaded into globalStorage on its own — point DOTNET_ROOT
        // at it. A relative `dotnetPath` means "use PATH", where DOTNET_ROOT is left alone so
        // the apphost falls back to standard probing.
        if (path.isAbsolute(this.dotnetPath)) {
            childEnv.DOTNET_ROOT = path.dirname(this.dotnetPath);
        }
        // windowsHide adds CREATE_NO_WINDOW (libuv only applies it when no stdio is
        // inherited — ours are all 'pipe'), giving the server a hidden console that its
        // browser grandchildren inherit instead of each opening their own CMD window. It is a
        // no-op off Windows, and detached must stay off there because DETACHED_PROCESS makes
        // CREATE_NO_WINDOW be ignored.
        const spawnOpts = {
            stdio: ['pipe', 'pipe', 'pipe'] as ['pipe', 'pipe', 'pipe'],
            detached: process.platform !== 'win32',
            windowsHide: true,
            env: childEnv,
            cwd: path.join(this.basePath, 'bin'),
        };
        this.serverProcess = useAppHost
            ? spawn(exePath, ['--urls', serverUrl], spawnOpts)
            : spawn(this.dotnetPath, [dllPath, '--urls', serverUrl], spawnOpts);
        this.serverProcess.unref();
        this._owned = true;
        // Reset spawn-failure state for this attempt; the 'error' handler below will
        // flip these if Windows blocks the exe (Defender / SmartScreen / AppLocker).
        this._spawnFailed = false;
        this._spawnFailedCode = null;
        this._processClosed = false;
        this.log(`Spawned via ${useAppHost ? 'apphost' : 'dotnet'} (PID ${this.serverProcess.pid}, detached)`);

        // Rewrite the lock with the actual child PID (replacing our host-PID placeholder).
        if (this.serverProcess.pid) {
            this.writeLockFile({
                pid: this.serverProcess.pid,
                port: this.port,
                startedAt: Date.now(),
                token
            });
        }

        // stdout → server debug channel only. Not buffered, not surfaced in crash messages.
        this.serverProcess.stdout?.on('data', (data: Buffer) => {
            const text = data.toString().trim();
            this.logger.appendLine(`[ServerManager] [stdout] ${text}`);
        });

        // stderr → main extension log + crash buffer. These are the lines we actually
        // want visible to the user and included in crash reports.
        this.serverProcess.stderr?.on('data', (data: Buffer) => {
            const text = data.toString().trim();
            this.mainLogger.appendLine(`[ServerManager] [stderr] ${text}`);
            this._lastCrashOutput.push(text);
            if (this._lastCrashOutput.length > 20) {
                this._lastCrashOutput.shift();
            }
        });

        // The 'close' event fires after all stdio streams are drained,
        // so _lastCrashOutput is fully populated by the time this fires.
        this.serverProcess.on('close', () => {
            this._processClosed = true;
        });

        // 'error' fires when spawn itself fails (EACCES from Windows Defender, ENOENT for a
        // missing dotnet runtime), in which case 'exit'/'close' may never fire, so
        // _processClosed is flipped here to stop waitForReady polling. No fallback spawn: the
        // user has to unblock the file in Explorer and click Refresh.
        this.serverProcess.on('error', (err: NodeJS.ErrnoException) => {
            const code = err.code ?? '';
            this.log(`[error] Failed to start server: ${err.message}${code ? ` (${code})` : ''}`);
            this._spawnFailed = true;
            this._spawnFailedCode = code;
            this._isRunning = false;

            let detail = `Spawn failed: ${err.message}${code ? ` (${code})` : ''}`;
            if (isPermissionDeniedCode(code)) {
                detail +=
                    `\nWindows blocked the executable (Defender / SmartScreen / AppLocker). ` +
                    `Right-click ${useAppHost ? path.basename(exePath) : path.basename(this.dotnetPath)} ` +
                    `in Windows Explorer → Properties → check "Unblock", then click the CalcpadCE refresh button to retry.`;
            }
            this._lastCrashOutput.push(detail);
            this._processClosed = true; // make waitForReady fast-fail
        });

        this.serverProcess.on('exit', (code, signal) => {
            const decoded = decodeExitCode(code);
            this.log(`[exit] Server process exited (code=${code}${decoded ? ` ${decoded}` : ''}, signal=${signal})`);
            if (code !== null && code !== 0) {
                this.persistCrashRecord(code, signal);
            }
            this._isRunning = false;
            // Don't null out serverProcess during startup — waitForReady checks
            // _processClosed (from the 'close' event) to ensure stderr is fully drained.
            // Nulling here would cause waitForReady to bail before close fires.
            if (!this._startingUp) {
                this.serverProcess = null;
            }
            // Only clear the lock if we owned the process. A non-owner will never
            // see this handler since it doesn't hold a child-process reference.
            if (this._owned) {
                this.removeLockFile();
            }

            // Auto-restart if not intentionally disposed and not in initial startup
            // (during startup, waitForReady will detect the exit and report the error)
            if (!this._disposed && !this._startingUp && code !== 0) {
                this._restartCount++;
                if (this._restartCount < BaseServerManager.MAX_RESTARTS) {
                    this.log(`Unexpected exit — attempting restart ${this._restartCount}/${BaseServerManager.MAX_RESTARTS} in 2 seconds...`);
                    setTimeout(() => {
                        if (!this._disposed) {
                            this.start().catch(err => {
                                this.log(`Restart failed: ${err instanceof Error ? err.message : String(err)}`);
                            });
                        }
                    }, 2000);
                } else {
                    const crashOutput = this._lastCrashOutput.join('\n');
                    this.log(`Server crashed ${this._restartCount} times — auto-restart disabled. Use refresh to restart manually.`);
                    this.onCrashExhausted?.(crashOutput);
                }
            }
        });

        this._startingUp = true;
        try {
            await this.waitForReady(serverUrl);
            this._isRunning = true;
            this._lastCrashOutput = [];
            this.log(`Server is ready at ${serverUrl}`);
        } catch (err) {
            // If the spawn failed (Windows blocked the .exe, dotnet missing, etc.) the
            // child PID is unknown and the placeholder lock still holds the extension
            // host PID. Clean it up so peers don't wait on a phantom server and so a
            // later stop() doesn't try to kill our own process.
            if (this._spawnFailed) {
                this.removeLockFile();
            }
            throw err;
        } finally {
            this._startingUp = false;
            // If the process exited during startup, the exit handler deferred
            // nulling serverProcess so waitForReady could use _processClosed.
            // Clean it up now.
            if (this._processClosed) {
                this.serverProcess = null;
            }
        }
    }

    /**
     * Explicitly kill the server, used by the `calcpad.stopServer` / refresh commands. Kills
     * regardless of ownership — a server spawned by another window is killed by the PID
     * recorded in the lock file.
     */
    public async stop(): Promise<void> {
        this._disposed = true;

        if (!this.serverProcess) {
            // We don't own the process — kill whatever PID the lock file records. SAFETY:
            // when start() failed before the child PID was known, the placeholder lock still
            // carries our own process.pid, and killing that would take down the extension
            // host, so skip the kill and just clean up the stale lock.
            const lock = this.readLockFile();
            if (lock) {
                if (lock.pid === process.pid) {
                    this.log(`Discarding stale placeholder lock (our own PID ${lock.pid}, no child to kill)`);
                } else {
                    this.log(`Stopping shared server (PID ${lock.pid})`);
                    this.killByPid(lock.pid);
                }
                this.removeLockFile();
            } else if (fs.existsSync(this.lockFilePath)) {
                // Present but unreadable or out of range — nothing safe to kill,
                // and leaving it would make every peer wait on a phantom server.
                this.log('Discarding unusable lock file');
                this.removeLockFile();
            }
            this._isRunning = false;
            return;
        }

        this.log('Stopping server...');

        const proc = this.serverProcess;
        const pid = proc.pid;
        const spawnNeverStarted = this._spawnFailed || pid === undefined;
        this.serverProcess = null;
        this._isRunning = false;

        if (pid) {
            this.killByPid(pid);
        }
        if (!spawnNeverStarted) {
            // Wait for the OS to confirm the process is gone. If spawn never produced
            // a real process (e.g. Windows blocked the exe), there's no exit event to
            // wait for — short-circuit so refresh can retry immediately.
            await new Promise<void>((resolve) => {
                if (proc.exitCode !== null) {
                    resolve();
                    return;
                }
                const timeout = setTimeout(() => resolve(), 5000);
                proc.once('exit', () => {
                    clearTimeout(timeout);
                    resolve();
                });
            });
        }

        this.removeLockFile();
        this.log('Server stopped');
    }

    /**
     * Detach from the server without killing it. Used by `deactivate()` so the
     * server keeps running for other VS Code windows (and for this window if
     * the extension reactivates).
     */
    public disconnect(): void {
        this._disposed = true;
        if (this.serverProcess) {
            // We were the owner, and the child was spawned detached + unref'd so it already
            // survives our exit. Drop our handle so Node doesn't keep the event loop alive on
            // the stdio pipes.
            try {
                this.serverProcess.stdout?.destroy();
                this.serverProcess.stderr?.destroy();
                this.serverProcess.stdin?.end();
            } catch {
                // best-effort
            }
            this.serverProcess = null;
        }
        this._isRunning = false;
        this.log('Disconnected from server (left running for other instances)');
    }

    private killByPid(pid: number): void {
        if (process.platform === 'win32') {
            try {
                execSync(`taskkill /F /T /PID ${pid}`, { timeout: 10000, stdio: 'ignore' });
            } catch {
                // already dead
            }
        } else {
            try { process.kill(pid, 'SIGTERM'); } catch { /* already dead */ }
            setTimeout(() => {
                try { process.kill(pid, 'SIGKILL'); } catch { /* already dead */ }
            }, 5000);
        }
    }

    /**
     * Get the base URL of the running server.
     */
    public getBaseUrl(): string {
        return `http://localhost:${this.port}`;
    }

    /**
     * Token this server requires on `/api` routes — ours if we spawned it, the
     * adopted one from the lock file if a peer window did. Null before `start()`,
     * and for a server that predates the token (an older extension's orphan).
     */
    public getAuthToken(): string | null {
        return this.authToken;
    }

    public get isRunning(): boolean {
        return this._isRunning;
    }

    /**
     * Stop and restart the server, resetting the retry counter.
     * Use this for manual restarts (e.g., refresh button).
     */
    public async restart(): Promise<void> {
        this._disposed = false;
        this._restartCount = 0;
        await this.stop();
        this._disposed = false; // stop() sets _disposed = true
        await this.start();
    }

    public getLastCrashOutput(): string {
        return this._lastCrashOutput.join('\n');
    }

    /**
     * Absolute path of the directory holding server logs, crash records, and
     * minidumps. Folder is created on demand by the spawn / persist paths,
     * so callers should ensure it exists (e.g. via fs.mkdirSync(..., { recursive: true }))
     * before opening it in the OS file explorer.
     */
    public getLogsDirectory(): string {
        return path.join(this.basePath, 'bin', 'logs');
    }

    public dispose(): void {
        // Default disposal = disconnect, not kill. Use stop() for explicit kill.
        this.stopCrashWatcher();
        this.disconnect();
    }

    private writeLockFile(lock: LockFileContents): void {
        try {
            fs.writeFileSync(this.lockFilePath, JSON.stringify(lock), { encoding: 'utf-8', mode: LOCK_FILE_MODE });
        } catch (err) {
            this.log(`Warning: Could not write lock file: ${err instanceof Error ? err.message : String(err)}`);
        }
    }

    /**
     * Atomic exclusive-create: write the lock only if no lock file currently exists.
     * Returns true if this process won the claim, false on EEXIST (peer beat us).
     */
    private tryClaimLockExclusive(lock: LockFileContents): boolean {
        try {
            fs.writeFileSync(this.lockFilePath, JSON.stringify(lock), { encoding: 'utf-8', flag: 'wx', mode: LOCK_FILE_MODE });
            return true;
        } catch (err: unknown) {
            if (err && typeof err === 'object' && 'code' in err && (err as { code: string }).code === 'EEXIST') {
                return false;
            }
            this.log(`Warning: Could not claim lock file: ${err instanceof Error ? err.message : String(err)}`);
            return false;
        }
    }

    /**
     * Poll for a peer-spawned server to come online and become healthy.
     * Used when we lost the lock-claim race: another window is in the middle
     * of spawning, and we want to reuse its server rather than spawn our own.
     */
    private async waitForPeerServer(timeoutMs: number): Promise<LockFileContents | null> {
        const deadline = Date.now() + timeoutMs;
        while (Date.now() < deadline) {
            const existing = await this.tryReuseExistingServer();
            if (existing) {
                return existing;
            }
            // If the lock has disappeared, the peer aborted — stop waiting.
            if (!fs.existsSync(this.lockFilePath)) {
                return null;
            }
            await new Promise(r => setTimeout(r, 500));
        }
        return null;
    }

    private readLockFile(): LockFileContents | null {
        try {
            if (!fs.existsSync(this.lockFilePath)) {
                return null;
            }
            const lock = JSON.parse(fs.readFileSync(this.lockFilePath, 'utf-8'));
            if (!isValidPid(lock.pid) || typeof lock.port !== 'number'
                || !Number.isInteger(lock.port) || lock.port < 1 || lock.port > 65535) {
                return null;
            }
            if (lock.token !== undefined && typeof lock.token !== 'string') {
                return null;
            }
            return lock;
        } catch {
            return null;
        }
    }

    private removeLockFile(): void {
        try {
            if (fs.existsSync(this.lockFilePath)) {
                fs.unlinkSync(this.lockFilePath);
            }
        } catch {
            // Ignore — best effort cleanup
        }
    }

    private async findFreePort(): Promise<number> {
        return new Promise((resolve, reject) => {
            const server = net.createServer();
            server.listen(0, '127.0.0.1', () => {
                const address = server.address();
                if (address && typeof address !== 'string') {
                    const port = address.port;
                    server.close(() => resolve(port));
                } else {
                    server.close(() => reject(new Error('Could not allocate port')));
                }
            });
            server.on('error', reject);
        });
    }

    private async waitForReady(serverUrl: string, maxAttempts: number = 60, intervalMs: number = 500): Promise<void> {
        const healthUrl = `${serverUrl}/api/calcpad/snippets`;

        for (let i = 0; i < maxAttempts; i++) {
            // Fail fast if the server process has fully closed (stdio drained).
            // We check _processClosed (set by 'close' event) instead of exitCode
            // because 'close' fires after all stderr data events, ensuring
            // _lastCrashOutput is fully populated before we read it.
            if (!this.serverProcess || this._processClosed) {
                // Crash info comes from stderr (what we actually surface) plus the
                // server log file as fallback. stdout is intentionally excluded —
                // it's informational and goes only to the server debug channel.
                const stderr = this._lastCrashOutput.join('\n');
                const logFile = this.readServerLogFile();
                const parts: string[] = [];
                if (stderr) { parts.push(`[stderr]\n${stderr}`); }
                if (!stderr && logFile) { parts.push(`[log file]\n${logFile}`); }
                const crashOutput = parts.join('\n\n');
                throw new Error(
                    crashOutput
                        ? `Server process crashed during startup:\n${crashOutput}`
                        : 'Server process exited unexpectedly during startup (no output captured)'
                );
            }

            try {
                const response = await fetch(healthUrl, {
                    headers: this.authToken ? { [API_TOKEN_HEADER]: this.authToken } : {},
                });
                if (response.ok) {
                    return;
                }
            } catch {
                // Server not ready yet
            }
            await new Promise(r => setTimeout(r, intervalMs));
        }

        throw new Error(`Server did not become ready within ${maxAttempts * intervalMs / 1000} seconds`);
    }

    /**
     * Read the most recent server log file as a fallback when stderr capture is empty.
     * The server writes crash details via FileLogger to bin/logs/CalcpadServer-{date}.log.
     */
    private readServerLogFile(): string {
        try {
            const today = new Date();
            const dateStr = today.getFullYear().toString()
                + (today.getMonth() + 1).toString().padStart(2, '0')
                + today.getDate().toString().padStart(2, '0');
            const logPath = path.join(this.basePath, 'bin', 'logs', `CalcpadServer-${dateStr}.log`);

            if (!fs.existsSync(logPath)) {
                return '';
            }

            const content = fs.readFileSync(logPath, 'utf-8');
            // Return the last 40 lines to capture the most recent crash
            const lines = content.split('\n');
            return lines.slice(-40).join('\n').trim();
        } catch {
            return '';
        }
    }

    private log(message: string): void {
        this.logger.appendLine(`[ServerManager] ${message}`);
    }

    /**
     * Persist a crash record to disk so it survives extension reload, complementing the
     * in-process FileLogger which cannot capture StackOverflow / FailFast paths. Always
     * writes to a fixed `last-crash.txt`, matching the dump-file rolling-overwrite policy.
     */
    private persistCrashRecord(code: number | null, signal: NodeJS.Signals | null): void {
        this.writeCrashTxt(code, signal);
    }

    /**
     * Write `last-crash.txt` with whatever crash info is available: exit code/signal, the
     * in-memory stderr tail, and a parsed traceback from `last-crash.dmp.crashreport.json`.
     * Called from both the 'exit' handler (has exit info, may run before the JSON exists)
     * and the crash watcher (catches dumps the exit handler missed), so the on-disk format
     * is identical either way and last writer wins.
     */
    private writeCrashTxt(code: number | null, signal: NodeJS.Signals | null): void {
        try {
            const crashDir = path.join(this.basePath, 'bin', 'logs');
            fs.mkdirSync(crashDir, { recursive: true });
            const file = path.join(crashDir, 'last-crash.txt');

            const reportPath = path.join(crashDir, 'last-crash.dmp.crashreport.json');
            let reportJson: string | null = null;
            try { reportJson = fs.readFileSync(reportPath, 'utf-8'); } catch { /* no dump report */ }

            // buildCrashRecord (calcpad-frontend) owns the format so this and the
            // Tauri desktop shell emit an identical record.
            const record = buildCrashRecord({
                timestampIso: new Date().toISOString(),
                code,
                signal,
                stderrTail: this._lastCrashOutput.join('\n'),
                reportJson,
            });

            fs.writeFileSync(file, record, 'utf-8');
            this.mainLogger.appendLine(`[ServerManager] Crash record written: ${file}`);
        } catch (err) {
            this.log(`Warning: could not write crash record: ${err instanceof Error ? err.message : String(err)}`);
        }
    }

    /**
     * Poll the dump JSON's mtime every 5 seconds and regenerate `last-crash.txt` when it
     * advances. Polling rather than fs.watch, since the watcher must survive stop/start cycles and
     * adopted servers and fs.watch disagrees across platforms about createdump's fresh JSON, and
     * seeded at construction so a stale dump from a previous run doesn't immediately re-fire.
     */
    private startCrashWatcher(): void {
        if (this._crashWatchInterval) return;
        const reportPath = path.join(this.basePath, 'bin', 'logs', 'last-crash.dmp.crashreport.json');
        try {
            this._lastSeenDumpMtimeMs = fs.statSync(reportPath).mtimeMs;
        } catch {
            this._lastSeenDumpMtimeMs = 0;
        }
        this._crashWatchInterval = setInterval(() => {
            let mtimeMs: number;
            try {
                mtimeMs = fs.statSync(reportPath).mtimeMs;
            } catch {
                return;
            }
            if (mtimeMs > this._lastSeenDumpMtimeMs) {
                this._lastSeenDumpMtimeMs = mtimeMs;
                this.log(`Detected fresh crash dump (mtime=${new Date(mtimeMs).toISOString()})`);
                this.writeCrashTxt(null, null);
            }
        }, 5000);
    }

    private stopCrashWatcher(): void {
        if (this._crashWatchInterval) {
            clearInterval(this._crashWatchInterval);
            this._crashWatchInterval = null;
        }
    }
}

/**
 * True when a libuv spawn-error code indicates Windows (or another OS) refused to
 * start the executable. EACCES/EPERM cover Defender, SmartScreen, AppLocker, and
 * NTFS permission denials; we treat all of them the same and tell the user to
 * unblock the file.
 */
function isPermissionDeniedCode(code: string | null | undefined): boolean {
    return code === 'EACCES' || code === 'EPERM';
}
