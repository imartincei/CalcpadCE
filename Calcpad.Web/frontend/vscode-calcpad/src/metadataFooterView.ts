import * as vscode from 'vscode';
import { findMetadataHiddenSpan, keepSelectionOutOfSpan, METADATA_HIDDEN_TEXT } from 'calcpad-frontend';

type Span = { start: number; end: number };

/** Hides a document's metadata footer unless the Show File Metadata setting is on. */
export class MetadataFooterView implements vscode.Disposable {
    // VS Code has no API for hiding a line, so the footer's text is collapsed behind a placeholder.
    private readonly decoration = vscode.window.createTextEditorDecorationType({ textDecoration: 'none; display: none;' });
    private readonly disposables: vscode.Disposable[];

    constructor(private readonly isShown: () => boolean) {
        this.disposables = [
            this.decoration,
            vscode.window.onDidChangeVisibleTextEditors(editors => this.refresh(editors)),
            vscode.window.onDidChangeTextEditorSelection(e => this.keepSelectionOut(e.textEditor)),
            vscode.workspace.onDidChangeTextDocument(e => {
                if (e.contentChanges.length) this.refresh(vscode.window.visibleTextEditors.filter(ed => ed.document === e.document));
            }),
        ];
        this.refresh();
    }

    dispose(): void {
        for (const d of this.disposables) d.dispose();
    }

    refresh(editors: readonly vscode.TextEditor[] = vscode.window.visibleTextEditors): void {
        for (const editor of editors) {
            if (editor.document.languageId !== 'calcpad') continue;
            const span = this.hiddenSpan(editor.document);
            const options: vscode.DecorationOptions[] = [];
            for (let line = span?.start ?? 0; span && line <= span.end; line++) {
                options.push({
                    range: editor.document.lineAt(line).range,
                    renderOptions: line === span.start
                        ? { before: { contentText: METADATA_HIDDEN_TEXT, color: new vscode.ThemeColor('editorCodeLens.foreground'), fontStyle: 'italic' } }
                        : undefined,
                });
            }
            editor.setDecorations(this.decoration, options);
            this.keepSelectionOut(editor);
        }
    }

    private hiddenSpan(document: vscode.TextDocument): Span | null {
        return this.isShown() ? null : findMetadataHiddenSpan(i => document.lineAt(i).text, document.lineCount);
    }

    // Ctrl+End and Ctrl+A reach into the hidden comments; typing there would edit them unseen.
    private keepSelectionOut(editor: vscode.TextEditor): void {
        const document = editor.document;
        if (document.languageId !== 'calcpad') return;
        const span = this.hiddenSpan(document);
        if (!span) return;
        let changed = false;
        const next = editor.selections.map(sel => {
            const fixed = keepSelectionOutOfSpan(sel.anchor, sel.active, span, document.lineCount, i => document.lineAt(i).text.length);
            if (!fixed) return sel;
            changed = true;
            return new vscode.Selection(fixed.anchor.line, fixed.anchor.character, fixed.active.line, fixed.active.character);
        });
        if (changed) editor.selections = next;
    }
}
