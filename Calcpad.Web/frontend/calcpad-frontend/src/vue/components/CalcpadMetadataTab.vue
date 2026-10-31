<template>
  <div class="metadata-tab" :class="{ 'search-active': searchActive }">
    <div class="metadata-toolbar">
      <CalcpadSearchField
        v-model="searchQuery"
        class="metadata-search"
        :placeholder="t('Search properties...')"
      />
      <CollapseAllButton v-if="!searchActive" :expand="allCollapsed" @click="setAll(!allCollapsed)" />
    </div>

    <div ref="container" class="metadata-container">
      <section v-show="sectionVisible('fileInfo')" data-anchor="fileInfo" class="metadata-section">
        <h3 class="section-header" @click="toggle('fileInfo')">
          <span class="expand-icon" :class="{ collapsed: isCollapsed('fileInfo') }">&#x25BC;</span>
          <CalcpadSearchHighlight :text="t('File Info')" :query="query" />
          <span v-if="fileInfoDirty" class="dirty-mark" :title="t('Unsaved changes - Apply or Reset')">*</span>
        </h3>
        <div v-show="bodyVisible('fileInfo')" class="section-body">
          <CalcpadFileInfoSection
            :info="fileInfo"
            :has-document="hasDocument"
            :doc-key="docKey"
            :user-name="userName"
            :query="query"
            :row-visible="(key: string) => rowVisible('fileInfo', key)"
            @update="emit('update-file-info', $event)"
            @mark-reviewed="emit('mark-reviewed')"
            @open-settings="emit('open-settings')"
            @draft-dirty="onFileInfoDirty"
          />
        </div>
      </section>

      <section v-show="sectionVisible('uiOverrides')" data-anchor="uiOverrides" class="metadata-section">
        <h3 class="section-header" @click="toggle('uiOverrides')">
          <span class="expand-icon" :class="{ collapsed: isCollapsed('uiOverrides') }">&#x25BC;</span>
          <CalcpadSearchHighlight :text="t('UI Overrides')" :query="query" />
          <span v-if="uiOverridesDirty" class="dirty-mark" :title="t('Unsaved changes - Apply or Reset')">*</span>
        </h3>
        <div v-show="bodyVisible('uiOverrides')" class="section-body">
          <p v-if="sourceUnavailable" class="note">{{ sourceUnavailableNote }}</p>
          <CalcpadUiOverridesSection
            v-else
            :overrides="savedUiOverrides"
            :has-document="hasDocument"
            :doc-key="docKey"
            :ui-controls="uiControls"
            :visible="bodyVisible('uiOverrides')"
            :query="query"
            :filter="uiOverridesFilter"
            @update="emit('update-ui-overrides', $event)"
            @go-to-line="emit('go-to-line', $event)"
            @refresh-ui-controls="emit('refresh-ui-controls')"
            @draft-dirty="onUiOverridesDirty"
          />
        </div>
      </section>

      <section v-show="sectionVisible('fields')" data-anchor="fields" class="metadata-section">
        <h3 class="section-header" @click="toggle('fields')">
          <span class="expand-icon" :class="{ collapsed: isCollapsed('fields') }">&#x25BC;</span>
          <CalcpadSearchHighlight :text="t('Fields')" :query="query" />
          <span v-if="dirty" class="dirty-mark" :title="t('Unsaved changes - Apply or Reset')">*</span>
        </h3>
        <div v-show="bodyVisible('fields')" class="section-body">
          <p v-if="sourceUnavailable" class="note">{{ sourceUnavailableNote }}</p>
          <p v-else-if="!boundBlock" class="note">{{ t('Place the cursor on a line to edit its properties.') }}</p>
          <template v-else>
            <div class="bound-target" :class="kindClass">
              <button
                class="line-chip"
                :title="t('Go to this line')"
                @click="emit('go-to-line', boundBlock.line + 1)"
              >Line {{ boundBlock.line + 1 }}</button>
              <span v-if="targetKind" class="target-kind">{{ targetKind }}</span>
              <span class="target-name" :title="targetName">{{ targetName }}</span>
            </div>

            <p v-if="!boundBlock.valid" class="warning">
              This comment contains invalid JSON. Applying will replace it with the
              values below.
            </p>

            <div v-if="showDesc" v-show="rowVisible('fields', 'desc')" class="field">
              <label><CalcpadSearchHighlight :text="t('Description')" :query="query" /></label>
              <textarea
                v-model="model.desc"
                rows="2"
                :placeholder="t('What this definition does')"
              ></textarea>
            </div>

            <div v-if="showParams" v-show="rowVisible('fields', 'params')" class="field">
              <label><CalcpadSearchHighlight :text="t('Parameter types')" :query="query" /></label>
              <div v-for="(_, i) in model.paramTypes" :key="'pt' + i" class="list-row">
                <select v-model="model.paramTypes[i]">
                  <option value="">(none)</option>
                  <optgroup v-if="showFunctionTypes" :label="t('Function')">
                    <option v-for="t in functionTypes" :key="t" :value="t">{{ t }}</option>
                  </optgroup>
                  <optgroup v-if="showMacroTypes" :label="t('Macro (TokenType)')">
                    <option v-for="t in macroTypes" :key="t" :value="t">{{ t }}</option>
                  </optgroup>
                </select>
                <button class="icon" :title="t('Remove')" @click="model.paramTypes.splice(i, 1)">✕</button>
              </div>
              <button class="tertiary" @click="model.paramTypes.push('')">+ Add type</button>
            </div>

            <div v-if="showParams" v-show="rowVisible('fields', 'params')" class="field">
              <label><CalcpadSearchHighlight :text="t('Parameter descriptions')" :query="query" /></label>
              <div v-for="(_, i) in model.paramDesc" :key="'pd' + i" class="list-row">
                <input type="text" v-model="model.paramDesc[i]" :placeholder="t('Description')" />
                <button class="icon" :title="t('Remove')" @click="model.paramDesc.splice(i, 1)">✕</button>
              </div>
              <button class="tertiary" @click="model.paramDesc.push('')">+ Add description</button>
            </div>

            <div v-if="showReturnType" v-show="rowVisible('fields', 'returnType')" class="field">
              <label><CalcpadSearchHighlight :text="t('Return type')" :query="query" /></label>
              <select v-model="model.returnType">
                <option value="">(none)</option>
                <option v-for="t in functionTypes" :key="t" :value="t">{{ t }}</option>
              </select>
            </div>

            <div v-if="showUi" v-show="rowVisible('fields', 'ui')" class="field">
              <label><CalcpadSearchHighlight text="#UI Control" :query="query" /></label>
              <p v-if="uiBlock && !uiBlock.valid" class="warning">{{ t('This #UI directive contains invalid JSON. Applying will replace it with the values below.') }}</p>

              <div class="sub-row">
                <span class="sub-label">{{ t('Type') }}</span>
                <select v-model="model.ui.type">
                  <option v-for="opt in uiTypeOptions" :key="opt.value" :value="opt.value">{{ opt.label }}</option>
                </select>
              </div>
              <div class="sub-row">
                <span class="sub-label">{{ t('Mode') }}</span>
                <select v-model="model.ui.mode">
                  <option v-for="opt in uiModeOptions" :key="opt.value" :value="opt.value">{{ opt.label }}</option>
                </select>
              </div>
              <div class="sub-row">
                <span class="sub-label">{{ t('Style class') }}</span>
                <input type="text" v-model="model.ui.style" />
              </div>
              <div class="sub-row">
                <span class="sub-label">{{ t('Report style class') }}</span>
                <input type="text" v-model="model.ui.reportStyle" />
              </div>
              <div class="sub-row">
                <span class="sub-label" :title="t('Off puts the unit in the control instead of leaving it in the document.')">{{ t('Keep the unit') }}</span>
                <select v-model="model.ui.forceUnits">
                  <option :value="''">(default: on)</option>
                  <option :value="true">On</option>
                  <option :value="false">{{ t('Off') }}</option>
                </select>
              </div>
              <div class="sub-row">
                <span class="sub-label" :title="t('Accept any expression, written to the document verbatim. Turns \'Keep the unit\' off.')">{{ t('Allow expressions') }}</span>
                <select v-model="model.ui.allowExpression">
                  <option :value="''">(default: off)</option>
                  <option :value="true">On</option>
                  <option :value="false">{{ t('Off') }}</option>
                </select>
              </div>

              <template v-if="model.ui.type === 'datagrid'">
                <div class="sub-row">
                  <span class="sub-label">{{ t('Rows') }}</span>
                  <input type="number" min="0" v-model.number="model.ui.rows" />
                </div>
                <div class="sub-row">
                  <span class="sub-label">{{ t('Columns') }}</span>
                  <input type="number" min="0" v-model.number="model.ui.columns" />
                </div>

                <label>{{ t('Column headers') }}</label>
                <div v-for="(_, i) in model.ui.columnHeaders" :key="'ch' + i" class="list-row">
                  <input type="text" v-model="model.ui.columnHeaders[i]" />
                  <button class="icon" :title="t('Remove')" @click="model.ui.columnHeaders.splice(i, 1)">✕</button>
                </div>
                <button class="tertiary" @click="model.ui.columnHeaders.push('')">+ Add column header</button>

                <label>{{ t('Row headers') }}</label>
                <div v-for="(_, i) in model.ui.rowHeaders" :key="'rh' + i" class="list-row">
                  <input type="text" v-model="model.ui.rowHeaders[i]" />
                  <button class="icon" :title="t('Remove')" @click="model.ui.rowHeaders.splice(i, 1)">✕</button>
                </div>
                <button class="tertiary" @click="model.ui.rowHeaders.push('')">+ Add row header</button>

                <div class="sub-row">
                  <span class="sub-label" :title="t('Pixels, or a percentage of the line such as 75%. Natural width when empty.')">{{ t('Grid width') }}</span>
                  <input type="text" placeholder="100%" v-model="model.ui.width" />
                </div>
                <div class="sub-row">
                  <span class="sub-label">{{ t('Row header width') }}</span>
                  <input type="number" min="1" v-model.number="model.ui.rowHeaderWidth" />
                </div>

                <label>{{ t('Column widths') }}</label>
                <div v-for="(_, i) in model.ui.columnWidths" :key="'cw' + i" class="list-row">
                  <input type="number" min="1" v-model.number="model.ui.columnWidths[i]" />
                  <button class="icon" :title="t('Remove')" @click="model.ui.columnWidths.splice(i, 1)">✕</button>
                </div>
                <button class="tertiary" @click="model.ui.columnWidths.push('')">+ Add column width</button>
              </template>

              <template v-if="model.ui.type === 'dropdown' || model.ui.type === 'radio'">
                <label>Options (key / value)</label>
                <div v-for="(_, i) in model.ui.keys" :key="'kv' + i" class="list-row">
                  <input type="text" placeholder="Key" v-model="model.ui.keys[i]" />
                  <input type="text" placeholder="Value" v-model="model.ui.values[i]" />
                  <button class="icon" title="Remove" @click="removeUiOption(i)">✕</button>
                </div>
                <button class="tertiary" @click="model.ui.keys.push(''); model.ui.values.push('')">+ Add option</button>
                <div v-if="uiKeysValuesError" class="setting-error">{{ uiKeysValuesError }}</div>
              </template>
            </div>

            <div v-if="addableFields.length" v-show="addFieldVisible('fields', addableFields)" class="field add-field">
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
          </template>
        </div>
      </section>

      <section v-show="sectionVisible('settings')" data-anchor="settings" class="metadata-section">
        <h3 class="section-header" @click="toggle('settings')">
          <span class="expand-icon" :class="{ collapsed: isCollapsed('settings') }">&#x25BC;</span>
          <CalcpadSearchHighlight text="Settings" :query="query" />
          <span v-if="dirty" class="dirty-mark" title="Unsaved changes - Apply or Reset">*</span>
        </h3>
        <div v-show="bodyVisible('settings')" class="section-body">
          <p v-if="sourceUnavailable" class="note">{{ sourceUnavailableNote }}</p>
          <p v-else-if="!boundBlock" class="note">Place the cursor on a line to edit its settings.</p>
          <template v-else>
            <div v-if="showPdf" v-show="rowVisible('settings', 'pdf')" data-anchor="pdf" class="field">
              <label><CalcpadSearchHighlight text="PDF export" :query="query" /></label>
              <p class="section-desc">
                Applies to the whole document when it is exported to PDF, overriding the
                defaults on the Settings tab key by key.
              </p>
              <div v-for="(row, i) in model.pdf" :key="'p' + i" class="setting-row">
                <div class="list-row">
                  <select v-model="row.key" @change="pdf.seedDefault(row)">
                    <option value="">(select)</option>
                    <option v-for="s in pdfKeys" :key="s.key" :value="s.key" :title="s.detail">{{ s.label }}</option>
                  </select>
                  <select v-if="pdf.type(row.key) === 'boolean'" v-model="row.value">
                    <option value="true">true</option>
                    <option value="false">false</option>
                  </select>
                  <select v-else-if="pdf.type(row.key) === 'enum'" v-model="row.value">
                    <option v-for="opt in pdf.options(row.key)" :key="opt.value" :value="opt.value">{{ opt.label }}</option>
                  </select>
                  <input
                    v-else
                    :type="pdf.type(row.key) === 'number' ? 'number' : 'text'"
                    :class="{ 'input-invalid': !!pdf.error(row) }"
                    :min="pdf.min(row.key)"
                    :max="pdf.max(row.key)"
                    :step="pdf.step(row.key)"
                    v-model="row.value"
                  />
                  <span v-if="row.key" class="setting-info" :title="pdf.detail(row.key)">ⓘ</span>
                  <button class="icon" title="Remove" @click="model.pdf.splice(i, 1)">✕</button>
                </div>
                <div v-if="pdf.error(row)" class="setting-error">{{ pdf.error(row) }}</div>
              </div>
              <button class="tertiary" @click="model.pdf.push({ key: '', value: '' })">+ Add PDF setting</button>
            </div>

            <div v-if="showSettings" v-show="rowVisible('settings', 'settingsDirective')" data-anchor="settingsDirective" class="field">
              <label><CalcpadSearchHighlight text="Settings (#settings directive)" :query="query" /></label>
              <div v-for="(row, i) in model.settings" :key="'s' + i" class="setting-row">
                <div class="list-row">
                  <select v-model="row.key" @change="setting.seedDefault(row)">
                    <option value="">(select)</option>
                    <option v-for="s in settingKeys" :key="s.key" :value="s.key" :title="s.detail">{{ s.label }}</option>
                  </select>
                  <select v-if="setting.type(row.key) === 'boolean'" v-model="row.value">
                    <option value="true">true</option>
                    <option value="false">false</option>
                  </select>
                  <select v-else-if="setting.type(row.key) === 'enum'" v-model="row.value">
                    <option v-for="opt in setting.options(row.key)" :key="opt.value" :value="opt.value">{{ opt.label }}</option>
                  </select>
                  <input
                    v-else
                    :type="setting.type(row.key) === 'number' ? 'number' : 'text'"
                    :class="{ 'input-invalid': !!setting.error(row) }"
                    :min="setting.min(row.key)"
                    :max="setting.max(row.key)"
                    :step="setting.step(row.key)"
                    v-model="row.value"
                  />
                  <span v-if="row.key" class="setting-info" :title="setting.detail(row.key)">ⓘ</span>
                  <button class="icon" title="Remove" @click="model.settings.splice(i, 1)">✕</button>
                </div>
                <div v-if="setting.error(row)" class="setting-error">{{ setting.error(row) }}</div>
              </div>
              <button class="tertiary" @click="model.settings.push({ key: '', value: '' })">+ Add setting</button>
            </div>

            <div v-if="showLint" v-show="rowVisible('settings', 'lint')" data-anchor="lint" class="field">
              <label><CalcpadSearchHighlight text="Lint ignore" :query="query" /></label>

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

            <div v-if="addableSettings.length" v-show="addFieldVisible('settings', addableSettings)" class="field add-field">
              <div class="sub-row">
                <select
                  class="add-field-select"
                  @change="addField(($event.target as HTMLSelectElement).value); ($event.target as HTMLSelectElement).value = ''"
                >
                  <option value="">(select a field to add…)</option>
                  <option v-for="f in addableSettings" :key="f.id" :value="f.id">{{ f.label }}</option>
                </select>
              </div>
            </div>
          </template>
        </div>
      </section>

      <div v-if="searchActive && !anyVisible" class="no-results">
        No properties found for "{{ searchQuery.trim() }}"
      </div>

      <div v-if="boundBlock && !sourceUnavailable && ((sectionVisible('fields') && bodyVisible('fields')) || (sectionVisible('settings') && bodyVisible('settings')))" class="actions line-actions">
        <button :disabled="hasErrors" :title="hasErrors ? 'Fix the highlighted fields before applying' : `Apply the Fields and Settings to line ${boundBlock.line + 1}`" @click="onApply">Apply</button>
        <button class="secondary" @click="clearDraft">Reset</button>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { reactive, ref, computed, watch, nextTick, onBeforeUnmount } from 'vue'
