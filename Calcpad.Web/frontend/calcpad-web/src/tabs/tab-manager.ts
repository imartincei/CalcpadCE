import * as monaco from 'monaco-editor';

export interface TabState {
    id: string;
    /** Display label — filename or "Untitled-N". */
    title: string;
    /** Absolute filesystem path; null for unsaved untitled tabs. */
    filePath: string | null;
    dirty: boolean;
}

export interface TabSnapshot extends TabState {
    isActive: boolean;
}

/**
 * Per-model document metadata (path, title, save baseline). Lives outside any
 * single TabManager because a model can be shared by tabs in more than one
 * group (see openLinked) — filePath/title/dirty describe the document, not a
 * particular view of it, so they must stay in one place and be visible to
 * every tab that shares the model.
 */
interface DocEntry {
    filePath: string | null;
    title: string;
    savedVersionId: number;
    refCount: number;
    /** Fired after a save (or rename) so every tab sharing this model re-derives dirty/title. */
    onChanged: Set<() => void>;
}

const docs = new WeakMap<monaco.editor.ITextModel, DocEntry>();

function docFor(model: monaco.editor.ITextModel): DocEntry {
    const d = docs.get(model);
    if (!d) throw new Error('tab-manager: model has no document entry');
    return d;
}

interface InternalTab {
    id: string;
    model: monaco.editor.ITextModel;
    viewState: monaco.editor.ICodeEditorViewState | null;
    dirty: boolean;
    /** Disposable for the model's content-change subscription (dirty tracking). */
    contentSub: monaco.IDisposable;
    /** Unsubscribe this tab from its DocEntry's onChanged set. */
    unlisten: () => void;
}

export type TabsListener = (tabs: TabSnapshot[], activeId: string | null) => void;
export type ActiveModelChangeListener = (
    tabId: string | null,
    model: monaco.editor.ITextModel | null,
) => void;
export type TabContentChangeListener = (tabId: string) => void;
export type TabRemovedListener = (tabId: string) => void;

/**
 * Owns the open-tabs list, their Monaco models, and view-state restoration.
 * Mirrors VS Code's tab semantics: one editor instance, many models, view
 * state saved/restored on switch.
 *
 * The TabManager is platform-agnostic — file I/O lives in the caller. It
 * just tracks `filePath` so the caller can decide what to read/write.
 *
 * A model may be shared by tabs in more than one TabManager (see
 * openLinked/modelForTab) — used to split the same file into another editor
 * group with live-synced content, same as VS Code's "split into new group".
 * Models are refcounted across all TabManagers so the underlying document
 * survives as long as any tab still references it.
 */
export class TabManager {
    private tabs: InternalTab[] = [];
    private _activeId: string | null = null;
    private _untitledCounter = 0;
    private _seq = 0;

    private listeners = new Set<TabsListener>();
    private activeModelListeners = new Set<ActiveModelChangeListener>();
    private contentListeners = new Set<TabContentChangeListener>();
    private removedListeners = new Set<TabRemovedListener>();

    /**
     * @param editor   The editor instance this manager swaps models on.
     * @param idPrefix Namespacing prefix for tab ids and model URIs. Must be
     *                 unique per editor group so two groups can't create two
     *                 Monaco models with the same URI (which Monaco rejects).
     */
    constructor(
        private editor: monaco.editor.IStandaloneCodeEditor,
        private idPrefix: string = '',
    ) {}

    // ---- Subscription ----

    onTabsChanged(listener: TabsListener): () => void {
        this.listeners.add(listener);
        return () => this.listeners.delete(listener);
    }

    onActiveModelChanged(listener: ActiveModelChangeListener): () => void {
        this.activeModelListeners.add(listener);
        return () => this.activeModelListeners.delete(listener);
    }

    onTabContentChanged(listener: TabContentChangeListener): () => void {
        this.contentListeners.add(listener);
        return () => this.contentListeners.delete(listener);
    }

    onTabRemoved(listener: TabRemovedListener): () => void {
        this.removedListeners.add(listener);
        return () => this.removedListeners.delete(listener);
    }

    // ---- Read API ----

    get activeId(): string | null {
        return this._activeId;
    }

