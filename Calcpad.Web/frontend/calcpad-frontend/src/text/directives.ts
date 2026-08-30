/**
 * `#include`/`#read`/`#write`/`#append`/`#ProjectPath`/`#LibraryPath` directive parsing and
 * path-root resolution shared between the Monaco (`calcpad-web`) and VS Code include-completion
 * providers, so both hosts read a directive line and resolve a `{project}`/`{library}`/`{user}`
 * token the same way instead of maintaining their own copies.
 */

import {
    isUserToken,
    expandUserToken,
    scanDeclaredPathRoots,
    type PathRootKind,
    type ResolvedPathRoots,
} from './path-roots';

const DIRECTIVES = ['include', 'read', 'write', 'append', 'projectpath', 'librarypath'] as const;
export type Directive = typeof DIRECTIVES[number];

export interface DirectiveParse {
    directive: Directive;
    /** 0-indexed column where the file path begins. */
    pathStartCol: number;
    /** The partial file path typed so far (everything from `pathStartCol` to end of line). */
    partialPath: string;
}

/**
 * Parses a directive line up to the cursor:
 *   `#include FILEPATH`, `#read varName from FILEPATH[@options...]`,
 *   `#write`/`#append varName to FILEPATH[@options...]`, `#ProjectPath PATH`, `#LibraryPath PATH`.
 * Returns `undefined` for any other line, or when the cursor hasn't reached the path portion yet.
 */
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

    if (keyword === 'include' || keyword === 'projectpath' || keyword === 'librarypath') {
        return { directive: keyword, pathStartCol: i, partialPath: lineText.substring(i) };
    }

    // #read/#write/#append: skip the variable name, then expect 'from'/'to'.
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

export const INCLUDE_EXTENSIONS = ['cpd', 'txt'];
export const DATA_EXTENSIONS = ['csv', 'tsv', 'xlsx', 'xlsm', 'xls'];

/** File extensions a directive's completions should offer — none for the directory-only path roots. */
export function extensionsForDirective(directive: Directive): string[] {
    if (directive === 'projectpath' || directive === 'librarypath') return [];
    if (directive === 'include') return INCLUDE_EXTENSIONS;
    return [...INCLUDE_EXTENSIONS, ...DATA_EXTENSIONS];
}

export const PATH_ROOT_TOKEN: Record<PathRootKind, string> = { project: '{project}', library: '{library}' };
export const PATH_ROOT_LABEL: Record<PathRootKind, string> = { project: 'Project', library: 'Library' };
export const USER_TOKEN = '{user}';

/** `{` opens the completion widget on a bare directive line too, not just after a separator. */
export const DIRECTIVE_TRIGGER_CHARACTERS = [' ', '/', '\\', '{'];

export interface PathRootTokenOption {
    /** The word to show/insert-as, e.g. `user`. */
    label: string;
    /** The full token, e.g. `{user}`. */
    token: string;
    detail: string;
}

/**
 * The `{user}`/`{project}`/`{library}` options to offer at root level, before the user has
 * committed to one — picking one inserts just the token (plus a trailing separator) and
 * re-triggers completion, which then drills into that root via `getPathRootTokenKind`/
 * `isUserToken`. `{user}` needs no declaration so it's offered whenever `homeDirAvailable`;
 * `{project}`/`{library}` only when declared for this document, per `roots`.
 */
export function pathRootTokenOptions(roots: ResolvedPathRoots, homeDirAvailable: boolean): PathRootTokenOption[] {
    const options: PathRootTokenOption[] = [];
    if (homeDirAvailable) options.push({ label: 'user', token: USER_TOKEN, detail: 'User' });
    for (const kind of ['project', 'library'] as const) {
        if (roots[kind] !== null) options.push({ label: kind, token: PATH_ROOT_TOKEN[kind], detail: PATH_ROOT_LABEL[kind] });
    }
    return options;
}

/**
 * Whether `nextChar` (the character immediately after the cursor) is an editor-auto-inserted `}`
 * pairing an unmatched `{` in `partialPath` — both Monaco and VS Code auto-close a typed `{` into
 * `{}` with the cursor left in between. A completion's replace range needs to extend one column
 * further to swallow it, or inserting a full `{user}`/`{project}`/`{library}` token leaves a
 * stray `}` behind.
 */
export function hasDanglingCloseBrace(partialPath: string, nextChar: string): boolean {
    if (nextChar !== '}') return false;
    const openBraces = (partialPath.match(/\{/g) || []).length;
    const closeBraces = (partialPath.match(/\}/g) || []).length;
    return openBraces > closeBraces;
}

export interface CompletionPathRootsParams {
    /** The server's resolved roots for this document, or null when none is cached yet. */
    serverRoots: ResolvedPathRoots | null;
    /** The full document text, for the text-scan fallback. */
    sourceText: string;
    /** Stop the fallback scan here — declared-before-first-use, tooling-only. */
    beforeLine?: number;
    documentDir: string;
    /** Tauri's is async, VS Code's is sync — both are accepted. */
    expandEnvVars: (raw: string) => string | Promise<string>;
    resolve: (dir: string, file: string) => string;
    homeDir?: string | null;
}

/**
 * Resolves each root independently: the server's value when non-null — live regardless of where
 * in the `#include` chain it was declared — otherwise the entry document's own text scan. This
 * keeps a root declared purely locally resolving even before the server round-trip lands (or in
 * browser mode, where the server can't read included files at all).
 */
export async function resolveCompletionPathRoots(
    params: CompletionPathRootsParams,
): Promise<ResolvedPathRoots> {
    const { serverRoots, sourceText, beforeLine, documentDir, expandEnvVars, resolve, homeDir = null } = params;
    const declared = scanDeclaredPathRoots(sourceText, beforeLine);

    const resolveKind = async (kind: PathRootKind): Promise<string | null> => {
        const fromServer = serverRoots?.[kind] ?? null;
        if (fromServer !== null) return fromServer;

        const raw = declared[kind];
        if (raw === null) return null;
        const withUser = homeDir !== null && isUserToken(raw) ? expandUserToken(raw, homeDir) : raw;
        return resolve(documentDir, await expandEnvVars(withUser));
    };

    return {
        project: await resolveKind('project'),
        library: await resolveKind('library'),
    };
}
