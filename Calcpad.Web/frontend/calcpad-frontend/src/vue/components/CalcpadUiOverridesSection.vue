<template>
  <div class="ui-overrides-section">
    <p v-if="!hasDocument" class="note">{{ t('No document is open.') }}</p>
    <p v-else-if="!rows.length && !dirty" class="note">{{ t('No saved #UI values. Values entered into the input form are saved here, at the end of the file.') }}</p>

    <template v-else>
      <p class="note">{{ t('Values entered into the input form. Click a name to go to the #UI line that declares it.') }}</p>
      <p v-if="!uiControlsResolved" class="note">{{ t('Checking which controls still exist…') }}</p>
      <p v-else-if="unusedRows.length" class="warning">
        {{ unusedRows.length }} value{{ unusedRows.length === 1 ? '' : 's' }}
        no longer match a #UI control.
      </p>

      <div v-for="(row, i) in classified" v-show="rowMatches(row)" :key="'uo' + row.key" class="list-row">
        <button
          class="key-button"
          :disabled="row.line === null"
          :title="row.line === null ? 'No #UI control uses this value' : `Go to line ${row.line}`"
          @click="goTo(row)"
        ><CalcpadSearchHighlight :text="row.key" :query="query" /></button>
        <input type="text" v-model="rows[i].value" @keyup.enter="apply" />
        <span v-if="uiControlsResolved && !row.used" class="unused-badge">unused</span>
        <button class="icon" :title="t('Remove')" @click="rows.splice(i, 1)">✕</button>
      </div>

      <div class="actions">
        <button :disabled="!dirty" @click="apply">{{ t('Apply') }}</button>
        <button class="secondary" :disabled="!dirty" @click="reset">{{ t('Reset') }}</button>
        <button
          class="tertiary"
          :disabled="!unusedRows.length"
          :title="uiControlsResolved ? undefined : 'Waiting for the document to be rendered'"
          @click="purgeUnused"
        >{{ t('Purge unused') }}</button>
      </div>
    </template>
  </div>
</template>

<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import CalcpadSearchHighlight from './CalcpadSearchHighlight.vue'
import { classifyUiOverrides } from '../../services/ui-overrides'
import type { UiControl, UiOverrideRow, UiOverrides } from '../../services/ui-overrides'
import { getUiOverridesDraft, setUiOverridesDraft, clearUiOverridesDraft, metadataDraftDiscarded } from '../metadata-drafts'
import type { UiOverrideDraftRow } from '../metadata-drafts'

const { t } = useI18n()

interface Props {
  overrides?: UiOverrides
  hasDocument?: boolean
  docKey?: string
  /** Controls of the last input-form render, or null until the host resolves them. */
  uiControls?: UiControl[] | null
  /** Whether the section is showing, since resolving controls renders the document. */
  visible?: boolean
  query?: string
  /** Lowercase search text the rows must contain, or '' for all of them. */
  filter?: string
}

const props = withDefaults(defineProps<Props>(), {
  overrides: () => ({}),
  hasDocument: false,
  docKey: '',
  uiControls: null,
  visible: true,
  query: '',
  filter: '',
})

const emit = defineEmits<{
  'update': [overrides: UiOverrides]
  'go-to-line': [line: number]
  'refresh-ui-controls': []
  'draft-dirty': [payload: { docKey: string; dirty: boolean }]
}>()

const savedRows = (overrides: UiOverrides): UiOverrideDraftRow[] =>
  Object.entries(overrides).map(([key, value]) => ({ key, value }))
const sameRows = (a: UiOverrideDraftRow[], b: UiOverrideDraftRow[]) => JSON.stringify(a) === JSON.stringify(b)

const rows = ref<UiOverrideDraftRow[]>(getUiOverridesDraft(props.docKey) ?? savedRows(props.overrides))
const dirty = computed(() => !sameRows(rows.value, savedRows(props.overrides)))
let boundDocKey = props.docKey

function reportDirty(docKey: string, value: boolean) {
  if (docKey) emit('draft-dirty', { docKey, dirty: value })
}

