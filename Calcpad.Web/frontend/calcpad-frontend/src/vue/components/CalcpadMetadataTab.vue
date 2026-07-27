<template>
  <div class="metadata-tab">
    <div class="metadata-container p-3">
      <h3 class="section-title">Properties</h3>

      <template v-if="block">
        <p v-if="!block.valid" class="warning">
          This comment contains invalid JSON. Applying will replace it with the
          values below.
        </p>

        <div v-if="showDesc" class="field">
          <label>Description</label>
          <textarea
            v-model="model.desc"
            rows="2"
            placeholder="What this definition does"
          ></textarea>
        </div>

        <div v-if="showParams" class="field">
          <label>Parameter types</label>
          <div v-for="(_, i) in model.paramTypes" :key="'pt' + i" class="list-row">
            <select v-model="model.paramTypes[i]">
              <option value="">(none)</option>
              <optgroup v-if="showFunctionTypes" label="Function">
                <option v-for="t in functionTypes" :key="t" :value="t">{{ t }}</option>
              </optgroup>
              <optgroup v-if="showMacroTypes" label="Macro (TokenType)">
                <option v-for="t in macroTypes" :key="t" :value="t">{{ t }}</option>
              </optgroup>
            </select>
            <button class="icon-button" title="Remove" @click="model.paramTypes.splice(i, 1)">✕</button>
          </div>
          <button class="add-button" @click="model.paramTypes.push('')">+ Add type</button>
        </div>

        <div v-if="showParams" class="field">
          <label>Parameter descriptions</label>
          <div v-for="(_, i) in model.paramDesc" :key="'pd' + i" class="list-row">
            <input type="text" v-model="model.paramDesc[i]" placeholder="Description" />
            <button class="icon-button" title="Remove" @click="model.paramDesc.splice(i, 1)">✕</button>
          </div>
          <button class="add-button" @click="model.paramDesc.push('')">+ Add description</button>
        </div>

        <div v-if="showReturnType" class="field">
          <label>Return type</label>
          <select v-model="model.returnType">
            <option value="">(none)</option>
            <option v-for="t in functionTypes" :key="t" :value="t">{{ t }}</option>
          </select>
        </div>

        <div v-if="showSettings" class="field">
          <label>Settings (#settings directive)</label>
          <div v-for="(row, i) in model.settings" :key="'s' + i" class="setting-row">
            <div class="list-row">
              <select v-model="row.key" @change="onSettingKeyChange(row)">
                <option value="">(select)</option>
                <option v-for="s in settingKeys" :key="s.key" :value="s.key" :title="s.detail">{{ s.label }}</option>
              </select>
              <select v-if="settingType(row.key) === 'boolean'" v-model="row.value">
                <option value="true">true</option>
                <option value="false">false</option>
              </select>
              <select v-else-if="settingType(row.key) === 'enum'" v-model="row.value">
                <option v-for="opt in settingOptions(row.key)" :key="opt.value" :value="opt.value">{{ opt.label }}</option>
              </select>
              <input
                v-else
                :type="settingType(row.key) === 'number' ? 'number' : 'text'"
                :class="{ 'input-invalid': !!settingError(row) }"
                :min="settingMin(row.key)"
                :max="settingMax(row.key)"
                :step="settingStep(row.key)"
                v-model="row.value"
              />
              <span v-if="row.key" class="setting-info" :title="settingDetail(row.key)">ⓘ</span>
              <button class="icon-button" title="Remove" @click="model.settings.splice(i, 1)">✕</button>
            </div>
            <div v-if="settingError(row)" class="setting-error">{{ settingError(row) }}</div>
          </div>
          <button class="add-button" @click="model.settings.push({ key: '', value: '' })">+ Add setting</button>
        </div>

        <div v-if="showLint" class="field">
          <label>Lint ignore</label>

          <div class="sub-row">
            <span class="sub-label">Start region</span>
            <select v-model="model.startLintMode">
              <option value="off">Off</option>
              <option value="all">Ignore all</option>
              <option value="specific">Ignore specific…</option>
            </select>
          </div>
          <select
            v-if="model.startLintMode === 'specific'"
            class="code-multiselect"
            multiple
            size="8"
            v-model="model.startLintCodes"
          >
            <option v-for="c in lintCodes" :key="c.code" :value="c.code" :title="c.description">
              {{ c.code }} — {{ c.description }}
            </option>
          </select>

          <template v-if="showEndLint">
            <div class="sub-row">
              <span class="sub-label">End region</span>
              <select v-model="model.endLintMode">
                <option value="off">Off</option>
                <option value="all">End all</option>
                <option value="specific">End specific…</option>
              </select>
            </div>
            <select
              v-if="model.endLintMode === 'specific'"
              class="code-multiselect"
              multiple
              size="8"
              v-model="model.endLintCodes"
            >
              <option v-for="c in lintCodes" :key="c.code" :value="c.code" :title="c.description">
                {{ c.code }} — {{ c.description }}
              </option>
            </select>
          </template>
        </div>

        <div v-if="addableFields.length" class="field add-field">
          <label>Add field</label>
          <div class="sub-row">
            <select
              class="add-field-select"
              @change="addField(($event.target as HTMLSelectElement).value); ($event.target as HTMLSelectElement).value = ''"
            >
              <option value="">(select a field to add…)</option>
              <option v-for="f in addableFields" :key="f.id" :value="f.id">{{ f.label }}</option>
            </select>
          </div>
        </div>

        <div class="actions">
          <button class="primary-button" :disabled="hasSettingErrors" :title="hasSettingErrors ? 'Fix the highlighted settings before applying' : undefined" @click="onApply">Apply</button>
          <button class="secondary-button" @click="populate">Reset</button>
        </div>
      </template>
    </div>
  </div>
</template>

<script setup lang="ts">
import { reactive, computed, watch } from 'vue'
import {
  FUNCTION_PARAM_TYPES,
  MACRO_PARAM_TYPES,
  METADATA_SETTINGS_KEYS,
  settingSpec,
  validateSettingValue,
  LINT_CODES,
} from '../../text/metadata-comment'
import type { MetadataCommentBlock, MetadataCommentData, MetadataDefKind, SettingsValues, SettingOption } from '../../text/metadata-comment'

interface Props {
  block?: MetadataCommentBlock | null
}
const props = withDefaults(defineProps<Props>(), { block: null })

const emit = defineEmits<{
  'apply': [payload: { data: MetadataCommentData; settings: SettingsValues }]
}>()

const functionTypes = FUNCTION_PARAM_TYPES
const macroTypes = MACRO_PARAM_TYPES
const settingKeys = METADATA_SETTINGS_KEYS
const lintCodes = LINT_CODES

type LintMode = 'off' | 'all' | 'specific'

const KNOWN_KEYS = new Set(['desc', 'paramTypes', 'paramDesc', 'returnType', 'LintIgnore', 'EndLintIgnore'])

interface SettingRow { key: string; value: string }

const model = reactive({
  desc: '',
  paramTypes: [] as string[],
  paramDesc: [] as string[],
  returnType: '',
  settings: [] as SettingRow[],
  startLintMode: 'off' as LintMode,
  startLintCodes: [] as string[],
  endLintMode: 'off' as LintMode,
  endLintCodes: [] as string[],
  extra: {} as Record<string, unknown>,
})

// Fields the user opted into via "Add field" even though the cursor context
// wouldn't offer them (e.g. a description on a generic line). Reset per block.
const added = reactive(new Set<string>())

// When the host provides no context (non-VS Code), show every field.
const noContext = computed(() => !props.block?.context)

const defKind = computed<MetadataDefKind>(() => props.block?.context?.defKind ?? null)

// Description documents a definition, so it's offered on any definition line
// (variable, function, or macro) but hidden on generic lines unless added.
const showDesc = computed(() =>
  noContext.value
  || defKind.value !== null
  || model.desc.trim() !== ''
  || added.has('desc'))

// Parameter fields only apply to functions and macros.
const showParams = computed(() =>
  noContext.value
  || defKind.value === 'function'
  || defKind.value === 'macro'
  || model.paramTypes.length > 0
  || model.paramDesc.length > 0
  || added.has('params'))

// A function takes value/vector/matrix/any; a macro takes TokenType names. When
// the kind is unknown (no context, or user-added on a variable) offer both.
const showFunctionTypes = computed(() => defKind.value !== 'macro')
const showMacroTypes = computed(() => defKind.value !== 'function')

// A return type only applies to custom functions.
const showReturnType = computed(() =>
  noContext.value
  || defKind.value === 'function'
  || model.returnType !== ''
  || added.has('returnType'))

// The End-region control only makes sense inside an open LintIgnore region, or
// when this comment already carries an EndLintIgnore to stay editable.
const showEndLint = computed(() =>
  noContext.value || !!props.block?.context?.insideOpenLintRegion || model.endLintMode !== 'off')

// Settings and lint-ignore aren't tied to a definition, so they're hidden on
// definition lines (where the panel documents the variable/function/macro) and
// only offered on generic lines — or explicitly, via "Add field".
const isDefinition = computed(() => defKind.value !== null)

// Settings drive the document-level #settings directive, not the cursor's
// metadata comment, so the section is always available regardless of context.
const showSettings = computed(() => true)

const showLint = computed(() =>
  noContext.value
  || !isDefinition.value
  || model.startLintMode !== 'off'
  || model.endLintMode !== 'off'
  || !!props.block?.context?.insideOpenLintRegion
  || added.has('lint'))

// On a definition line the panel already shows exactly the fields that apply, so
// "Add field" is only offered for the null case (a comment not attached to a
// variable/function/macro), where the definition-oriented fields are hidden.
const addableFields = computed(() => {
  const out: { id: string; label: string }[] = []
  if (isDefinition.value) return out
  if (!showDesc.value) out.push({ id: 'desc', label: 'Description' })
  if (!showParams.value) out.push({ id: 'params', label: 'Parameter types & descriptions' })
  if (!showReturnType.value) out.push({ id: 'returnType', label: 'Return type' })
  if (!showLint.value) out.push({ id: 'lint', label: 'Lint ignore' })
  return out
})

function addField(id: string) {
  added.add(id)
  if (id === 'params') {
    if (model.paramTypes.length === 0) model.paramTypes.push('')
    if (model.paramDesc.length === 0) model.paramDesc.push('')
  }
}

function settingType(key: string): MetadataSettingKind {
  return METADATA_SETTINGS_KEYS.find(s => s.key === key)?.type ?? 'string'
}
type MetadataSettingKind = 'number' | 'boolean' | 'string' | 'enum'

function settingOptions(key: string): SettingOption[] {
  return METADATA_SETTINGS_KEYS.find(s => s.key === key)?.options ?? []
}

function settingDetail(key: string): string {
  return settingSpec(key)?.detail ?? ''
}

function settingMin(key: string): number | undefined {
  return settingSpec(key)?.min
}

function settingMax(key: string): number | undefined {
  return settingSpec(key)?.max
}

function settingStep(key: string): string | undefined {
  if (settingType(key) !== 'number') return undefined
  return key === 'precision' || key === 'tol' ? 'any' : '1'
}

function settingError(row: SettingRow): string | null {
  return row.key ? validateSettingValue(row.key, row.value) : null
}

const hasSettingErrors = computed(() => model.settings.some(r => !!settingError(r)))

function onSettingKeyChange(row: SettingRow) {
  const def = METADATA_SETTINGS_KEYS.find(s => s.key === row.key)?.def
  row.value = def === undefined ? '' : String(def)
}

function populate() {
  added.clear()
  const data = props.block?.data ?? {}
  model.desc = typeof data.desc === 'string' ? data.desc : ''
  model.paramTypes = Array.isArray(data.paramTypes) ? data.paramTypes.map(String) : []
  model.paramDesc = Array.isArray(data.paramDesc) ? data.paramDesc.map(String) : []
  model.returnType = typeof data.returnType === 'string' ? data.returnType : ''
  const settings = props.block?.settings
  model.settings = settings && typeof settings === 'object'
    ? Object.entries(settings).map(([key, value]) => ({ key, value: String(value) }))
    : []
  ;[model.startLintMode, model.startLintCodes] = readLintField(data.LintIgnore)
  ;[model.endLintMode, model.endLintCodes] = readLintField(data.EndLintIgnore)
  const extra: Record<string, unknown> = {}
  for (const [key, value] of Object.entries(data)) {
    if (!KNOWN_KEYS.has(key)) extra[key] = value
  }
  model.extra = extra

  // Pre-size the parameter rows to the definition's parameter count so the
  // form matches the signature without the user adding rows by hand.
  const paramCount = props.block?.context?.paramCount ?? 0
  if (paramCount > 0) {
    while (model.paramTypes.length < paramCount) model.paramTypes.push('')
    while (model.paramDesc.length < paramCount) model.paramDesc.push('')
  }
}

// An array value maps to 'all' (empty) or 'specific' (codes); absent → 'off'.
function readLintField(value: unknown): [LintMode, string[]] {
  if (!Array.isArray(value)) return ['off', []]
  return value.length === 0 ? ['all', []] : ['specific', value.map(String)]
}

function lintFieldValue(mode: LintMode, codes: string[]): string[] | undefined {
  if (mode === 'off') return undefined
  if (mode === 'all') return []
  return codes.slice()
}

// Vue casts `<input type="number">` v-model to a number, so `value` may arrive
// as a number (or boolean) rather than the string the row nominally holds.
function coerceSetting(key: string, value: string | number | boolean): string | number | boolean {
  const type = settingType(key)
  const str = String(value)
  if (type === 'boolean') return str === 'true'
  if (type === 'number' || type === 'enum') {
    const n = Number(str)
    return Number.isFinite(n) && str.trim() !== '' ? n : str
  }
  return str
}

function onApply() {
  if (hasSettingErrors.value) return
  const data: MetadataCommentData = { ...model.extra }

  if (model.desc.trim()) data.desc = model.desc.trim()

  const types = model.paramTypes.filter(t => t !== '')
  if (types.length) data.paramTypes = types

  const descs = model.paramDesc.filter(d => d.trim() !== '')
  if (descs.length) data.paramDesc = descs

  if (model.returnType) data.returnType = model.returnType

  const settings: SettingsValues = {}
  for (const row of model.settings) {
    if (row.key) settings[row.key] = coerceSetting(row.key, row.value)
  }

  const lintIgnore = lintFieldValue(model.startLintMode, model.startLintCodes)
  if (lintIgnore !== undefined) data.LintIgnore = lintIgnore

  const endLintIgnore = lintFieldValue(model.endLintMode, model.endLintCodes)
  if (endLintIgnore !== undefined) data.EndLintIgnore = endLintIgnore

  emit('apply', { data, settings })
}

// Identity of the target the form is bound to. Cursor jitter within the same
// definition re-pushes an equal block; re-populating then would discard the
// user's unsaved edits, so only repopulate when the target actually changes.
function blockSignature(b: MetadataCommentBlock | null | undefined): string {
  if (!b) return ''
  return [b.line, b.isNew ? 1 : 0, b.rawJson, JSON.stringify(b.settings ?? {}), b.context?.defKind ?? '', b.context?.paramCount ?? ''].join('|')
}

let lastSignature = ''
watch(
  () => props.block,
  (block) => {
    const sig = blockSignature(block)
    if (sig === lastSignature) return
    lastSignature = sig
    populate()
  },
  { immediate: true },
)
</script>

<style scoped>
.metadata-tab {
  height: 100%;
  display: flex;
  flex-direction: column;
}

.metadata-container {
  overflow-y: auto;
  flex: 1;
  padding: 12px;
}

.section-title {
  margin: 0 0 8px 0;
  font-size: 13px;
  font-weight: 600;
  color: var(--vscode-foreground);
}

.section-desc {
  font-size: 12px;
  color: var(--vscode-descriptionForeground);
  margin: 0 0 16px 0;
  line-height: 1.5;
}

.section-desc code {
  background: var(--vscode-textCodeBlock-background);
  padding: 1px 4px;
  border-radius: 2px;
  font-size: 11px;
}

.warning {
  font-size: 12px;
  color: var(--vscode-editorWarning-foreground, #cca700);
  margin: 0 0 12px 0;
  line-height: 1.5;
}

.field {
  margin-bottom: 16px;
}

.field > label {
  display: block;
  font-size: 12px;
  font-weight: 600;
  color: var(--vscode-foreground);
  margin-bottom: 6px;
}

.list-row {
  display: flex;
  align-items: center;
  gap: 6px;
  margin-bottom: 6px;
}

.setting-row {
  margin-bottom: 6px;
}

.setting-row .list-row {
  margin-bottom: 0;
}

.setting-info {
  cursor: help;
  color: var(--vscode-descriptionForeground);
  font-size: 11px;
}

.setting-error {
  color: var(--vscode-errorForeground, #f14c4c);
  font-size: 11px;
  margin-top: 2px;
  margin-left: 2px;
}

.input-invalid {
  border-color: var(--vscode-inputValidation-errorBorder, #f14c4c) !important;
  outline: 1px solid var(--vscode-inputValidation-errorBorder, #f14c4c);
}

.sub-row {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 6px;
}

.sub-label {
  min-width: 80px;
  font-size: 12px;
  color: var(--vscode-foreground);
}

.sub-row select {
  flex: 1;
  min-width: 0;
  background: var(--vscode-input-background);
  color: var(--vscode-input-foreground);
  border: 1px solid var(--vscode-input-border, transparent);
  padding: 4px 6px;
  font-size: 12px;
  font-family: var(--vscode-font-family);
  border-radius: 2px;
}

.code-multiselect {
  width: 100%;
  margin-bottom: 8px;
  background: var(--vscode-input-background);
  color: var(--vscode-input-foreground);
  border: 1px solid var(--vscode-input-border, transparent);
  font-size: 11px;
  font-family: var(--vscode-font-family);
  border-radius: 2px;
  padding: 2px;
}

.code-multiselect option {
  padding: 2px 4px;
}

.field textarea,
.field input[type="text"],
.field input[type="number"],
.field > select,
.list-row input,
.list-row select {
  flex: 1;
  min-width: 0;
  background: var(--vscode-input-background);
  color: var(--vscode-input-foreground);
  border: 1px solid var(--vscode-input-border, transparent);
  padding: 4px 6px;
  font-size: 12px;
  font-family: var(--vscode-font-family);
  border-radius: 2px;
}

.field textarea {
  width: 100%;
  resize: vertical;
}

.icon-button {
  background: transparent;
  border: none;
  color: var(--vscode-descriptionForeground);
  cursor: pointer;
  padding: 2px 4px;
  font-size: 11px;
  border-radius: 2px;
  flex: 0 0 auto;
}

.icon-button:hover {
  background: var(--vscode-toolbar-hoverBackground);
  color: var(--vscode-foreground);
}

.add-button {
  background: transparent;
  border: 1px dashed var(--vscode-input-border, var(--vscode-widget-border));
  color: var(--vscode-foreground);
  cursor: pointer;
  padding: 3px 8px;
  font-size: 11px;
  border-radius: 2px;
}

.add-button:hover {
  background: var(--vscode-toolbar-hoverBackground);
}

.checkbox-label {
  display: flex;
  align-items: center;
  gap: 6px;
  cursor: pointer;
  font-size: 12px;
  color: var(--vscode-foreground);
  margin-top: 8px;
}

.checkbox-label input {
  margin: 0;
}

.actions {
  display: flex;
  gap: 8px;
  margin-top: 8px;
}

.primary-button {
  background: var(--vscode-button-background);
  color: var(--vscode-button-foreground);
  border: none;
  padding: 6px 14px;
  font-size: 12px;
  font-family: var(--vscode-font-family);
  cursor: pointer;
  border-radius: 2px;
}

.primary-button:hover:not(:disabled) {
  background: var(--vscode-button-hoverBackground);
}

.primary-button:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.secondary-button {
  background: var(--vscode-button-secondaryBackground);
  color: var(--vscode-button-secondaryForeground);
  border: none;
  padding: 6px 14px;
  font-size: 12px;
  font-family: var(--vscode-font-family);
  cursor: pointer;
  border-radius: 2px;
}

.secondary-button:hover {
  background: var(--vscode-button-secondaryHoverBackground);
}
</style>