    get activeTab(): TabState | null {
        const t = this.findActive();
        return t ? this.toState(t) : null;
    }

    get activeModel(): monaco.editor.ITextModel | null {
        return this.findActive()?.model ?? null;
    }

    get all(): TabState[] {
        return this.tabs.map(t => this.toState(t));
    }

    findByPath(filePath: string): TabState | null {
        const t = this.tabs.find(t => docFor(t.model).filePath === filePath);
        return t ? this.toState(t) : null;
    }

    /** Lookup the Monaco model for the tab matching `filePath`, or null. */
    findModelByPath(filePath: string): monaco.editor.ITextModel | null {
        return this.tabs.find(t => docFor(t.model).filePath === filePath)?.model ?? null;
    }

    /** The model backing a given tab, for handing off to openLinked() on another group. */
    modelForTab(id: string): monaco.editor.ITextModel | null {
        return this.tabs.find(t => t.id === id)?.model ?? null;
    }

    /**
     * True if closing `id` would be the last tab (in any group) referencing
     * its model — i.e. closing it actually discards unsaved content, rather
     * than just closing one of several synced views onto it.
     */
    isLastReference(id: string): boolean {
        const t = this.tabs.find(t => t.id === id);
        if (!t) return true;
        return docFor(t.model).refCount <= 1;
    }

    isDirty(id?: string): boolean {
        const t = id ? this.tabs.find(t => t.id === id) : this.findActive();
        return !!t?.dirty;
    }

    getContent(id: string): string | null {
        return this.tabs.find(t => t.id === id)?.model.getValue() ?? null;
    }

    getFilePath(id: string): string | null {
        const t = this.tabs.find(t => t.id === id);
        return t ? docFor(t.model).filePath : null;
    }

    getTitle(id: string): string | null {
        const t = this.tabs.find(t => t.id === id);
        return t ? docFor(t.model).title : null;
    }

    anyDirty(): boolean {
        return this.tabs.some(t => t.dirty);
    }

    get count(): number {
        return this.tabs.length;
    }

    // ---- Mutation ----

    /**
     * Create a new untitled tab with the given content (default empty) and
     * activate it. Returns the new tab's id. `title` overrides the default
     * "Untitled-N" label (used for read-view tabs like "Open Full HTML").
     */
    newUntitled(content: string = '', title?: string): string {
        let label = title;
        if (!label) {
            this._untitledCounter += 1;
            label = `Untitled-${this._untitledCounter}`;
        }
        const tab = this.createTab({ title: label, filePath: null, content });
        this.activate(tab.id);
        return tab.id;
    }

    /**
     * Open a file in a new tab. If a tab with that path is already open,
     * activates it instead and ignores `content` (caller already has it open).
     * Returns the tab id.
     */
    openFile(filePath: string, content: string): string {
        const existing = this.tabs.find(t => docFor(t.model).filePath === filePath);
        if (existing) {
            this.activate(existing.id);
            return existing.id;
        }

        // If the active tab is an empty untitled scratch buffer, replace it
        // in place rather than stacking another tab. Matches VS Code's
        // "untitled-1 disappears when you open a file" behavior.
        const active = this.findActive();
        if (active && docFor(active.model).filePath === null && active.model.getValue() === '' && !active.dirty) {
            const doc = docFor(active.model);
            active.model.setValue(content);
            doc.filePath = filePath;
            doc.title = baseName(filePath);
            doc.savedVersionId = active.model.getAlternativeVersionId();
            active.dirty = false;
            this.emit();
            return active.id;
        }

        const tab = this.createTab({ title: baseName(filePath), filePath, content });
        this.activate(tab.id);
        return tab.id;
    }

