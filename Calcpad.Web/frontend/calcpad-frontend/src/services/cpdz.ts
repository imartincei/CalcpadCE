/**
 * The compiled worksheet format (`.cpdz`) — a binary container holding the
 * deflated source, optionally alongside the images it references.
 *
 * Encoding and decoding happen server-side (`CalcpadApiClient.decodeCpdz` /
 * `encodeCpdz`) so hosts don't need a deflate implementation. A compiled
 * worksheet is meant to be filled in rather than read: editors open it in `#UI`
 * input mode with the source locked, and saving re-encodes around the entered
 * values instead of writing plain text.
 */
import { bytesToBase64, isImageExtension, mimeFromExtension } from './image-utils';
import type { IFileSystem } from '../types/interfaces';

export const COMPILED_EXTENSION = '.cpdz';

export const COMPILED_MIME = 'application/x-calcpadce-compiled';

/** True for a path naming a compiled worksheet, which is not editable as text. */
export function isCompiledPath(filePath: string): boolean {
    return filePath.toLowerCase().endsWith(COMPILED_EXTENSION);
}

/**
 * Replaces `<img src="local/path">` references with base64 data URIs, resolving
 * each `src` through `resolve` — which handles a `<project>`/`<library>` token
 * and environment variables the same way the rest of the document's references
 * do, then returns an absolute path. Remote and `data:` sources are left alone,
 * so this is idempotent, and a source that cannot be read (or whose token root
 * isn't declared) keeps its original `src` rather than failing the whole
 * document.
 *
 * Used two ways: to make a compiled worksheet self-contained before it is
 * deflated, and to feed exported HTML to headless Chromium, which has no
 * local-filesystem access. Both accept the same input because the CalcPad
 * source carries images as `'<img src="…">` comment lines.
 */
export async function inlineImageSources(
    text: string,
    fs: Pick<IFileSystem, 'readFile'>,
    resolve: (src: string) => string | Promise<string>,
): Promise<string> {
    const cache: Record<string, string> = {};
    const seen = new Set<string>();
    // Built per call: `exec` state is stateful, and this loop awaits between matches.
    const imgTag = /<img\s[^>]*?src\s*=\s*["']([^"']+)["'][^>]*>/gi;
    let m: RegExpExecArray | null;
    while ((m = imgTag.exec(text)) !== null) {
        const src = m[1];
        if (seen.has(src)) continue;
        seen.add(src);
        if (src.startsWith('data:') || /^https?:\/\//i.test(src)) continue;

        const ext = (src.split('.').pop() ?? '').toLowerCase();
        if (!isImageExtension(ext)) continue;

        try {
            const bytes = await fs.readFile(await resolve(src));
            cache[src] = `data:${mimeFromExtension(ext)};base64,${bytesToBase64(bytes)}`;
        } catch {
            // missing file, permission error, or undeclared token root → leave src untouched
        }
    }

    if (Object.keys(cache).length === 0) return text;
    return text.replace(
        /<img\s([^>]*?)src\s*=\s*["']([^"']+)["']([^>]*?)>/gi,
        (full, before, src, after) =>
            cache[src] ? `<img ${before}src="${cache[src]}"${after}>` : full);
}