import {
  FUNCTION_PARAM_TYPES,
  MACRO_PARAM_TYPES,
  LINT_CODES,
} from '../../text/metadata-comment'
import type { MetadataCommentBlock, MetadataCommentData, MetadataDefKind, PdfCommentValues, SettingsValues } from '../../text/metadata-comment'
import { METADATA_SETTINGS_KEYS } from '../../types/settings'
import { PDF_SETTING_KEYS } from '../../types/pdf-settings'
import { specForKey, validateCatalogValue } from '../../types/catalog'
import type { MetadataSettingKey } from '../../types/catalog'
import { UI_PROPERTY_KEYS } from '../../text/ui-directive'
import { FILE_INFO_KEY, UI_OVERRIDES_KEY } from '../../services/metadata-footer'
import type { UiDirectiveData } from '../../text/ui-directive'
import { getMetadataDraft, setMetadataDraft, clearMetadataDraft, metadataDraftDiscarded } from '../metadata-drafts'
import type { MetadataDraft } from '../metadata-drafts'
import type { UiControl, UiOverrides } from '../../services/ui-overrides'
import type { FileInfo } from '../../services/file-info'
import CollapseAllButton from './CollapseAllButton.vue'
import CalcpadSearchField from './CalcpadSearchField.vue'
import CalcpadSearchHighlight from './CalcpadSearchHighlight.vue'
import CalcpadFileInfoSection from './CalcpadFileInfoSection.vue'
import CalcpadUiOverridesSection from './CalcpadUiOverridesSection.vue'