    /**
     * Open a tab in this group that shares `model` with a tab elsewhere
     * (typically another group's active tab, via modelForTab). Edits, saves
     * and dirty state all stay in sync with every other tab sharing the
     * model, since they're the same Monaco document — only the view state
     * (cursor/scroll) is independent per tab.
     */
    openLinked(model: monaco.editor.ITextModel): string {
        const doc = docFor(model);
        doc.refCount += 1;
        const id = `${this.idPrefix}tab-${++this._seq}`;
        const onChanged = () => this.recomputeDirty(id);
        doc.onChanged.add(onChanged);
        const tab: InternalTab = {
            id,
            model,
            viewState: null,
            dirty: model.getAlternativeVersionId() !== doc.savedVersionId,
            contentSub: model.onDidChangeContent(() => {
                this.recomputeDirty(id);
                for (const l of this.contentListeners) l(id);
            }),
            unlisten: () => doc.onChanged.delete(onChanged),
        };
        this.tabs.push(tab);
        this.activate(tab.id);
        this.emit();
        return tab.id;
    }

    /**
     * Switch the editor to the given tab. Saves the previous tab's view state
     * so cursor + scroll restore on switch-back.
     */
    activate(id: string): void {
        if (id === this._activeId) return;
        const next = this.tabs.find(t => t.id === id);
        if (!next) return;

        const prev = this.findActive();
        if (prev) {
            prev.viewState = this.editor.saveViewState();
        }

        this._activeId = id;
        this.editor.setModel(next.model);
        if (next.viewState) {
            this.editor.restoreViewState(next.viewState);
        }
        this.editor.focus();
        this.emit();
        this.emitActiveModel();
    }

    /**
     * Close a tab. Caller is responsible for the dirty-prompt (so it can
     * await the user's choice with platform-appropriate UI). When the active
     * tab closes, focus moves to the right neighbor (then left, then none).
     */
    close(id: string): void {
        const idx = this.tabs.findIndex(t => t.id === id);
        if (idx < 0) return;

        const tab = this.tabs[idx];
        const wasActive = id === this._activeId;

        tab.contentSub.dispose();
        tab.unlisten();
        this.releaseModel(tab.model);
        this.tabs.splice(idx, 1);
        for (const l of this.removedListeners) l(id);

        if (wasActive) {
            const nextActive = this.tabs[idx] ?? this.tabs[idx - 1] ?? null;
            this._activeId = nextActive?.id ?? null;
            if (nextActive) {
                this.editor.setModel(nextActive.model);
                if (nextActive.viewState) {
                    this.editor.restoreViewState(nextActive.viewState);
                }
                this.editor.focus();
            } else {
                // No tabs left — give the editor an empty model so it stays usable.
                this.newUntitled();
                return; // newUntitled() emits already
            }
        }

        this.emit();
        if (wasActive) this.emitActiveModel();
    }

    /**
     * Mark the active tab saved at its current content. Optionally update its
     * file path / title (for save-as). Notifies every other tab sharing this
     * model (e.g. a synced split of the same file) so their dirty/title
     * state updates too.
     */
    markActiveSaved(opts?: { filePath?: string }): void {
        const t = this.findActive();
        if (!t) return;
        const doc = docFor(t.model);
        if (opts?.filePath) {
            doc.filePath = opts.filePath;
            doc.title = baseName(opts.filePath);
        }
        doc.savedVersionId = t.model.getAlternativeVersionId();
        for (const l of doc.onChanged) l();
        this.emit();
    }

    /**
     * Restore a tab from an autosave draft. Unlike openFile, the tab starts
     * dirty — the on-disk file (if any) may differ from `content`, so the
     * user must explicitly save to reconcile.
     */
    openDraft(opts: { filePath: string | null; title: string; content: string }): string {
        const tab = this.createTab({ title: opts.title, filePath: opts.filePath, content: opts.content });
        // Force dirty even though content matches the model's initial value:
        // set savedVersionId to a value the alternative-version-id can never
        // hit (it starts at 1 and only grows), so recomputeDirty sees a diff.
        docFor(tab.model).savedVersionId = -1;
        tab.dirty = true;
        this.activate(tab.id);
        this.emit();
        return tab.id;
    }

    /** Replace the active tab's content as if it had just been opened from disk. */
    reloadActive(content: string): void {
        const t = this.findActive();
        if (!t) return;
        const doc = docFor(t.model);
        t.model.setValue(content);
        doc.savedVersionId = t.model.getAlternativeVersionId();
        for (const l of doc.onChanged) l();
        if (t.dirty) {
            this.emit();
        }
    }

