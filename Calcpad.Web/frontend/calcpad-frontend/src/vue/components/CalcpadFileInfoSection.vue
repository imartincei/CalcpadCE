<template>
  <div class="file-info-section">
    <p v-if="!userName" class="note">{{ t('fileInfo.noUserNameIsSetSoSavesAnd') }}<button class="link-button" @click="emit('open-settings')">{{ t('fileInfo.setItInSettings') }}</button>
    </p>

    <p v-if="!hasDocument" class="note">{{ t('fileInfo.noDocumentIsOpen') }}</p>
    <p v-else-if="!info" class="note">{{ t('fileInfo.fileInfoIsAddedToTheLastLine') }}</p>

    <template v-else>
      <div v-show="rowVisible('title')" class="field">
        <label for="fileInfoTitle">
          <CalcpadSearchHighlight :text="t('common.title')" :query="query" />
          <span class="setting-info" :title="t('fileInfo.alsoTheHeaderTitleOfPdfExportsUnless')">ⓘ</span>
        </label>
        <input id="fileInfoTitle" v-model="draft.title" type="text" @keyup.enter="apply" />
      </div>
      <div v-show="rowVisible('project')" class="field">
        <label for="fileInfoProject"><CalcpadSearchHighlight :text="t('directives.project')" :query="query" /></label>
        <input id="fileInfoProject" v-model="draft.project" type="text" :placeholder="t('fileInfo.none')" @keyup.enter="apply" />
      </div>
      <p v-if="changeCount" v-show="rowVisible('title') || rowVisible('project')" class="pending">
        {{ t('fileInfo.applyToSaveNChangeResetToRevert', { n: changeCount }, changeCount) }}
      </p>
      <div v-show="rowVisible('title') || rowVisible('project')" class="actions">
        <button :disabled="!dirty" @click="apply">{{ t('common.apply') }}</button>
        <button class="secondary" :disabled="!dirty" @click="reset">{{ t('common.reset') }}</button>
      </div>

      <dl v-show="rowVisible('details')" class="details">
        <dt>{{ t('fileInfo.author') }}</dt><dd>{{ info.author || '—' }}</dd>
        <dt>{{ t('fileInfo.created') }}</dt><dd>{{ formatTimestamp(info.createdAt) }}</dd>
        <dt>{{ t('fileInfo.lastUpdated') }}</dt><dd>{{ formatTimestamp(info.lastUpdatedAt) }}</dd>
        <dt>{{ t('fileInfo.lastUpdatedBy') }}</dt><dd>{{ info.lastUpdatedBy || '—' }}</dd>
        <dt>{{ t('fileInfo.programVersion') }}</dt><dd>{{ info.programVersion || '—' }}</dd>
      </dl>

      <div v-show="rowVisible('review')">
        <h4 class="sub-title"><CalcpadSearchHighlight :text="t('common.review')" :query="query" /></h4>
        <dl class="details">
          <dt>{{ t('fileInfo.reviewedBy') }}</dt><dd>{{ info.reviewedBy || 'Not reviewed' }}</dd>
          <dt>{{ t('fileInfo.reviewed') }}</dt><dd>{{ formatTimestamp(info.reviewedAt) }}</dd>
        </dl>
        <p v-if="savedSinceReview(info)" class="warning">
          ⚠ This file was changed after it was reviewed (last updated
          {{ formatTimestamp(info.lastUpdatedAt) }}{{ info.lastUpdatedBy ? ` by ${info.lastUpdatedBy}` : '' }}),
          so the review may not cover its latest changes. Consider having it re-reviewed, then
          use Set as Reviewed to record the new review.
        </p>
        <div class="actions">
          <button
            :disabled="!userName"
            :title="userName ? `Record ${userName} as the reviewer, as of now, and save the file` : 'Set a user name in Settings first'"
            @click="markReviewed"
          >{{ t('fileInfo.setAsReviewed') }}</button>
          <button v-if="info.reviewedBy || info.reviewedAt" class="secondary" @click="clearReview">{{ t('fileInfo.clearReview') }}</button>
        </div>
      </div>
    </template>
  </div>
</template>

<script setup lang="ts">
import { useI18n } from '../../i18n'
import { computed, onBeforeUnmount, reactive, watch } from 'vue'
import CalcpadSearchHighlight from './CalcpadSearchHighlight.vue'
import { savedSinceReview } from '../../services/file-info'
import type { FileInfo } from '../../services/file-info'
import { getFileInfoDraft, setFileInfoDraft, clearFileInfoDraft, metadataDraftDiscarded } from '../metadata-drafts'
import type { FileInfoDraft } from '../metadata-drafts'

const { t } = useI18n()

interface Props {
  info?: FileInfo | null
  hasDocument?: boolean
  /** Identifies the document `info` came from, so an unapplied draft stays with it. */
  docKey?: string
  userName?: string
  query?: string
  rowVisible?: (key: string) => boolean
}

