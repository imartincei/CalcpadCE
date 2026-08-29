import * as monaco from 'monaco-editor';
import {
    getPathRootTokenKind,
    isUserToken,
    resolveCompletionPathRoots,
    extensionsForDirective,
    parseDirectiveLine,
    PATH_ROOT_TOKEN,
    USER_TOKEN,
    DIRECTIVE_TRIGGER_CHARACTERS,
    pathRootTokenOptions,
    hasDanglingCloseBrace,
    type ResolvedPathRoots,
} from 'calcpad-frontend';
import { pathResolve } from 'calcpad-frontend/services/paths';

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
    /** Expands %VAR%/$VAR references against the host's environment. */
    expandEnvVars(raw: string): Promise<string>;
    /** The current OS user's home directory, for the `{user}` path-root token, or null if unknown. */
    getHomeDir(): Promise<string | null>;
    /** The server's resolved roots for the active document, from the last cached `/definitions`. */
    getServerPathRoots(): ResolvedPathRoots;
}

/**
 * Register a Monaco completion provider for #include / #read / #write / #append /
 * #ProjectPath / #LibraryPath directives, the latter two offering folders only. Search roots in
 * priority order — the current file's parent directory, the opened workspace folder, the OS home
 * directory as `{user}/…`, and the document's own root declarations as `{project}/…`/`{library}/…`
 * — are deduplicated by absolute path, so completions lead an author to the portable form.
 */
export function registerIncludeCompletionProvider(
    ctx: IncludeCompletionsContext
): monaco.IDisposable {
    return monaco.languages.registerCompletionItemProvider('calcpad', {
        triggerCharacters: DIRECTIVE_TRIGGER_CHARACTERS,

        async provideCompletionItems(model, position) {
            const line = model.getLineContent(position.lineNumber);
            const lineToCursor = line.substring(0, position.column - 1);

            const parsed = parseDirectiveLine(lineToCursor);
            if (!parsed) return { suggestions: [], incomplete: true };

            const extensions = extensionsForDirective(parsed.directive);

            // Strip trailing options (@sheet, type=, sep=)
            let partialPath = parsed.partialPath;
            const atIndex = partialPath.indexOf('@');
            if (atIndex !== -1) partialPath = partialPath.substring(0, atIndex);

            const currentFilePath = ctx.getCurrentFilePath();
            const currentDir = currentFilePath ? pathDirname(currentFilePath) : '';
            const openedFolder = await ctx.getOpenedFolder();
            const homeDir = await ctx.getHomeDir();

            const resolvedRoots = currentDir
                ? await resolveCompletionPathRoots({
                    serverRoots: ctx.getServerPathRoots(),
                    sourceText: model.getValue(),
                    beforeLine: position.lineNumber - 1,
                    documentDir: currentDir,
                    expandEnvVars: (raw) => ctx.expandEnvVars(raw),
                    resolve: pathResolve,
                    homeDir,
                })
                : { project: null, library: null };

            // Monaco auto-closes a typed `{` into `{}`, leaving a `}` sitting right after the
            // cursor — swallow it so inserting a full token doesn't leave it dangling.
            const nextChar = line.charAt(position.column - 1);
            const extraColumn = hasDanglingCloseBrace(partialPath, nextChar) ? 1 : 0;

            const range: monaco.IRange = {
                startLineNumber: position.lineNumber,
                startColumn: parsed.pathStartCol + 1,
                endLineNumber: position.lineNumber,
                endColumn: position.column + extraColumn,
            };

            const suggestions: monaco.languages.CompletionItem[] = [];
            const seenAbsolute = new Set<string>();

            const tokenKind = getPathRootTokenKind(partialPath);
            const isUser = isUserToken(partialPath);
            const hasSeparator = partialPath.includes('/') || partialPath.includes('\\');

            if (isUser) {
                // Drilling into a {user} reference: search the OS home directory, but
                // reinsert completions in token form so the reference stays portable.
                if (homeDir === null) return { suggestions: [], incomplete: true };

                const tokenText = partialPath.slice(0, USER_TOKEN.length);
                let rest = partialPath.slice(tokenText.length);
                if (rest.startsWith('/') || rest.startsWith('\\')) rest = rest.slice(1);
                await addEntries(
                    ctx, homeDir, rest, extensions, range,
                    currentFilePath, suggestions, seenAbsolute, '', false, `${tokenText}/`
                );
            } else if (tokenKind !== null) {
                // Drilling into a {project}/{library} reference: search the resolved root,
                // but reinsert completions in token form so the reference stays portable.
                const root = resolvedRoots[tokenKind];
                if (root === null) return { suggestions: [], incomplete: true };

                const tokenText = partialPath.slice(0, PATH_ROOT_TOKEN[tokenKind].length);
                let rest = partialPath.slice(tokenText.length);
                if (rest.startsWith('/') || rest.startsWith('\\')) rest = rest.slice(1);
                await addEntries(
                    ctx, root, rest, extensions, range,
                    currentFilePath, suggestions, seenAbsolute, '', false, `${tokenText}/`
                );
            } else if (hasSeparator && !pathIsAbsolute(partialPath)) {
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
                // Root level: current file dir + opened workspace folder + declared roots (dedup).
                if (currentDir) {
                    await addEntries(
                        ctx, currentDir, partialPath, extensions, range,
                        currentFilePath, suggestions, seenAbsolute, ''
                    );
                }
                if (openedFolder && normalize(openedFolder) !== normalize(currentDir)) {
                    await addEntries(
                        ctx, openedFolder, partialPath, extensions, range,
                        currentFilePath, suggestions, seenAbsolute, 'Workspace', true
                    );
                }
                // Offer {user}/{project}/{library} as pick-a-root placeholders rather than
                // eagerly flattening their contents in — selecting one inserts just the token
                // and re-triggers completion, which then drills into that root above.
                for (const opt of pathRootTokenOptions(resolvedRoots, homeDir !== null)) {
                    suggestions.push({
                        label: opt.label,
                        kind: monaco.languages.CompletionItemKind.Folder,
                        insertText: `${opt.token}/`,
                        filterText: `${opt.token}/`,
                        range,
                        sortText: '0_' + opt.label,
                        detail: opt.detail,
                        command: {
                            id: 'editor.action.triggerSuggest',
                            title: 'Re-trigger completions',
                        },
                    });
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
    sourceLabel: string,
    useAbsolute: boolean = false,
    insertPrefix: string = ''
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
            : insertPrefix + pathPrefix + entry.name + sep;

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

        const insertText = useAbsolute ? absPath : insertPrefix + pathPrefix + entry.name;

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