const { t } = useI18n()

interface Props {
  block?: MetadataCommentBlock | null
  /** Controls of the last input-form render, or null until the host resolves them. */
  uiControls?: UiControl[] | null
  fileInfo?: FileInfo | null
  savedUiOverrides?: UiOverrides
  hasDocument?: boolean
  /** Document the file info and saved #UI values came from. */
  docKey?: string
  userName?: string
  /** Input mode: only File Info stays editable. */
  sourceUnavailable?: boolean
  sourceUnavailableNote?: string
}
const props = withDefaults(defineProps<Props>(), {
  block: null,
  uiControls: null,
  fileInfo: null,
  savedUiOverrides: () => ({}),
  hasDocument: false,
  docKey: '',
  userName: '',
  sourceUnavailable: false,
  sourceUnavailableNote: '',
})

const emit = defineEmits<{
  'apply': [payload: { data: MetadataCommentData; settings: SettingsValues; ui?: UiDirectiveData; block: MetadataCommentBlock }]
  'go-to-line': [line: number]
  'refresh-ui-controls': []
  'draft-dirty': [payload: { docKey: string; dirty: boolean }]
  'update-file-info': [patch: Partial<FileInfo>]
  'mark-reviewed': []
  'open-settings': []
  'file-info-draft-dirty': [payload: { docKey: string; dirty: boolean }]
  'update-ui-overrides': [overrides: UiOverrides]
  'ui-overrides-draft-dirty': [payload: { docKey: string; dirty: boolean }]
}>()

