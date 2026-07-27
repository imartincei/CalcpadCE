import { invoke } from '@tauri-apps/api/core';
import { listen, type UnlistenFn } from '@tauri-apps/api/event';
import { join } from '@tauri-apps/api/path';
import { readTextFile, writeTextFile, exists } from '@tauri-apps/plugin-fs';
import { buildCrashRecord } from 'calcpad-frontend';

/**
 * Manages the client-side view of the Calcpad.Server lifecycle. The actual
 * process is spawned and killed by the Tauri Rust layer (see src-tauri/src/lib.rs):
 *   - `spawn_sidecar` runs at app startup and emits `server-url` when Kestrel binds.
 *   - `stop_server` / `restart_server` are exposed as invoke commands.
 *   - The child is killed on window close and app exit — no TS shutdown work.
 *
 * This class keeps the crash counter and the "auto-restart exhausted" UI hook,
 * which are the parts the frontend has to own. Everything else is delegated.
 */

const MAX_AUTO_RESTARTS = 3;
const AUTO_RESTART_DELAY_MS = 2000;
const START_TIMEOUT_MS = 30_000;
// A server that stays up this long is treated as recovered, so the crash
// streak resets. Resetting immediately on bind (crashes almost always happen
// after a successful start) would let a crash-on-use loop restart forever and
// never hit the give-up threshold.
const STABLE_RESET_MS = 30_000;

export interface ServerManagerLogger {
    appendLine(message: string): void;
}

interface ServerCrashPayload {
    code: number | null;
    tail: string;
}

interface ServerLogPayload {
    stream: 'stdout' | 'stderr';
    line: string;
}

export class TauriServerManager {
    private url = '';
    private _isRunning = false;
    private _crashCount = 0;
    private _stableTimer: ReturnType<typeof setTimeout> | null = null;
    private unlistenUrl: UnlistenFn | null = null;
    private unlistenCrash: UnlistenFn | null = null;
    private unlistenStartupError: UnlistenFn | null = null;
    private unlistenLog: UnlistenFn | null = null;
    private logger: ServerManagerLogger;

    public onCrashExhausted?: (crashOutput: string) => void;
    public onUrlChanged?: (newUrl: string) => void;
    public onStartupBlocked?: (details: string) => void;
    public onServerLog?: (line: string, stream: 'stdout' | 'stderr') => void;

    constructor(logger: ServerManagerLogger) {
        this.logger = logger;
    }

    setLogger(logger: ServerManagerLogger): void {
        this.logger = logger;
    }

    get isRunning(): boolean { return this._isRunning; }
    getBaseUrl(): string { return this.url; }

    async start(): Promise<void> {
        // Callers block on start() so the API bridge sees a real URL from
        // its first request. Resolve as soon as we learn the URL from either
        // channel; time out after START_TIMEOUT_MS to avoid a wedged boot.
        const startedAt = performance.now();
        const t = () => Math.round(performance.now() - startedAt);
        this.log(`[timing] start() called`);
        let resolveReady: ((url: string) => void) | null = null;
        let rejectReady: ((err: Error) => void) | null = null;
        const ready = new Promise<string>((resolve, reject) => {
            resolveReady = resolve;
            rejectReady = reject;
        });

        try {
            this.unlistenUrl = await listen<string>('server-url', (evt) => {
                this.url = evt.payload;
                this._isRunning = true;
                this.scheduleStabilityReset();
                this.log(`[timing] server-url event received ${t()}ms after start()`);
                this.log(`Server ready at ${this.url}`);
                this.onUrlChanged?.(this.url);
                resolveReady?.(this.url);
                resolveReady = null;
            });
            this.log(`[timing] server-url listener ready at ${t()}ms`);
        } catch (err) {
            this.log(`[timing] server-url listener FAILED at ${t()}ms: ${err instanceof Error ? err.message : String(err)}`);
            throw err;
        }

        this.unlistenCrash = await listen<ServerCrashPayload>('server-crashed', (evt) => {
            this._isRunning = false;
            this.url = '';
            // Crash before the stability window elapsed — the streak stands.
            this.clearStabilityReset();
            this._crashCount++;
            this.log(`Server crashed (code=${evt.payload.code ?? 'unknown'}) — attempt ${this._crashCount}/${MAX_AUTO_RESTARTS}`);
            void this.writeCrashRecord(evt.payload);
            if (this._crashCount < MAX_AUTO_RESTARTS) {
                setTimeout(() => { void this.autoRestart(); }, AUTO_RESTART_DELAY_MS);
            } else {
                this.onCrashExhausted?.(evt.payload.tail || '');
            }
        });

        this.unlistenLog = await listen<ServerLogPayload>('server-log', (evt) => {
            this.onServerLog?.(evt.payload.line, evt.payload.stream);
        });

        this.unlistenStartupError = await listen<string>('server-startup-error', (evt) => {
            this.log(`Server failed to start: ${evt.payload}`);
            this.onStartupBlocked?.(evt.payload);
            rejectReady?.(new Error(evt.payload));
            rejectReady = null;
            resolveReady = null;
        });

        this.log(`[timing] all listeners ready at ${t()}ms`);

        // Rust starts spawning the sidecar in setup() before our listeners
        // registered, so first check if the URL is already known — otherwise
        // wait for the server-url event to fire.
        try {
            const current = await invoke<string | null>('server_url');
            this.log(`[timing] server_url invoke returned "${current ?? 'null'}" at ${t()}ms`);
            if (current) {
                this.url = current;
                this._isRunning = true;
                this.log(`Server already running at ${current}`);
                this.onUrlChanged?.(this.url);
                return;
            }
        } catch (err) {
            this.log(`[timing] server_url invoke failed at ${t()}ms: ${err instanceof Error ? err.message : String(err)}`);
        }

        this.log(`[timing] awaiting server-url event race at ${t()}ms`);
        const timeout = new Promise<never>((_, reject) =>
            setTimeout(() => reject(new Error(`server did not report a URL within ${START_TIMEOUT_MS}ms`)), START_TIMEOUT_MS),
        );
        try {
            await Promise.race([ready, timeout]);
            this.log(`[timing] race resolved at ${t()}ms`);
        } catch (err) {
            this.log(`[timing] race REJECTED at ${t()}ms: ${err instanceof Error ? err.message : String(err)}`);
            throw err;
        }
    }

