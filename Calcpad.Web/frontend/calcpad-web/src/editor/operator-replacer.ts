import * as monaco from 'monaco-editor';
import {
    isOperatorTriggerChar,
    isInsideStringOrComment,
    findOperatorReplacement,
} from 'calcpad-frontend/text/operators';

/**
 * Replace C-style operator sequences with Unicode equivalents:
 * `==` → `≡`, `!=` → `≠`, `>=` → `≥`, `<=` → `≤`, `&&` → `∧`, `||` → `∨`, etc.
 */
export function attachOperatorReplacer(
    editor: monaco.editor.IStandaloneCodeEditor,
): monaco.IDisposable {
    let suppress = false;

    return editor.onDidChangeModelContent(e => {
        if (suppress) return;
        if (e.changes.length !== 1) return;

        const change = e.changes[0];
        if (change.text.length !== 1) return;
        if (!isOperatorTriggerChar(change.text)) return;

        const model = editor.getModel();
        if (!model) return;

        const lineNumber = change.range.startLineNumber;
        const lineText = model.getLineContent(lineNumber);
        const insertCol = change.range.startColumn; // 1-based

        if (isInsideStringOrComment(lineText, insertCol - 1)) return;

        const replacement = findOperatorReplacement(lineText, insertCol);
        if (!replacement) return;

        suppress = true;
        try {
            editor.executeEdits('calcpad-operator-replace', [{
                range: new monaco.Range(
                    lineNumber, replacement.startPos + 1,
                    lineNumber, replacement.endPos + 1,
                ),
                text: replacement.replacement,
                forceMoveMarkers: true,
            }]);
        } finally {
            suppress = false;
        }
    });
}
