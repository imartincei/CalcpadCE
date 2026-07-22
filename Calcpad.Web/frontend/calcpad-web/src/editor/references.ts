import * as monaco from 'monaco-editor';
import type { SymbolAtPositionResponse, SymbolLocation } from 'calcpad-frontend/types/api';
import type { EditorBridge } from './bridge';
import type { FileContextProvider } from './diagnostics';
import { parseDirectiveLine } from './include-completions';

/**
 * Resolves a `SymbolLocation` whose `source !== 'local'` to a Monaco model URI
 * by opening the referenced include file (reading it from disk and registering
 * a Monaco model) so the Find All References panel can render its snippet.
 *
 * Only available on platforms with disk access (Tauri desktop, VS Code).
 * Returns null if the file cannot be opened.
 *
 * TODO(web/remote): When running in a browser tab or against a remote server,
 * disk access is not available — we need a story for fetching include content
 * from the server (or from the in-memory `includeFiles` map the linter already
 * accepts) and creating a read-only Monaco model from it so cross-file
 * navigation works there too. Until then, navigation into includes is a
 * desktop-only feature.
 */
export type IncludeFileOpener = (rawFileName: string) => Promise<monaco.Uri | null>;

/**
 * Resolves an include's raw file name to a Monaco URI for go-to-definition,
 * registering a detached (tab-less) Monaco model for it so Monaco can render
 * the Ctrl+hover underline + preview *without* opening or activating a tab.
 * Used instead of `IncludeFileOpener` because Monaco calls `provideDefinition`
 * on Ctrl+hover, so navigation must not happen here — the real tab open +
 * cursor move happen later in an editor opener that Monaco only invokes on a
 * real click / F12. Desktop-only. Returns null when the path can't be resolved.
 */
export type IncludeUriResolver = (rawFileName: string) => Promise<monaco.Uri | null>;

async function resolveSymbol(
    bridge: EditorBridge,
    model: monaco.editor.ITextModel,
    position: monaco.Position,
    getFileContext?: FileContextProvider,
): Promise<SymbolAtPositionResponse | null> {
    const content = model.getValue();
    const ctx = getFileContext ? await getFileContext(content) : {};
    return bridge.api.symbolAtPosition(
        content,
        position.lineNumber - 1,
        position.column - 1,
        ctx.sourceFilePath,
    );
}

function locationToRange(loc: SymbolLocation): monaco.IRange {
    const line = Math.max(0, loc.line) + 1; // 0-based → 1-based
    return {
        startLineNumber: line,
        startColumn: loc.column + 1,
        endLineNumber: line,
        endColumn: loc.column + loc.length + 1,
    };
}

/**
 * Resolve a SymbolLocation to a Monaco URI for the find-references panel. Local
 * locations stay in the active model; include locations are opened on disk via
 * the opener (desktop only) so the panel can render their content. This path
 * never repositions the cursor — the panel just lists the results.
 */
async function resolveReferenceUri(
    loc: SymbolLocation,
    localUri: monaco.Uri,
    openIncludeFile: IncludeFileOpener | undefined,
): Promise<monaco.Uri | null> {
    if (loc.source === 'local') return localUri;
    if (!openIncludeFile || !loc.sourceFile) return null;
    return openIncludeFile(loc.sourceFile);
}

/**
 * When `position` sits on (or past) the path portion of an `#include
 * FILEPATH` line, returns that raw path plus the range it spans (so Monaco
 * can underline just the path, not the whole line). Undefined otherwise.
 */
function includePathAt(model: monaco.editor.ITextModel, position: monaco.Position): { path: string; range: monaco.IRange } | undefined {
    const line = model.getLineContent(position.lineNumber);
    const parsed = parseDirectiveLine(line);
    if (!parsed || parsed.directive !== 'include') return undefined;
    if (position.column - 1 < parsed.pathStartCol) return undefined;
    const path = parsed.partialPath.trim();
    if (!path) return undefined;
    return {
        path,
        range: {
            startLineNumber: position.lineNumber,
            startColumn: parsed.pathStartCol + 1,
            endLineNumber: position.lineNumber,
            endColumn: line.length + 1,
        },
    };
}

/**
 * Renders `#include FILEPATH` paths as clickable links, styled by Monaco's
 * built-in link decoration (underline + link color) same as it does for
 * http(s) URLs — unlike go-to-definition's underline, this is always visible,
 * not just on Ctrl-hover. Clicking is handled by the `registerLinkOpener`
 * wired up in main.ts alongside the `calcpad-include:` editor opener.
 */
export function registerIncludeLinkProvider(resolveIncludeUri?: IncludeUriResolver): monaco.IDisposable {
    return monaco.languages.registerLinkProvider('calcpad', {
        async provideLinks(model) {
            if (!resolveIncludeUri) return { links: [] };
            const links: monaco.languages.ILink[] = [];
            const lineCount = model.getLineCount();
            for (let lineNumber = 1; lineNumber <= lineCount; lineNumber++) {
                const line = model.getLineContent(lineNumber);
                const parsed = parseDirectiveLine(line);
                if (!parsed || parsed.directive !== 'include') continue;
                const path = parsed.partialPath.trim();
                if (!path) continue;
                const url = await resolveIncludeUri(path);
                if (!url) continue;
                links.push({
                    url,
                    tooltip: `Open ${path}`,
                    range: {
                        startLineNumber: lineNumber,
                        startColumn: parsed.pathStartCol + 1,
                        endLineNumber: lineNumber,
                        endColumn: line.length + 1,
                    },
                });
            }
            return { links };
        },
    });
}

