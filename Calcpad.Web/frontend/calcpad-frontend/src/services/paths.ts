/**
 * Minimal path helpers that work against both POSIX and Windows separators.
 * Hosts run in webviews with no Node `path` module, and the paths being handled
 * come from whichever OS the desktop app is on, so separator handling stays
 * lenient: either separator is accepted on input, and the dominant one in the
 * input directory is used on output.
 */

export function pathDirname(p: string): string {
    const idx = Math.max(p.lastIndexOf('/'), p.lastIndexOf('\\'));
    return idx > 0 ? p.slice(0, idx) : '';
}

export function pathBasename(p: string): string {
    const idx = Math.max(p.lastIndexOf('/'), p.lastIndexOf('\\'));
    return idx >= 0 ? p.slice(idx + 1) : p;
}

/** POSIX-style relative path from `from` to `to`, using forward slashes. */
export function pathRelative(from: string, to: string): string {
    const norm = (p: string) => p.replace(/\\/g, '/').replace(/\/+$/, '');
    const fromParts = norm(from).split('/').filter(Boolean);
    const toParts = norm(to).split('/').filter(Boolean);
    let i = 0;
    while (i < fromParts.length && i < toParts.length && fromParts[i] === toParts[i]) i++;
    const up = fromParts.slice(i).map(() => '..');
    const down = toParts.slice(i);
    const rel = [...up, ...down].join('/');
    return rel || '.';
}

export function pathIsAbsolute(p: string): boolean {
    return p.startsWith('/') || /^[a-zA-Z]:[\\/]/.test(p);
}

export function pathResolve(dir: string, file: string): string {
    if (pathIsAbsolute(file)) return file;
    if (!dir) return file;
    const sep = dir.includes('\\') ? '\\' : '/';
    const raw = `${dir}${sep}${file}`.replace(/\\/g, '/');
    const parts = raw.split('/');
    const result: string[] = [];
    for (const part of parts) {
        if (part === '..') result.pop();
        else if (part !== '.') result.push(part);
    }
    const joined = result.join('/');
    return sep === '\\' ? joined.replace(/\//g, '\\') : joined;
}

/** The lowercased extension without its dot, or `''` when there is none. */
export function pathExtension(p: string): string {
    const name = pathBasename(p);
    const dot = name.lastIndexOf('.');
    return dot > 0 ? name.slice(dot + 1).toLowerCase() : '';
}
