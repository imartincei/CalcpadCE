import * as monaco from 'monaco-editor';
import type { EditorGroup } from '../editor/editor-group';
import type { TabState } from '../tabs/tab-manager';

/** What the agent tools need from main.ts's editor groups. */
export interface AgentEditorContext {
    groups(): EditorGroup[];
    activeGroup(): EditorGroup;
    openFile(path: string): Promise<void>;
}

export interface DocRef {
    group: EditorGroup;
    tab: TabState;
    model: monaco.editor.ITextModel;
}

export interface PreparedEdit {
    doc: DocRef;
    original: string;
    modified: string;
    summary: string;
}

type Args = Record<string, unknown>;

function samePath(a: string, b: string): boolean {
    const norm = (p: string) => p.replace(/\\/g, '/').replace(/\/+$/, '');
    return norm(a) === norm(b) || norm(a).toLowerCase() === norm(b).toLowerCase();
}

export function findDoc(ctx: AgentEditorContext, ref: unknown): DocRef {
    if (typeof ref !== 'string' || !ref.trim()) {
        const group = ctx.activeGroup();
        const tab = group.tabs.activeTab;
        const model = group.tabs.activeModel;
        if (!tab || !model) throw new Error('No worksheet is open.');
        return { group, tab, model };
    }
    const order = [ctx.activeGroup(), ...ctx.groups().filter(g => g !== ctx.activeGroup())];
    for (const group of order) {
        for (const tab of group.tabs.all) {
            if (tab.id === ref || (tab.filePath && samePath(tab.filePath, ref))) {
                const model = group.tabs.modelForTab(tab.id);
                if (model) return { group, tab, model };
            }
        }
    }
    throw new Error(`"${ref}" is not open in Calcpad. Use list_open_documents, or open_document to open it.`);
}

function describe(doc: DocRef) {
    return {
        id: doc.tab.id,
        title: doc.tab.title,
        path: doc.tab.filePath,
        dirty: doc.tab.dirty,
        lineCount: doc.model.getLineCount(),
    };
}

function listOpenDocuments(ctx: AgentEditorContext) {
    const active = ctx.activeGroup();
    const seen = new Set<monaco.editor.ITextModel>();
    const out: unknown[] = [];
    for (const group of [active, ...ctx.groups().filter(g => g !== active)]) {
        for (const tab of group.tabs.all) {
            const model = group.tabs.modelForTab(tab.id);
            if (!model || seen.has(model)) continue;
            seen.add(model);
            out.push({
                id: tab.id,
                title: tab.title,
                path: tab.filePath,
                dirty: tab.dirty,
                active: group === active && tab.id === group.tabs.activeId,
            });
        }
    }
    return out;
}

const SEVERITY: Record<number, string> = {
    [monaco.MarkerSeverity.Error]: 'error',
    [monaco.MarkerSeverity.Warning]: 'warning',
    [monaco.MarkerSeverity.Info]: 'info',
    [monaco.MarkerSeverity.Hint]: 'hint',
};

function getDiagnostics(doc: DocRef) {
    const markers = monaco.editor.getModelMarkers({ resource: doc.model.uri });
    return {
        path: doc.tab.filePath ?? doc.tab.title,
        diagnostics: markers.map(m => ({
            severity: SEVERITY[m.severity] ?? 'info',
            line: m.startLineNumber,
            column: m.startColumn,
            endLine: m.endLineNumber,
            endColumn: m.endColumn,
            code: typeof m.code === 'string' ? m.code : m.code?.value,
            message: m.message.replace(/^\[Linter\] /, ''),
        })),
    };
}

function getSelection(ctx: AgentEditorContext) {
    const group = ctx.activeGroup();
    const model = group.tabs.activeModel;
    const tab = group.tabs.activeTab;
    const selection = group.editor.getSelection();
    if (!model || !tab || !selection) throw new Error('No worksheet is open.');
    return {
        path: tab.filePath ?? tab.title,
        startLine: selection.startLineNumber,
        startColumn: selection.startColumn,
        endLine: selection.endLineNumber,
        endColumn: selection.endColumn,
        text: model.getValueInRange(selection),
    };
}

function countOccurrences(haystack: string, needle: string): number {
    let count = 0;
    for (let i = haystack.indexOf(needle); i !== -1; i = haystack.indexOf(needle, i + needle.length)) count++;
    return count;
}

/** Builds the proposed text without touching the editor. Throws a message the agent can act on. */
export function prepareEdit(ctx: AgentEditorContext, args: Args): PreparedEdit {
    const doc = findDoc(ctx, args.path);
    const original = doc.model.getValue();
    let modified: string;
    if (typeof args.text === 'string') {
        modified = args.text;
    } else if (Array.isArray(args.edits) && args.edits.length) {
        modified = original;
        args.edits.forEach((edit, i) => {
            const oldText = (edit as Args)?.oldText;
            const newText = (edit as Args)?.newText;
            if (typeof oldText !== 'string' || typeof newText !== 'string' || !oldText) {
                throw new Error(`Edit ${i + 1} needs non-empty oldText and a newText string.`);
            }
            const count = countOccurrences(modified, oldText);
            if (count !== 1) {
                throw new Error(`Edit ${i + 1}: oldText was found ${count} times; it must match exactly once. Re-read the document with get_document.`);
            }
            modified = modified.replace(oldText, () => newText);
        });
    } else {
        throw new Error('Provide either `edits` or the full `text`.');
    }
    if (modified === original) throw new Error('The proposed edit does not change the document.');
    return { doc, original, modified, summary: typeof args.summary === 'string' ? args.summary : '' };
}

/** Applies only the changed span so undo, cursor and folding survive. */
export function applyEdit(edit: PreparedEdit): void {
    const { model } = edit.doc;
    if (model.isDisposed()) throw new Error('The worksheet was closed.');
    if (model.getValue() !== edit.original) throw new Error('The worksheet changed while the edit was being reviewed.');
    const { original, modified } = edit;
    let start = 0;
    while (start < original.length && start < modified.length && original[start] === modified[start]) start++;
    let endOld = original.length;
    let endNew = modified.length;
    while (endOld > start && endNew > start && original[endOld - 1] === modified[endNew - 1]) {
        endOld--;
        endNew--;
    }
    const from = model.getPositionAt(start);
    const to = model.getPositionAt(endOld);
    model.pushStackElement();
    model.pushEditOperations(
        [],
        [{ range: new monaco.Range(from.lineNumber, from.column, to.lineNumber, to.column), text: modified.slice(start, endNew) }],
        () => null,
    );
    model.pushStackElement();
}

export async function runEditorTool(ctx: AgentEditorContext, tool: string, args: Args): Promise<unknown> {
    switch (tool) {
        case 'list_open_documents':
            return listOpenDocuments(ctx);
        case 'get_document': {
            const doc = findDoc(ctx, args.path);
            return { ...describe(doc), text: doc.model.getValue() };
        }
        case 'get_selection':
            return getSelection(ctx);
        case 'get_diagnostics':
            return getDiagnostics(findDoc(ctx, args.path));
        case 'open_document': {
            if (typeof args.path !== 'string' || !args.path) throw new Error('`path` is required.');
            await ctx.openFile(args.path);
            return describe(findDoc(ctx, args.path));
        }
        default:
            throw new Error(`Unknown tool ${tool}`);
    }
}
