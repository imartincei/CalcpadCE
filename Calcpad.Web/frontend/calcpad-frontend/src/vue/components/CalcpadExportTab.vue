<template>
  <div class="export-tab">
    <div class="export-container p-3">
      <div v-for="group in EXPORT_GROUPS" :key="group.variant" class="export-group">
        <h3 class="export-group-title" :title="group.detail">{{ group.label }}</h3>
        <div class="header-actions">
          <button class="btn" @click="$emit('savePdf', group.variant)" :title="`Save this document's ${group.label.toLowerCase()} as a PDF`">
            Save PDF…
          </button>
          <button
            v-if="group.word"
            class="btn"
            @click="$emit('saveDocx', group.variant)"
            :title="`Save this document's ${group.label.toLowerCase()} as a Word .docx file`"
          >
            Save Word…
          </button>
          <button class="btn" @click="$emit('saveHtml', group.variant)" :title="`Save this document's ${group.label.toLowerCase()} as standalone HTML`">
            Save HTML…
          </button>
        </div>
      </div>

      <div class="export-group">
        <h3 class="export-group-title" :title="COMPILED_DETAIL">Compiled worksheet</h3>
        <div class="header-actions">
          <button
            class="btn"
            @click="$emit('saveCompiled')"
            title="Save this document as a compiled .cpdz worksheet"
          >
            Save Compiled…
          </button>
        </div>
      </div>

      <!-- Not in the browser: the references are read from the folder the document is saved
           in, and there is neither in a browser tab. -->
      <div v-if="!versionConfig.isWeb" class="export-group">
        <h3 class="export-group-title" :title="PORTABLE_DETAIL">Portable package</h3>
        <div class="header-actions">
          <button
            class="btn"
            @click="$emit('savePortable')"
            title="Save this document and everything it references as a ZIP that runs anywhere"
          >
            Export Portable…
          </button>
        </div>

        <div class="path-roots">
          <div class="path-root-row">
            <span class="path-root-label">&lt;project&gt;:</span>
            <span class="path-root-value">{{ declaredPathRoots.project ?? 'not declared' }}</span>
          </div>
          <div class="path-root-row">
            <span class="path-root-label">&lt;library&gt;:</span>
            <span class="path-root-value">{{ declaredPathRoots.library ?? 'not declared' }}</span>
          </div>
        </div>

        <label class="bundle-option" :title="BUNDLE_PROJECT_DETAIL">
          <input
            type="checkbox"
            :checked="bundleProjectReferences"
            @change="$emit('updateBundleProjectReferences', ($event.target as HTMLInputElement).checked)"
          />
          Bundle &lt;project&gt; references
        </label>
        <label class="bundle-option" :title="BUNDLE_LIBRARY_DETAIL">
          <input
            type="checkbox"
            :checked="bundleLibraryReferences"
            @change="$emit('updateBundleLibraryReferences', ($event.target as HTMLInputElement).checked)"
          />
          Bundle &lt;library&gt; references
        </label>
      </div>

      <!-- Governs both exports above: an absolute #write/#append target only ever matters once
           the worksheet leaves the folder it was written in. -->
      <label class="write-next-to-worksheet" :title="WRITE_NEXT_TO_WORKSHEET_DETAIL">
        <input
          type="checkbox"
          :checked="writeNextToWorksheet"
          @change="$emit('updateWriteNextToWorksheet', ($event.target as HTMLInputElement).checked)"
        />
        Write outputs next to the worksheet
      </label>

      <div class="plots-section">
        <div class="plots-header">
          <h3>Plots</h3>
          <div class="plots-actions">
            <button
              class="btn"
              :disabled="loading"
              @click="$emit('refreshPlots')"
              title="Re-run the current document and list its plots"
            >
              {{ loading ? 'Loading…' : 'Refresh' }}
            </button>
            <button
              class="btn"
              :disabled="loading || plots.length === 0"
              @click="$emit('savePlotsZip')"
              title="Download every plot as a single ZIP archive"
            >
              Download all (ZIP)
            </button>
          </div>
        </div>

        <p v-if="!loading && plots.length === 0" class="empty">
          No plots in the current document.
        </p>

        <ul v-if="plots.length > 0" class="plots-list">
          <li v-for="p in plots" :key="p.index" class="plot-item">
            <img class="thumb" :src="p.dataUri" :alt="`Plot ${p.index + 1}`" />
            <div class="plot-meta">
              <div class="plot-name">Plot {{ p.index + 1 }}.{{ p.ext }}</div>
              <div class="plot-size">{{ formatSize(p.sizeBytes) }}</div>
            </div>
            <button
              class="btn"
              @click="$emit('savePlot', p.index)"
              title="Download this plot as an image file"
            >
              Save…
            </button>
          </li>
        </ul>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import type { ExportVariant } from '../../types/api'
import type { VersionConfig } from '../types'
import { DEFAULT_VERSION_CONFIG } from '../types'
import type { DeclaredPathRoots } from '../../text/path-roots'

export interface PlotSummary {
  index: number
  ext: 'png' | 'svg'
  dataUri: string
  sizeBytes: number
}

// Each group's `detail` is the title's tooltip rather than body text: the tab is a column
// of buttons, and spelling every variant out inline buried them.
const COMPILED_DETAIL =
  'A .cpdz for handing out: it opens as an input form with the source locked, '
  + 'and referenced images are embedded so it travels as one file.'

const PORTABLE_DETAIL =
  'A ZIP holding this document as text beside a folder of everything it references, '
  + 'with the paths rewritten to reach them there. For a recipient who has to read or '
  + 'edit the calculation, not just fill it in.'

