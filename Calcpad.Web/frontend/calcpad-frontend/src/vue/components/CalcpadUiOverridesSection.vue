<template>
  <div ref="root" class="ui-overrides-section">
    <p v-if="!hasDocument" class="note">{{ t('fileInfo.noDocumentIsOpen') }}</p>
    <p v-else-if="!rows.length && !dirty" class="note">{{ t('uiOverrides.noSavedHashUiValuesValuesEnteredInto') }}</p>

    <template v-else>
      <p class="note">{{ t('uiOverrides.valuesEnteredIntoTheInputFormClickA') }}</p>
      <p v-if="!uiControlsResolved" class="note">{{ t('uiOverrides.checkingWhichControlsStillExistEllipsis') }}</p>
      <p v-else-if="unusedRows.length" class="warning">
        {{ unusedRows.length }} value{{ unusedRows.length === 1 ? '' : 's' }}
        no longer match a #UI control.
      </p>

      <label class="selection-filter" :title="t('uiOverrides.includesValuesFromHashIncludeLinesAndMacro')">
        <input type="checkbox" v-model="selectedLinesOnly" />
        <span>{{ t('uiOverrides.selectedLinesOnly') }}</span>
        <span v-if="selectedLinesOnly && selection" class="selection-lines">({{ selectionLabel }})</span>
      </label>
      <p v-if="selectedLinesOnly && !selection" class="note">{{ t('uiOverrides.placeTheCursorOnALineToSee') }}</p>
      <p v-else-if="selectedLinesOnly && uiControlsResolved && !classified.some(rowMatches)" class="note">
        {{ t('uiOverrides.noValuesForTheSelectedLines') }}
      </p>

      <div
        v-for="(row, i) in classified"
        v-show="rowMatches(row)"
        :key="'uo' + row.key"
        class="list-row"
        :class="{ flash: flashKeys.has(row.key) }"
        :data-key="row.key"
      >
        <button
          class="key-button"
          :disabled="row.line === null"
          :title="row.line === null ? 'No #UI control uses this value' : `Go to line ${row.line}`"
          @click="goTo(row)"
        ><CalcpadSearchHighlight :text="row.key" :query="query" /></button>
        <input type="text" v-model="rows[i].value" @keyup.enter="apply" />
        <span v-if="uiControlsResolved && !row.used" class="unused-badge">unused</span>
        <button class="icon" :title="t('common.remove')" @click="rows.splice(i, 1)">✕</button>
      </div>

      <p v-if="changeCount" class="pending">
        {{ t('fileInfo.applyToSaveNChangeResetToRevert', { n: changeCount }, changeCount) }}
      </p>
      <div class="actions">
        <button :disabled="!dirty" @click="apply">{{ t('common.apply') }}</button>
        <button class="secondary" :disabled="!dirty" @click="reset">{{ t('common.reset') }}</button>
        <button
          class="tertiary"
          :disabled="!unusedRows.length"
          :title="uiControlsResolved ? undefined : 'Waiting for the document to be rendered'"
          @click="purgeUnused"
        >{{ t('uiOverrides.purgeUnused') }}</button>
      </div>
      <div class="actions">
        <button
          class="secondary"
          :disabled="dirty || !sourceKeys.length"
          :title="t('uiOverrides.writeHashUiOverridesIntoTheCalcpadceFile')"
          @click="emit('override-defaults', sourceKeys)"
        >{{ sourceKeys.length ? t('uiOverrides.overrideDefaultsN', { n: sourceKeys.length }) : t('uiOverrides.overrideDefaults') }}</button>
        <button
          class="secondary"
          :disabled="!shownRows.length"
          :title="t('uiOverrides.revertsDisplayedHashUiOverridesBackToTheir')"
          @click="revertToDefaults"
        >{{ t('uiOverrides.revertToDefaults') }}</button>
      </div>
    </template>
  </div>
</template>

<script setup lang="ts">
import { useI18n } from '../../i18n'
import { computed, nextTick, onBeforeUnmount, ref, watch } from 'vue'
import CalcpadSearchHighlight from './CalcpadSearchHighlight.vue'
import { classifyUiOverrides, rowInRange } from '../../services/ui-overrides'
import { effectiveUiOverrideKey } from '../../services/ui-source-sync'
import type { LineRange, UiControl, UiOverrideRow, UiOverrides } from '../../services/ui-overrides'
import { getUiOverridesDraft, setUiOverridesDraft, clearUiOverridesDraft, metadataDraftDiscarded } from '../metadata-drafts'
import type { UiOverrideDraftRow } from '../metadata-drafts'

const { t } = useI18n()

interface Props {
  overrides?: UiOverrides
  hasDocument?: boolean
  docKey?: string
  /** Controls of the last input-form render, or null until the host resolves them. */
  uiControls?: UiControl[] | null
  /** Editor lines the cursor or selection covers. */
  selection?: LineRange | null
  /** Saved values an editor hint asked to show. */
  reveal?: { keys: string[]; seq: number } | null
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
  selection: null,
  reveal: null,
  visible: true,
  query: '',
  filter: '',
})

