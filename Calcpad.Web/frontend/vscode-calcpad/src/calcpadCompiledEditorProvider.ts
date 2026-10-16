import * as vscode from 'vscode';
import type { ILogger } from 'calcpad-frontend';
import * as path from 'path';
import { writeUiOverrides } from 'calcpad-frontend';
import type { CalcpadApiClient, UiOverrideStore } from 'calcpad-frontend';
import { handleFrameStateMessage } from './previewFrame';

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

/**
 * Renders `text` into `panel` — as the `#UI` input form, or as the print report of the
 * values entered into it. Supplied by the extension, which owns the conversion.
 */
type RenderPanel = (
    panel: vscode.WebviewPanel,
    text: string,
    uri: vscode.Uri,
    kind: 'form' | 'report',
) => Promise<void>;

/**
 * Opens compiled worksheets as an editor in their own right: a `.cpdz` is binary and meant to be
 * filled in rather than read, so it never becomes a `TextDocument` — the file is decoded, rendered
 * as the `#UI` input form, and the values entered into it written back by re-encoding around them
 * without exposing the source. Values mark the document dirty as content changes rather than
 * undoable edits, so `Ctrl+S` and the save prompts work normally.
 */
export class CalcpadCompiledEditorProvider
    implements vscode.CustomEditorProvider<CompiledWorksheetDocument>, vscode.Disposable {

    public static readonly viewType = 'calcpad.compiledWorksheet';
    /** The report panel's view type, which package.json's menus key their buttons off. */
    public static readonly reportViewType = 'calcpadCompiledReport';

    private readonly _onDidChange =
        new vscode.EventEmitter<vscode.CustomDocumentContentChangeEvent<CompiledWorksheetDocument>>();
    public readonly onDidChangeCustomDocument = this._onDidChange.event;

    private readonly _panels = new Map<string, Set<vscode.WebviewPanel>>();
    // Open documents by uri, so the extension's preview and export commands can reach a
    // compiled worksheet's source: it is held here and nowhere else.
    private readonly _docs = new Map<string, CompiledWorksheetDocument>();
    private readonly _reports = new Map<string, vscode.WebviewPanel>();
    private _registration: vscode.Disposable | undefined;

    constructor(
        private readonly _apiClient: CalcpadApiClient,
        private readonly _uiOverrides: UiOverrideStore,
        private readonly _renderPanel: RenderPanel,
        private readonly _log: ILogger,
    ) {}

    public static register(
        apiClient: CalcpadApiClient,
        uiOverrides: UiOverrideStore,
        renderPanel: RenderPanel,
        log: ILogger,
    ): CalcpadCompiledEditorProvider {
        const provider = new CalcpadCompiledEditorProvider(apiClient, uiOverrides, renderPanel, log);
        provider._registration = vscode.window.registerCustomEditorProvider(
            CalcpadCompiledEditorProvider.viewType,
            provider,
            { supportsMultipleEditorsPerDocument: true, webviewOptions: { retainContextWhenHidden: true } },
        );
        return provider;
    }

    dispose(): void {
        this._registration?.dispose();
        for (const report of this._reports.values()) report.dispose();
    }

    /**
     * The decoded source of an open compiled worksheet, or undefined when that file has
     * no editor open on it. Entered values are not applied here — they live in the shared
     * override store under the same uri, which the render and export paths pass along.
     */
    public sourceFor(uri: vscode.Uri): string | undefined {
        return this._docs.get(uri.toString())?.text;
    }

    /**
     * Opens the report for a compiled worksheet beside its form, or closes it if it is
     * already open. Together with the form it is the whole of what a `.cpdz` shows — the
     * source stays hidden — and it re-renders as values are entered.
     */
    public async toggleReport(uri: vscode.Uri): Promise<void> {
        const key = uri.toString();
        const open = this._reports.get(key);
        if (open) {
            open.dispose();
            return;
        }
        const document = this._docs.get(key);
        if (!document) return;

        const forms = [...(this._panels.get(key) ?? [])];
        const form = forms.find(p => p.active) ?? forms[0];
        const panel = vscode.window.createWebviewPanel(
            CalcpadCompiledEditorProvider.reportViewType,
            `CalcpadCE Report - ${path.basename(uri.fsPath)}`,
            vscode.ViewColumn.Beside,
            // The rendered worksheet is sandboxed a frame deeper (see previewFrame.ts),
            // which the workbench's find widget cannot reach and which needs no local
            // resources — a compiled worksheet carries its images inline.
            { enableScripts: true, retainContextWhenHidden: true, localResourceRoots: [] },
        );
        this._reports.set(key, panel);
        // The report carries no controls, so the frame's own state — where it was
        // scrolled to, links clicked in it — is all it has to send.
        panel.webview.onDidReceiveMessage(message => { handleFrameStateMessage(panel, message); });
        panel.onDidDispose(() => {
            if (this._reports.get(key) === panel) this._reports.delete(key);
        });
        this._log.appendLine(`[cpdz] report opened for ${uri.fsPath}`);
        await this._renderPanel(panel, document.text, document.uri, 'report');
        // The report is there to be watched while the form is filled in, so the form
        // takes the focus back off it.
        form?.reveal(form.viewColumn, false);
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
        panel.webview.options = { enableScripts: true, localResourceRoots: [] };
        this._track(document, panel);

        panel.webview.onDidReceiveMessage(message => {
            // Scroll offset, #UI position and outbound links belong to the frame the
            // worksheet renders in, and are the same for every panel that hosts one.
            if (handleFrameStateMessage(panel, message)) return;
            // The form posts every edited control; anything else the preview scripts
            // send (line links, console relay) has no meaning without a text editor.
            if (message?.type !== 'uiValueChange') return;
            const docKey = document.uri.toString();
            if (!this._uiOverrides.set(docKey, String(message.varName), String(message.newValue))) return;
            this._onDidChange.fire({ document });
            // Re-render so results depending on the edited value recalculate.
            void this._refresh(document);
        });

        await this._renderPanel(panel, document.text, document.uri, 'form');
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
     * Re-encodes around the entered values and writes to `target`, handing the original bytes
     * to the encoder so a composite archive keeps its bundled images. Writing in place adopts
     * the result as the document's new baseline; a backup or Save As leaves it alone.
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
        this._docs.set(key, document);
        panel.onDidDispose(() => {
            panels.delete(panel);
            if (panels.size > 0) return;
            this._panels.delete(key);
            this._docs.delete(key);
            // The report is a view of the form's values; with the form gone it has
            // nothing left to show.
            this._reports.get(key)?.dispose();
            // Entered values only live in memory; with no view left on the document
            // there is nothing to keep them for.
            this._uiOverrides.clear(key);
        });
    }

    private async _refresh(document: CompiledWorksheetDocument): Promise<void> {
        const key = document.uri.toString();
        for (const panel of this._panels.get(key) ?? [])
            await this._renderPanel(panel, document.text, document.uri, 'form');
        const report = this._reports.get(key);
        if (report) await this._renderPanel(report, document.text, document.uri, 'report');
    }
}
