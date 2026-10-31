import type { MetadataCommentBlock } from '../text/metadata-comment';
import { FILE_INFO_KEY, applyLineEdits, findMetadataFooter, planMetadataFooter, type LineEdit } from './metadata-footer';

export { FILE_INFO_KEY };

/** Document-level details kept in a `fileInfo` metadata comment on the file's last line. */
export interface FileInfo {
    title: string;
    project: string;
    /** User name that first saved the file. */
    author: string;
    /** ISO 8601 timestamps. */
    createdAt: string;
    lastUpdatedBy: string;
    lastUpdatedAt: string;
    reviewedBy: string;
    reviewedAt: string;
    /** Version of the app that last saved the file. */
    programVersion: string;
}

/** What a save stamps into the comment. */
export interface FileInfoStamp {
    userName: string;
    programVersion: string;
    /** Name the file is saved under; its stem fills an empty title. */
    fileName: string;
    /** Nothing but the review changed since the file was last saved (see {@link onlyReviewChanged}). */
    reviewOnly?: boolean;
    now?: Date;
}

export const FILE_INFO_FIELDS: readonly (keyof FileInfo)[] = [
    'title', 'project', 'author', 'createdAt', 'lastUpdatedBy', 'lastUpdatedAt',
    'reviewedBy', 'reviewedAt', 'programVersion',
];

function emptyFileInfo(): FileInfo {
    return Object.fromEntries(FILE_INFO_FIELDS.map(f => [f, ''])) as unknown as FileInfo;
}

function isPlainObject(value: unknown): value is Record<string, unknown> {
    return !!value && typeof value === 'object' && !Array.isArray(value);
}

/** The file-info comment, which only counts as the last non-blank line. */
export function findFileInfoBlock(lines: string[]): MetadataCommentBlock | null {
    return findMetadataFooter(lines).fileInfo;
}

export function readFileInfo(lines: string[]): FileInfo | null {
    const raw = findFileInfoBlock(lines)?.data?.[FILE_INFO_KEY];
    if (!isPlainObject(raw)) return null;
    const info = emptyFileInfo();
    for (const field of FILE_INFO_FIELDS) {
        const value = raw[field];
        if (value !== undefined && value !== null) info[field] = String(value);
    }
    return info;
}

/** Keeps only known fields with string values, so a message can't write arbitrary keys. */
export function sanitizeFileInfoPatch(patch: unknown): Partial<FileInfo> {
    if (!isPlainObject(patch)) return {};
    const out: Partial<FileInfo> = {};
    for (const field of FILE_INFO_FIELDS) {
        if (typeof patch[field] === 'string') out[field] = patch[field] as string;
    }
    return out;
}

/** The edits that write {@link info}, keeping any other keys the comment carries. */
export function planFileInfoEdit(lines: string[], info: FileInfo): LineEdit[] {
    return planMetadataFooter(lines, { fileInfo: { ...info } });
}

export function writeFileInfo(source: string, info: FileInfo): string {
    return applyLineEdits(source, planFileInfoEdit(source.split(/\r?\n/), info));
}

/** File name without its directory or extension. */
export function fileStem(fileName: string): string {
    const base = fileName.slice(Math.max(fileName.lastIndexOf('/'), fileName.lastIndexOf('\\')) + 1);
    return base.replace(/\.[^.]+$/, '');
}

function timeOf(iso: string): number {
    const time = Date.parse(iso);
    return Number.isNaN(time) ? -Infinity : time;
}

/** True when the file was saved after its review, so the review no longer covers it. */
export function savedSinceReview(info: FileInfo): boolean {
    return !!info.reviewedAt && timeOf(info.lastUpdatedAt) > timeOf(info.reviewedAt);
}

/** {@link source} with its review fields blanked and line endings unified, for comparing content. */
function withoutReview(source: string): string {
    const lines = source.split(/\r?\n/);
    const info = readFileInfo(lines);
    if (!info) return lines.join('\n');
    return applyLineEdits(lines.join('\n'), planFileInfoEdit(lines, { ...info, reviewedBy: '', reviewedAt: '' }));
}

/** True when {@link current} differs from the {@link saved} file in nothing but its review. */
export function onlyReviewChanged(saved: string, current: string): boolean {
    return withoutReview(saved.replace(/^﻿/, '')) === withoutReview(current);
}

/**
 * The info a save writes: created/author on the first save, last-updated and version on
 * every save, and the title only while it is still empty, so a renamed title sticks.
 * Recording a review is not an update, so a save that changes nothing else keeps the
 * last-updated time and user, and the review stays newer than them.
 */
export function stampFileInfo(current: FileInfo | null, stamp: FileInfoStamp): FileInfo {
    const now = (stamp.now ?? new Date()).toISOString();
    const base = current ?? { ...emptyFileInfo(), author: stamp.userName, createdAt: now };
    const update = current && stamp.reviewOnly ? {} : { lastUpdatedBy: stamp.userName, lastUpdatedAt: now };
    return {
        ...base,
        ...update,
        title: base.title || fileStem(stamp.fileName),
        programVersion: stamp.programVersion,
    };
}

/** The PDF header title: the file info's title, else the file name. */
export function pdfDocumentTitle(lines: string[], fileName: string): string {
    return readFileInfo(lines)?.title || fileName;
}

/** The save-time edits for a document, given its current lines; they also move a stray `uiOverrides` down. */
export function planFileInfoStamp(lines: string[], stamp: FileInfoStamp): LineEdit[] {
    return planFileInfoEdit(lines, stampFileInfo(readFileInfo(lines), stamp));
}

/** {@link source} as a save would write it, for hosts that hold the text rather than an editor. */
export function stampFileInfoText(source: string, stamp: FileInfoStamp): string {
    return applyLineEdits(source, planFileInfoStamp(source.split(/\r?\n/), stamp));
}

/** {@link source} with the Properties tab's file-info edit applied, or null when it has no file info to edit. */
export function patchFileInfoText(source: string, patch: unknown): string | null {
    const lines = source.split(/\r?\n/);
    const current = readFileInfo(lines);
    if (!current) return null;
    return applyLineEdits(source, planFileInfoEdit(lines, { ...current, ...sanitizeFileInfoPatch(patch) }));
}