const props = withDefaults(defineProps<Props>(), {
  info: null,
  hasDocument: false,
  docKey: '',
  userName: '',
  query: '',
  rowVisible: () => true,
})

const emit = defineEmits<{
  'update': [patch: Partial<FileInfo>]
  'mark-reviewed': []
  'open-settings': []
  'draft-dirty': [payload: { docKey: string; dirty: boolean }]
}>()

const savedFields = (info: FileInfo | null): FileInfoDraft => ({ title: info?.title ?? '', project: info?.project ?? '' })
const sameFields = (a: FileInfoDraft, b: FileInfoDraft) => a.title === b.title && a.project === b.project

const draft = reactive<FileInfoDraft>({ ...(getFileInfoDraft(props.docKey) ?? savedFields(props.info)) })
const dirty = computed(() => !!props.info && !sameFields(draft, savedFields(props.info)))
const changeCount = computed(() => {
  if (!props.info) return 0
  const saved = savedFields(props.info)
  return (draft.title !== saved.title ? 1 : 0) + (draft.project !== saved.project ? 1 : 0)
})
let boundDocKey = props.docKey

// The host prompts before closing a document with an unapplied form.
function reportDirty(docKey: string, value: boolean) {
  if (docKey) emit('draft-dirty', { docKey, dirty: value })
}

watch(dirty, value => reportDirty(boundDocKey, value))

/** Park unapplied edits under the bound document, or drop a draft nothing changed. */
function stash() {
  if (!boundDocKey) return
  if (dirty.value) setFileInfoDraft(boundDocKey, { ...draft })
  else clearFileInfoDraft(boundDocKey)
  reportDirty(boundDocKey, dirty.value)
}

watch(() => [props.docKey, props.info] as const, ([docKey, info], [, oldInfo]) => {
  if (docKey !== boundDocKey) {
    stash()
    boundDocKey = docKey
    Object.assign(draft, getFileInfoDraft(docKey) ?? savedFields(info))
    reportDirty(docKey, dirty.value)
    return
  }
  // Unapplied edits outrank a refresh (a save stamping the comment, a review); clean fields follow it.
  if (sameFields(draft, savedFields(oldInfo))) Object.assign(draft, savedFields(info))
})

// The host drops a document's draft when its file closes.
watch(metadataDraftDiscarded, ({ docKey }) => {
  if (docKey && docKey === boundDocKey) Object.assign(draft, savedFields(props.info))
})

onBeforeUnmount(stash)

function apply() {
  if (!props.info || !dirty.value) return
  const saved = savedFields(props.info)
  const patch: Partial<FileInfo> = {}
  if (draft.title !== saved.title) patch.title = draft.title
  if (draft.project !== saved.project) patch.project = draft.project
  clearFileInfoDraft(boundDocKey)
  emit('update', patch)
}

function reset() {
  clearFileInfoDraft(boundDocKey)
  Object.assign(draft, savedFields(props.info))
}

// The host stamps the time: it saves any pending edits first, which must not postdate the review.
function markReviewed() {
  emit('mark-reviewed')
}

function clearReview() {
  emit('update', { reviewedBy: '', reviewedAt: '' })
}

function formatTimestamp(iso: string): string {
  if (!iso) return '—'
  const date = new Date(iso)
  return Number.isNaN(date.getTime()) ? iso : date.toLocaleString()
}
</script>

<style scoped>
.sub-title {
  margin: 16px 0 8px 0;
  font-weight: 600;
}

.actions + .details {
  margin-top: 16px;
}

.warning {
  margin: 0 0 8px 0;
  color: var(--vscode-editorWarning-foreground, #cca700);
  line-height: 1.5;
}

.setting-info {
  cursor: help;
  color: var(--vscode-descriptionForeground);
}

.note {
  margin: 0 0 12px 0;
  color: var(--vscode-descriptionForeground);
  line-height: 1.5;
}

.field {
  display: flex;
  flex-direction: column;
  gap: 4px;
  margin-bottom: 10px;
}

.field input {
  background: var(--vscode-input-background);
  color: var(--vscode-input-foreground);
  border: 1px solid var(--vscode-input-border, transparent);
  padding: 4px 6px;
  font-family: var(--vscode-font-family);
  border-radius: 2px;
}

.details {
  display: grid;
  grid-template-columns: max-content 1fr;
  gap: 4px 12px;
  margin: 0 0 8px 0;
}

.details dt {
  color: var(--vscode-descriptionForeground);
}

.details dd {
  margin: 0;
  overflow-wrap: anywhere;
}

.actions {
  display: flex;
  gap: 8px;
  margin-top: 8px;
}

.link-button {
  background: none;
  border: none;
  padding: 0;
  color: var(--vscode-textLink-foreground);
  font: inherit;
  text-decoration: underline;
}

.pending {
  margin: 8px 0 0 0;
  color: var(--vscode-editorWarning-foreground, #cca700);
  font-style: italic;
}
</style>
