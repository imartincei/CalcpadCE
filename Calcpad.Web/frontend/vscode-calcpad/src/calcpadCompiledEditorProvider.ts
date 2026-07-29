import * as vscode from 'vscode';
import { writeUiOverrides } from 'calcpad-frontend';
import type { CalcpadApiClient, UiOverrideStore } from 'calcpad-frontend';

/**
 * A `.cpdz` opened in the workbench. The decoded source is held in memory — it is
 * never surfaced as a text document — alongside the file's original bytes, which
 * `encodeCpdz` needs to keep the other entries of a composite archive on save.
 */
class CompiledWorksheetDocument implements vscode.CustomDocument {
    constructor(
        readonly uri: vscode.Uri,
        public text: string,
        public originalBytes: Uint8Array,
    ) {}

    dispose(): void { /* nothing held beyond the fields above */ }
}

/** Renders the `#UI` input form for `text` into `panel`. Supplied by the extension. */
type RenderForm = (panel: vscode.WebviewPanel, text: string, uri: vscode.Uri) => Promise<void>;

/**
 * Opens compiled worksheets as an editor in their own right. A `.cpdz` is binary and
 * is meant to be filled in rather than read, so it never becomes a `TextDocument`:
 * the file is decoded, rendered as the `#UI` input form, and the values entered into
 * that form are written back by re-encoding around them. The source is not exposed
 * on the way through.
 *
 * Values mark the document dirty, so `Ctrl+S` and the workbench's save prompts work
 * as they do for any other editor. Edits are content changes rather than undoable
 * edits — a form control's own history is what the user reaches for, not the
 * workbench undo stack.
 */
export class CalcpadCompiledEditorProvider
    implements vscode.CustomEditorProvider<CompiledWorksheetDocument> {

    public static readonly viewType = 'calcpad.compiledWorksheet';

    private readonly _onDidChange =
        new vscode.EventEmitter<vscode.CustomDocumentContentChangeEvent<CompiledWorksheetDocument>>();
    public readonly onDidChangeCustomDocument = this._onDidChange.event;

    private readonly _panels = new Map<string, Set<vscode.WebviewPanel>>();

    constructor(
        private readonly _apiClient: CalcpadApiClient,
        private readonly _uiOverrides: UiOverrideStore,
        private readonly _renderForm: RenderForm,
        private readonly _log: vscode.OutputChannel,
    ) {}

    public static register(
        apiClient: CalcpadApiClient,
        uiOverrides: UiOverrideStore,
        renderForm: RenderForm,
        log: vscode.OutputChannel,
    ): vscode.Disposable {
        return vscode.window.registerCustomEditorProvider(
            CalcpadCompiledEditorProvider.viewType,
            new CalcpadCompiledEditorProvider(apiClient, uiOverrides, renderForm, log),
            { supportsMultipleEditorsPerDocument: true, webviewOptions: { retainContextWhenHidden: true } },
        );
    }

    async openCustomDocument(uri: vscode.Uri): Promise<CompiledWorksheetDocument> {
        const bytes = await vscode.workspace.fs.readFile(uri);
        const decoded = await this._apiClient.decodeCpdz(bytes);
        if (!decoded) throw new Error(`Could not read the compiled worksheet ${uri.fsPath}`);
        this._log.appendLine(`[cpdz] opened ${uri.fsPath} (composite: ${decoded.composite})`);
        return new CompiledWorksheetDocument(uri, decoded.content, bytes);
    }

    async resolveCustomEditor(
        document: CompiledWorksheetDocument,
        panel: vscode.WebviewPanel,
    ): Promise<void> {
        panel.webview.options = { enableScripts: true };
        this._track(document, panel);

        panel.webview.onDidReceiveMessage(message => {
            // The form posts every edited control; anything else the preview scripts
            // send (line links, console relay) has no meaning without a text editor.
            if (message?.type !== 'uiValueChange') return;
            const docKey = document.uri.toString();
            if (!this._uiOverrides.set(docKey, String(message.varName), String(message.newValue))) return;
            this._onDidChange.fire({ document });
            // Re-render so results depending on the edited value recalculate.
            void this._refresh(document);
        });

        await this._renderForm(panel, document.text, document.uri);
    }

    async saveCustomDocument(document: CompiledWorksheetDocument): Promise<void> {
        await this._write(document, document.uri);
    }

    async saveCustomDocumentAs(
        document: CompiledWorksheetDocument,
        destination: vscode.Uri,
    ): Promise<void> {
        await this._write(document, destination);
    }

    async revertCustomDocument(document: CompiledWorksheetDocument): Promise<void> {
        const bytes = await vscode.workspace.fs.readFile(document.uri);
        const decoded = await this._apiClient.decodeCpdz(bytes);
        if (!decoded) throw new Error(`Could not re-read the compiled worksheet ${document.uri.fsPath}`);
        document.text = decoded.content;
        document.originalBytes = bytes;
        // Drop the entered values so the form comes back as the file has it.
        this._uiOverrides.clear(document.uri.toString());
        await this._refresh(document);
    }

    async backupCustomDocument(
        document: CompiledWorksheetDocument,
        context: vscode.CustomDocumentBackupContext,
    ): Promise<vscode.CustomDocumentBackup> {
        await this._write(document, context.destination, { inPlace: false });
        return {
            id: context.destination.toString(),
            delete: async () => {
                try { await vscode.workspace.fs.delete(context.destination); } catch { /* already gone */ }
            },
        };
    }

    /**
     * Re-encodes around the entered values and writes to `target`. The original bytes
     * are handed to the encoder so a composite archive keeps its bundled images.
     * Writing in place adopts the result as the document's new baseline; a backup or
     * Save As leaves the open document alone.
     */
    private async _write(
        document: CompiledWorksheetDocument,
        target: vscode.Uri,
        { inPlace = true }: { inPlace?: boolean } = {},
    ): Promise<void> {
        const overrides = this._uiOverrides.toRecord(document.uri.toString());
        const text = overrides ? writeUiOverrides(document.text, overrides) : document.text;
        const bytes = await this._apiClient.encodeCpdz(text, document.originalBytes);
        if (!bytes) throw new Error(`Could not write the compiled worksheet ${target.fsPath}`);
        await vscode.workspace.fs.writeFile(target, bytes);
        if (inPlace) {
            document.text = text;
            document.originalBytes = bytes;
        }
        this._log.appendLine(`[cpdz] wrote ${target.fsPath}`);
    }

    private _track(document: CompiledWorksheetDocument, panel: vscode.WebviewPanel): void {
        const key = document.uri.toString();
        const panels = this._panels.get(key) ?? new Set<vscode.WebviewPanel>();
        panels.add(panel);
        this._panels.set(key, panels);
        panel.onDidDispose(() => {
            panels.delete(panel);
            if (panels.size > 0) return;
            this._panels.delete(key);
            // Entered values only live in memory; with no view left on the document
            // there is nothing to keep them for.
            this._uiOverrides.clear(key);
        });
    }

    private async _refresh(document: CompiledWorksheetDocument): Promise<void> {
        const panels = this._panels.get(document.uri.toString());
        if (!panels) return;
        for (const panel of panels)
            await this._renderForm(panel, document.text, document.uri);
    }
}
