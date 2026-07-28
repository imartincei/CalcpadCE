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
export const COMPILED_EXTENSION = '.cpdz';

/** True for a path naming a compiled worksheet, which is not editable as text. */
export function isCompiledPath(filePath: string): boolean {
    return filePath.toLowerCase().endsWith(COMPILED_EXTENSION);
}
