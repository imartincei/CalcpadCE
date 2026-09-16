/**
 * Where the user left off — which panes were open, what the results pane was showing, which
 * files were loaded. Kept out of CalcpadSettings and its extras: this is a record of the last
 * session rather than a preference, so it must not travel with settings presets, import or
 * export. Desktop stores it in its own `workspace.json` store file, web in localStorage.
 */

/**
 * What the results pane shows: 'preview' (the document as written), 'unwrapped' (the source
 * listing with macros and includes resolved), 'ui' (`#UI` lines as interactive controls,
 * `#post` hidden) and 'report' (the print layout with entered `#UI` values applied).
 */
export type ResultMode = 'preview' | 'unwrapped' | 'ui' | 'report';

/** Which panes are showing. Pane *sizes* persist separately, from App.vue. */
export interface WorkspaceLayout {
    sidebarVisible: boolean;
    previewVisible: boolean;
    bottomPanelOpen: boolean;
    activeBottomTab: 'problems' | 'output';
}

export interface WorkspaceState {
    resultMode: ResultMode;
    layout: WorkspaceLayout;
    /** Paths only — unsaved and dirty buffers are the drafts mechanism's job. */
    openFiles: string[];
    activeFile: string;
}

const RESULT_MODES: ResultMode[] = ['preview', 'unwrapped', 'ui', 'report'];

const DEFAULT_STATE: WorkspaceState = {
    resultMode: 'preview',
    layout: {
        sidebarVisible: true,
        previewVisible: true,
        bottomPanelOpen: false,
        activeBottomTab: 'problems',
    },
    openFiles: [],
    activeFile: '',
};

const WEB_STORAGE_KEY = 'calcpad-workspace-state';
const TAURI_STORE_FILE = 'workspace.json';
const TAURI_STORE_KEY = 'state';
const SAVE_DEBOUNCE_MS = 500;

export function isResultMode(value: unknown): value is ResultMode {
    return RESULT_MODES.includes(value as ResultMode);
}

function coerceState(raw: unknown): WorkspaceState {
    const stored = (raw ?? {}) as Partial<WorkspaceState>;
    const layout = (stored.layout ?? {}) as Partial<WorkspaceLayout>;
    const flag = (value: unknown, fallback: boolean): boolean =>
        typeof value === 'boolean' ? value : fallback;
    return {
        resultMode: isResultMode(stored.resultMode) ? stored.resultMode : DEFAULT_STATE.resultMode,
        layout: {
            sidebarVisible: flag(layout.sidebarVisible, DEFAULT_STATE.layout.sidebarVisible),
            previewVisible: flag(layout.previewVisible, DEFAULT_STATE.layout.previewVisible),
            bottomPanelOpen: flag(layout.bottomPanelOpen, DEFAULT_STATE.layout.bottomPanelOpen),
            activeBottomTab: layout.activeBottomTab === 'output' ? 'output' : 'problems',
        },
        openFiles: Array.isArray(stored.openFiles)
            ? stored.openFiles.filter(p => typeof p === 'string')
            : [],
        activeFile: typeof stored.activeFile === 'string' ? stored.activeFile : '',
    };
}

interface WorkspaceStateBackend {
    read(): Promise<unknown>;
    write(state: WorkspaceState): Promise<void>;
}

function webBackend(): WorkspaceStateBackend {
    return {
        async read() {
            const raw = localStorage.getItem(WEB_STORAGE_KEY);
            return raw ? JSON.parse(raw) : null;
        },
        async write(state) {
            localStorage.setItem(WEB_STORAGE_KEY, JSON.stringify(state));
        },
    };
}

async function tauriBackend(): Promise<WorkspaceStateBackend> {
    const { Store } = await import('@tauri-apps/plugin-store');
    const store = await Store.load(TAURI_STORE_FILE);
    return {
        read: () => store.get(TAURI_STORE_KEY),
        async write(state) {
            await store.set(TAURI_STORE_KEY, state);
            await store.save();
        },
    };
}

export class WorkspaceStateStore {
    private state: WorkspaceState;
    private saveTimer: ReturnType<typeof setTimeout> | null = null;
    /** Nothing persisted yet, so callers may seed the state from an older store. */
    readonly isFresh: boolean;

    private constructor(private readonly backend: WorkspaceStateBackend, raw: unknown) {
        this.isFresh = raw === null || raw === undefined;
        this.state = coerceState(raw);
    }

    static async load(isTauri: boolean): Promise<WorkspaceStateStore> {
        const backend = isTauri ? await tauriBackend() : webBackend();
        let raw: unknown = null;
        try {
            raw = await backend.read();
        } catch {
            raw = null;
        }
        return new WorkspaceStateStore(backend, raw);
    }

    get(): Readonly<WorkspaceState> {
        return this.state;
    }

    /** Merge and schedule a write, coalesced: tab switches report on every tab change. */
    update(patch: Partial<WorkspaceState>): void {
        this.state = { ...this.state, ...patch };
        if (this.saveTimer) clearTimeout(this.saveTimer);
        this.saveTimer = setTimeout(() => {
            this.saveTimer = null;
            void this.backend.write(this.state);
        }, SAVE_DEBOUNCE_MS);
    }

    /** Write now, dropping any pending debounce — the exit path can't wait it out. */
    async flush(): Promise<void> {
        if (this.saveTimer) {
            clearTimeout(this.saveTimer);
            this.saveTimer = null;
        }
        await this.backend.write(this.state);
    }
}