const SECTIONS = ['fileInfo', 'uiOverrides', 'fields', 'settings']

const searchQuery = ref('')
const query = computed(() => searchQuery.value.trim().toLowerCase())
const searchActive = computed(() => query.value.length > 0)

const keyLabels = (keys: { key: string; label: string }[]) => keys.map(k => `${k.key} ${k.label}`).join(' ')

// Section titles and per-row search keywords, as in the Settings tab.
const SECTION_META: Record<string, { title: string; rows: Record<string, string> }> = {
  fileInfo: {
    title: 'File Info',
    rows: {
      title: 'title pdf header name',
      project: 'project',
      details: 'author created last updated by program version date',
      review: 'review reviewed reviewer set as reviewed clear',
    },
  },
  uiOverrides: {
    title: 'UI Overrides',
    rows: { values: 'saved #ui values input form entered purge unused' },
  },
  fields: {
    title: 'Fields',
    rows: {
      desc: 'description desc documentation',
      params: 'parameter types descriptions paramtypes paramdesc arguments',
      returnType: 'return type returntype',
      ui: '#ui control type mode style class report keep unit allow expressions dropdown radio checkbox datagrid entry options rows columns headers widths',
      addField: 'add field',
    },
  },
  settings: {
    title: 'Settings',
    rows: {
      pdf: `pdf export ${keyLabels(PDF_SETTING_KEYS)}`.toLowerCase(),
      settingsDirective: `#settings directive ${keyLabels(METADATA_SETTINGS_KEYS)}`.toLowerCase(),
      lint: 'lint ignore lintignore endlintignore diagnostics region codes',
      addField: 'add field',
    },
  },
}

const titleMatches = (id: string) => SECTION_META[id].title.toLowerCase().includes(query.value)

const rowVisible = (id: string, key: string) =>
  !searchActive.value || titleMatches(id) || (SECTION_META[id].rows[key] || '').includes(query.value)

// Saved override names and values are searchable too.
const overridesMatch = computed(() => Object.entries(props.savedUiOverrides)
  .some(([k, v]) => k.toLowerCase().includes(query.value) || v.toLowerCase().includes(query.value)))

const sectionVisible = (id: string) => {
  if (!searchActive.value || titleMatches(id)) return true
  if (id === 'uiOverrides' && overridesMatch.value) return true
  return Object.values(SECTION_META[id].rows).some(r => r.includes(query.value))
}

// A field the cursor's line doesn't show is still reachable through "add field".
const addFieldVisible = (id: string, options: { label: string }[]) =>
  rowVisible(id, 'addField') || options.some(o => o.label.toLowerCase().includes(query.value))

const anyVisible = computed(() => SECTIONS.some(id => sectionVisible(id)))

// Narrows the override rows unless the search matched the section itself.
const uiOverridesFilter = computed(() =>
  !searchActive.value || titleMatches('uiOverrides') || rowVisible('uiOverrides', 'values') ? '' : query.value)
const COLLAPSED_KEY = 'calcpad.properties.collapsed'
const collapsed = reactive<Record<string, boolean>>({})
try {
  Object.assign(collapsed, JSON.parse(localStorage.getItem(COLLAPSED_KEY) || '{}'))
} catch {
  // ignore unavailable/corrupt storage
}
watch(collapsed, () => {
  try {
    localStorage.setItem(COLLAPSED_KEY, JSON.stringify(collapsed))
  } catch {
    // ignore unavailable storage
  }
}, { deep: true })

const isCollapsed = (id: string) => !!collapsed[id]
const toggle = (id: string) => { collapsed[id] = !collapsed[id] }
const bodyVisible = (id: string) => searchActive.value || !collapsed[id]
const setAll = (value: boolean) => { for (const id of SECTIONS) collapsed[id] = value }
const allCollapsed = computed(() => SECTIONS.every(id => collapsed[id]))

// Each section's draft reports itself; only the current document's flag shows in its header.
const fileInfoDirtyByDoc = reactive<Record<string, boolean>>({})
const uiOverridesDirtyByDoc = reactive<Record<string, boolean>>({})
const fileInfoDirty = computed(() => !!fileInfoDirtyByDoc[props.docKey])
const uiOverridesDirty = computed(() => !!uiOverridesDirtyByDoc[props.docKey])

function onFileInfoDirty(payload: { docKey: string; dirty: boolean }) {
  fileInfoDirtyByDoc[payload.docKey] = payload.dirty
  emit('file-info-draft-dirty', payload)
}

function onUiOverridesDirty(payload: { docKey: string; dirty: boolean }) {
  uiOverridesDirtyByDoc[payload.docKey] = payload.dirty
  emit('ui-overrides-draft-dirty', payload)
}

const container = ref<HTMLElement | null>(null)

interface RevealTarget { section: string; anchor: string }

/** The section a cursor target is edited in, or null for a plain line. */
function revealTargetFor(b: MetadataCommentBlock | null): RevealTarget | null {
  if (!b) return null
  const data = b.isNew ? {} : b.data ?? {}
  if (FILE_INFO_KEY in data) return { section: 'fileInfo', anchor: 'fileInfo' }
  if (UI_OVERRIDES_KEY in data) return { section: 'uiOverrides', anchor: 'uiOverrides' }
  if (b.settingsLine != null) return { section: 'settings', anchor: 'settingsDirective' }
  if (data.pdf) return { section: 'settings', anchor: 'pdf' }
  if (data.LintIgnore || data.EndLintIgnore) return { section: 'settings', anchor: 'lint' }
  if (b.uiDirective || b.context?.defName) return { section: 'fields', anchor: 'fields' }
  return null
}

function reveal(target: RevealTarget) {
  collapsed[target.section] = false
  void nextTick(() => container.value
    ?.querySelector(`[data-anchor="${target.anchor}"]`)
    ?.scrollIntoView({ block: 'start', behavior: 'smooth' }))
}

