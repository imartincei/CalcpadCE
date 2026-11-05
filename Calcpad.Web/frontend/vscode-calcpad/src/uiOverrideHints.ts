import * as vscode from 'vscode';
import { computeUiOverrideHints, readUiOverrides, uiOverrideHintTitle, UiOverrideHintTracker } from 'calcpad-frontend';
import type { UiControl } from 'calcpad-frontend';

export const REVEAL_UI_OVERRIDES_COMMAND = 'vscode-calcpad.revealUiOverrides';

/** CodeLens above each line whose `#UI` controls take a saved value, linking to the Properties tab. */
export class UiOverrideHintsProvider implements vscode.CodeLensProvider, vscode.Disposable {
    private readonly changed = new vscode.EventEmitter<void>();
    readonly onDidChangeCodeLenses = this.changed.event;
    readonly tracker: UiOverrideHintTracker;
    private readonly disposables: vscode.Disposable[];

    constructor(
        private readonly isEnabled: () => boolean,
        render: (document: vscode.TextDocument) => Promise<UiControl[] | null>,
    ) {
        this.tracker = new UiOverrideHintTracker({
            resolve: async (docKey) => {
                const document = vscode.window.visibleTextEditors.find(e => e.document.uri.toString() === docKey)?.document;
                if (!document || !this.isEnabled() || !readUiOverrides(document.getText())) return null;
                return render(document);
            },
            changed: () => this.changed.fire(),
        });
        this.disposables = [
            this.changed,
            vscode.languages.registerCodeLensProvider({ language: 'calcpad' }, this),
            vscode.workspace.onDidChangeTextDocument(e => {
                if (e.document.languageId !== 'calcpad' || !e.contentChanges.length) return;
                this.tracker.applyEdits(e.document.uri.toString(), e.contentChanges.map(c => ({
                    startLine: c.range.start.line + 1,
                    startColumn: c.range.start.character + 1,
                    endLine: c.range.end.line + 1,
                    endColumn: c.range.end.character + 1,
                    text: c.text,
                })));
            }),
            vscode.workspace.onDidCloseTextDocument(d => this.tracker.forget(d.uri.toString())),
        ];
    }

    refresh(): void {
        this.changed.fire();
    }

    provideCodeLenses(document: vscode.TextDocument): vscode.CodeLens[] {
        if (!this.isEnabled()) return [];
        const overrides = readUiOverrides(document.getText());
        if (!overrides || !Object.keys(overrides).length) return [];
        const docKey = document.uri.toString();
        const controls = this.tracker.controlsFor(docKey);
        if (!controls) {
            void this.tracker.ensure(docKey);
            return [];
        }
        const hints = computeUiOverrideHints(overrides, controls,
            line => line >= 1 && line <= document.lineCount ? document.lineAt(line - 1).text : undefined);
        return hints.filter(h => h.line <= document.lineCount).map(h => new vscode.CodeLens(
            document.lineAt(h.line - 1).range,
            {
                command: REVEAL_UI_OVERRIDES_COMMAND,
                title: uiOverrideHintTitle(h),
                tooltip: 'Edit in the Properties tab',
                arguments: [document.uri, h.line, h.keys],
            },
        ));
    }

    dispose(): void {
        this.tracker.dispose();
        for (const d of this.disposables) d.dispose();
    }
}
