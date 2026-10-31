import { ref } from 'vue';
import type { MetadataCommentBlock } from '../text/metadata-comment';

/** A Properties form the user edited but hasn't applied yet. */
export interface MetadataDraft {
    /** The block the edits apply to, not necessarily the one under the cursor now. */
    block: MetadataCommentBlock;
    state: string;
    /** The state as populated from the document, so the draft stays marked unsaved. */
    baseline: string;
}

// Outside the component: the tab is unmounted on a panel-tab switch, and both panes
// share one draft for a file open twice.
const drafts = new Map<string, MetadataDraft>();

export function getMetadataDraft(docKey: string): MetadataDraft | undefined {
    return drafts.get(docKey);
}

export function setMetadataDraft(docKey: string, draft: MetadataDraft): void {
    drafts.set(docKey, draft);
}

export function clearMetadataDraft(docKey: string): void {
    drafts.delete(docKey);
}

/** The File Info section's unapplied text fields, kept per document for the same reasons. */
export interface FileInfoDraft {
    title: string;
    project: string;
}

const fileInfoDrafts = new Map<string, FileInfoDraft>();

export function getFileInfoDraft(docKey: string): FileInfoDraft | undefined {
    return fileInfoDrafts.get(docKey);
}

export function setFileInfoDraft(docKey: string, draft: FileInfoDraft): void {
    fileInfoDrafts.set(docKey, draft);
}

export function clearFileInfoDraft(docKey: string): void {
    fileInfoDrafts.delete(docKey);
}

/** The UI Overrides section's unapplied rows. */
export interface UiOverrideDraftRow {
    key: string;
    value: string;
}

const uiOverridesDrafts = new Map<string, UiOverrideDraftRow[]>();

export function getUiOverridesDraft(docKey: string): UiOverrideDraftRow[] | undefined {
    return uiOverridesDrafts.get(docKey);
}

export function setUiOverridesDraft(docKey: string, rows: UiOverrideDraftRow[]): void {
    uiOverridesDrafts.set(docKey, rows);
}

export function clearUiOverridesDraft(docKey: string): void {
    uiOverridesDrafts.delete(docKey);
}

/** Bumped on a host-driven discard so a panel still bound to that document lets its form go. */
export const metadataDraftDiscarded = ref({ docKey: '', n: 0 });

export function discardMetadataDraft(docKey: string): void {
    drafts.delete(docKey);
    fileInfoDrafts.delete(docKey);
    uiOverridesDrafts.delete(docKey);
    metadataDraftDiscarded.value = { docKey, n: metadataDraftDiscarded.value.n + 1 };
}
