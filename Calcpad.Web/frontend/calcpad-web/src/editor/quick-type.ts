import * as monaco from 'monaco-editor';
import { findQuickTypeReplacement } from 'calcpad-frontend/text/quick-type';
import type { EditorBridge } from './bridge';

/**
 * Replace `~xxx` shortcuts with Unicode symbols (e.g. `~a ` → `α `).
 * Triggered when the user types a space character; the map is rebuilt whenever
 * snippets reload from the server.
 */
export function attachQuickTyper(
    editor: monaco.editor.IStandaloneCodeEditor,
    bridge: EditorBridge,
): monaco.IDisposable {
    let quickTypeMap = new Map<string, string>();
    let suppress = false;

    function rebuild(): void {
        quickTypeMap = bridge.snippets.buildQuickTypeMap();
    }
    if (bridge.snippets.isLoaded()) rebuild();
    bridge.snippets.onSnippetsLoaded(rebuild);

    return editor.onDidChangeModelContent(e => {
        if (suppress) return;
        if (bridge.getExtraSetting('quickTyping') === 'false') return;
        if (quickTypeMap.size === 0) return;
        if (e.changes.length !== 1) return;

        const change = e.changes[0];
        if (change.text !== ' ') return;

        const model = editor.getModel();
        if (!model) return;

        const lineNumber = change.range.startLineNumber;
        const lineText = model.getLineContent(lineNumber);
        // Cursor sits one column past the inserted space.
        const insertCol = change.range.startColumn; // 1-based column where the space was inserted
        const replacement = findQuickTypeReplacement(lineText, insertCol - 1, quickTypeMap);
        if (!replacement) return;

        suppress = true;
        try {
            editor.executeEdits('calcpad-quick-type', [{
                range: new monaco.Range(
                    lineNumber, replacement.startPos + 1,
                    lineNumber, insertCol + 1, // include the just-inserted space
                ),
                text: replacement.replacement,
                forceMoveMarkers: true,
            }]);
        } finally {
            suppress = false;
        }
    });
}
