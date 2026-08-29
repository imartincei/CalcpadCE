import * as monaco from 'monaco-editor';
import { CalcpadApiClient } from 'calcpad-frontend/api/client';
import { CalcpadLintService } from 'calcpad-frontend/services/linter';
import type { LintDiagnostic } from 'calcpad-frontend/types/api';

export type LintSeverity = 'error' | 'warning' | 'information';

export type FileContextProvider =
    (content: string) => Promise<{ sourceFilePath?: string }>;

export interface DiagnosticsHandle extends monaco.IDisposable {
    /** Re-run lint immediately (used by manual Refresh). */
    refresh(): Promise<void>;
}

/**
 * Set up diagnostics: lint on content change (debounced), filter by severity, show markers in
 * Monaco. `getMinSeverity` and `getFileContext` are read on every lint pass, so Settings-tab
 * changes take effect on the next refresh and #include directives resolve correctly.
 */
export function setupDiagnostics(
    editor: monaco.editor.IStandaloneCodeEditor,
    apiClient: CalcpadApiClient,
    getMinSeverity: () => LintSeverity = () => 'information',
    getFileContext?: FileContextProvider,
    key?: string,
): DiagnosticsHandle {
    let debounceTimer: ReturnType<typeof setTimeout> | null = null;

    const run = () => lintAndMark(editor, apiClient, getMinSeverity(), getFileContext, key);

    const listener = editor.onDidChangeModelContent(() => {
        if (debounceTimer) clearTimeout(debounceTimer);
        debounceTimer = setTimeout(run, 500);
    });

    setTimeout(run, 300);

    return {
        dispose: () => {
            if (debounceTimer) clearTimeout(debounceTimer);
            listener.dispose();
        },
        refresh: run,
    };
}

async function lintAndMark(
    editor: monaco.editor.IStandaloneCodeEditor,
    apiClient: CalcpadApiClient,
    minSeverity: LintSeverity,
    getFileContext?: FileContextProvider,
    key?: string,
): Promise<void> {
    const model = editor.getModel();
    if (!model) return;

    const content = model.getValue();
    const ctx = getFileContext ? await getFileContext(content) : {};
    const response = await apiClient.lint(content, ctx.sourceFilePath, { key });

    if (!response?.diagnostics) {
        monaco.editor.setModelMarkers(model, 'calcpad', []);
        return;
    }

    const filtered = CalcpadLintService.filterBySeverity(response.diagnostics, minSeverity);

    const markers: monaco.editor.IMarkerData[] = filtered.map(
        (diag: LintDiagnostic) => ({
            severity: mapSeverity(diag.severityId),
            message: diag.message,
            startLineNumber: diag.line + 1,
            startColumn: diag.column + 1,
            endLineNumber: diag.line + 1,
            endColumn: diag.endColumn + 1,
        })
    );

    monaco.editor.setModelMarkers(model, 'calcpad', markers);
}

function mapSeverity(severityId: number): monaco.MarkerSeverity {
    switch (severityId) {
        case 0: return monaco.MarkerSeverity.Error;
        case 1: return monaco.MarkerSeverity.Warning;
        default: return monaco.MarkerSeverity.Info;
    }
}