watch(dirty, value => reportDirty(boundDocKey, value))

function stash() {
  if (!boundDocKey) return
  if (dirty.value) setUiOverridesDraft(boundDocKey, rows.value.map(r => ({ ...r })))
  else clearUiOverridesDraft(boundDocKey)
  reportDirty(boundDocKey, dirty.value)
}

watch(() => [props.docKey, props.overrides] as const, ([docKey, overrides], [, oldOverrides]) => {
  if (docKey !== boundDocKey) {
    stash()
    boundDocKey = docKey
    rows.value = getUiOverridesDraft(docKey) ?? savedRows(overrides)
    reportDirty(docKey, dirty.value)
    return
  }
  // Unapplied edits outrank a refresh; clean rows follow it.
  if (sameRows(rows.value, savedRows(oldOverrides))) rows.value = savedRows(overrides)
})

watch(metadataDraftDiscarded, ({ docKey }) => {
  if (docKey && docKey === boundDocKey) rows.value = savedRows(props.overrides)
})

onBeforeUnmount(stash)

// Until the host reports what the document renders, nothing can be called stale.
const uiControlsResolved = computed(() => props.uiControls !== null)

const classified = computed(() => classifyUiOverrides(
  Object.fromEntries(rows.value.map(r => [r.key, r.value])),
  props.uiControls ?? []))

const unusedRows = computed(() => uiControlsResolved.value ? classified.value.filter(r => !r.used) : [])

// Resolving renders the document, so it waits until there are values on screen to check.
watch(() => props.visible && rows.value.length > 0 && !uiControlsResolved.value, need => {
  if (need) emit('refresh-ui-controls')
}, { immediate: true })

const rowMatches = (row: UiOverrideRow) =>
  !props.filter || row.key.toLowerCase().includes(props.filter) || row.value.toLowerCase().includes(props.filter)

function purgeUnused() {
  const stale = new Set(unusedRows.value.map(r => r.key))
  rows.value = rows.value.filter(r => !stale.has(r.key))
}

function goTo(row: UiOverrideRow) {
  if (row.line !== null) emit('go-to-line', row.line)
}

function apply() {
  if (!dirty.value) return
  clearUiOverridesDraft(boundDocKey)
  emit('update', Object.fromEntries(rows.value.map(r => [r.key, r.value])))
}

function reset() {
  clearUiOverridesDraft(boundDocKey)
  rows.value = savedRows(props.overrides)
}
</script>

<style scoped>
.note {
  margin: 0 0 12px 0;
  color: var(--vscode-descriptionForeground);
  line-height: 1.5;
}

.warning {
  color: var(--vscode-editorWarning-foreground, #cca700);
  margin: 0 0 12px 0;
  line-height: 1.5;
}

.list-row {
  display: flex;
  align-items: center;
  gap: 6px;
  margin-bottom: 6px;
}

.list-row input {
  flex: 1;
  min-width: 0;
  background: var(--vscode-input-background);
  color: var(--vscode-input-foreground);
  border: 1px solid var(--vscode-input-border, transparent);
  padding: 4px 6px;
  font-family: var(--vscode-font-family);
  border-radius: 2px;
}

.key-button {
  flex: 0 0 auto;
  max-width: 45%;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  text-align: left;
  background: transparent;
  border: none;
  padding: 2px 4px;
  border-radius: 2px;
  font-family: var(--vscode-editor-font-family, monospace);
  color: var(--vscode-textLink-foreground);
  cursor: pointer;
}

.key-button:hover:not(:disabled) {
  background: var(--vscode-toolbar-hoverBackground);
  text-decoration: underline;
}

.key-button:disabled {
  cursor: default;
  color: var(--vscode-descriptionForeground);
}

.unused-badge {
  flex: 0 0 auto;
  padding: 1px 4px;
  border-radius: 2px;
  color: var(--vscode-editorWarning-foreground, #cca700);
  border: 1px solid var(--vscode-editorWarning-foreground, #cca700);
}

.actions {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  margin-top: 8px;
}
</style>
