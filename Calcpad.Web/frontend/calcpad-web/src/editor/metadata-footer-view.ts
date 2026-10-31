import * as monaco from 'monaco-editor';
import { findMetadataHiddenSpan, keepSelectionOutOfSpan } from 'calcpad-frontend';

const HIDDEN_SOURCE = 'calcpad-metadata-footer';

type Span = { start: number; end: number };

/** Hides an editor's metadata footer unless the Show File Metadata setting is on. */
export class MetadataFooterView implements monaco.IDisposable {
    private readonly disposables: monaco.IDisposable[] = [];
    private hidden: Span | null = null;
    private applied = '';

    constructor(
        private readonly editor: monaco.editor.IStandaloneCodeEditor,
        private readonly isShown: () => boolean,
    ) {
        this.disposables.push(
            editor.onDidChangeModel(() => this.update()),
            editor.onDidChangeModelContent(() => this.update()),
            editor.onDidChangeCursorSelection(() => this.keepSelectionOut()),
        );
        window.addEventListener('message', this.onMessage);
        this.update();
    }

    dispose(): void {
        window.removeEventListener('message', this.onMessage);
        for (const d of this.disposables) d.dispose();
    }

    private readonly onMessage = (event: MessageEvent): void => {
        if (event.source === null && event.data?.type === 'showFileMetadataChanged') this.update();
    };

    private update(): void {
        const model = this.editor.getModel();
        const span = model && !this.isShown()
            ? findMetadataHiddenSpan(i => model.getLineContent(i + 1), model.getLineCount())
            : null;
        // Hiding every line would leave Monaco nothing to show.
        this.hidden = span && span.start > 0 ? span : null;
        const applied = `${model?.id}:${this.hidden?.start ?? ''}:${this.hidden?.end ?? ''}`;
        if (applied === this.applied) return;
        this.applied = applied;
        const range = this.hidden && new monaco.Range(this.hidden.start + 1, 1, this.hidden.end + 1, 1);
        (this.editor as unknown as { setHiddenAreas(ranges: monaco.IRange[], source?: unknown): void })
            .setHiddenAreas(range ? [range] : [], HIDDEN_SOURCE);
        this.keepSelectionOut();
    }

    // Ctrl+End and Ctrl+A reach into the hidden comments; typing there would edit them unseen.
    private keepSelectionOut(): void {
        const span = this.hidden;
        const model = this.editor.getModel();
        const selections = this.editor.getSelections();
        if (!span || !model || !selections) return;
        const toPos = (line: number, column: number) => ({ line: line - 1, character: column - 1 });
        let changed = false;
        const next = selections.map(sel => {
            const fixed = keepSelectionOutOfSpan(
                toPos(sel.selectionStartLineNumber, sel.selectionStartColumn),
                toPos(sel.positionLineNumber, sel.positionColumn),
                span, model.getLineCount(), i => model.getLineMaxColumn(i + 1) - 1);
            if (!fixed) return sel;
            changed = true;
            return new monaco.Selection(fixed.anchor.line + 1, fixed.anchor.character + 1, fixed.active.line + 1, fixed.active.character + 1);
        });
        if (changed) this.editor.setSelections(next);
    }
}
