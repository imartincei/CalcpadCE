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
 * each `src` to an absolute path through `resolve`. Remote and `data:` sources
 * are left alone, so this is idempotent, and a source that cannot be read keeps
 * its original `src` rather than failing the whole document.
 *
 * Used two ways, with a different `resolve` each time. Rendered HTML arrives
 * with `{project}`/`{library}`/`{user}` and environment variables already
 * expanded by `Calcpad.Core.ImageReferences`, so it only needs a path made
 * absolute against the document's folder. Raw `.cpd` source — a compiled
 * worksheet, which is deflated before Core ever sees it — still needs the full
 * {@link createReferenceResolver}. Both accept the same input because the
 * CalcPad source carries images as `'<img src="…">` comment lines.
 *
 * `budget` caps the total inlined bytes, for the preview — where a document that
 * inlines gigabytes of images is a crash rather than a render. It is left off when
 * writing a file: a saved worksheet must not silently lose an image.
 */
export interface InlineImageBudget {
    maxTotalBytes: number;
    /** Called once, with how many sources were left as-is, if the budget ran out. */
    onSkip?: (skipped: number) => void;
}

export async function inlineImageSources(
    text: string,
    fs: Pick<IFileSystem, 'readFile'>,
    resolve: (src: string) => string | Promise<string>,
    budget?: InlineImageBudget,
): Promise<string> {
    const cache: Record<string, string> = {};
    const seen = new Set<string>();
    let inlined = 0;
    let skipped = 0;
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

        if (budget && inlined >= budget.maxTotalBytes) {
            skipped++;
            continue;
        }

        try {
            const bytes = await fs.readFile(await resolve(src));
            cache[src] = `data:${mimeFromExtension(ext)};base64,${bytesToBase64(bytes)}`;
            inlined += bytes.length;
        } catch {
            // missing file, permission error, or undeclared token root → leave src untouched
        }
    }
    if (skipped) budget?.onSkip?.(skipped);

    if (Object.keys(cache).length === 0) return text;
    return text.replace(
        /<img\s([^>]*?)src\s*=\s*["']([^"']+)["']([^>]*?)>/gi,
        (full, before, src, after) =>
            cache[src] ? `<img ${before}src="${cache[src]}"${after}>` : full);
}
