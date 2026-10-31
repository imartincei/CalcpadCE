import {
    findMetadataCommentBlock,
    hasMetadataContent,
    serializeMetadataComment,
    type MetadataCommentBlock,
    type MetadataCommentData,
} from '../text/metadata-comment';

/**
 * Document-level metadata lives at the end of the file: an anchor line, a `uiOverrides`
 * comment, then a `fileInfo` comment as the last non-blank line.
 */
export const FILE_INFO_KEY = 'fileInfo';
export const UI_OVERRIDES_KEY = 'uiOverrides';
export const METADATA_ANCHOR = "'<!--CalcpadCE Metadata-->";

/** Placeholder over the hidden footer where an editor can't remove the lines. */
export const METADATA_HIDDEN_TEXT = '⋯ file metadata hidden (see Properties tab)';

/** Replace lines `line..endLine` with {@link lines}, or insert them above `line` when `isNew`. */
export interface LineEdit {
    line: number;
    endLine: number;
    lines: string[];
    isNew: boolean;
}

/** A {@link LineEdit} as a 0-based character range, for hosts with range-based edit APIs. */
export interface RangeEdit {
    startLine: number;
    startCharacter: number;
    endLine: number;
    endCharacter: number;
    text: string;
}

export interface MetadataFooter {
    fileInfo: MetadataCommentBlock | null;
    uiOverrides: MetadataCommentBlock | null;
    /** 0-based line of {@link METADATA_ANCHOR} directly above the footer comments, if present. */
    anchor: number | null;
}

export interface FooterChanges {
    /** Fields merged into the `fileInfo` object; undefined leaves it as is. */
    fileInfo?: Record<string, unknown>;
    /** Replaces the saved overrides; an empty map removes them, undefined keeps them. */
    uiOverrides?: Record<string, string>;
}

/** How far from the end {@link findMetadataFooterSpan} reads, so it stays cheap on every edit. */
const FOOTER_WINDOW = 64;

function isPlainObject(value: unknown): value is Record<string, unknown> {
    return !!value && typeof value === 'object' && !Array.isArray(value);
}

function lastContentLine(lines: string[], before: number): number {
    for (let i = before - 1; i >= 0; i--) {
        if (lines[i].trim() !== '') return i;
    }
    return -1;
}

function commentEndingAt(lines: string[], line: number): MetadataCommentBlock | null {
    if (line < 0) return null;
    const block = findMetadataCommentBlock(lines, line);
    return block?.endLine === line ? block : null;
}

function holds(block: MetadataCommentBlock | null, key: string): block is MetadataCommentBlock {
    return !!block?.valid && !!block.data && key in block.data;
}

export function findMetadataFooter(lines: string[]): MetadataFooter {
    let block = commentEndingAt(lines, lastContentLine(lines, lines.length));
    const fileInfo = holds(block, FILE_INFO_KEY) ? block : null;
    if (fileInfo) block = commentEndingAt(lines, lastContentLine(lines, fileInfo.line));
    const uiOverrides = holds(block, UI_OVERRIDES_KEY) ? block : null;
    const above = lastContentLine(lines, (uiOverrides ?? fileInfo)?.line ?? lines.length);
    const anchor = above >= 0 && lines[above].trim() === METADATA_ANCHOR ? above : null;
    return { fileInfo, uiOverrides, anchor };
}

/** First `uiOverrides` comment outside the footer, where files saved before it existed keep theirs. */
export function findStrayUiOverrides(lines: string[], footer = findMetadataFooter(lines)): MetadataCommentBlock | null {
    const skip = new Set([footer.fileInfo?.line, footer.uiOverrides?.line]);
    for (let i = 0; i < lines.length; i++) {
        const block = findMetadataCommentBlock(lines, i);
        if (!block) continue;
        i = block.endLine;
        if (!skip.has(block.line) && holds(block, UI_OVERRIDES_KEY)) return block;
    }
    return null;
}

/** 0-based line span of the footer comments, reading only the end of the document. */
export function findMetadataFooterSpan(lineAt: (index: number) => string, lineCount: number): { start: number; end: number } | null {
    let start = Math.max(0, lineCount - FOOTER_WINDOW);
    while (start > 0 && /_\s*$/.test(lineAt(start - 1))) start--;
    const lines: string[] = [];
    for (let i = start; i < lineCount; i++) lines.push(lineAt(i));

    const { fileInfo, uiOverrides, anchor } = findMetadataFooter(lines);
    const first = uiOverrides ?? fileInfo;
    const last = fileInfo ?? uiOverrides;
    return first && last ? { start: (anchor ?? first.line) + start, end: last.endLine + start } : null;
}

/** The lines the editors hide: from the anchor (or first footer comment) through the end of the document. */
export function findMetadataHiddenSpan(lineAt: (index: number) => string, lineCount: number): { start: number; end: number } | null {
    const span = findMetadataFooterSpan(lineAt, lineCount);
    return span ? { start: span.start, end: lineCount - 1 } : null;
}

/** 0-based position, as both Monaco (after shifting) and VS Code use. */
export interface LinePosition {
    line: number;
    character: number;
}

/**
 * {@link anchor}/{@link active} moved out of the hidden footer {@link span}, or null when the
 * selection doesn't reach it. A selection running into it stops at the last visible line above.
 */
export function keepSelectionOutOfSpan(
    anchor: LinePosition,
    active: LinePosition,
    span: { start: number; end: number },
    lineCount: number,
    lineLength: (index: number) => number,
): { anchor: LinePosition; active: LinePosition } | null {
    const before = span.start > 0 ? { line: span.start - 1, character: lineLength(span.start - 1) } : null;
    const after = span.end + 1 < lineCount ? { line: span.end + 1, character: 0 } : null;
    const fix = (p: LinePosition, other: LinePosition): LinePosition => {
        if (p.line < span.start) return p;
        if (p.line > span.end) return other.line < span.start && before ? before : p;
        if (other.line > span.end && after) return after;
        return before ?? after ?? p;
    };
    const next = { anchor: fix(anchor, active), active: fix(active, anchor) };
    return next.anchor === anchor && next.active === active ? null : next;
}

