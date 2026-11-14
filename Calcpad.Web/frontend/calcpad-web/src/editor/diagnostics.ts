import * as monaco from 'monaco-editor';
import { CalcpadApiClient } from 'calcpad-frontend/api/client';
import { CalcpadLintService } from 'calcpad-frontend/services/linter';
import type { CalcpadError, LintDiagnostic } from 'calcpad-frontend/types/api';
import { expandRunErrors } from 'calcpad-frontend/services/run-errors';
import type { IncludedLintDiagnostic } from 'calcpad-frontend/services/included-lint';

export type LintSeverity = 'error' | 'warning' | 'information';

export type FileContextProvider =
    (content: string) => Promise<{ sourceFilePath?: string }>;

/** A clickable URI that opens a file at a 1-based line and column, or null when files can't be opened. */
export type FileLinker = (path: string, line: number, column: number) => monaco.Uri | null;

export interface DiagnosticsHandle extends monaco.IDisposable {
    /** Re-run lint immediately (used by manual Refresh). */
    refresh(): Promise<void>;
}

/**
 * Set up diagnostics: lint on content change (debounced), filter by severity, show markers in
 * Monaco. `getMinSeverity` and `getFileContext` are read on every lint pass, so Settings-tab
 * changes take effect on the next refresh and #include directives resolve correctly.
 * When `deferToRun` is true an edit doesn't lint; the run that follows calls `refresh`.
 */
