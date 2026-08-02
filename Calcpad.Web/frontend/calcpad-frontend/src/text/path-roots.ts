/**
 * The `<project>`/`<library>` path-root tokens, mirrored from `Calcpad.Core.PathRoots` so the
 * editor resolves `#include`/`#read`/`#write`/`<img src>` references the same way the engine
 * does. A root is declared once, in the document, by a `#ProjectPath = ...` / `#LibraryPath =
 * ...` line — there is no host-level default, so a document with no declaration simply has no
 * root to expand against.
 *
 * A third token, `<user>` (see `isUserToken`/`expandUserToken`), needs no declaration at all: it
 * always expands to the current OS user's home directory, so it is handled separately from the
 * two declared roots rather than folded into `PathRootKind`.
 */

export type PathRootKind = 'project' | 'library';

const PROJECT_TOKEN = '<project>';
const LIBRARY_TOKEN = '<library>';
const USER_TOKEN = '<user>';
const PROJECT_KEYWORD = '#projectpath';
const LIBRARY_KEYWORD = '#librarypath';

/** Whether `raw` starts with a root token, case-insensitively. */
export function hasPathRootToken(raw: string): boolean {
    const lower = raw.toLowerCase();
    return lower.startsWith(PROJECT_TOKEN) || lower.startsWith(LIBRARY_TOKEN);
}

/** Which root `raw` starts with, or `null` when it names neither. */
export function getPathRootTokenKind(raw: string): PathRootKind | null {
    const lower = raw.toLowerCase();
    if (lower.startsWith(PROJECT_TOKEN)) return 'project';
    if (lower.startsWith(LIBRARY_TOKEN)) return 'library';
    return null;
}

/**
 * Whether `raw` starts with the self-resolving `<user>` token, mirroring
 * `Calcpad.Core.PathRoots.IsUserToken`.
 */
export function isUserToken(raw: string): boolean {
    return raw.toLowerCase().startsWith(USER_TOKEN);
}

/**
 * Joins `raw`'s remainder onto `homeDir`, mirroring `Calcpad.Core.PathRoots`'s private
 * `ExpandUserToken`. `homeDir` is host-supplied — the browser/editor layer has no OS API of its
 * own to ask for it, unlike Core's `Environment.GetFolderPath`.
 */
export function expandUserToken(raw: string, homeDir: string): string {
    let rest = raw.slice(USER_TOKEN.length);
    if (rest.length > 0 && (rest[0] === '/' || rest[0] === '\\')) rest = rest.slice(1);
    if (rest.length === 0) return homeDir;
    const sep = homeDir.includes('\\') ? '\\' : '/';
    return `${homeDir.replace(/[\\/]+$/, '')}${sep}${rest}`;
}

/** A `#ProjectPath`/`#LibraryPath` declaration found on a line, with its raw (unresolved) value. */
export interface PathRootDeclaration {
    kind: PathRootKind;
    /** The value as written, trimmed and with a trailing `'`/`"` comment stripped. Empty when the directive names no path. */
    value: string;
}

/**
 * Parses a `#ProjectPath`/`#LibraryPath` line the same way `PathRoots.IsDeclaration` does in
 * Core: keyword matched case-insensitively at the start of the (already-trimmed) line, value is
 * everything after the first `=` up to a trailing comment. Returns `null` for any other line,
 * including a bare `#ProjectPath` with no `=` at all.
 */
export function parsePathRootDeclaration(trimmedLine: string): PathRootDeclaration | null {
    const lower = trimmedLine.toLowerCase();
    let kind: PathRootKind;
    let keywordLength: number;
    if (lower.startsWith(PROJECT_KEYWORD)) {
        kind = 'project';
        keywordLength = PROJECT_KEYWORD.length;
    } else if (lower.startsWith(LIBRARY_KEYWORD)) {
        kind = 'library';
        keywordLength = LIBRARY_KEYWORD.length;
    } else {
        return null;
    }

    const rest = trimmedLine.slice(keywordLength);
    const eq = rest.indexOf('=');
    if (eq < 0) return { kind, value: '' };

    let value = rest.slice(eq + 1).replace(/^[ \t]+/, '');
    const commentIndex = Math.min(
        ...['\'', '"'].map(q => { const idx = value.indexOf(q); return idx < 0 ? Infinity : idx; }),
    );
    if (Number.isFinite(commentIndex)) value = value.slice(0, commentIndex);
    return { kind, value: value.trimEnd() };
}

/** The document's declared roots, as written — not yet expanded or resolved to an absolute path. */
export interface DeclaredPathRoots {
    project: string | null;
    library: string | null;
}

/**
 * Scans `source` top to bottom for its `#ProjectPath`/`#LibraryPath` declarations. Only the
 * first of each counts — a second is a document error Core reports at render time, not
 * something the editor needs to re-validate — and a declaration with no value is skipped the
 * same way. Callers needing the tooling-only "declared before first use" guarantee (e.g.
 * include completions offering `<library>/` only once it is live) can pass a `beforeLine` to
 * stop the scan there.
 */
export function scanDeclaredPathRoots(source: string, beforeLine?: number): DeclaredPathRoots {
    const roots: DeclaredPathRoots = { project: null, library: null };
    const lines = source.split(/\r\n|\r|\n/);
    const limit = beforeLine === undefined ? lines.length : Math.min(beforeLine, lines.length);
    for (let i = 0; i < limit; ++i) {
        const trimmed = lines[i].replace(/^[ \t]+/, '');
        const declaration = parsePathRootDeclaration(trimmed);
        if (!declaration || !declaration.value) continue;
        if (declaration.kind === 'project' && roots.project === null) roots.project = declaration.value;
        else if (declaration.kind === 'library' && roots.library === null) roots.library = declaration.value;
    }
    return roots;
}