function serialize(data: MetadataCommentData, block: MetadataCommentBlock | null): string[] {
    return serializeMetadataComment(data, block?.indent ?? '', block?.trailingQuote ?? '', block?.layout).split('\n');
}

/**
 * The edits that write {@link changes} into the footer. A `uiOverrides` comment found elsewhere
 * (the old top-of-file spot) moves down into it, leaving any other keys it carried behind.
 */
export function planMetadataFooter(lines: string[], changes: FooterChanges = {}): LineEdit[] {
    const footer = findMetadataFooter(lines);
    const stray = footer.uiOverrides ? null : findStrayUiOverrides(lines, footer);
    const edits: LineEdit[] = [];

    if (stray?.data) {
        const rest = { ...stray.data };
        delete rest[UI_OVERRIDES_KEY];
        edits.push({
            line: stray.line,
            endLine: stray.endLine,
            lines: hasMetadataContent(rest) ? serialize(rest, stray) : [],
            isNew: false,
        });
    }

    const footerLines: string[] = [];

    const overridesBlock = footer.uiOverrides;
    const overridesData: MetadataCommentData = { ...(overridesBlock?.data ?? {}) };
    const overrides = changes.uiOverrides ?? (overridesBlock ?? stray)?.data?.[UI_OVERRIDES_KEY];
    if (isPlainObject(overrides) && Object.keys(overrides).length) overridesData[UI_OVERRIDES_KEY] = overrides;
    else delete overridesData[UI_OVERRIDES_KEY];
    if (hasMetadataContent(overridesData)) footerLines.push(...serialize(overridesData, overridesBlock));

    const infoBlock = footer.fileInfo;
    if (infoBlock?.data || changes.fileInfo) {
        const infoData: MetadataCommentData = { ...(infoBlock?.data ?? {}) };
        const existing = infoData[FILE_INFO_KEY];
        if (changes.fileInfo) infoData[FILE_INFO_KEY] = { ...(isPlainObject(existing) ? existing : {}), ...changes.fileInfo };
        footerLines.push(...serialize(infoData, infoBlock));
    }

    // The anchor, then the footer, ending the file; lines above it are left alone. Rewriting it
    // restores an anchor that was deleted. A file's trailing newline is where a new footer goes.
    const lastLine = lines.length - 1;
    const footerStart = footer.anchor ?? (overridesBlock ?? infoBlock)?.line ?? null;
    const body = footerLines.length ? [METADATA_ANCHOR, ...footerLines] : [];
    if (footerStart === null && !body.length) return edits;
    const from = footerStart ?? (lines[lastLine] === '' ? lastLine : lines.length);
    const edit = minimalEdit(lines.slice(from), body, from);
    if (edit) edits.push(edit);
    return edits;
}

/**
 * The edit turning {@link current} (starting at line {@link offset}) into {@link next}, touching only
 * the lines between their common start and end.
 */
function minimalEdit(current: string[], next: string[], offset: number): LineEdit | null {
    let head = 0;
    while (head < current.length && head < next.length && current[head] === next[head]) head++;
    if (head === current.length && head === next.length) return null;
    let tail = 0;
    while (tail < current.length - head && tail < next.length - head
        && current[current.length - 1 - tail] === next[next.length - 1 - tail]) tail++;
    const line = offset + head;
    const endLine = offset + current.length - tail - 1;
    const lines = next.slice(head, next.length - tail);
    return endLine < line ? { line, endLine: line, lines, isNew: true } : { line, endLine, lines, isNew: false };
}

export function applyLineEdits(source: string, edits: LineEdit[]): string {
    if (!edits.length) return source;
    const eol = source.includes('\r\n') ? '\r\n' : '\n';
    const lines = source.split(/\r?\n/);
    for (const edit of [...edits].sort((a, b) => b.line - a.line))
        lines.splice(edit.line, edit.isNew ? 0 : edit.endLine - edit.line + 1, ...edit.lines);
    return lines.join(eol);
}

/** {@link edits} as character ranges over a document of {@link lineCount} lines. */
export function toRangeEdits(edits: LineEdit[], lineCount: number, lineLength: (index: number) => number, eol: string): RangeEdit[] {
    return edits.map(edit => {
        const text = edit.lines.join(eol);
        if (edit.isNew) {
            if (edit.line < lineCount)
                return { startLine: edit.line, startCharacter: 0, endLine: edit.line, endCharacter: 0, text: text + eol };
            const last = lineCount - 1;
            const end = lineLength(last);
            return { startLine: last, startCharacter: end, endLine: last, endCharacter: end, text: eol + text };
        }
        if (edit.lines.length)
            return { startLine: edit.line, startCharacter: 0, endLine: edit.endLine, endCharacter: lineLength(edit.endLine), text };
        // A removed comment takes its line break with it.
        if (edit.endLine + 1 < lineCount)
            return { startLine: edit.line, startCharacter: 0, endLine: edit.endLine + 1, endCharacter: 0, text: '' };
        if (edit.line > 0)
            return { startLine: edit.line - 1, startCharacter: lineLength(edit.line - 1), endLine: edit.endLine, endCharacter: lineLength(edit.endLine), text: '' };
        return { startLine: 0, startCharacter: 0, endLine: edit.endLine, endCharacter: lineLength(edit.endLine), text: '' };
    });
}
