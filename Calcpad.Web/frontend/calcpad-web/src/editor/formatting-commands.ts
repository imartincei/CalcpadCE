import * as monaco from 'monaco-editor';
import type { EditorBridge } from './bridge';
import {
    HTML_INLINE,
    MARKDOWN_INLINE,
    getCommentPrefixInsertColumn,
    buildHeadingLine,
    buildParagraphLine,
    buildListLines,
    type InlineFormat,
    type CommentFormat,
} from 'calcpad-frontend';

/**
 * Register the 18 CalcPad text-formatting commands plus their keybindings on the editor.
 * Every command is gated on `extraSettings.formattingHotkeys !== 'false'`.
 */
export function registerFormattingCommands(
    editor: monaco.editor.IStandaloneCodeEditor,
    bridge: EditorBridge,
): monaco.IDisposable {
    const disposables: monaco.IDisposable[] = [];

    const guard = (run: () => void | Promise<void>) => async () => {
        if (bridge.getExtraSetting('formattingHotkeys') === 'false') return;
        await run();
    };

    function add(id: string, label: string, keybindings: number[], run: () => void | Promise<void>): void {
        disposables.push(editor.addAction({
            id,
            label,
            keybindings,
            run: guard(run),
        }));
    }

    const KM = monaco.KeyMod;
    const KC = monaco.KeyCode;

    // Inline formatting
    add('calcpad.formatBold',         'CalcpadCE: Bold',         [KM.CtrlCmd | KC.KeyB], () => wrapInline(editor, bridge, 'bold'));
    add('calcpad.formatItalic',       'CalcpadCE: Italic',       [KM.CtrlCmd | KC.KeyI], () => wrapInline(editor, bridge, 'italic'));
    add('calcpad.formatUnderline',    'CalcpadCE: Underline',    [KM.CtrlCmd | KC.KeyU], () => wrapInline(editor, bridge, 'underline'));
    add('calcpad.formatSubscript',    'CalcpadCE: Subscript',    [KM.CtrlCmd | KC.Equal], () => wrapInline(editor, bridge, 'subscript'));
    add('calcpad.formatSuperscript',  'CalcpadCE: Superscript',  [KM.CtrlCmd | KM.Shift | KC.Equal], () => wrapInline(editor, bridge, 'superscript'));

    // Headings (Ctrl+1..Ctrl+6)
    const digitKeys = [KC.Digit1, KC.Digit2, KC.Digit3, KC.Digit4, KC.Digit5, KC.Digit6];
    for (let i = 0; i < 6; i++) {
        const level = i + 1;
        add(`calcpad.formatHeading${level}`, `CalcpadCE: Heading ${level}`,
            [KM.CtrlCmd | digitKeys[i]],
            () => insertHeading(editor, bridge, level));
    }

    // Block elements
    add('calcpad.formatParagraph',    'CalcpadCE: Paragraph',     [KM.CtrlCmd | KC.KeyL], () => insertParagraph(editor));
    add('calcpad.formatLineBreak',    'CalcpadCE: Line Break',    [KM.CtrlCmd | KC.KeyR], () => insertLineBreak(editor));
    add('calcpad.formatBulletedList', 'CalcpadCE: Bulleted List', [KM.CtrlCmd | KM.Shift | KC.KeyL], () => insertBulletedList(editor, bridge));
    add('calcpad.formatNumberedList', 'CalcpadCE: Numbered List', [KM.CtrlCmd | KM.Shift | KC.KeyN], () => insertNumberedList(editor, bridge));

    // Comments
    add('calcpad.toggleComment',  'CalcpadCE: Toggle Comment',   [KM.CtrlCmd | KC.KeyQ], () => toggleComment(editor));
    add('calcpad.uncomment',      'CalcpadCE: Uncomment',        [KM.CtrlCmd | KM.Shift | KC.KeyQ], () => uncomment(editor));
    add('calcpad.pasteAsComment', 'CalcpadCE: Paste as Comment', [KM.CtrlCmd | KM.Shift | KC.KeyV], () => pasteAsComment(editor));

    return { dispose() { for (const d of disposables) d.dispose(); } };
}

function getFormat(editor: monaco.editor.IStandaloneCodeEditor, bridge: EditorBridge): CommentFormat {
    const setting = bridge.getExtraSetting('commentFormat') ?? 'auto';
    if (setting === 'markdown') return 'markdown';
    if (setting === 'auto') return detectFormatAtCursor(editor);
    return 'html';
}

function detectFormatAtCursor(editor: monaco.editor.IStandaloneCodeEditor): CommentFormat {
    const model = editor.getModel();
    const pos = editor.getPosition();
    if (!model || !pos) return 'html';

    let mdActive = false;
    for (let i = 1; i <= pos.lineNumber; i++) {
        const t = model.getLineContent(i).trim();
        if (t === '#md on') mdActive = true;
        else if (t === '#md off') mdActive = false;
    }
    return mdActive ? 'markdown' : 'html';
}