export function setupDiagnostics(
    editor: monaco.editor.IStandaloneCodeEditor,
    apiClient: CalcpadApiClient,
    getMinSeverity: () => LintSeverity = () => 'information',
    getFileContext?: FileContextProvider,
    key?: string,
    useTrace: () => boolean = () => false,
    deferToRun: () => boolean = () => false,
    onLinted?: (model: monaco.editor.ITextModel, diagnostics: LintDiagnostic[]) => void,
    linkFile?: FileLinker,
    isComplex: () => boolean = () => false,
): DiagnosticsHandle {
    let debounceTimer: ReturnType<typeof setTimeout> | null = null;

    const run = () => lintAndMark(editor, apiClient, getMinSeverity(), getFileContext, key, useTrace(), onLinted, linkFile, isComplex());

    const listener = editor.onDidChangeModelContent(() => {
        if (debounceTimer) clearTimeout(debounceTimer);
        if (!deferToRun()) debounceTimer = setTimeout(run, 500);
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
    useTrace?: boolean,
    onLinted?: (model: monaco.editor.ITextModel, diagnostics: LintDiagnostic[]) => void,
    linkFile?: FileLinker,
    isComplex?: boolean,
): Promise<void> {
    const model = editor.getModel();
    if (!model) return;

    const content = model.getValue();
    const ctx = getFileContext ? await getFileContext(content) : {};
    const response = await apiClient.lint(content, ctx.sourceFilePath, { key, useTrace, isComplex });

    if (model.isDisposed()) return;
    onLinted?.(model, [...response?.diagnostics ?? [], ...response?.includeOnly ?? []]);
    if (!response?.diagnostics) {
        monaco.editor.setModelMarkers(model, 'calcpad', []);
        return;
    }

    const filtered = CalcpadLintService.filterBySeverity(response.diagnostics, minSeverity);

    const markers: monaco.editor.IMarkerData[] = filtered.map(
        (diag: LintDiagnostic) => ({
            severity: mapSeverity(diag.severityId),
            message: `[Linter] ${diag.message}`,
            code: diag.code,
            startLineNumber: diag.line + 1,
            startColumn: diag.column + 1,
            endLineNumber: diag.line + 1,
            endColumn: diag.endColumn + 1,
            relatedInformation: [
                ...diag.related?.map(r => ({
                    resource: model.uri,
                    message: r.message,
                    startLineNumber: r.line + 1,
                    startColumn: r.column + 1,
                    endLineNumber: r.line + 1,
                    endColumn: r.endColumn + 1,
                })) ?? [],
                ...diag.includeOrigins?.flatMap(o => {
                    const column = Math.max(o.column, 0) + 1;
                    const resource = linkFile?.(o.filePath, o.line + 1, column);
                    return resource ? [{
                        resource,
                        message: 'Error in the included file',
                        startLineNumber: o.line + 1,
                        startColumn: column,
                        endLineNumber: o.line + 1,
                        endColumn: Math.max(o.endColumn, 0) + 1,
                    }] : [];
                }) ?? [],
            ],
        })
    );

    monaco.editor.setModelMarkers(model, 'calcpad', markers);
}

/** Shows the errors other open files' lint found in this included file, beside its own lint. */
export function setIncludedMarkers(model: monaco.editor.ITextModel, diagnostics: IncludedLintDiagnostic[], linkFile?: FileLinker): void {
    if (model.isDisposed()) return;
    const lineCount = model.getLineCount();
    monaco.editor.setModelMarkers(model, 'calcpad-included', diagnostics
        .filter(d => d.line < lineCount)
        .map(d => {
            const line = d.line + 1;
            const whole = d.column < 0;
            return {
                severity: mapSeverity(d.severityId),
                message: `[Linter] ${d.message}`,
                code: d.code,
                startLineNumber: line,
                startColumn: whole ? model.getLineFirstNonWhitespaceColumn(line) || 1 : d.column + 1,
                endLineNumber: line,
                endColumn: whole ? model.getLineMaxColumn(line) : d.endColumn + 1,
                relatedInformation: d.related.map(r => ({
                    resource: linkFile?.(d.basePath, r.line + 1, r.column + 1) ?? monaco.Uri.parse(d.base),
                    message: r.message,
                    startLineNumber: r.line + 1,
                    startColumn: r.column + 1,
                    endLineNumber: r.line + 1,
                    endColumn: r.endColumn + 1,
                })),
            };
        }));
}

/** Shows a run's errors beside lint's, until the next run replaces them. */
export function setRunErrorMarkers(model: monaco.editor.ITextModel, errors: CalcpadError[]): void {
    if (model.isDisposed()) return;
    const lineCount = model.getLineCount();
    const lineOf = (sourceLine: number) => Math.min(Math.max(sourceLine, 1), lineCount);
    // 1-based [line, start, end] on one line, or the whole line when the columns don't fit
    const span = (sourceLine: number, start = -1, end = -1) => {
        const line = lineOf(sourceLine);
        return start >= 0 && end > start && end < model.getLineMaxColumn(line)
            ? [line, start + 1, end + 1]
            : [line, model.getLineFirstNonWhitespaceColumn(line) || 1, model.getLineMaxColumn(line)];
    };
    monaco.editor.setModelMarkers(model, 'calcpad-run', expandRunErrors(errors).map(err => {
        const [line, startColumn, endColumn] = span(err.sourceLine, err.startColumn, err.endColumn);
        const last = err.additionalRanges?.[err.additionalRanges.length - 1];
        const [endLine, , lastEnd] = last ? span(last.sourceLine, last.startColumn, last.endColumn) : [line, 0, endColumn];
        return {
            severity: monaco.MarkerSeverity.Error,
            message: `[Runtime] ${err.message}`,
            code: err.code,
            startLineNumber: line,
            startColumn,
            endLineNumber: endLine,
            endColumn: lastEnd,
            relatedInformation: err.related.map(r => ({
                resource: model.uri,
                message: r.message,
                startLineNumber: lineOf(r.line),
                startColumn: model.getLineFirstNonWhitespaceColumn(lineOf(r.line)) || 1,
                endLineNumber: lineOf(r.line),
                endColumn: model.getLineMaxColumn(lineOf(r.line)),
            })),
        };
    }));
}

function mapSeverity(severityId: number): monaco.MarkerSeverity {
    switch (severityId) {
        case 0: return monaco.MarkerSeverity.Error;
        case 1: return monaco.MarkerSeverity.Warning;
        default: return monaco.MarkerSeverity.Info;
    }
}