/** The block the form is editing - not the one under the cursor while edits are held. */
const boundBlock = ref<MetadataCommentBlock | null>(null)

const functionTypes = FUNCTION_PARAM_TYPES
const macroTypes = MACRO_PARAM_TYPES
const settingKeys = METADATA_SETTINGS_KEYS
const pdfKeys = PDF_SETTING_KEYS
const lintCodes = LINT_CODES

type LintMode = 'off' | 'all' | 'specific'

const KNOWN_KEYS = new Set(['desc', 'paramTypes', 'paramDesc', 'returnType', 'LintIgnore', 'EndLintIgnore', 'pdf'])

interface SettingRow { key: string; value: string }

const model = reactive({
  desc: '',
  paramTypes: [] as string[],
  paramDesc: [] as string[],
  returnType: '',
  settings: [] as SettingRow[],
  pdf: [] as SettingRow[],
  startLintMode: 'off' as LintMode,
  startLintCodes: [] as string[],
  endLintMode: 'off' as LintMode,
  endLintCodes: [] as string[],
  extra: {} as Record<string, unknown>,
  ui: {
    type: '',
    mode: '',
    style: '',
    reportStyle: '',
    forceUnits: '' as boolean | '',
    allowExpression: '' as boolean | '',
    rows: '' as number | '',
    columns: '' as number | '',
    columnHeaders: [] as string[],
    rowHeaders: [] as string[],
    width: '' as string,
    rowHeaderWidth: '' as number | '',
    columnWidths: [] as (number | '')[],
    keys: [] as string[],
    values: [] as string[],
  },
})

// Fields the user opted into via "Add field" even though the cursor context
// wouldn't offer them (e.g. a description on a generic line). Reset per block.
const added = reactive(new Set<string>())

// When the host provides no context (non-VS Code), show every field.
const noContext = computed(() => !boundBlock.value?.context)

const defKind = computed<MetadataDefKind>(() => boundBlock.value?.context?.defKind ?? null)

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
  noContext.value || !!boundBlock.value?.context?.insideOpenLintRegion || model.endLintMode !== 'off')

// Settings and lint-ignore aren't tied to a definition, so they're hidden on
// definition lines (where the panel documents the variable/function/macro) and
// only offered on generic lines — or explicitly, via "Add field".
const isDefinition = computed(() => defKind.value !== null)

// Settings drive the document-level #settings directive, not the cursor's
// metadata comment, so the section is always available regardless of context.
const showSettings = computed(() => true)

// The #UI section only makes sense when the cursor sits on an actual #UI
// line — unlike the comment/settings sections, there's nothing to synthesize
// (a #UI line requires a pre-existing variable assignment).
const uiBlock = computed(() => boundBlock.value?.uiDirective ?? null)
const showUi = computed(() => !!uiBlock.value)

const uiTypeOptions = specForKey(UI_PROPERTY_KEYS, 'type')?.options ?? []
const uiModeOptions = specForKey(UI_PROPERTY_KEYS, 'mode')?.options ?? []

const uiKeysValuesError = computed(() => {
  if (!showUi.value) return null
  if (model.ui.type !== 'dropdown' && model.ui.type !== 'radio') return null
  return model.ui.keys.length === model.ui.values.length
    ? null
    : 'Keys and values must have the same number of entries'
})
const hasUiErrors = computed(() => !!uiKeysValuesError.value)

function removeUiOption(i: number) {
  model.ui.keys.splice(i, 1)
  model.ui.values.splice(i, 1)
}

const showLint = computed(() =>
  noContext.value
  || !isDefinition.value
  || model.startLintMode !== 'off'
  || model.endLintMode !== 'off'
  || !!boundBlock.value?.context?.insideOpenLintRegion
  || added.has('lint'))

// PDF settings configure the whole export, not the definition below the comment,
// so they're offered on generic lines (where a document-level comment naturally
// lives) and stay hidden on definition comments unless already present or added.
const showPdf = computed(() =>
  noContext.value
  || !isDefinition.value
  || model.pdf.length > 0
  || added.has('pdf'))

// On a definition line the panel already shows exactly the fields that apply, so
// "Add field" is only offered for the null case (a comment not attached to a
// variable/function/macro), where the definition-oriented fields are hidden.
const addableFields = computed(() => {
  const out: { id: string; label: string }[] = []
  if (isDefinition.value) return out
  if (!showDesc.value) out.push({ id: 'desc', label: 'Description' })
  if (!showParams.value) out.push({ id: 'params', label: 'Parameter types & descriptions' })
  if (!showReturnType.value) out.push({ id: 'returnType', label: 'Return type' })
  return out
})