/**
 * Insert a comment quote on every selected line that needs one, right after
 * its indentation (same rule headings use). A line is skipped when it already
 * opens a comment or when the selection already lands inside a text region
 * mid-line. Returns the 1-based column each quote was inserted at, keyed by
 * line number, for shifting selections.
 */
function ensureCommentPrefixes(
    model: monaco.editor.ITextModel,
    selections: readonly monaco.Selection[],
    edits: monaco.editor.IIdentifiedSingleEditOperation[],
): Map<number, number> {
    const startColByLine = new Map<number, number>();
    for (const sel of selections) {
        const prev = startColByLine.get(sel.startLineNumber);
        if (prev === undefined || sel.startColumn < prev) startColByLine.set(sel.startLineNumber, sel.startColumn);
    }

    const insertedAt = new Map<number, number>();
    for (const [lineNumber, startColumn] of startColByLine) {
        const insertCol = getCommentPrefixInsertColumn(model.getLineContent(lineNumber), startColumn - 1);
        if (insertCol === null) continue;
        edits.push({
            range: new monaco.Range(lineNumber, insertCol, lineNumber, insertCol),
            text: "'",
            forceMoveMarkers: true,
        });
        insertedAt.set(lineNumber, insertCol);
    }
    return insertedAt;
}

function wrapInline(
    editor: monaco.editor.IStandaloneCodeEditor,
    bridge: EditorBridge,
    type: InlineFormat,
): void {
    const model = editor.getModel();
    if (!model) return;
    const format = getFormat(editor, bridge);
    const [prefix, suffix] = format === 'html' ? HTML_INLINE[type] : MARKDOWN_INLINE[type];
    const selections = editor.getSelections() ?? [];
    if (selections.length === 0) return;

    const edits: monaco.editor.IIdentifiedSingleEditOperation[] = [];
    // Bold/italic/etc. need the line wrapped in a comment for the HTML tags
    // to render, same as headings — add one if missing.
    const insertedAt = ensureCommentPrefixes(model, selections, edits);

    const newSelections: monaco.Selection[] = [];

    for (const sel of selections) {
        const shift = insertedAt.has(sel.startLineNumber) ? 1 : 0;
        const startCol = sel.startColumn + shift;
        const endShift = sel.endLineNumber === sel.startLineNumber ? shift : 0;
        const isEmpty = sel.isEmpty();
        if (isEmpty) {
            const text = prefix + suffix;
            edits.push({ range: sel, text, forceMoveMarkers: true });
            // After insert, cursor sits between prefix and suffix.
            const placedCol = startCol + prefix.length;
            newSelections.push(new monaco.Selection(sel.startLineNumber, placedCol, sel.startLineNumber, placedCol));
        } else {
            const selectedText = model.getValueInRange(sel);
            edits.push({ range: sel, text: prefix + selectedText + suffix, forceMoveMarkers: true });
            newSelections.push(new monaco.Selection(
                sel.startLineNumber, startCol,
                sel.endLineNumber, sel.endColumn + endShift + prefix.length + suffix.length,
            ));
        }
    }

    editor.executeEdits('calcpad-format-inline', edits, newSelections);
}

