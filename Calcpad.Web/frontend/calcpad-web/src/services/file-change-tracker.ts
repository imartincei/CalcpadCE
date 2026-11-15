export interface FileStamp {
    modifiedMs: number;
    len: number;
}

export type FileConflictChoice = 'mine' | 'theirs' | 'both';

/** Resolves false when the user put the decision off, so it resurfaces on the next save. */
export type ExternalChangeHandler = (path: string, diskContent: string) => Promise<boolean>;

interface Baseline {
    stamp: FileStamp | null;
    /** The file's text as this window last read or wrote it. */
    content: string;
}

interface TrackerDeps {
    /** Null for a missing or unreadable file. */
    stamp(path: string): Promise<FileStamp | null>;
    read(path: string): Promise<string>;
}

const POLL_MS = 1500;

function sameStamp(a: FileStamp | null, b: FileStamp | null): boolean {
    return a?.modifiedMs === b?.modifiedMs && a?.len === b?.len;
}

/**
 * Detects edits other programs make to open files by polling their mtime/size against the
 * baseline this window last read or wrote. Checks and saves run one at a time, so a write of
 * our own is never mistaken for an external one and only one conflict prompt is up at a time.
 */
export class FileChangeTracker {
    private baselines = new Map<string, Baseline>();
    private queue: Promise<unknown> = Promise.resolve();
    private polling = false;
    private timer: ReturnType<typeof setInterval> | null = null;
    private openPaths: () => string[] = () => [];
    private onChange: ExternalChangeHandler = async () => true;

    constructor(private deps: TrackerDeps) {}

    stampOf(path: string): Promise<FileStamp | null> {
        return this.deps.stamp(path);
    }

    /** An existing baseline wins, so re-reading an open file cannot mask a pending change. */
    noteRead(path: string, content: string, stamp: FileStamp | null): void {
        if (!this.baselines.has(path)) this.baselines.set(path, { stamp, content });
    }

    async noteWritten(path: string, content: string): Promise<void> {
        this.baselines.set(path, { stamp: await this.stampOf(path), content });
    }

    /** Runs `task` with no check in flight. Must not be called from inside the change handler. */
    exclusive<T>(task: () => Promise<T>): Promise<T> {
        const next = this.queue.then(task);
        this.queue = next.catch(() => undefined);
        return next;
    }

    start(openPaths: () => string[], onChange: ExternalChangeHandler): void {
        this.openPaths = openPaths;
        this.onChange = onChange;
        this.timer ??= setInterval(() => this.pollSoon(), POLL_MS);
    }

    pollSoon(): void {
        if (this.polling) return;
        this.polling = true;
        void this.exclusive(() => this.poll()).finally(() => { this.polling = false; });
    }

    /** Surfaces any external change to `path` before it is overwritten; false if the user deferred. */
    verifyBeforeSave(path: string): Promise<boolean> {
        return this.exclusive(() => this.check(path, null, true));
    }

    private async poll(): Promise<void> {
        const paths = [...new Set(this.openPaths())];
        for (const tracked of this.baselines.keys()) {
            if (!paths.includes(tracked)) this.baselines.delete(tracked);
        }
        for (const path of paths) {
            const stamp = await this.deps.stamp(path);
            if (!stamp) continue;
            const baseline = this.baselines.get(path);
            if (!baseline) {
                // Opened without a tracked read (e.g. a recovered draft): adopt the disk as-is.
                try {
                    this.baselines.set(path, { stamp, content: await this.deps.read(path) });
                } catch { /* unreadable; retried next tick */ }
                continue;
            }
            if (!sameStamp(baseline.stamp, stamp)) await this.check(path, stamp, false);
        }
    }

    private async check(path: string, stamp: FileStamp | null, force: boolean): Promise<boolean> {
        const baseline = this.baselines.get(path);
        if (!baseline) return true;
        stamp ??= await this.stampOf(path);
        // A deleted file is simply recreated by the save.
        if (!stamp) return true;
        if (!force && sameStamp(baseline.stamp, stamp)) return true;
        let disk: string;
        try {
            disk = await this.deps.read(path);
        } catch {
            return true;
        }
        if (disk === baseline.content) {
            baseline.stamp = stamp;
            return true;
        }
        const resolved = await this.onChange(path, disk);
        if (resolved) {
            this.baselines.set(path, { stamp, content: disk });
        } else {
            baseline.stamp = stamp;
        }
        return resolved;
    }
}