/**
 * Go-to-Definition (F12 / Ctrl+click). On an `#include FILEPATH` line, jumps
 * straight to that file. Otherwise asks the server for the symbol under the
 * cursor and points at the first assignment location. Kept SIDE-EFFECT FREE:
 * Monaco invokes this on Ctrl+hover just to draw the definition underline, so
 * it must not switch tabs or move the cursor. When the definition lives in an
 * `#include` file, `resolveIncludeUri` returns a pure URI for it; the actual
 * file open + navigation is handled by the editor opener registered in main.ts,
 * which Monaco calls only on a real click / F12.
 */
export function registerDefinitionProvider(
    bridge: EditorBridge,
    getFileContext?: FileContextProvider,
    resolveIncludeUri?: IncludeUriResolver,
): monaco.IDisposable {
    return monaco.languages.registerDefinitionProvider('calcpad', {
        async provideDefinition(model, position) {
            const include = includePathAt(model, position);
            if (include) {
                if (!resolveIncludeUri) return null;
                const uri = await resolveIncludeUri(include.path);
                if (!uri) return null;
                return { uri, range: include.range };
            }

            const sym = await resolveSymbol(bridge, model, position, getFileContext);
            if (!sym) return null;

            const definition = sym.locations.find(loc => loc.isAssignment);
            if (!definition) return null;

            if (definition.source === 'local') {
                return { uri: model.uri, range: locationToRange(definition) };
            }
            if (!resolveIncludeUri || !definition.sourceFile) return null;
            const uri = await resolveIncludeUri(definition.sourceFile);
            if (!uri) return null;

            return { uri, range: locationToRange(definition) };
        },
    });
}

/**
 * Find All References (Shift+F12). Returns every occurrence of the symbol,
 * including those that live in `#include` files when an `openIncludeFile` is
 * available (desktop only — see `IncludeFileOpener` TODO).
 */
export function registerReferenceProvider(
    bridge: EditorBridge,
    getFileContext?: FileContextProvider,
    openIncludeFile?: IncludeFileOpener,
): monaco.IDisposable {
    return monaco.languages.registerReferenceProvider('calcpad', {
        async provideReferences(model, position, context) {
            const sym = await resolveSymbol(bridge, model, position, getFileContext);
            if (!sym) return null;

            const filtered = context.includeDeclaration
                ? sym.locations
                : sym.locations.filter(l => !l.isAssignment);

            const results: monaco.languages.Location[] = [];
            for (const loc of filtered) {
                const uri = await resolveReferenceUri(loc, model.uri, openIncludeFile);
                if (uri) results.push({ uri, range: locationToRange(loc) });
            }
            return results;
        },
    });
}

/**
 * Rename Symbol (F2). Renames all local occurrences in the active document.
 * Cross-file rename is not supported — the user is told to rename in-place
 * if the definition lives in an include.
 */
export function registerRenameProvider(
    bridge: EditorBridge,
    getFileContext?: FileContextProvider,
): monaco.IDisposable {
    return monaco.languages.registerRenameProvider('calcpad', {
        async resolveRenameLocation(model, position) {
            const sym = await resolveSymbol(bridge, model, position, getFileContext);
            if (!sym) {
                return { text: '', range: new monaco.Range(1, 1, 1, 1), rejectReason: 'No renameable symbol at cursor' };
            }
            if (sym.locations.every(l => l.source !== 'local')) {
                return { text: sym.symbolName, range: new monaco.Range(1, 1, 1, 1), rejectReason: `'${sym.symbolName}' is defined in an include file` };
            }
            // Anchor the rename UI on a local occurrence at or covering the cursor.
            const line = position.lineNumber - 1;
            const col = position.column - 1;
            const anchor = sym.locations.find(l =>
                l.source === 'local' &&
                l.line === line &&
                col >= l.column &&
                col <= l.column + l.length,
            ) ?? sym.locations.find(l => l.source === 'local');
            if (!anchor) {
                return { text: sym.symbolName, range: new monaco.Range(1, 1, 1, 1), rejectReason: 'No local occurrence to anchor the rename' };
            }
            return { text: sym.symbolName, range: locationToRange(anchor) };
        },

        async provideRenameEdits(model, position, newName) {
            const sym = await resolveSymbol(bridge, model, position, getFileContext);
            if (!sym || sym.symbolName === newName) return null;

            const localLocations = sym.locations.filter(l => l.source === 'local');
            const edits: monaco.languages.IWorkspaceTextEdit[] = localLocations.map(l => ({
                resource: model.uri,
                versionId: model.getVersionId(),
                textEdit: {
                    range: locationToRange(l),
                    text: newName,
                },
            }));

            return { edits };
        },
    });
}