    /** Ask Rust to stop the sidecar. */
    async stop(): Promise<void> {
        try {
            await invoke('stop_server');
        } catch (err) {
            this.log(`stop_server invoke failed: ${err instanceof Error ? err.message : String(err)}`);
        }
        this._isRunning = false;
        this.url = '';
    }

    /** Manual force-stop then respawn via Rust (menu / refresh). Resets the crash streak. */
    async restart(): Promise<void> {
        this.clearStabilityReset();
        this._crashCount = 0;
        try {
            const newUrl = await invoke<string>('restart_server');
            this.url = newUrl;
            this._isRunning = true;
            this.onUrlChanged?.(this.url);
            this.log(`Server restarted at ${newUrl}`);
        } catch (err) {
            const msg = err instanceof Error ? err.message : String(err);
            this.log(`restart_server invoke failed: ${msg}`);
            this._isRunning = false;
            this.url = '';
            throw err;
        }
    }

    /**
     * Auto-restart after a crash. Unlike the manual restart(), this preserves
     * the crash streak so a crash-loop eventually gives up. If the respawn
     * itself fails (crash-on-startup), Rust may never re-emit `server-crashed`,
     * so we reschedule here until the streak is exhausted.
     */
    private async autoRestart(): Promise<void> {
        try {
            const newUrl = await invoke<string>('restart_server');
            this.url = newUrl;
            this._isRunning = true;
            this.onUrlChanged?.(this.url);
            this.log(`Server auto-restarted at ${newUrl} (attempt ${this._crashCount}/${MAX_AUTO_RESTARTS})`);
        } catch (err) {
            const msg = err instanceof Error ? err.message : String(err);
            this._isRunning = false;
            this.url = '';
            this._crashCount++;
            this.log(`Auto-restart failed: ${msg} — attempt ${this._crashCount}/${MAX_AUTO_RESTARTS}`);
            if (this._crashCount < MAX_AUTO_RESTARTS) {
                setTimeout(() => { void this.autoRestart(); }, AUTO_RESTART_DELAY_MS);
            } else {
                this.onCrashExhausted?.(msg);
            }
        }
    }

    /** Clear the crash streak once the server has stayed up past the stability window. */
    private scheduleStabilityReset(): void {
        this.clearStabilityReset();
        this._stableTimer = setTimeout(() => {
            this._stableTimer = null;
            if (this._crashCount > 0) {
                this.log(`Server stable for ${STABLE_RESET_MS / 1000}s — resetting crash streak`);
                this._crashCount = 0;
            }
        }, STABLE_RESET_MS);
    }

    private clearStabilityReset(): void {
        if (this._stableTimer) {
            clearTimeout(this._stableTimer);
            this._stableTimer = null;
        }
    }

    /**
     * Render a human-readable `last-crash.txt` beside the .NET minidump, matching
     * the VS Code extension. The dump's `createdump` crashreport.json carries the
     * managed exception + traceback for faults (StackOverflow / FailFast) that the
     * server's own FileLogger never sees. Formatting is shared via calcpad-frontend
     * so both hosts emit an identical record.
     */
    private async writeCrashRecord(payload: ServerCrashPayload): Promise<void> {
        try {
            const dir = await invoke<string>('log_dir');
            const reportPath = await join(dir, 'last-crash.dmp.crashreport.json');
            // createdump writes the JSON as the runtime tears down; it's normally
            // there by the time the process is reaped and this fires, but retry
            // briefly to absorb filesystem-flush latency.
            let reportJson: string | null = null;
            for (let i = 0; i < 10; i++) {
                if (await exists(reportPath)) {
                    try { reportJson = await readTextFile(reportPath); } catch { /* unreadable */ }
                    break;
                }
                await new Promise(r => setTimeout(r, 200));
            }
            const record = buildCrashRecord({
                timestampIso: new Date().toISOString(),
                code: payload.code,
                signal: null,
                stderrTail: payload.tail || '',
                reportJson,
            });
            await writeTextFile(await join(dir, 'last-crash.txt'), record);
            this.log(`Crash record written to ${dir}/last-crash.txt`);
        } catch (err) {
            this.log(`Could not write crash record: ${err instanceof Error ? err.message : String(err)}`);
        }
    }

    /** Alias — Rust owns kill-on-exit, but the menu action still exists. */
    async forceStop(): Promise<void> {
        return this.stop();
    }

    /** Detach event listeners. Rust reaps the sidecar on window close. */
    async dispose(): Promise<void> {
        this.clearStabilityReset();
        try { this.unlistenUrl?.(); } catch { /* ignore */ }
        try { this.unlistenCrash?.(); } catch { /* ignore */ }
        try { this.unlistenStartupError?.(); } catch { /* ignore */ }
        try { this.unlistenLog?.(); } catch { /* ignore */ }
        this.unlistenUrl = null;
        this.unlistenCrash = null;
        this.unlistenStartupError = null;
        this.unlistenLog = null;
    }

    private log(message: string): void {
        this.logger.appendLine(`[ServerManager] ${message}`);
    }
}
