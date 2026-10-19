import type * as monaco from 'monaco-editor';

const ids = new WeakMap<monaco.editor.ITextModel, string>();

/**
 * A random id per model, so an untitled document's last run never applies to another one. Tab
 * ids restart after a reload and repeat across windows.
 */
export function documentIdFor(model: monaco.editor.ITextModel | null): string | undefined {
    if (!model) return undefined;
    let id = ids.get(model);
    if (!id) {
        id = Array.from(crypto.getRandomValues(new Uint8Array(16)), b => b.toString(16).padStart(2, '0')).join('');
        ids.set(model, id);
    }
    return id;
}
