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
 * `budget` caps the total inlined bytes, and what happens at the cap differs by caller.
 * A preview skips the rest: it is a view, and a partly-illustrated one beats an app that
 * runs out of memory. A compiled worksheet fails instead — the images become the file, so
 * one that quietly dropped them would be a lossy save, and the recipient has no original
 * to compare against.
 */
export interface InlineImageBudget {
    maxTotalBytes: number;
    /**
     * `skip` leaves the sources past the cap as they were written. `fail` throws
     * {@link ImageBudgetError}, for a caller writing a file that has to be whole.
     */
    onExceeded: 'skip' | 'fail';
    /** Called once with how many sources were left as-is, when skipping. */
    onSkip?: (skipped: number) => void;
}

/**
 * The images a compiled worksheet is asked to carry exceed what one may hold. Thrown
 * rather than reported, matching the server's own limit on embedded `#read` data
 * (`ExpressionParser.MaxEmbeddedDataSize`, whose message this deliberately echoes) — both
 * end up in the same "this worksheet cannot be compiled" report.
 */
export class ImageBudgetError extends Error {
    constructor(readonly limitBytes: number, readonly src: string) {
        super(`Images embedded in a compiled worksheet cannot exceed ${Math.round(limitBytes / (1024 * 1024))} MB. `
            + `Reduce or remove images, starting with "${src}".`);
        this.name = 'ImageBudgetError';
    }
}

/**
 * How many bytes of images a compiled worksheet may carry. The parallel to the server's
 * `ExpressionParser.MaxEmbeddedDataSize` for `#read` data, and the same reasoning: a
 * `.cpdz` is decoded whole and run from memory, so what it carries is what a recipient
 * pays for. Kept in step with that constant by hand — there is no shared source.
 */
export const MAX_COMPILED_IMAGE_TOTAL_BYTES = 10 * 1024 * 1024;

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
            if (budget.onExceeded === 'fail') throw new ImageBudgetError(budget.maxTotalBytes, src);
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
        // Checked after adding as well, so a single image over the cap fails on itself
        // rather than only being noticed by whatever came next.
        if (budget?.onExceeded === 'fail' && inlined > budget.maxTotalBytes)
            throw new ImageBudgetError(budget.maxTotalBytes, src);
    }
    if (skipped) budget?.onSkip?.(skipped);

    if (Object.keys(cache).length === 0) return text;
    return text.replace(
        /<img\s([^>]*?)src\s*=\s*["']([^"']+)["']([^>]*?)>/gi,
        (full, before, src, after) =>
            cache[src] ? `<img ${before}src="${cache[src]}"${after}>` : full);
}