const WRITE_NEXT_TO_WORKSHEET_DETAIL =
  'When a #write or #append target is an absolute path, rewrite it to a bare filename so '
  + 'the output lands beside the exported worksheet instead of a folder that may not exist '
  + 'on the recipient\'s machine. A relative target already does that and is never touched.'

const BUNDLE_PROJECT_DETAIL =
  'Off (default): a <project> reference is left exactly as written, for the recipient\'s own '
  + '#ProjectPath to resolve. On: resolved to your local path and bundled into the package '
  + 'like any other absolute reference — for a one-off recipient who has no #ProjectPath of '
  + 'their own.'

const BUNDLE_LIBRARY_DETAIL =
  'The same choice as "Bundle <project> references", for <library> — leave off when the '
  + 'recipient already has the shared library and only needs its own #LibraryPath to find it.'

// Report first: it is the default rendering everywhere else, so it reads as the one to
// reach for. A form and a code listing have no meaningful Word form, hence `word: false`.
const EXPORT_GROUPS: { variant: ExportVariant; label: string; detail: string; word: boolean }[] = [
  {
    variant: 'report',
    label: 'Report',
    detail: '#pre hidden, #post shown, entered #UI values applied.',
    word: true,
  },
  {
    variant: 'preview',
    label: 'Preview',
    detail: 'What the results pane shows: #pre and #post, document values.',
    word: true,
  },
  {
    variant: 'input',
    label: 'Input form',
    detail: 'The #UI form itself, #post hidden. Exported controls are static.',
    word: false,
  },
  {
    variant: 'unwrapped',
    label: 'Unwrapped',
    detail: 'The source listing with macros and includes resolved.',
    word: false,
  },
]

withDefaults(defineProps<{
  plots: PlotSummary[]
  loading: boolean
  versionConfig?: VersionConfig
  writeNextToWorksheet?: boolean
  bundleProjectReferences?: boolean
  bundleLibraryReferences?: boolean
  declaredPathRoots?: DeclaredPathRoots
}>(), {
  versionConfig: () => ({ ...DEFAULT_VERSION_CONFIG }),
  writeNextToWorksheet: true,
  bundleProjectReferences: false,
  bundleLibraryReferences: false,
  declaredPathRoots: () => ({ project: null, library: null }),
})

defineEmits<{
  savePdf: [variant: ExportVariant]
  saveHtml: [variant: ExportVariant]
  saveDocx: [variant: ExportVariant]
  saveCompiled: []
  savePortable: []
  updateWriteNextToWorksheet: [enabled: boolean]
  updateBundleProjectReferences: [enabled: boolean]
  updateBundleLibraryReferences: [enabled: boolean]
  refreshPlots: []
  savePlot: [index: number]
  savePlotsZip: []
}>()

function formatSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  return `${(bytes / (1024 * 1024)).toFixed(2)} MB`
}
</script>

<style scoped>
.export-tab {
  height: 100%;
  display: flex;
  flex-direction: column;
}

.export-container {
  overflow-y: auto;
  flex: 1;
}

.header-actions {
  display: flex;
  gap: 6px;
  flex-wrap: wrap;
}

.export-group + .export-group {
  margin-top: 14px;
}

/* inline-block so the tooltip's hover target hugs the label instead of spanning
   the full row, and `help` to advertise that there is one. */
.export-group-title {
  display: inline-block;
  margin: 0 0 6px;
  font-size: 12px;
  text-transform: uppercase;
  letter-spacing: 0.5px;
  opacity: 0.8;
  cursor: help;
}

.write-next-to-worksheet {
  display: flex;
  align-items: center;
  gap: 6px;
  margin-top: 10px;
  font-size: 12px;
  cursor: help;
}

.path-roots {
  margin-top: 10px;
  font-size: 11px;
  opacity: 0.8;
}

.path-root-row {
  display: flex;
  gap: 6px;
}

.path-root-label {
  font-family: var(--vscode-editor-font-family, monospace);
}

.path-root-value {
  overflow-wrap: anywhere;
}

.bundle-option {
  display: flex;
  align-items: center;
  gap: 6px;
  margin-top: 6px;
  font-size: 12px;
  cursor: help;
}

.plots-section {
  margin-top: 16px;
}

.plots-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 8px;
  margin-bottom: 6px;
}

.plots-header h3 {
  margin: 0;
  font-size: 12px;
  text-transform: uppercase;
  letter-spacing: 0.5px;
  opacity: 0.8;
}

.plots-actions {
  display: flex;
  gap: 6px;
}

.empty {
  margin: 4px 0;
  font-size: 11px;
  opacity: 0.7;
}

.plots-list {
  list-style: none;
  padding: 0;
  margin: 0;
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.plot-item {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 4px;
  border: 1px solid var(--vscode-panel-border, rgba(128, 128, 128, 0.3));
  border-radius: 2px;
}

.thumb {
  width: 48px;
  height: 48px;
  object-fit: contain;
  background: var(--vscode-editor-background, #fff);
  border: 1px solid var(--vscode-panel-border, rgba(128, 128, 128, 0.2));
}

.plot-meta {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
}

.plot-name {
  font-size: 11px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.plot-size {
  font-size: 10px;
  opacity: 0.7;
}

.btn {
  background: var(--vscode-button-background);
  color: var(--vscode-button-foreground);
  border: none;
  padding: 4px 10px;
  border-radius: 2px;
  font-size: 11px;
  cursor: pointer;
}

.btn:hover:not(:disabled) {
  background: var(--vscode-button-hoverBackground);
}

.btn:disabled {
  opacity: 0.5;
  cursor: default;
}
</style>
