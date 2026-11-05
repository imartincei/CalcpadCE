import type { UiOverrideStore } from './ui-overrides';

/** One entered value; `before` is undefined when the control showed the document's own value. */
interface UiEdit {
    key: string;
    before: string | undefined;
    after: string;
}

/** What a scope can step through, for menus and the form's datagrid menu. */
export interface UiHistoryState {
    undo: boolean;
    redo: boolean;
}

interface UiEditStacks {
    undo: UiEdit[];
    redo: UiEdit[];
}

const MAX_UI_HISTORY = 200;

/**
 * Undo/redo of values entered into `#UI` controls, kept apart from the editor's own history.
 * Scoped by whatever the host calls a tab, while the values themselves stay keyed by document.
 */
export class UiEditHistory {
    private readonly byScope = new Map<string, UiEditStacks>();

    /** Sets a value in `store` and records it. Returns false when nothing changed. */
    public set(scope: string, store: UiOverrideStore, docKey: string, key: string, value: string): boolean {
        const before = store.get(docKey, key);
        if (!store.set(docKey, key, value)) return false;

        const stacks = this.stacksFor(scope);
        stacks.undo.push({ key, before, after: value });
        if (stacks.undo.length > MAX_UI_HISTORY) stacks.undo.shift();
        stacks.redo.length = 0;
        return true;
    }

    /** Reverts the scope's last edit. Returns false when there is none. */
    public undo(scope: string, store: UiOverrideStore, docKey: string): boolean {
        const stacks = this.byScope.get(scope);
        const edit = stacks?.undo.pop();
        if (!stacks || !edit) return false;

        stacks.redo.push(edit);
        if (edit.before === undefined) store.unset(docKey, edit.key);
        else store.set(docKey, edit.key, edit.before);
        return true;
    }

    /** Re-applies the scope's last undone edit. Returns false when there is none. */
    public redo(scope: string, store: UiOverrideStore, docKey: string): boolean {
        const stacks = this.byScope.get(scope);
        const edit = stacks?.redo.pop();
        if (!stacks || !edit) return false;

        stacks.undo.push(edit);
        store.set(docKey, edit.key, edit.after);
        return true;
    }

    public state(scope: string): UiHistoryState {
        const stacks = this.byScope.get(scope);
        return { undo: !!stacks?.undo.length, redo: !!stacks?.redo.length };
    }

    public clear(scope: string): void {
        this.byScope.delete(scope);
    }

    private stacksFor(scope: string): UiEditStacks {
        let stacks = this.byScope.get(scope);
        if (!stacks) {
            stacks = { undo: [], redo: [] };
            this.byScope.set(scope, stacks);
        }
        return stacks;
    }
}
