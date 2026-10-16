import * as monaco from 'monaco-editor';
import {
    shouldIncreaseIndent,
    shouldDecreaseIndent,
    getIndentation,
    couldCompleteDedentKeyword,
    calculateExpectedIndent,
} from 'calcpad-frontend/text/auto-indent';

/**
 * Smart indentation for CalcPad control blocks (#if / #for / #while / #def):
 *  - Newline after a block-opener line → indent one level deeper
 *  - Typing a block-closer keyword (#end if, #else, #loop, #end def) → dedent
 */
export function attachAutoIndenter(
    editor: monaco.editor.IStandaloneCodeEditor,
): monaco.IDisposable {
    let suppress = false;

    function indentUnit(): string {
        const opts = editor.getOptions();
        const tabSize = editor.getModel()?.getOptions().tabSize ?? 4;
        const insertSpaces = editor.getModel()?.getOptions().insertSpaces ?? true;
        // Reference opts to keep TS happy if needed; falls through.
        void opts;
        return insertSpaces ? ' '.repeat(tabSize) : '\t';
    }

    return editor.onDidChangeModelContent(e => {
        if (suppress) return;
        const model = editor.getModel();
        if (!model) return;

        for (const change of e.changes) {
            if (change.text.includes('\n')) {
                handleNewline(editor, model, change, indentUnit(), () => suppress, v => { suppress = v; });
            } else if (change.text.length > 0) {
                const lineNumber = change.range.startLineNumber;
                const lineText = model.getLineContent(lineNumber);
                if (couldCompleteDedentKeyword(lineText)) {
                    handleDedentKeyword(editor, model, lineNumber, () => suppress, v => { suppress = v; });
                }
            }
        }
    });
}

function handleNewline(
    editor: monaco.editor.IStandaloneCodeEditor,
    model: monaco.editor.ITextModel,
    change: monaco.editor.IModelContentChange,
    indent: string,
    isSuppressed: () => boolean,
    setSuppressed: (v: boolean) => void,
): void {
    if (isSuppressed()) return;

    const previousLineNumber = change.range.startLineNumber;
    const newLineNumber = previousLineNumber + 1;
    if (newLineNumber > model.getLineCount()) return;

    const previousLineText = model.getLineContent(previousLineNumber);
    if (!shouldIncreaseIndent(previousLineText)) return;

    const previousIndent = getIndentation(previousLineText);
    const targetIndent = previousIndent + indent;

    const currentLineText = model.getLineContent(newLineNumber);
    const currentIndent = getIndentation(currentLineText);
    if (currentIndent === targetIndent) return;

    setSuppressed(true);
    try {
        editor.executeEdits('calcpad-auto-indent', [{
            range: new monaco.Range(newLineNumber, 1, newLineNumber, currentIndent.length + 1),
            text: targetIndent,
            forceMoveMarkers: true,
        }]);
        editor.setPosition({ lineNumber: newLineNumber, column: targetIndent.length + 1 });
    } finally {
        setSuppressed(false);
    }
}

function handleDedentKeyword(
    editor: monaco.editor.IStandaloneCodeEditor,
    model: monaco.editor.ITextModel,
    lineNumber: number,
    isSuppressed: () => boolean,
    setSuppressed: (v: boolean) => void,
): void {
    if (isSuppressed()) return;

    const lineText = model.getLineContent(lineNumber);
    if (!shouldDecreaseIndent(lineText)) return;

    const alsoIncreases = shouldIncreaseIndent(lineText);
    const expectedIndent = calculateExpectedIndent(
        lineNumber - 1, // function uses 0-based indexing
        i => model.getLineContent(i + 1),
        alsoIncreases,
    );
    const currentIndent = getIndentation(lineText);
    if (currentIndent === expectedIndent) return;

    setSuppressed(true);
    try {
        editor.executeEdits('calcpad-auto-indent', [{
            range: new monaco.Range(lineNumber, 1, lineNumber, currentIndent.length + 1),
            text: expectedIndent,
            forceMoveMarkers: true,
        }]);
    } finally {
        setSuppressed(false);
    }
}
