import * as monaco from 'monaco-editor';

const DIRECTIVES = ['include', 'read', 'write', 'append'] as const;
type Directive = typeof DIRECTIVES[number];

const INCLUDE_EXTENSIONS = ['cpd', 'txt'];
const DATA_EXTENSIONS = ['csv', 'tsv', 'xlsx', 'xlsm', 'xls'];

export interface DirectiveParse {
    directive: Directive;
    pathStartCol: number;   // 0-indexed
    partialPath: string;
}

/** Ported from vscode-calcpad/calcpadIncludeCompletionProvider.ts. */
export function parseDirectiveLine(lineText: string): DirectiveParse | undefined {
    let i = 0;
    while (i < lineText.length && (lineText[i] === ' ' || lineText[i] === '\t')) i++;
    if (i >= lineText.length || lineText[i] !== '#') return undefined;
    i++;

    const keywordStart = i;
    while (i < lineText.length && lineText[i] !== ' ' && lineText[i] !== '\t') i++;
    const keyword = lineText.substring(keywordStart, i).toLowerCase() as Directive;
    if (!DIRECTIVES.includes(keyword)) return undefined;

    const afterKeyword = i;
    while (i < lineText.length && (lineText[i] === ' ' || lineText[i] === '\t')) i++;
    if (i === afterKeyword) return undefined;

    if (keyword === 'include') {
        return { directive: keyword, pathStartCol: i, partialPath: lineText.substring(i) };
    }

    while (i < lineText.length && lineText[i] !== ' ' && lineText[i] !== '\t') i++;
    const afterVar = i;
    while (i < lineText.length && (lineText[i] === ' ' || lineText[i] === '\t')) i++;
    if (i === afterVar) return undefined;

    const connStart = i;
    while (i < lineText.length && lineText[i] !== ' ' && lineText[i] !== '\t') i++;
    const connector = lineText.substring(connStart, i).toLowerCase();
    const expected = keyword === 'read' ? 'from' : 'to';
    if (connector !== expected) return undefined;

    const afterConn = i;
    while (i < lineText.length && (lineText[i] === ' ' || lineText[i] === '\t')) i++;
    if (i === afterConn) return undefined;

    return { directive: keyword, pathStartCol: i, partialPath: lineText.substring(i) };
}

function pathDirname(p: string): string {
    const idx = Math.max(p.lastIndexOf('/'), p.lastIndexOf('\\'));
    return idx > 0 ? p.slice(0, idx) : '';
}

function pathIsAbsolute(p: string): boolean {
    return p.startsWith('/') || /^[a-zA-Z]:[\\/]/.test(p);
}

function joinPath(dir: string, rel: string): string {
    if (!dir) return rel;
    if (!rel) return dir;
    const sep = dir.includes('\\') ? '\\' : '/';
    const cleanDir = dir.replace(/[\\/]+$/, '');
    const cleanRel = rel.replace(/^[\\/]+/, '');
    return `${cleanDir}${sep}${cleanRel}`;
}

function normalize(p: string): string {
    return p.replace(/\\/g, '/').replace(/\/+$/, '').toLowerCase();
}

export interface IncludeCompletionsContext {
    /** Directory-listing service (usually tauriBridge.listDirectory). */
    listDirectory(dirPath: string): Promise<Array<{ name: string; path: string; isDirectory: boolean }>>;
    /** Absolute path of the file being edited, or null (untitled). */
    getCurrentFilePath(): string | null;
    /** Currently-opened workspace folder root, or null. */
    getOpenedFolder(): Promise<string | null>;
    /**
     * Configured library folder, or null. Searched at the root level in
     * addition to the current file's directory and any opened workspace
     * folder, so shared includes remain reachable regardless of what the
     * user has open in the Files panel.
     */
    getLibraryPath?(): Promise<string | null>;
}

/**
 * Register a Monaco completion provider for #include / #read / #write / #append
 * directives. Search roots (in priority order):
 *   1. The current file's parent directory.
 *   2. The opened workspace folder (if any and different).
 * Duplicates (same file reachable via multiple roots) are filtered by absolute
 * path so the same file only appears once in the completion list.
 */