function insertHeading(
    editor: monaco.editor.IStandaloneCodeEditor,
    bridge: EditorBridge,
    level: number,
): void {
    const model = editor.getModel();
    if (!model) return;
    const format = getFormat(editor, bridge);
    const selections = editor.getSelections() ?? [];

    const edits: monaco.editor.IIdentifiedSingleEditOperation[] = [];
    const contentWasEmpty: boolean[] = [];
    for (const sel of selections) {
        const lineNumber = sel.positionLineNumber;
        const lineText = model.getLineContent(lineNumber);
        const strippedContent = lineText
            .replace(/^<h[1-6]>(.*)<\/h[1-6]>$/, '$1')
            .replace(/^#{1,6}\s+(.*)$/, '$1');
        contentWasEmpty.push(strippedContent.trim().length === 0);
        edits.push({
            range: new monaco.Range(lineNumber, 1, lineNumber, lineText.length + 1),
            text: buildHeadingLine(lineText, level, format),
            forceMoveMarkers: true,
        });
    }
    editor.executeEdits('calcpad-format-heading', edits);

    // Place cursor between tags when the line's content was empty, same as the VS Code extension.
    if (format === 'html') {
        const closingTag = `</h${level}>`;
        const newSelections: monaco.Selection[] = [];
        for (let i = 0; i < selections.length; i++) {
            if (contentWasEmpty[i]) {
                const lineNumber = selections[i].positionLineNumber;
                const lineLength = model.getLineLength(lineNumber);
                const col = lineLength + 1 - closingTag.length;
                newSelections.push(new monaco.Selection(lineNumber, col, lineNumber, col));
            } else {
                newSelections.push(selections[i]);
            }
        }
        editor.setSelections(newSelections);
    }
}

function insertParagraph(editor: monaco.editor.IStandaloneCodeEditor): void {
    const model = editor.getModel();
    if (!model) return;
    const selections = editor.getSelections() ?? [];

    const edits: monaco.editor.IIdentifiedSingleEditOperation[] = [];
    for (const sel of selections) {
        const lineNumber = sel.positionLineNumber;
        const lineText = model.getLineContent(lineNumber);
        edits.push({
            range: new monaco.Range(lineNumber, 1, lineNumber, lineText.length + 1),
            text: buildParagraphLine(lineText),
            forceMoveMarkers: true,
        });
    }
    editor.executeEdits('calcpad-format-paragraph', edits);
}

function insertLineBreak(editor: monaco.editor.IStandaloneCodeEditor): void {
    const selections = editor.getSelections() ?? [];
    const edits: monaco.editor.IIdentifiedSingleEditOperation[] = selections.map(sel => ({
        range: sel,
        text: "'<br/>\n",
        forceMoveMarkers: true,
    }));
    editor.executeEdits('calcpad-format-linebreak', edits);
}

function insertBulletedList(
    editor: monaco.editor.IStandaloneCodeEditor,
    bridge: EditorBridge,
): void {
    const model = editor.getModel();
    if (!model) return;
    const sel = editor.getSelection();
    if (!sel) return;
    const format = getFormat(editor, bridge);
    const startLine = sel.startLineNumber;
    const endLine = sel.endLineNumber;

    const lineTexts: string[] = [];
    for (let i = startLine; i <= endLine; i++) lineTexts.push(model.getLineContent(i));
    const lines = buildListLines(lineTexts, format, false);

    editor.executeEdits('calcpad-format-ul', [{
        range: new monaco.Range(startLine, 1, endLine, model.getLineLength(endLine) + 1),
        text: lines.join('\n'),
        forceMoveMarkers: true,
    }]);
}

function insertNumberedList(
    editor: monaco.editor.IStandaloneCodeEditor,
    bridge: EditorBridge,
): void {
    const model = editor.getModel();
    if (!model) return;
    const sel = editor.getSelection();
    if (!sel) return;
    const format = getFormat(editor, bridge);
    const startLine = sel.startLineNumber;
    const endLine = sel.endLineNumber;

    const lineTexts: string[] = [];
    for (let i = startLine; i <= endLine; i++) lineTexts.push(model.getLineContent(i));
    const lines = buildListLines(lineTexts, format, true);

    editor.executeEdits('calcpad-format-ol', [{
        range: new monaco.Range(startLine, 1, endLine, model.getLineLength(endLine) + 1),
        text: lines.join('\n'),
        forceMoveMarkers: true,
    }]);
}

function toggleComment(editor: monaco.editor.IStandaloneCodeEditor): void {
    const model = editor.getModel();
    if (!model) return;
    const sel = editor.getSelection();
    if (!sel) return;
    const startLine = sel.startLineNumber;
    const endLine = sel.endLineNumber;

    let allCommented = true;
    for (let i = startLine; i <= endLine; i++) {
        if (!model.getLineContent(i).startsWith("'")) { allCommented = false; break; }
    }

    const edits: monaco.editor.IIdentifiedSingleEditOperation[] = [];
    for (let i = startLine; i <= endLine; i++) {
        const text = model.getLineContent(i);
        if (allCommented) {
            if (text.startsWith("'")) {
                edits.push({ range: new monaco.Range(i, 1, i, 2), text: '', forceMoveMarkers: true });
            }
        } else {
            edits.push({ range: new monaco.Range(i, 1, i, 1), text: "'", forceMoveMarkers: true });
        }
    }
    editor.executeEdits('calcpad-toggle-comment', edits);
}

function uncomment(editor: monaco.editor.IStandaloneCodeEditor): void {
    const model = editor.getModel();
    if (!model) return;
    const sel = editor.getSelection();
    if (!sel) return;

    const edits: monaco.editor.IIdentifiedSingleEditOperation[] = [];
    for (let i = sel.startLineNumber; i <= sel.endLineNumber; i++) {
        if (model.getLineContent(i).startsWith("'")) {
            edits.push({ range: new monaco.Range(i, 1, i, 2), text: '', forceMoveMarkers: true });
        }
    }
    editor.executeEdits('calcpad-uncomment', edits);
}

async function pasteAsComment(editor: monaco.editor.IStandaloneCodeEditor): Promise<void> {
    let clipboardText = '';
    try {
        clipboardText = await navigator.clipboard.readText();
    } catch {
        return;
    }
    if (!clipboardText) return;

    const commented = clipboardText.split('\n').map(l => "'" + l).join('\n');
    const sel = editor.getSelection();
    if (!sel) return;

    editor.executeEdits('calcpad-paste-as-comment', [{
        range: sel,
        text: commented,
        forceMoveMarkers: true,
    }]);
}
