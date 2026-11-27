import * as monaco from 'monaco-editor';
import { documentDirty, documentPath, reloadDocument, replaceDocumentText } from '../tabs/tab-manager';

/** Keeps every open copy of a file, in any window, on the same text and saved state. */

const EVENT = 'calcpad-doc-sync';
const STATE_TIMEOUT_MS = 2000;

interface Change {
    offset: number;
    length: number;
    text: string;
}

type Message =
    | { kind: 'edit'; path: string; before: string; after: string; changes: Change[] }
    | { kind: 'saved'; path: string; hash: string }
    | { kind: 'want'; path: string }
    | { kind: 'state'; path: string; text: string; dirty: boolean; to: string };

export interface DocSyncDeps {
    /** Another window saved `path` and this window's copy matches it. */
    savedElsewhere(path: string, content: string): void;
}

let label = '';
let deps: DocSyncDeps | null = null;
let emit: ((message: Message) => void) | null = null;
const hashes = new WeakMap<monaco.editor.ITextModel, string>();
const wanted = new Map<string, ReturnType<typeof setTimeout>>();
let applying = 0;

function hash(text: string): string {
    let h = 0x811c9dc5;
    for (let i = 0; i < text.length; i++) {
        h ^= text.charCodeAt(i);
        h = Math.imul(h, 0x01000193);
    }
    return `${text.length}:${(h >>> 0).toString(36)}`;
}

function norm(path: string): string {
    return path.replace(/\\/g, '/').toLowerCase();
}

function modelsFor(path: string, except?: monaco.editor.ITextModel): monaco.editor.ITextModel[] {
    const key = norm(path);
    return monaco.editor.getModels().filter(m => {
        const p = m !== except && !m.isDisposed() ? documentPath(m) : null;
        return p !== null && norm(p) === key;
    });
}

function hashOf(model: monaco.editor.ITextModel): string {
    let h = hashes.get(model);
    if (h === undefined) hashes.set(model, h = hash(model.getValue()));
    return h;
}

function quietly(task: () => void): void {
    applying++;
    try {
        task();
    } finally {
        applying--;
    }
}

/** Applies another copy's edit; false when this copy had drifted and needs the full text. */
function applyChanges(model: monaco.editor.ITextModel, before: string, after: string, changes: Change[]): boolean {
    if (hashOf(model) !== before) return false;
    quietly(() => {
        model.pushStackElement();
        for (const c of changes) {
            const range = monaco.Range.fromPositions(model.getPositionAt(c.offset), model.getPositionAt(c.offset + c.length));
            model.pushEditOperations([], [{ range, text: c.text }], () => null);
        }
        model.pushStackElement();
    });
    return hashOf(model) === after;
}

function want(path: string): void {
    if (wanted.has(path)) return;
    wanted.set(path, setTimeout(() => wanted.delete(path), STATE_TIMEOUT_MS));
    emit?.({ kind: 'want', path });
}

function watch(model: monaco.editor.ITextModel): void {
    hashes.set(model, hash(model.getValue()));
    model.onDidChangeContent(e => {
        const before = hashOf(model);
        const text = model.getValue();
        const after = hash(text);
        hashes.set(model, after);
        if (applying) return;
        const path = documentPath(model);
        if (!path) return;
        const changes = e.changes.map(c => ({ offset: c.rangeOffset, length: c.rangeLength, text: c.text }));
        for (const twin of modelsFor(path, model)) {
            if (!applyChanges(twin, before, after, changes)) quietly(() => replaceDocumentText(twin, text));
        }
        emit?.({ kind: 'edit', path, before, after, changes });
    });
}

function onMessage(message: Message & { source: string }): void {
    if (message.source === label) return;
    switch (message.kind) {
        case 'edit':
            for (const model of modelsFor(message.path)) {
                if (!applyChanges(model, message.before, message.after, message.changes)) want(message.path);
            }
            return;
        case 'saved':
            for (const model of modelsFor(message.path)) {
                if (hashOf(model) !== message.hash) continue;
                reloadDocument(model, model.getValue());
                deps?.savedElsewhere(message.path, model.getValue());
            }
            return;
        case 'want': {
            const model = modelsFor(message.path)[0];
            if (model) emit?.({ kind: 'state', path: message.path, text: model.getValue(), dirty: documentDirty(model), to: message.source });
            return;
        }
        case 'state': {
            if (message.to !== label || !wanted.has(message.path)) return;
            clearTimeout(wanted.get(message.path));
            wanted.delete(message.path);
            for (const model of modelsFor(message.path)) {
                quietly(() => message.dirty
                    ? replaceDocumentText(model, message.text)
                    : reloadDocument(model, message.text));
            }
            return;
        }
    }
}

export async function startDocSync(windowLabel: string, syncDeps: DocSyncDeps): Promise<void> {
    label = windowLabel;
    deps = syncDeps;
    for (const model of monaco.editor.getModels()) watch(model);
    monaco.editor.onDidCreateModel(watch);
    const { emit: send, listen } = await import('@tauri-apps/api/event');
    emit = message => {
        void send(EVENT, { source: label, ...message }).catch(() => { /* other windows just won't sync */ });
    };
    await listen<Message & { source: string }>(EVENT, e => onMessage(e.payload));
}

/** Picks up another window's text, unsaved changes included, for a file just opened from disk. */
export function joinDocument(path: string): void {
    want(path);
}

/** Tells other windows `model` was just saved to its path. */
export function noteDocumentSaved(model: monaco.editor.ITextModel): void {
    const path = documentPath(model);
    if (path) emit?.({ kind: 'saved', path, hash: hashOf(model) });
}