export function registerIncludeCompletionProvider(
    ctx: IncludeCompletionsContext
): monaco.IDisposable {
    return monaco.languages.registerCompletionItemProvider('calcpad', {
        triggerCharacters: [' ', '/', '\\'],

        async provideCompletionItems(model, position) {
            const line = model.getLineContent(position.lineNumber);
            const lineToCursor = line.substring(0, position.column - 1);

            const parsed = parseDirectiveLine(lineToCursor);
            if (!parsed) return { suggestions: [], incomplete: true };

            const isInclude = parsed.directive === 'include';
            const extensions = isInclude
                ? INCLUDE_EXTENSIONS
                : [...INCLUDE_EXTENSIONS, ...DATA_EXTENSIONS];

            // Strip trailing options (@sheet, type=, sep=)
            let partialPath = parsed.partialPath;
            const atIndex = partialPath.indexOf('@');
            if (atIndex !== -1) partialPath = partialPath.substring(0, atIndex);

            const currentFilePath = ctx.getCurrentFilePath();
            const currentDir = currentFilePath ? pathDirname(currentFilePath) : '';
            const openedFolder = await ctx.getOpenedFolder();
            const libraryFolder = ctx.getLibraryPath ? await ctx.getLibraryPath() : null;

            const range: monaco.IRange = {
                startLineNumber: position.lineNumber,
                startColumn: parsed.pathStartCol + 1,
                endLineNumber: position.lineNumber,
                endColumn: position.column,
            };

            const suggestions: monaco.languages.CompletionItem[] = [];
            const seenAbsolute = new Set<string>();

            const hasSeparator = partialPath.includes('/') || partialPath.includes('\\');

            if (hasSeparator && !pathIsAbsolute(partialPath)) {
                // Drill down: resolve the typed prefix relative to the doc dir only.
                await addEntries(
                    ctx, currentDir, partialPath, extensions, range,
                    currentFilePath, suggestions, seenAbsolute, ''
                );
            } else if (pathIsAbsolute(partialPath)) {
                // User typed an absolute path — list from that path directly.
                const absDir = pathDirname(partialPath);
                const relTail = partialPath.substring(absDir.length).replace(/^[\\/]+/, '');
                await addEntries(
                    ctx, absDir, relTail, extensions, range,
                    currentFilePath, suggestions, seenAbsolute, ''
                );
            } else {
                // Root level: current file dir + opened workspace folder (dedup).
                if (currentDir) {
                    await addEntries(
                        ctx, currentDir, partialPath, extensions, range,
                        currentFilePath, suggestions, seenAbsolute, ''
                    );
                }
                if (openedFolder && normalize(openedFolder) !== normalize(currentDir)) {
                    await addEntries(
                        ctx, openedFolder, partialPath, extensions, range,
                        currentFilePath, suggestions, seenAbsolute, 'Workspace'
                    );
                }
                if (libraryFolder
                    && normalize(libraryFolder) !== normalize(currentDir)
                    && (!openedFolder || normalize(libraryFolder) !== normalize(openedFolder))) {
                    await addEntries(
                        ctx, libraryFolder, partialPath, extensions, range,
                        currentFilePath, suggestions, seenAbsolute, 'Library'
                    );
                }
            }

            return { suggestions, incomplete: true };
        },
    });
}

async function addEntries(
    ctx: IncludeCompletionsContext,
    baseDir: string,
    relativePath: string,
    extensions: string[],
    range: monaco.IRange,
    currentFilePath: string | null,
    suggestions: monaco.languages.CompletionItem[],
    seenAbsolute: Set<string>,
    sourceLabel: string
): Promise<void> {
    let searchDir: string;
    let pathPrefix: string;

    if (relativePath.includes('/') || relativePath.includes('\\')) {
        const lastSep = Math.max(relativePath.lastIndexOf('/'), relativePath.lastIndexOf('\\'));
        pathPrefix = relativePath.substring(0, lastSep + 1);
        searchDir = joinPath(baseDir, pathPrefix);
    } else {
        pathPrefix = '';
        searchDir = baseDir;
    }

    const entries = await ctx.listDirectory(searchDir);
    if (!entries.length) return;

    // For the opened-folder ("Workspace") and library ("Library") sources,
    // inserted paths are absolute so #include resolves regardless of the
    // current file's location. For the doc-dir root we insert relative paths
    // (using the same prefix the user has typed so far).
    const useAbsolute = sourceLabel === 'Workspace' || sourceLabel === 'Library';

    // Folders
    for (const entry of entries) {
        if (!entry.isDirectory || entry.name.startsWith('.')) continue;

        const absPath = entry.path;
        const dedupKey = normalize(absPath);
        if (seenAbsolute.has(dedupKey)) continue;
        seenAbsolute.add(dedupKey);

        const sep = absPath.includes('\\') ? '\\' : '/';
        const insertText = useAbsolute
            ? absPath + sep
            : pathPrefix + entry.name + sep;

        suggestions.push({
            label: entry.name,
            kind: monaco.languages.CompletionItemKind.Folder,
            insertText,
            filterText: insertText,
            range,
            sortText: '0_' + entry.name,
            detail: sourceLabel || undefined,
            command: {
                id: 'editor.action.triggerSuggest',
                title: 'Re-trigger completions',
            },
        });
    }

    // Files
    for (const entry of entries) {
        if (entry.isDirectory) continue;

        const dotIdx = entry.name.lastIndexOf('.');
        const ext = dotIdx >= 0 ? entry.name.substring(dotIdx + 1).toLowerCase() : '';
        if (!extensions.includes(ext)) continue;

        const absPath = entry.path;
        if (currentFilePath && normalize(absPath) === normalize(currentFilePath)) continue;

        const dedupKey = normalize(absPath);
        if (seenAbsolute.has(dedupKey)) continue;
        seenAbsolute.add(dedupKey);

        const insertText = useAbsolute ? absPath : pathPrefix + entry.name;

        suggestions.push({
            label: entry.name,
            kind: monaco.languages.CompletionItemKind.File,
            insertText,
            filterText: insertText,
            range,
            sortText: '1_' + entry.name,
            detail: sourceLabel
                ? `${ext.toUpperCase()} file (${sourceLabel})`
                : `${ext.toUpperCase()} file`,
        });
    }
}
