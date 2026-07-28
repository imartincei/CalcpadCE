import {
    findMetadataCommentBlock,
    serializeMetadataComment,
    type MetadataCommentData,
} from '../text/metadata-comment';

/** Control key (the preview's `data-ui-var`) mapped to the entered value. */
export type UiOverrides = Record<string, string>;

/** A value edited in the preview, as reported by the injected event script. */
export interface UiValueChange {
    /** Control identity, e.g. `"L:1"` or `"L:1:2"` inside a loop. */
    varName: string;
    newValue: string;
    /** 1-based source line the control was declared on. */
    sourceLine: number;
}

/** Metadata-comment key the saved overrides live under. */
const OVERRIDES_KEY = 'uiOverrides';

/**
 * Values entered into `#UI` controls, held per document.
 *
 * Edits stay in memory so typing in a form never dirties the file; the
 * document is only touched when the user explicitly saves, which writes the
 * current map into a `'<!--{"uiOverrides":{...}}-->` comment at the top. That
 * comment is what {@link readFromSource} restores on the next open.
 */
export class UiOverrideStore {
    private readonly byDocument = new Map<string, Map<string, string>>();

    private mapFor(docKey: string): Map<string, string> {
        let map = this.byDocument.get(docKey);
        if (!map) {
            map = new Map();
            this.byDocument.set(docKey, map);
        }
        return map;
    }

    /** Records an edit. Returns true when it actually changed the stored value. */
    public set(docKey: string, key: string, value: string): boolean {
        const map = this.mapFor(docKey);
        if (map.get(key) === value) return false;
        map.set(key, value);
        return true;
    }

    public get(docKey: string, key: string): string | undefined {
        return this.byDocument.get(docKey)?.get(key);
    }

    /** The overrides for a document, or undefined when it has none. */
    public toRecord(docKey: string): UiOverrides | undefined {
        const map = this.byDocument.get(docKey);
        if (!map?.size) return undefined;
        return Object.fromEntries(map);
    }

    public has(docKey: string): boolean {
        return (this.byDocument.get(docKey)?.size ?? 0) > 0;
    }

    public clear(docKey: string): void {
        this.byDocument.delete(docKey);
    }

    /** Discards every document's overrides (e.g. on workspace close). */
    public clearAll(): void {
        this.byDocument.clear();
    }

    public replace(docKey: string, overrides: UiOverrides): void {
        this.byDocument.set(docKey, new Map(Object.entries(overrides)));
    }

    /**
     * Seeds the store from a saved `uiOverrides` metadata comment, replacing
     * whatever the document held. No comment leaves the store untouched, so
     * reloading a file that was never saved with values keeps the session's.
     */
    public readFromSource(docKey: string, source: string): boolean {
        const saved = readUiOverrides(source);
        if (!saved) return false;
        this.replace(docKey, saved);
        return true;
    }
}

/**
 * Finds the document's saved overrides, or null when it has none. Scans from the
 * top rather than taking a cursor position: the comment is document-level, and
 * writing it is the only thing that puts a `uiOverrides` key in a metadata comment.
 */
export function readUiOverrides(source: string): UiOverrides | null {
    const lines = source.split('\n');
    for (let i = 0; i < lines.length; i++) {
        const block = findMetadataCommentBlock(lines, i);
        if (!block?.valid || !block.data) continue;

        const raw = block.data[OVERRIDES_KEY];
        if (raw && typeof raw === 'object' && !Array.isArray(raw))
            return Object.fromEntries(
                Object.entries(raw as Record<string, unknown>).map(([k, v]) => [k, String(v)]));

        // A multi-line block spans several indices; skip past it either way.
        i = block.endLine;
    }
    return null;
}

/**
 * Returns {@link source} with the saved overrides updated. An existing
 * `uiOverrides` comment is rewritten in place, keeping any other keys it carries;
 * otherwise a new comment goes on the first line. Passing an empty map removes
 * the key, and with it the comment when nothing else is left in it.
 */
export function writeUiOverrides(source: string, overrides: UiOverrides): string {
    const eol = source.includes('\r\n') ? '\r\n' : '\n';
    const lines = source.split(/\r?\n/);

    for (let i = 0; i < lines.length; i++) {
        const block = findMetadataCommentBlock(lines, i);
        if (!block?.valid || !block.data) continue;
        if (!(OVERRIDES_KEY in block.data)) {
            i = block.endLine;
            continue;
        }

        const data: MetadataCommentData = { ...block.data };
        if (Object.keys(overrides).length === 0)
            delete data[OVERRIDES_KEY];
        else
            data[OVERRIDES_KEY] = overrides;

        // serializeMetadataComment drops empty objects, so a comment that held
        // nothing but the overrides collapses to an empty one - remove it instead.
        // A layout-preserving serialization can span several physical lines.
        const replacement = Object.keys(data).length === 0
            ? []
            : serializeMetadataComment(data, block.indent, block.trailingQuote, block.layout).split('\n');

        lines.splice(block.line, block.endLine - block.line + 1, ...replacement);
        return lines.join(eol);
    }

    if (Object.keys(overrides).length === 0) return source;

    const comment = serializeMetadataComment({ [OVERRIDES_KEY]: overrides });
    return [comment, ...lines].join(eol);
}
