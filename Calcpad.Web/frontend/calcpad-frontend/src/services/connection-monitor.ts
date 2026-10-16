/**
 * Tracks whether the Calcpad server is answering, and reports recoveries so the app can
 * re-render against a server that just came back.
 *
 * The host pushes lifecycle transitions in (applyLifecycle, or the marks it delegates to) and an
 * adaptive poll is the backstop for what the host can't see — a hang, or a process that died
 * without the process supervisor noticing. Only the probe promotes a state to `connected`: a
 * bound port proves Kestrel is listening, not that the pipeline answers.
 */

export type ServerStatus = 'connected' | 'connecting' | 'disconnected';

/**
 * What the host has told us about the server process. A crash collapses into `starting` when a
 * retry is scheduled and `stopped` when the streak is exhausted — the host owns that counter.
 */
export type ServerLifecycleState = 'starting' | 'running' | 'stopped';

export interface ConnectionMonitorOptions {
    probe: (timeoutMs: number) => Promise<boolean>;
    onStatusChanged: (status: ServerStatus) => void;
    /** Fires on down -> up only, never on the session's first resolved status. */
    onRecovered?: () => void;
    log?: (message: string) => void;
    connectedIntervalMs?: number;
    disconnectedIntervalMs?: number;
    connectingIntervalMs?: number;
    probeTimeoutMs?: number;
    failuresToDisconnect?: number;
    connectingGraceMs?: number;
}

const DEFAULTS = {
    connectedIntervalMs: 10_000,
    disconnectedIntervalMs: 1_500,
    connectingIntervalMs: 750,
    probeTimeoutMs: 2_500,
    failuresToDisconnect: 2,
    connectingGraceMs: 20_000,
};

export class ConnectionMonitor {
    private readonly opts: ConnectionMonitorOptions & typeof DEFAULTS;
    private _status: ServerStatus = 'connecting';
    private suspended = true;
    private failures = 0;
    private sawFirstResult = false;
    private probeInFlight = false;
    private generation = 0;
    private connectingSince = Date.now();
    private timer: ReturnType<typeof setTimeout> | null = null;

    constructor(options: ConnectionMonitorOptions) {
        this.opts = { ...DEFAULTS, ...options };
    }

    get status(): ServerStatus { return this._status; }

    start(): void {
        this.suspended = false;
        this.schedule(0);
    }

    stop(): void {
        this.suspended = true;
        this.generation++;
        this.clearTimer();
    }

    /** A start or restart is in flight: amber, fast poll. Re-arms a suspended monitor. */
    markConnecting(reason: string): void {
        this.generation++;
        this.suspended = false;
        this.failures = 0;
        // Reset here, not only on transition: a retry re-enters an already-'connecting' state,
        // and setStatus early-returns, so the grace would still be timing the first attempt.
        this.connectingSince = Date.now();
        this.setStatus('connecting', reason);
        this.schedule(300);
    }

    /**
     * Deliberately stopped, or out of restart attempts: red, and polling suspended. Nothing but a
     * user action can bring the server back, and that action goes through markConnecting.
     */
    markStopped(reason: string): void {
        this.generation++;
        this.failures = 0;
        this.setStatus('disconnected', reason);
        this.suspended = true;
        this.clearTimer();
    }

    /** A host lifecycle report, mapped onto the three states. */
    applyLifecycle(state: ServerLifecycleState, detail?: string): void {
        switch (state) {
            case 'starting': this.markConnecting(detail ?? 'starting'); break;
            case 'running': this.notifyMaybeUp(); break;
            case 'stopped': this.markStopped(detail ?? 'stopped'); break;
        }
    }

    /** The host believes a port is bound. Probes to confirm before going green. */
    notifyMaybeUp(): void {
        this.suspended = false;
        this.generation++;
        this.schedule(0);
    }

    /** Probe now unless suspended — for a window regaining focus after timer throttling. */
    probeSoon(): void {
        if (this.suspended) return;
        this.generation++;
        this.schedule(0);
    }

    private cadence(): number {
        if (this._status === 'connected') return this.opts.connectedIntervalMs;
        if (this._status === 'connecting') return this.opts.connectingIntervalMs;
        return this.opts.disconnectedIntervalMs;
    }

    private clearTimer(): void {
        if (this.timer !== null) {
            clearTimeout(this.timer);
            this.timer = null;
        }
    }

    // Chained timeout rather than an interval, so a slow probe can never stack up behind itself.
    private schedule(delayMs: number): void {
        this.clearTimer();
        if (this.suspended) return;
        this.timer = setTimeout(() => { void this.runProbe(); }, delayMs);
    }

    private async runProbe(): Promise<void> {
        if (this.suspended || this.probeInFlight) return;
        this.probeInFlight = true;
        const generation = this.generation;
        let ok = false;
        try {
            ok = await this.opts.probe(this.opts.probeTimeoutMs);
        } catch {
            ok = false;
        } finally {
            this.probeInFlight = false;
        }

        // A bumped generation means the base URL or lifecycle changed mid-flight, so this
        // answer is about a server we no longer care about.
        if (generation !== this.generation) {
            this.schedule(0);
            return;
        }
        if (this.suspended) return;

        if (ok) {
            this.setStatus('connected');
        } else {
            this.failures++;
            const inGrace = this._status === 'connecting'
                && Date.now() - this.connectingSince < this.opts.connectingGraceMs;
            if (!inGrace && this.failures >= this.opts.failuresToDisconnect) {
                this.setStatus('disconnected');
            }
        }
        this.schedule(this.cadence());
    }

    private setStatus(next: ServerStatus, reason?: string): void {
        if (next === 'connected') this.failures = 0;
        if (next === this._status) {
            if (next !== 'connecting') this.sawFirstResult = true;
            return;
        }

        const previous = this._status;
        this._status = next;
        if (next === 'connecting') this.connectingSince = Date.now();
        this.opts.log?.(reason ? `${previous} -> ${next} (${reason})` : `${previous} -> ${next}`);
        this.opts.onStatusChanged(next);

        // Every observer of a recovery funnels through here, so a single transition fires
        // onRecovered once however many of them noticed it.
        const isFirstResult = !this.sawFirstResult;
        if (next !== 'connecting') this.sawFirstResult = true;
        if (next === 'connected' && !isFirstResult) this.opts.onRecovered?.();
    }
}