const emit = defineEmits<{
  'update': [overrides: UiOverrides]
  'go-to-line': [line: number]
  'refresh-ui-controls': []
  'override-defaults': [keys: string[]]
  'draft-dirty': [payload: { docKey: string; dirty: boolean }]
}>()

const savedRows = (overrides: UiOverrides): UiOverrideDraftRow[] =>
  Object.entries(overrides).map(([key, value]) => ({ key, value }))
const sameRows = (a: UiOverrideDraftRow[], b: UiOverrideDraftRow[]) => JSON.stringify(a) === JSON.stringify(b)

const rows = ref<UiOverrideDraftRow[]>(getUiOverridesDraft(props.docKey) ?? savedRows(props.overrides))
const dirty = computed(() => !sameRows(rows.value, savedRows(props.overrides)))

// Values added, edited or removed since the last save.
const changeCount = computed(() => {
  const draft = new Map(rows.value.map(r => [r.key, r.value]))
  const changed = [...draft].filter(([key, value]) => props.overrides[key] !== value).length
  return changed + Object.keys(props.overrides).filter(key => !draft.has(key)).length
})
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

const SELECTED_LINES_KEY = 'calcpad.properties.uiOverridesSelectedLinesOnly'
const selectedLinesOnly = ref(false)
try {
  selectedLinesOnly.value = localStorage.getItem(SELECTED_LINES_KEY) === 'true'
} catch {
  // ignore unavailable storage
}
watch(selectedLinesOnly, value => {
  try {
    localStorage.setItem(SELECTED_LINES_KEY, String(value))
  } catch {
    // ignore unavailable storage
  }
})

const selectionLabel = computed(() => {
  const s = props.selection
  if (!s) return ''
  return s.start === s.end ? `line ${s.start}` : `lines ${s.start}–${s.end}`
})

const matchesSearch = (row: UiOverrideRow) =>
  !props.filter || row.key.toLowerCase().includes(props.filter) || row.value.toLowerCase().includes(props.filter)

const rowMatches = (row: UiOverrideRow) =>
  matchesSearch(row) && (!selectedLinesOnly.value || (!!props.selection && rowInRange(row, props.selection)))

const root = ref<HTMLElement | null>(null)
const flashKeys = ref(new Set<string>())
// Rows can arrive after the reveal, since the document's values are fetched as the tab opens.
let pendingScroll = false
let flashTimer: ReturnType<typeof setTimeout> | null = null

function scrollToFlashed() {
  if (!pendingScroll) return
  const el = Array.from(root.value?.querySelectorAll<HTMLElement>('.list-row') ?? [])
    .find(r => flashKeys.value.has(r.dataset.key ?? '') && r.offsetParent !== null)
  if (!el) return
  pendingScroll = false
  const reduceMotion = window.matchMedia?.('(prefers-reduced-motion: reduce)').matches
  el.scrollIntoView({ block: 'center', behavior: reduceMotion ? 'auto' : 'smooth' })
}

watch(() => props.reveal?.seq, seq => {
  if (seq === undefined || !props.reveal) return
  flashKeys.value = new Set(props.reveal.keys)
  pendingScroll = true
  if (flashTimer) clearTimeout(flashTimer)
  flashTimer = setTimeout(() => {
    flashKeys.value = new Set()
    pendingScroll = false
  }, 2500)
  // After the tab scrolls to the section header.
  setTimeout(scrollToFlashed, 100)
}, { immediate: true })

watch(classified, () => { if (pendingScroll) void nextTick(scrollToFlashed) })

onBeforeUnmount(() => { if (flashTimer) clearTimeout(flashTimer) })

const shownRows = computed(() => classified.value.filter(rowMatches))

// Rows that are the only value a plain #UI line of this file renders with.
const sourceKeys = computed(() => {
  const controls = props.uiControls ?? []
  const overrides = Object.fromEntries(rows.value.map(r => [r.key, r.value]))
  return shownRows.value
    .filter(row => {
      const users = controls.filter(c => effectiveUiOverrideKey(overrides, c.key) === row.key)
      return users.length === 1 && !!users[0].inSource
    })
    .map(row => row.key)
})

function revertToDefaults() {
  const shown = new Set(shownRows.value.map(r => r.key))
  rows.value = rows.value.filter(r => !shown.has(r.key))
}

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

.pending {
  margin: 8px 0 0 0;
  color: var(--vscode-editorWarning-foreground, #cca700);
  font-style: italic;
}

.selection-filter {
  display: flex;
  align-items: center;
  gap: 6px;
  margin: 0 0 10px 0;
  color: var(--vscode-descriptionForeground);
  cursor: pointer;
  user-select: none;
}

.selection-filter input {
  margin: 0;
  cursor: pointer;
}

.selection-lines {
  font-family: var(--vscode-editor-font-family, monospace);
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

.list-row.flash {
  animation: uo-flash 2.5s ease-out;
}

@keyframes uo-flash {
  0%, 40% { background: var(--vscode-editor-findMatchHighlightBackground, rgba(234, 92, 0, 0.33)); }
  100% { background: transparent; }
}

@media (prefers-reduced-motion: reduce) {
  .list-row.flash {
    animation: none;
    background: var(--vscode-editor-findMatchHighlightBackground, rgba(234, 92, 0, 0.33));
  }
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
