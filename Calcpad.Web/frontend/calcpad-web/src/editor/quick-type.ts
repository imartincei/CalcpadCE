import * as monaco from 'monaco-editor';
import { findQuickTypeReplacement } from 'calcpad-frontend/text/quick-type';
import type { EditorBridge } from './bridge';

interface SnippetController extends monaco.editor.IEditorContribution {
    insert(template: string): void;
}

/**
 * Replace `~xxx` shortcuts with Unicode symbols (e.g. `~a ` → `α `) when a space is typed.
 * The map is read from the `quickTypes` setting at that moment, so edits apply at once.
 */
export function attachQuickTyper(
    editor: monaco.editor.IStandaloneCodeEditor,
    bridge: EditorBridge,
): monaco.IDisposable {
    let suppress = false;

    return editor.onDidChangeModelContent(e => {
        if (suppress) return;
        if (bridge.getExtraSetting('quickTyping') === 'false') return;
        if (e.changes.length !== 1) return;

        const change = e.changes[0];
        if (change.text !== ' ') return;
        const quickTypeMap = bridge.snippets.buildQuickTypeMap();

        const model = editor.getModel();
        if (!model) return;

        const lineNumber = change.range.startLineNumber;
        const lineText = model.getLineContent(lineNumber);
        // Cursor sits one column past the inserted space.
        const insertCol = change.range.startColumn; // 1-based column where the space was inserted
        const replacement = findQuickTypeReplacement(lineText, insertCol - 1, quickTypeMap);
        if (!replacement) return;

        const snippet = bridge.snippets.quickTypeSnippet(replacement.replacement);
        suppress = true;
        try {
            editor.executeEdits('calcpad-quick-type', [{
                range: new monaco.Range(
                    lineNumber, replacement.startPos + 1,
                    lineNumber, insertCol + 1, // include the just-inserted space
                ),
                text: snippet ? '' : replacement.replacement.replace(/§/g, ''),
                forceMoveMarkers: true,
            }]);
        } finally {
            suppress = false;
        }
        // Placeholders become tab stops, like an accepted completion.
        if (snippet) {
            queueMicrotask(() => editor.getContribution<SnippetController>('snippetController2')?.insert(snippet));
        }
    });
}