/**
 * Resolves a declared root's raw value to an absolute directory, the same way
 * `PathRoots.TryDeclare` does: a leading `<user>` expanded first (when `homeDir` is known), then
 * environment variables, then taken relative to `documentDir` when it isn't already absolute.
 */
export function resolvePathRoot(
    rawValue: string,
    documentDir: string,
    expandEnvVars: (raw: string) => string,
    resolve: (dir: string, file: string) => string,
    homeDir: string | null = null,
): string {
    const withUser = homeDir !== null && isUserToken(rawValue) ? expandUserToken(rawValue, homeDir) : rawValue;
    return resolve(documentDir, expandEnvVars(withUser));
}

/** The document's declared roots, resolved to absolute directories (or `null` where undeclared). */
export interface ResolvedPathRoots {
    project: string | null;
    library: string | null;
}

export function resolveDeclaredPathRoots(
    declared: DeclaredPathRoots,
    documentDir: string,
    expandEnvVars: (raw: string) => string,
    resolve: (dir: string, file: string) => string,
    homeDir: string | null = null,
): ResolvedPathRoots {
    return {
        project: declared.project === null ? null : resolvePathRoot(declared.project, documentDir, expandEnvVars, resolve, homeDir),
        library: declared.library === null ? null : resolvePathRoot(declared.library, documentDir, expandEnvVars, resolve, homeDir),
    };
}

/**
 * Expands a leading root token in `raw` against `roots` (and `<user>` against `homeDir`, when
 * known). Returns `raw` unchanged, together with `ok: true`, for anything that isn't a token
 * reference — so a caller can run this unconditionally ahead of its own environment-variable
 * expansion, exactly like the Core `PathRoots.TryExpand` it mirrors. `ok: false` when a
 * `<project>`/`<library>` token's root was never declared, or `<user>` is used but `homeDir`
 * isn't available — in which case `expanded` is still `raw` — the token is left in place rather
 * than guessed at.
 */
export function expandPathRootToken(
    raw: string,
    roots: ResolvedPathRoots,
    homeDir: string | null = null,
): { expanded: string; ok: boolean } {
    if (isUserToken(raw)) {
        if (homeDir === null) return { expanded: raw, ok: false };
        return { expanded: expandUserToken(raw, homeDir), ok: true };
    }

    const kind = getPathRootTokenKind(raw);
    if (kind === null) return { expanded: raw, ok: true };

    const root = roots[kind];
    if (root === null) return { expanded: raw, ok: false };

    const tokenLength = (kind === 'project' ? PROJECT_TOKEN : LIBRARY_TOKEN).length;
    let rest = raw.slice(tokenLength);
    if (rest.length > 0 && (rest[0] === '/' || rest[0] === '\\')) rest = rest.slice(1);

    const sep = root.includes('\\') ? '\\' : '/';
    const expanded = rest.length === 0 ? root : `${root.replace(/[\\/]+$/, '')}${sep}${rest}`;
    return { expanded, ok: true };
}

/**
 * Builds a `raw => absolute path` resolver out of `sourceText`'s own declared roots, for a
 * caller (e.g. image inlining) that needs to resolve many references the same way: token
 * expanded, then environment variables, then made absolute against `documentDir` — the same
 * order `Environment.ExpandEnvironmentVariables`/`Path.GetFullPath` apply on the Core side. The
 * roots are declared-and-resolved once, on first use, and reused for every later call.
 * Throws for a token that could not be expanded — a `<project>`/`<library>` root never declared,
 * or a `<user>` reference with no `getHomeDir` to ask — so a caller can catch it the same way a
 * missing file is caught.
 *
 * `getHomeDir`, when given, is called at most once (like the roots themselves) — there is no
 * in-process OS API for "the current user's home directory" the way Core has
 * `Environment.GetFolderPath`, so it is asked of whichever host embeds this (Tauri, VS Code) the
 * first time a reference actually needs it.
 */
export function createReferenceResolver(
    sourceText: string,
    documentDir: string,
    expandEnvVars: (raw: string) => string | Promise<string>,
    resolve: (dir: string, file: string) => string,
    getHomeDir?: () => string | null | Promise<string | null>,
): (raw: string) => Promise<string> {
    const declared = scanDeclaredPathRoots(sourceText);
    const declaresUser = (v: string | null) => v !== null && isUserToken(v);
    const rootsNeedHomeDir = declaresUser(declared.project) || declaresUser(declared.library);

    let resolvedRoots: ResolvedPathRoots | null = null;
    let homeDir: string | null | undefined;

    async function ensureHomeDir(): Promise<string | null> {
        if (homeDir === undefined) homeDir = getHomeDir ? await getHomeDir() : null;
        return homeDir;
    }

    async function resolveRoot(rawValue: string | null, home: string | null): Promise<string | null> {
        if (rawValue === null) return null;
        const withUser = home !== null && isUserToken(rawValue) ? expandUserToken(rawValue, home) : rawValue;
        return resolve(documentDir, await expandEnvVars(withUser));
    }

    return async (raw: string) => {
        if (resolvedRoots === null) {
            const home = rootsNeedHomeDir ? await ensureHomeDir() : null;
            resolvedRoots = {
                project: await resolveRoot(declared.project, home),
                library: await resolveRoot(declared.library, home),
            };
        }
        const home = isUserToken(raw) ? await ensureHomeDir() : null;
        const { expanded, ok } = expandPathRootToken(raw, resolvedRoots, home);
        if (!ok) throw new Error(`Path root not declared for reference: ${raw}`);
        return resolve(documentDir, await expandEnvVars(expanded));
    };
}