    activateNext(): void {
        if (this.tabs.length < 2 || !this._activeId) return;
        const i = this.tabs.findIndex(t => t.id === this._activeId);
        const next = this.tabs[(i + 1) % this.tabs.length];
        this.activate(next.id);
    }

    activatePrev(): void {
        if (this.tabs.length < 2 || !this._activeId) return;
        const i = this.tabs.findIndex(t => t.id === this._activeId);
        const prev = this.tabs[(i - 1 + this.tabs.length) % this.tabs.length];
        this.activate(prev.id);
    }

    activateByIndex(index: number): void {
        const t = this.tabs[index];
        if (t) this.activate(t.id);
    }

    /**
     * Dispose every model + subscription this manager owns. Used when an
     * editor group is closed (unsplit). Fires the removed-listeners so the
     * caller can clean up per-tab state (drafts) first, then clears listeners
     * so no stale callbacks fire against the disposed editor.
     */
    disposeAll(): void {
        for (const t of this.tabs) {
            for (const l of this.removedListeners) l(t.id);
            t.contentSub.dispose();
            t.unlisten();
            this.releaseModel(t.model);
        }
        this.tabs = [];
        this._activeId = null;
        this.listeners.clear();
        this.activeModelListeners.clear();
        this.contentListeners.clear();
        this.removedListeners.clear();
    }

    // ---- Internals ----

    private createTab(opts: { title: string; filePath: string | null; content: string }): InternalTab {
        const id = `${this.idPrefix}tab-${++this._seq}`;
        // Use a unique URI per model — Monaco needs this so markers/providers
        // can distinguish tabs. Path includes the tab id so the URI is stable
        // across rename and unique even when two tabs hold the same file path.
        const uri = monaco.Uri.parse(`inmemory:///${id}.cpd`);
        const model = monaco.editor.createModel(opts.content, 'calcpad', uri);
        const savedVersionId = model.getAlternativeVersionId();
        const doc: DocEntry = {
            filePath: opts.filePath,
            title: opts.title,
            savedVersionId,
            refCount: 1,
            onChanged: new Set(),
        };
        docs.set(model, doc);
        const onChanged = () => this.recomputeDirty(id);
        doc.onChanged.add(onChanged);
        const tab: InternalTab = {
            id,
            dirty: false,
            model,
            viewState: null,
            contentSub: model.onDidChangeContent(() => {
                this.recomputeDirty(id);
                for (const l of this.contentListeners) l(id);
            }),
            unlisten: () => doc.onChanged.delete(onChanged),
        };
        this.tabs.push(tab);
        this.emit();
        return tab;
    }

    /** Decrement a model's shared refcount, disposing it once no tab (in any group) references it. */
    private releaseModel(model: monaco.editor.ITextModel): void {
        const doc = docFor(model);
        doc.refCount -= 1;
        if (doc.refCount <= 0) model.dispose();
    }

    /**
     * Compare the model's alternative-version-id against the document's
     * last-saved id. Equality means the user has undone all post-save edits —
     * model is effectively clean again, so the dirty flag flips back off.
     */
    private recomputeDirty(id: string): void {
        const t = this.tabs.find(t => t.id === id);
        if (!t) return;
        const next = t.model.getAlternativeVersionId() !== docFor(t.model).savedVersionId;
        if (next !== t.dirty) {
            t.dirty = next;
            this.emit();
        }
    }

    private findActive(): InternalTab | null {
        return this.tabs.find(t => t.id === this._activeId) ?? null;
    }

    private toState(t: InternalTab): TabState {
        const doc = docFor(t.model);
        return { id: t.id, title: doc.title, filePath: doc.filePath, dirty: t.dirty };
    }

    private emit(): void {
        const snapshots: TabSnapshot[] = this.tabs.map(t => ({
            ...this.toState(t),
            isActive: t.id === this._activeId,
        }));
        for (const l of this.listeners) l(snapshots, this._activeId);
    }

    private emitActiveModel(): void {
        const t = this.findActive();
        for (const l of this.activeModelListeners) l(t?.id ?? null, t?.model ?? null);
    }
}

function baseName(path: string): string {
    return path.split(/[\\/]/).pop() || path;
}
