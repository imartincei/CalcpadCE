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

/** Bumped on a host-driven discard so a panel still bound to that document lets its form go. */
export const metadataDraftDiscarded = ref({ docKey: '', n: 0 });

export function discardMetadataDraft(docKey: string): void {
    drafts.delete(docKey);
    metadataDraftDiscarded.value = { docKey, n: metadataDraftDiscarded.value.n + 1 };
}
