/**
 * Single-source-of-truth for what the user is looking at, across the bridges: the content of
 * the model attached to the first registered Monaco editor, which with multi-tab editing is the
 * active tab's model. Falls back to the first model in the registry, then to '' so callers can
 * stay synchronous.
 */
export function getActiveEditorContent(): string {
    // Preferred: pull from the TabManager exposed by main.ts. Its activeModel
    // is the model currently swapped into the editor.
    const tabs = (window as { calcpadTabs?: { activeModel?: { getValue(): string } } }).calcpadTabs;
    const fromTabs = tabs?.activeModel?.getValue();
    if (typeof fromTabs === 'string') return fromTabs;

    // Fallback: query Monaco's global registry. Only works if main.ts exposed
    // it on window — kept for safety / future contexts.
    const m = (window as Window & { monaco?: typeof import('monaco-editor') }).monaco;
    if (!m) return '';
    const editor = m.editor.getEditors()[0];
    const model = editor?.getModel() ?? m.editor.getModels()[0];
    return model?.getValue() ?? '';
}
