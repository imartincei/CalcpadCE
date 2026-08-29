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

/** A `#UI` control the engine actually created, harvested from a rendered form. */
export interface UiControl {
    /** Control identity, as emitted in `data-ui-var`. */
    key: string;
    /** 1-based source line the control was declared on, from `data-ui-line`. */
    line: number;
}

/** A saved override paired with the control it resolves to, if any. */
export interface UiOverrideRow {
    key: string;
    value: string;
    /** Source line of the control this override applies to, null when it applies to none. */
    line: number | null;
    used: boolean;
}

/** Metadata-comment key the saved overrides live under. */
const OVERRIDES_KEY = 'uiOverrides';

/**
 * Values entered into `#UI` controls, held per document.
 *
 * Edits stay in memory so typing in a form never dirties the file; the
 * document is only touched when the user explicitly saves, which writes the
 * current map into a `'<!--{"uiOverrides":{...}}-->` comment at the top. That
 * comment is what {@link syncFromSource} restores on the next open, and what it
 * keeps the in-memory map in step with while the document is edited.
 */
export class UiOverrideStore {
    private readonly byDocument = new Map<string, Map<string, string>>();
    // Serialized form of each document's saved comment as the store last saw it, so a
    // hand-edit to that comment can be told apart from a comment that has not moved.
    private readonly lastSeenSaved = new Map<string, string>();

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

    public clear(docKey: string): void {
        this.byDocument.delete(docKey);
        this.lastSeenSaved.delete(docKey);
    }

    /** Discards every document's overrides (e.g. on workspace close). */
    public clearAll(): void {
        this.byDocument.clear();
        this.lastSeenSaved.clear();
    }

    public replace(docKey: string, overrides: UiOverrides): void {
        this.byDocument.set(docKey, new Map(Object.entries(overrides)));
    }

    /**
     * Adopts the document's saved `uiOverrides` comment whenever it has changed since the store
     * last read or wrote it, so a hand-edited key reaches the next render rather than being
     * shadowed by the cached map. Returns true when the stored values changed, which is when the
     * render that asked has to be redone.
     */
    public syncFromSource(docKey: string, source: string): boolean {
        const saved = readUiOverrides(source);
        const seen = this.lastSeenSaved.get(docKey);
        const fingerprint = saved ? JSON.stringify(saved) : '';
        if (seen === fingerprint) return false;
        this.lastSeenSaved.set(docKey, fingerprint);
        // First look at a document without the comment: the state an unsaved form starts
        // from, not a comment that was removed.
        if (!saved && seen === undefined) return false;

        const overrides = saved ?? {};
        const changed = !this.matches(docKey, overrides);
        this.replace(docKey, overrides);
        return changed;
    }

    private matches(docKey: string, overrides: UiOverrides): boolean {
        const map = this.byDocument.get(docKey);
        const entries = Object.entries(overrides);
        return (map?.size ?? 0) === entries.length && entries.every(([k, v]) => map?.get(k) === v);
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
 * Returns {@link source} with the saved overrides updated: an existing `uiOverrides` comment is
 * rewritten in place keeping any other keys it carries, otherwise a new comment goes on the first
 * line. Passing an empty map removes the key, and with it the comment when nothing else is left.
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

/** Opening tag of an element the engine tagged as a `#UI` control. */
const UI_CONTROL_TAG = /<[^>]+\sdata-ui-var="[^"]*"[^>]*>/g;

/**
 * The controls a rendered input form actually created, in document order. Reading them
 * back out of the markup rather than scanning the source is what makes them exact:
 * `#include`d files, macro expansions, taken `#if` branches and loop passes are all
 * already resolved by the time the engine emits the attributes.
 */
export function extractUiControls(html: string): UiControl[] {
    const controls: UiControl[] = [];
    const seen = new Set<string>();
    for (const [tag] of html.matchAll(UI_CONTROL_TAG)) {
        const key = /\sdata-ui-var="([^"]*)"/.exec(tag)?.[1];
        const line = /\sdata-ui-line="(\d+)"/.exec(tag)?.[1];
        if (!key || !line || seen.has(key)) continue;
        seen.add(key);
        controls.push({ key, line: Number(line) });
    }
    return controls;
}

/**
 * Resolves each saved override against the live controls, mirroring the narrowest-first match
 * `ExpressionParser.ApplyUiOverride` makes: this exact control, then every pass of its
 * declaration, then every declaration of the name. An override matching none of them is what
 * "unused" means, and key order is the saved one.
 */
export function classifyUiOverrides(overrides: UiOverrides, controls: UiControl[]): UiOverrideRow[] {
    const byKey = new Map<string, number>();
    const byDeclaration = new Map<string, number>();
    const byName = new Map<string, number>();
    for (const { key, line } of controls) {
        const [name, ordinal] = key.split(':');
        if (!byKey.has(key)) byKey.set(key, line);
        if (!byDeclaration.has(`${name}:${ordinal}`)) byDeclaration.set(`${name}:${ordinal}`, line);
        if (!byName.has(name)) byName.set(name, line);
    }

    return Object.entries(overrides).map(([key, value]) => {
        const line = byKey.get(key) ?? byDeclaration.get(key) ?? byName.get(key) ?? null;
        return { key, value, line, used: line !== null };
    });
}