const addableSettings = computed(() => {
  const out: { id: string; label: string }[] = []
  if (isDefinition.value) return out
  if (!showPdf.value) out.push({ id: 'pdf', label: 'PDF export' })
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

type MetadataSettingKind = 'number' | 'boolean' | 'string' | 'enum'

/**
 * The per-key lookups a key/value row section needs, bound to one catalog. The
 * `#settings` and `pdf` sections render the same row UI over different catalogs,
 * so binding once here keeps a single copy of the logic behind two names.
 */
function catalogHelpers(catalog: MetadataSettingKey[]) {
  const type = (key: string): MetadataSettingKind => specForKey(catalog, key)?.type ?? 'string'

  // Vue casts `<input type="number">` v-model to a number, so `value` may arrive
  // as a number (or boolean) rather than the string the row nominally holds.
  const coerce = (key: string, value: string | number | boolean): string | number | boolean => {
    const str = String(value)
    if (type(key) === 'boolean') return str === 'true'
    if (type(key) === 'number' || type(key) === 'enum') {
      const n = Number(str)
      return Number.isFinite(n) && str.trim() !== '' ? n : str
    }
    return str
  }

  return {
    type,
    coerce,
    options: (key: string) => specForKey(catalog, key)?.options ?? [],
    detail: (key: string) => specForKey(catalog, key)?.detail ?? '',
    min: (key: string) => specForKey(catalog, key)?.min,
    max: (key: string) => specForKey(catalog, key)?.max,
    step: (key: string): string | undefined => {
      if (type(key) !== 'number') return undefined
      return key === 'precision' || key === 'tol' ? 'any' : '1'
    },
    error: (row: SettingRow) => row.key ? validateCatalogValue(catalog, row.key, row.value) : null,
    /** Seed a freshly picked key with its default, so the row is never left blank. */
    seedDefault: (row: SettingRow) => {
      const def = specForKey(catalog, row.key)?.def
      row.value = def === undefined ? '' : String(def)
    },
    /** Collapse the rows into the JSON object the comment/directive carries. */
    collect: (rows: SettingRow[]): Record<string, string | number | boolean> => {
      const out: Record<string, string | number | boolean> = {}
      for (const row of rows)
        if (row.key) out[row.key] = coerce(row.key, row.value)
      return out
    },
  }
}

const setting = catalogHelpers(METADATA_SETTINGS_KEYS)
const pdf = catalogHelpers(PDF_SETTING_KEYS)

const hasSettingErrors = computed(() => model.settings.some(r => !!setting.error(r)))
const hasPdfErrors = computed(() => model.pdf.some(r => !!pdf.error(r)))
const hasErrors = computed(() => hasSettingErrors.value || hasPdfErrors.value || hasUiErrors.value)

/** Everything the user can change; compared as text to tell edited from untouched. */
function snapshot(): string {
  return JSON.stringify({ model, added: [...added] })
}

const baseline = ref('')
const dirty = computed(() => snapshot() !== baseline.value)

const KIND_LABELS: Record<string, string> = {
  variable: 'Variable',
  function: 'Function',
  macro: 'Macro',
}

// Only set on a definition; a bare #settings/#UI line has no kind to name.
const targetKind = computed(() => {
  const ctx = boundBlock.value?.context
  return ctx?.defName && ctx.defKind ? KIND_LABELS[ctx.defKind] : ''
})

const kindClass = computed(() => targetKind.value ? `kind-${defKind.value}` : '')

const targetName = computed(() => {
  const block = boundBlock.value
  if (block?.context?.defName) return block.context.defName
  if (block?.uiDirective) return '#UI'
  if (block?.settingsLine != null) return '#settings'
  return 'Document'
})

function reportDirty(docKey: string | undefined, value: boolean) {
  if (docKey) emit('draft-dirty', { docKey, dirty: value })
}

watch(dirty, value => reportDirty(boundBlock.value?.docKey, value))

/** Bind the form to a freshly pushed block, discarding nothing (the form is clean). */
function bind(block: MetadataCommentBlock | null) {
  boundBlock.value = block
  lastSignature = blockSignature(block)
  populate()
  reportDirty(block?.docKey, false)
}

function restoreDraft(draft: MetadataDraft) {
  const state = JSON.parse(draft.state)
  Object.assign(model, state.model)
  added.clear()
  for (const id of state.added as string[]) added.add(id)
  boundBlock.value = draft.block
  baseline.value = draft.baseline
  lastSignature = blockSignature(draft.block)
  reportDirty(draft.block.docKey, true)
}

/** Park the current edits under the bound document, or drop a draft nothing changed. */
function stash() {
  const block = boundBlock.value
  const key = block?.docKey
  if (!key) return
  const isDirty = dirty.value
  if (isDirty) setMetadataDraft(key, { block, state: snapshot(), baseline: baseline.value })
  else clearMetadataDraft(key)
  reportDirty(key, isDirty)
}

/** Reset drops the draft and hands the form to the current cursor position. */
function clearDraft() {
  const key = boundBlock.value?.docKey
  if (key) clearMetadataDraft(key)
  bind(props.block)
}

/** Apply keeps the form as-is: it already holds what was written. */
function acceptDraft() {
  const key = boundBlock.value?.docKey
  if (key) clearMetadataDraft(key)
  baseline.value = snapshot()
}

// The host drops a document's draft when its file closes.
watch(metadataDraftDiscarded, ({ docKey }) => {
  if (docKey && docKey === boundBlock.value?.docKey) bind(props.block)
})

onBeforeUnmount(stash)

function populate() {
  added.clear()
  const data = boundBlock.value?.data ?? {}
  model.desc = typeof data.desc === 'string' ? data.desc : ''
  model.paramTypes = Array.isArray(data.paramTypes) ? data.paramTypes.map(String) : []
  model.paramDesc = Array.isArray(data.paramDesc) ? data.paramDesc.map(String) : []
  model.returnType = typeof data.returnType === 'string' ? data.returnType : ''
  const settings = boundBlock.value?.settings
  model.settings = settings && typeof settings === 'object'
    ? Object.entries(settings).map(([key, value]) => ({ key, value: String(value) }))
    : []
  model.pdf = data.pdf && typeof data.pdf === 'object' && !Array.isArray(data.pdf)
    ? Object.entries(data.pdf).map(([key, value]) => ({ key, value: String(value) }))
    : []
  ;[model.startLintMode, model.startLintCodes] = readLintField(data.LintIgnore)
  ;[model.endLintMode, model.endLintCodes] = readLintField(data.EndLintIgnore)
  const extra: Record<string, unknown> = {}
  for (const [key, value] of Object.entries(data)) {
    if (!KNOWN_KEYS.has(key)) extra[key] = value
  }
  model.extra = extra

  const uiData = uiBlock.value?.data ?? {}
  model.ui.type = typeof uiData.type === 'string' ? uiData.type : ''
  model.ui.mode = typeof uiData.mode === 'string' ? uiData.mode : ''
  model.ui.style = typeof uiData.style === 'string' ? uiData.style : ''
  model.ui.reportStyle = typeof uiData.reportStyle === 'string' ? uiData.reportStyle : ''
  model.ui.forceUnits = typeof uiData.forceUnits === 'boolean' ? uiData.forceUnits : ''
  model.ui.allowExpression = typeof uiData.allowExpression === 'boolean' ? uiData.allowExpression : ''
  model.ui.rows = typeof uiData.rows === 'number' ? uiData.rows : ''
  model.ui.columns = typeof uiData.columns === 'number' ? uiData.columns : ''
  model.ui.columnHeaders = Array.isArray(uiData.columnHeaders) ? uiData.columnHeaders.map(String) : []
  model.ui.rowHeaders = Array.isArray(uiData.rowHeaders) ? uiData.rowHeaders.map(String) : []
  model.ui.width = uiData.width === undefined || uiData.width === null ? '' : String(uiData.width)
  model.ui.rowHeaderWidth = typeof uiData.rowHeaderWidth === 'number' ? uiData.rowHeaderWidth : ''
  model.ui.columnWidths = Array.isArray(uiData.columnWidths) ? uiData.columnWidths.map(Number) : []
  model.ui.keys = Array.isArray(uiData.keys) ? uiData.keys.map(String) : []
  model.ui.values = Array.isArray(uiData.values) ? uiData.values.map(String) : []

  // Pre-size the parameter rows to the definition's parameter count so the
  // form matches the signature without the user adding rows by hand.
  const paramCount = boundBlock.value?.context?.paramCount ?? 0
  if (paramCount > 0) {
    while (model.paramTypes.length < paramCount) model.paramTypes.push('')
    while (model.paramDesc.length < paramCount) model.paramDesc.push('')
  }

  baseline.value = snapshot()
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

function onApply() {
  if (hasSettingErrors.value || hasPdfErrors.value || hasUiErrors.value) return
  const data: MetadataCommentData = { ...model.extra }

  if (model.desc.trim()) data.desc = model.desc.trim()

  const types = model.paramTypes.filter(t => t !== '')
  if (types.length) data.paramTypes = types

  const descs = model.paramDesc.filter(d => d.trim() !== '')
  if (descs.length) data.paramDesc = descs

  if (model.returnType) data.returnType = model.returnType

  const settings: SettingsValues = setting.collect(model.settings)

  const pdfValues = pdf.collect(model.pdf)
  // cleanMetadata drops an empty object, so clearing every row removes the key.
  if (Object.keys(pdfValues).length) data.pdf = pdfValues as PdfCommentValues

  const lintIgnore = lintFieldValue(model.startLintMode, model.startLintCodes)
  if (lintIgnore !== undefined) data.LintIgnore = lintIgnore

  const endLintIgnore = lintFieldValue(model.endLintMode, model.endLintCodes)
  if (endLintIgnore !== undefined) data.EndLintIgnore = endLintIgnore

  let ui: UiDirectiveData | undefined
  if (showUi.value) {
    ui = {}
    if (model.ui.type) ui.type = model.ui.type
    if (model.ui.mode) ui.mode = model.ui.mode
    if (model.ui.style.trim()) ui.style = model.ui.style.trim()
    if (model.ui.reportStyle.trim()) ui.reportStyle = model.ui.reportStyle.trim()
    if (model.ui.forceUnits !== '') ui.forceUnits = model.ui.forceUnits
    if (model.ui.allowExpression !== '') ui.allowExpression = model.ui.allowExpression
    // The directive takes these on a grid only, and the fields above are hidden for the other
    // types - an undeclared type is auto-detected, so whatever it carries is left alone.
    if (!['entry', 'dropdown', 'radio', 'checkbox'].includes(model.ui.type)) {
      if (model.ui.rows !== '') ui.rows = Number(model.ui.rows)
      if (model.ui.columns !== '') ui.columns = Number(model.ui.columns)
      const columnHeaders = model.ui.columnHeaders.filter(h => h.trim() !== '')
      if (columnHeaders.length) ui.columnHeaders = columnHeaders
      const rowHeaders = model.ui.rowHeaders.filter(h => h.trim() !== '')
      if (rowHeaders.length) ui.rowHeaders = rowHeaders
      const width = model.ui.width.trim()
      // A plain number stays a number; a percentage goes through as a string.
      if (width) ui.width = /^\d+(\.\d+)?$/.test(width) ? Number(width) : width
      if (model.ui.rowHeaderWidth !== '') ui.rowHeaderWidth = Number(model.ui.rowHeaderWidth)
      const columnWidths = model.ui.columnWidths.filter(w => w !== '').map(Number)
      if (columnWidths.length) ui.columnWidths = columnWidths
    }
    if (model.ui.keys.length) {
      ui.keys = model.ui.keys.slice()
      ui.values = model.ui.values.slice()
    }
  }

  emit('apply', { data, settings, ui, block: boundBlock.value! })
  acceptDraft()
}

// Identity of the target the form is bound to. Cursor jitter within the same
// definition re-pushes an equal block; re-populating then would discard the
// user's unsaved edits, so only repopulate when the target actually changes.
function blockSignature(b: MetadataCommentBlock | null | undefined): string {
  if (!b) return ''
  return [
    b.line, b.isNew ? 1 : 0, b.rawJson, JSON.stringify(b.settings ?? {}),
    b.context?.defKind ?? '', b.context?.paramCount ?? '',
    b.uiDirective?.line ?? '', b.uiDirective?.rawJson ?? '',
  ].join('|')
}

let lastSignature = ''

/** Follow the target's line as document edits move it, so Apply still lands on it. */
function reanchor(block: MetadataCommentBlock | null) {
  const bound = boundBlock.value
  if (!block || !bound) return
  const name = block.context?.defName
  const same = name && bound.context?.defName ? name === bound.context.defName : block.line === bound.line
  if (same) boundBlock.value = block
}

watch(
  () => props.block,
  (block) => {
    const docKey = block?.docKey ?? ''
    // A different document (or the first bind): park what's here, pick up its own draft.
    if (!boundBlock.value || docKey !== (boundBlock.value.docKey ?? '')) {
      stash()
      const draft = getMetadataDraft(docKey)
      if (draft) restoreDraft(draft)
      else bind(block)
      return
    }

    // Unsaved edits outrank the cursor: the form stays on its target until Apply or Reset.
    if (dirty.value) {
      reanchor(block)
      return
    }

    const sig = blockSignature(block)
    if (sig === lastSignature) return
    const previous = revealTargetFor(boundBlock.value)
    const previousLine = boundBlock.value?.line
    bind(block)
    const target = revealTargetFor(block)
    if (target && (target.anchor !== previous?.anchor || block?.line !== previousLine)) reveal(target)
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

.metadata-toolbar {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 12px;
  flex: 0 0 auto;
  border-bottom: 1px solid var(--vscode-panel-border);
  background: var(--vscode-editor-background);
}

.metadata-container {
  overflow-y: auto;
  flex: 1 1 auto;
  min-height: 0;
  padding: 12px;
}

.section-header {
  display: flex;
  align-items: center;
  gap: 6px;
  margin: 12px 0 8px 0;
  color: var(--vscode-sideBarSectionHeader-foreground);
  font-weight: bold;
  border-bottom: 1px solid var(--vscode-panel-border);
  padding-bottom: 4px;
  cursor: pointer;
  user-select: none;
}

.metadata-section:first-child .section-header {
  margin-top: 0;
}

.section-header:hover {
  color: var(--vscode-foreground);
}

.expand-icon {
  transition: transform 0.2s;
}

.expand-icon.collapsed {
  transform: rotate(-90deg);
}

.search-active .expand-icon {
  visibility: hidden;
}

.metadata-search {
  flex: 1;
}

.no-results {
  padding: 16px 4px;
  color: var(--vscode-descriptionForeground);
  text-align: center;
}

.note {
  margin: 0 0 12px 0;
  color: var(--vscode-descriptionForeground);
  line-height: 1.5;
}

.line-actions {
  margin-top: 16px;
}

.bound-target {
  --target-color: var(--vscode-descriptionForeground);
  display: flex;
  align-items: baseline;
  gap: 8px;
  min-width: 0;
  margin: 0 0 12px 0;
  padding: 4px 8px;
  border-radius: 4px;
  border-left: 2px solid var(--target-color);
  background: var(--vscode-textCodeBlock-background);
  transition: border-color 150ms ease;
}

.bound-target.kind-variable {
  --target-color: var(--vscode-symbolIcon-variableForeground);
}

.bound-target.kind-function {
  --target-color: var(--vscode-symbolIcon-functionForeground);
}

.bound-target.kind-macro {
  --target-color: var(--vscode-symbolIcon-classForeground);
}

.line-chip {
  flex: none;
  background: none;
  border: none;
  padding: 0;
  color: var(--vscode-descriptionForeground);
  cursor: pointer;
}

.line-chip:hover {
  color: var(--vscode-textLink-foreground);
  text-decoration: underline;
}

.target-kind {
  flex: none;
  color: var(--target-color);
  opacity: 0.85;
  font-style: italic;
  transition: color 150ms ease;
}

.target-name {
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  font-family: var(--vscode-editor-font-family);
  font-weight: 600;
  color: var(--target-color);
  transition: color 150ms ease;
}

.dirty-mark {
  flex: none;
  margin-left: auto;
  color: var(--vscode-gitDecoration-modifiedResourceForeground);
  font-weight: 700;
}

.section-desc {
  color: var(--vscode-descriptionForeground);
  margin: 0 0 16px 0;
  line-height: 1.5;
}

.section-desc code {
  background: var(--vscode-textCodeBlock-background);
  padding: 1px 4px;
  border-radius: 2px;
}

.warning {
  color: var(--vscode-editorWarning-foreground, #cca700);
  margin: 0 0 12px 0;
  line-height: 1.5;
}

.field {
  margin-bottom: 16px;
}

.field > label {
  display: block;
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
}

.setting-error {
  color: var(--vscode-errorForeground, #f14c4c);
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
  color: var(--vscode-foreground);
}

.sub-row select {
  flex: 1;
  min-width: 0;
  background: var(--vscode-input-background);
  color: var(--vscode-input-foreground);
  border: 1px solid var(--vscode-input-border, transparent);
  padding: 4px 6px;
  font-family: var(--vscode-font-family);
  border-radius: 2px;
}

.code-multiselect {
  width: 100%;
  margin-bottom: 8px;
  background: var(--vscode-input-background);
  color: var(--vscode-input-foreground);
  border: 1px solid var(--vscode-input-border, transparent);
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
  font-family: var(--vscode-font-family);
  border-radius: 2px;
}

.field textarea {
  width: 100%;
  resize: vertical;
}

.checkbox-label {
  margin-top: 0;
}

.metadata-container {
  overflow-y: auto;
  flex: 1 1 auto;
  min-height: 0;
  padding: 12px;
}

.section-header {
  display: flex;
  align-items: center;
  gap: 6px;
  margin: 12px 0 8px 0;
  color: var(--vscode-sideBarSectionHeader-foreground);
  font-weight: bold;
  border-bottom: 1px solid var(--vscode-panel-border);
  padding-bottom: 4px;
  cursor: pointer;
  user-select: none;
}

.metadata-section:first-child .section-header {
  margin-top: 0;
}

.section-header:hover {
  color: var(--vscode-foreground);
}

.expand-icon {
  transition: transform 0.2s;
}

.expand-icon.collapsed {
  transform: rotate(-90deg);
}

.note {
  margin: 0 0 12px 0;
  color: var(--vscode-descriptionForeground);
  line-height: 1.5;
}

.line-actions {
  margin-top: 16px;
}

.bound-target {
  --target-color: var(--vscode-descriptionForeground);
  display: flex;
  align-items: baseline;
  gap: 8px;
  min-width: 0;
  margin: 0 0 12px 0;
  padding: 4px 8px;
  border-radius: 4px;
  border-left: 2px solid var(--target-color);
  background: var(--vscode-textCodeBlock-background);
  transition: border-color 150ms ease;
}

.bound-target.kind-variable {
  --target-color: var(--vscode-symbolIcon-variableForeground);
}

.bound-target.kind-function {
  --target-color: var(--vscode-symbolIcon-functionForeground);
}

.bound-target.kind-macro {
  --target-color: var(--vscode-symbolIcon-classForeground);
}

.line-chip {
  flex: none;
  background: none;
  border: none;
  padding: 0;
  color: var(--vscode-descriptionForeground);
  cursor: pointer;
}

.line-chip:hover {
  color: var(--vscode-textLink-foreground);
  text-decoration: underline;
}

.target-kind {
  flex: none;
  color: var(--target-color);
  opacity: 0.85;
  font-style: italic;
  transition: color 150ms ease;
}

.target-name {
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  font-family: var(--vscode-editor-font-family);
  font-weight: 600;
  color: var(--target-color);
  transition: color 150ms ease;
}

.dirty-mark {
  flex: none;
  margin-left: auto;
  color: var(--vscode-gitDecoration-modifiedResourceForeground);
  font-weight: 700;
}

.section-desc {
  color: var(--vscode-descriptionForeground);
  margin: 0 0 16px 0;
  line-height: 1.5;
}

.section-desc code {
  background: var(--vscode-textCodeBlock-background);
  padding: 1px 4px;
  border-radius: 2px;
}

.warning {
  color: var(--vscode-editorWarning-foreground, #cca700);
  margin: 0 0 12px 0;
  line-height: 1.5;
}

.field {
  margin-bottom: 16px;
}

.field > label {
  display: block;
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
}

.setting-error {
  color: var(--vscode-errorForeground, #f14c4c);
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
  color: var(--vscode-foreground);
}

.sub-row select {
  flex: 1;
  min-width: 0;
  background: var(--vscode-input-background);
  color: var(--vscode-input-foreground);
  border: 1px solid var(--vscode-input-border, transparent);
  padding: 4px 6px;
  font-family: var(--vscode-font-family);
  border-radius: 2px;
}

.code-multiselect {
  width: 100%;
  margin-bottom: 8px;
  background: var(--vscode-input-background);
  color: var(--vscode-input-foreground);
  border: 1px solid var(--vscode-input-border, transparent);
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
  font-family: var(--vscode-font-family);
  border-radius: 2px;
}

.field textarea {
  width: 100%;
  resize: vertical;
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

.checkbox-label {
  display: flex;
  align-items: center;
  gap: 6px;
  cursor: pointer;
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
</style>
