<template>
  <!-- @contextmenu.prevent on the whole panel: on Linux, the WebKitGTK
       hosting can segfault when the native context menu tries to render inside
       the embedded webview. Blocking the default menu everywhere is safer than
       relying on children to opt in. -->
  <div class="calcpad-vue-ui" :class="{ split: isSplit }" @contextmenu.prevent>
    <!-- Activity icons: only shown when the host app enables extra tabs (desktop).
         VS Code webview keeps a single Calcpad view. -->
    <div v-if="versionConfig.isDesktop" class="activity-icons" role="tablist">
      <button
        v-for="view in views"
        :key="view.id"
        :class="['activity-icon', { active: activeView === view.id }]"
        :title="view.label"
        :aria-label="view.label"
        :aria-selected="activeView === view.id"
        role="tab"
        @click="switchView(view.id)"
        v-html="view.icon"
      ></button>
    </div>

    <CalcpadFilesTab
      v-if="versionConfig.isDesktop && activeView === 'files'"
      :opened-folder="openedFolder"
      :tree-roots="fileTreeRoots"
      @open-folder-request="handleOpenFolderRequest"
      @open-file="handleOpenFile"
      @expand-folder="handleExpandFolder"
      @open-containing-folder="handleOpenContainingFolder"
      @close-folder="handleCloseFolder"
    />

    <div v-show="!versionConfig.isDesktop || activeView === 'calcpad'" class="calcpad-view" :class="{ split: isSplit }">
    <div v-for="(pane, paneIndex) in panes" :key="pane.id" class="calcpad-pane">
    <div class="tab-container">
      <button
        v-for="tab in tabs"
        :key="tab.id"
        :class="['tab', { active: pane.activeTab === tab.id }]"
        @click="activateTab(pane, tab.id)"
      >
        {{ tab.label }}
      </button>
      <button
        class="pane-action"
        :title="isSplit ? 'Close this panel' : 'Split panel'"
        :aria-label="isSplit ? 'Close this panel' : 'Split panel'"
        @click="isSplit ? closePane(paneIndex) : splitPane()"
        v-html="isSplit ? CLOSE_ICON : SPLIT_ICON"
      ></button>
    </div>

    <div class="tab-content">
      <CalcpadInsertTab
        v-if="pane.activeTab === 'insert'"
        :insert-items="insertItems"
        @insert-text="handleInsertText"
        @insert-image="handleInsertImage"
      />
      <CalcpadTocTab
        v-else-if="pane.activeTab === 'toc'"
        :headings="tocHeadings"
        :loading="tocLoading"
        @go-to-line="handleGoToLine"
      />
      <CalcpadSettingsTab
        v-else-if="pane.activeTab === 'settings'"
        :settings="settings"
        :initial-preview-theme="previewTheme"
        :initial-color-theme="colorTheme"
        :initial-available-themes="availableThemes"
        :initial-enable-quick-typing="enableQuickTyping"
        :initial-comment-format="commentFormat"
        :initial-enable-formatting-hotkeys="enableFormattingHotkeys"
        :initial-enable-preview-cursor-sync="enablePreviewCursorSync"
        :initial-enable-auto-run="enableAutoRun"
        :initial-dark-background="darkBackground"
        :initial-linter-min-severity="linterMinSeverity"
        :initial-max-output-lines="maxOutputLines"
        :version-config="versionConfig"
        :initial-library-path="libraryPath"
        :initial-active-config="activeConfig"
        :initial-available-configs="availableConfigs"
        :initial-editor-font-family="editorFontFamily"
        :initial-available-fonts="availableFonts"
        :app-version="appVersion"
        @update-settings="handleUpdateSettings"
        @update-preview-theme="handleUpdatePreviewTheme"
        @update-color-theme="handleUpdateColorTheme"
        @update-quick-typing="handleUpdateQuickTyping"
        @update-comment-format="handleUpdateCommentFormat"
        @update-formatting-hotkeys="handleUpdateFormattingHotkeys"
        @update-preview-cursor-sync="handleUpdatePreviewCursorSync"
        @update-auto-run="handleUpdateAutoRun"
        @update-dark-background="handleUpdateDarkBackground"
        @update-linter-min-severity="handleUpdateLinterMinSeverity"
        @update-max-output-lines="handleUpdateMaxOutputLines"
        @update-library-path="handleUpdateLibraryPath"
        @reset-settings="handleResetSettings"
        @save-named-config="handleSaveNamedConfig"
        @switch-config="handleSwitchConfig"
        @open-settings-folder="handleOpenSettingsFolder"
        @open-logs-folder="handleOpenLogsFolder"
        @open-fonts-folder="handleOpenFontsFolder"
        @refresh-fonts="handleRefreshFonts"
        @update-editor-font-family="handleUpdateEditorFontFamily"
      />
      <CalcpadVariablesTab
        v-else-if="pane.activeTab === 'variables'"
        :variables-data="variablesData"
        :loading="variablesLoading"
        @insert-text="handleInsertText"
      />
      <CalcpadPdfTab
        v-else-if="pane.activeTab === 'pdf'"
        :pdf-settings="pdfSettings"
        @update-pdf-settings="handleUpdatePdfSettings"
        @reset-pdf-settings="handleResetPdfSettings"
        @generate-pdf="handleGeneratePdf"
      />
      <CalcpadFormattingTab
        v-else-if="pane.activeTab === 'formatting'"
        :indent-style="prettifyIndentStyle"
        :indent-size="prettifyIndentSize"
        :trim-trailing-whitespace="prettifyTrimTrailing"
        @prettify="handlePrettify"
        @update-indent-style="handleUpdatePrettifyIndentStyle"
        @update-indent-size="handleUpdatePrettifyIndentSize"
        @update-trim-trailing="handleUpdatePrettifyTrim"
      />
      <CalcpadExportTab
        v-else-if="pane.activeTab === 'export'"
        :plots="plots"
        :loading="plotsLoading"
        @save-html="handleSaveSourceHtml"
        @save-docx="handleSaveDocx"
        @refresh-plots="handleRefreshPlots"
        @save-plot="handleSavePlot"
        @save-plots-zip="handleSavePlotsZip"
      />
      <CalcpadErrorsTab
        v-else-if="pane.activeTab === 'errors'"
        :errors="convertErrors"
        @go-to-line="handleGoToLine"
      />
      <CalcpadMetadataTab
        v-else-if="pane.activeTab === 'metadata'"
        :block="metadataBlock"
        @apply="handleApplyMetadata"
      />
    </div>
    </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from 'vue'
import CalcpadInsertTab from './CalcpadInsertTab.vue'
import CalcpadTocTab from './CalcpadTocTab.vue'
import CalcpadSettingsTab from './CalcpadSettingsTab.vue'
import CalcpadVariablesTab from './CalcpadVariablesTab.vue'
import CalcpadPdfTab from './CalcpadPdfTab.vue'
import CalcpadFormattingTab from './CalcpadFormattingTab.vue'
import CalcpadExportTab from './CalcpadExportTab.vue'
import CalcpadFilesTab from './CalcpadFilesTab.vue'
import CalcpadErrorsTab from './CalcpadErrorsTab.vue'
import CalcpadMetadataTab from './CalcpadMetadataTab.vue'
import { postMessage } from '../services/messaging'
import type { MetadataCommentBlock, MetadataCommentData, SettingsValues } from '../../text/metadata-comment'
import type { UiDirectiveData } from '../../text/ui-directive'
import type { Tab, InsertItem, Settings, VariablesData, PdfSettings, TocHeading, ThemeInfo, FileNode, VersionConfig } from '../types'
import { DEFAULT_VERSION_CONFIG } from '../types'
import type { CalcpadError } from '../../types/api'
import { DEFAULT_PDF_SETTINGS } from '../types'

// Props
interface Props {
  versionConfig?: VersionConfig
  appVersion?: string
}
const props = withDefaults(defineProps<Props>(), {
  versionConfig: () => ({ ...DEFAULT_VERSION_CONFIG }),
  appVersion: '',
})

// Views: string-identified so future views can be added by extending this list.
// Icons are inline SVGs so no external asset dependencies.
interface ViewDef { id: string; label: string; icon: string }
const FOLDER_ICON = '<svg width="20" height="20" viewBox="0 0 16 16" fill="currentColor" xmlns="http://www.w3.org/2000/svg"><path d="M14.5 3H7.71l-.85-.85L6.51 2h-5l-.5.5v11l.5.5h13l.5-.5v-10L14.5 3zm-.5 10H2V3h4.29l.85.85.35.15H14v9z"/></svg>'
// CalcpadCE brand logo (from docs/media/logo.svg): blue starburst with yellow
// diagonal + central hole. Kept in its native colors so the branding stays
// recognizable in the activity bar.
const CALCPAD_ICON = '<svg width="20" height="20" viewBox="0 0 100 100" xmlns="http://www.w3.org/2000/svg"><defs><linearGradient id="cp-icon-gradient" x1="0%" y1="0%" x2="0%" y2="100%"><stop offset="0%" stop-color="#23A9F2"/><stop offset="100%" stop-color="#006DB0"/></linearGradient><mask id="cp-icon-hole"><rect width="100" height="100" fill="white"/><circle cx="50" cy="50" r="9" fill="black"/></mask></defs><path d="M 50 20 L 80 50" stroke="#FFC107" stroke-width="12" stroke-linecap="round" stroke-linejoin="round"/><path d="M 17 17 Q 50 42 83 17 Q 58 50 83 83 Q 50 58 17 83 Q 42 50 17 17 Z" mask="url(#cp-icon-hole)" fill="url(#cp-icon-gradient)" stroke="url(#cp-icon-gradient)" stroke-width="3" stroke-linejoin="round"/><path d="M 80 50 L 50 80" stroke="#FFC107" stroke-width="12" stroke-linecap="round" stroke-linejoin="round"/></svg>'
const views: ViewDef[] = [
  { id: 'files', label: 'Files', icon: FOLDER_ICON },
  { id: 'calcpad', label: 'CalcpadCE', icon: CALCPAD_ICON }
]
const activeView = ref<string>('calcpad')

// Files view state
const openedFolder = ref<string | null>(null)
const fileTreeRoots = ref<FileNode[]>([])

// Split icon (two stacked rows) and close icon for the per-pane action button.
const SPLIT_ICON = '<svg width="16" height="16" viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.2" xmlns="http://www.w3.org/2000/svg"><rect x="2" y="2" width="12" height="12" rx="1"/><line x1="2" y1="8" x2="14" y2="8"/></svg>'
const CLOSE_ICON = '<svg width="16" height="16" viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.3" xmlns="http://www.w3.org/2000/svg"><line x1="4" y1="4" x2="12" y2="12"/><line x1="12" y1="4" x2="4" y2="12"/></svg>'

// The side panel can be split into two panes, each with its own active tab.
// A pane's `id` is a stable key for v-for so switching a pane's tab doesn't
// remount the other pane.
interface Pane { id: number; activeTab: string }
let paneSeq = 0
const panes = ref<Pane[]>([{ id: paneSeq++, activeTab: 'insert' }])
const isSplit = computed(() => panes.value.length > 1)
const insertItems = ref<InsertItem[]>([])
const settings = ref<Settings>()
const previewTheme = ref('system')
const colorTheme = ref('')
const availableThemes = ref<ThemeInfo[]>([])
const enableQuickTyping = ref(true)
const commentFormat = ref('auto')
const enableFormattingHotkeys = ref(true)
const enablePreviewCursorSync = ref(false)
const enableAutoRun = ref(true)
const darkBackground = ref('#1e1e1e')
const linterMinSeverity = ref('information')
const maxOutputLines = ref(1000)
const libraryPath = ref('')
const activeConfig = ref('default')
const availableConfigs = ref<string[]>(['default'])
const editorFontFamily = ref('JuliaMono')
const availableFonts = ref<string[]>([])
const variablesData = ref<VariablesData>({
  macros: [],
  variables: [],
  functions: [],
  customUnits: []
})
const variablesLoading = ref(false)
const tocHeadings = ref<TocHeading[]>([])
const tocLoading = ref(false)
const pdfSettings = ref<PdfSettings>({ ...DEFAULT_PDF_SETTINGS })
const prettifyIndentStyle = ref<'tab' | 'space'>('tab')
const prettifyIndentSize = ref(4)
const prettifyTrimTrailing = ref(true)

const metadataBlock = ref<MetadataCommentBlock | null>(null)

const tabs = computed<Tab[]>(() => {
  const base: Tab[] = [
    { id: 'insert', label: 'Insert' },
    { id: 'toc', label: 'TOC' },
    { id: 'settings', label: 'Settings' },
    { id: 'variables', label: 'Variables' },
    { id: 'pdf', label: 'PDF' },
    { id: 'formatting', label: 'Formatting' },
    { id: 'export', label: 'Export' },
    { id: 'errors', label: 'Errors' }
  ]
  // The metadata comment editor is driven by editor cursor tracking, available
  // in the VS Code webview and the desktop app (both host a real editor).
  if (props.versionConfig.isVSCode || props.versionConfig.isDesktop) {
    base.push({ id: 'metadata', label: 'Properties' })
  }
  return base
})

const convertErrors = ref<CalcpadError[]>([])

interface PlotSummary { index: number; ext: 'png' | 'svg'; dataUri: string; sizeBytes: number }
const plots = ref<PlotSummary[]>([])
const plotsLoading = ref(false)

// Methods
const switchView = (viewId: string) => {
  activeView.value = viewId
  if (viewId === 'files' && !openedFolder.value) {
    // Ask host if a previously-opened folder is still around
    postMessage({ type: 'getOpenedFolder' })
  }
}

const handleOpenFolderRequest = () => {
  postMessage({ type: 'openFolder' })
}

const handleOpenFile = (path: string) => {
  postMessage({ type: 'openFileByPath', path })
}

const handleExpandFolder = (path: string) => {
  postMessage({ type: 'readDirectory', path })
}

const handleOpenContainingFolder = (path: string) => {
  postMessage({ type: 'openContainingFolder', path })
}

const handleCloseFolder = () => {
  openedFolder.value = null
  fileTreeRoots.value = []
  postMessage({ type: 'closeFolder' })
}

// Insert children into the tree at the given directory path.
const setDirectoryChildren = (parentPath: string, children: FileNode[]) => {
  const norm = (p: string) => p.replace(/\\/g, '/').replace(/\/+$/, '')
  const target = norm(parentPath)
  const walk = (nodes: FileNode[]): boolean => {
    for (const node of nodes) {
      if (norm(node.path) === target) {
        node.children = children
        node.loaded = true
        return true
      }
      if (node.children && walk(node.children)) return true
    }
    return false
  }
  walk(fileTreeRoots.value)
}

// Add a second pane, defaulting it to a tab other than the first pane's so the
// split shows two different tabs immediately.
const splitPane = () => {
  if (panes.value.length >= 2) return
  const first = panes.value[0].activeTab
  const second = tabs.value.find(t => t.id !== first)?.id ?? first
  panes.value.push({ id: paneSeq++, activeTab: second })
}

const closePane = (index: number) => {
  if (panes.value.length <= 1) return
  panes.value.splice(index, 1)
}

const activateTab = (pane: Pane, tabId: string) => {
  pane.activeTab = tabId

  // Request fresh data when switching to variables tab
  if (tabId === 'variables') {
    variablesLoading.value = true
    postMessage({ type: 'getVariables' })
  }

  // Request headings when switching to TOC tab
  if (tabId === 'toc') {
    tocLoading.value = true
    postMessage({ type: 'getHeadings' })
  }

  // Request prettify settings when switching to Formatting tab
  if (tabId === 'formatting') {
    postMessage({ type: 'getPrettifySettings' })
  }

  // Refresh the metadata comment at the cursor when opening the Metadata tab
  if (tabId === 'metadata') {
    postMessage({ type: 'getMetadataContext' })
  }

  // Fetch the current document's plots the first time the Export tab is opened.
  // Users can refresh manually after that.
  if (tabId === 'export' && plots.value.length === 0 && !plotsLoading.value) {
    plotsLoading.value = true
    postMessage({ type: 'getPlots' })
  }
}

// Host-facing entry point (focusTab message, defineExpose): always targets the
// first pane so external callers don't need to know about the split.
const switchTab = (tabId: string) => {
  activateTab(panes.value[0], tabId)
}

const handleSaveSourceHtml = () => {
  postMessage({ type: 'saveSourceHtml' })
}

const handleSaveDocx = () => {
  postMessage({ type: 'saveDocx' })
}

const handleRefreshPlots = () => {
  plotsLoading.value = true
  postMessage({ type: 'getPlots' })
}

const handleSavePlot = (index: number) => {
  postMessage({ type: 'savePlot', index })
}

const handleSavePlotsZip = () => {
  postMessage({ type: 'savePlotsZip' })
}

const handleInsertText = (text: string) => {
  postMessage({
    type: 'insertText',
    text
  })
}

const handleInsertImage = () => {
  postMessage({ type: 'insertImage' })
}

const handleUpdateSettings = (newSettings: Settings) => {
  settings.value = { ...newSettings }
  postMessage({
    type: 'updateSettings',
    settings: newSettings
  })
}

const handleUpdatePreviewTheme = (theme: string) => {
  previewTheme.value = theme
  postMessage({ type: 'updatePreviewTheme', theme })
}

const handleUpdateColorTheme = (theme: string) => {
  colorTheme.value = theme
  postMessage({ type: 'updateColorTheme', theme })
}

const handleUpdateQuickTyping = (enabled: boolean) => {
  enableQuickTyping.value = enabled
  postMessage({ type: 'updateQuickTyping', enabled })
}

const handleUpdateCommentFormat = (format: string) => {
  commentFormat.value = format
  postMessage({ type: 'updateCommentFormat', format })
}

const handleUpdateFormattingHotkeys = (enabled: boolean) => {
  enableFormattingHotkeys.value = enabled
  postMessage({ type: 'updateFormattingHotkeys', enabled })
}

const handleUpdatePreviewCursorSync = (enabled: boolean) => {
  enablePreviewCursorSync.value = enabled
  postMessage({ type: 'updatePreviewCursorSync', enabled })
}

const handleUpdateAutoRun = (enabled: boolean) => {
  enableAutoRun.value = enabled
  postMessage({ type: 'updateAutoRun', enabled })
}

const handleUpdateDarkBackground = (color: string) => {
  darkBackground.value = color
  postMessage({ type: 'updateDarkBackground', color })
}

const handleUpdateLinterMinSeverity = (severity: string) => {
  linterMinSeverity.value = severity
  postMessage({ type: 'updateLinterMinSeverity', severity })
}

const handleUpdateMaxOutputLines = (value: number) => {
  maxOutputLines.value = value
  postMessage({ type: 'updateMaxOutputLines', value })
}

const handleUpdateLibraryPath = (path: string) => {
  libraryPath.value = path
  postMessage({ type: 'updateLibraryPath', path })
}

const handleResetSettings = () => {
  postMessage({
    type: 'resetSettings'
  })
}

const handleSaveNamedConfig = (name: string) => {
  postMessage({ type: 'saveNamedConfig', name })
}

const handleSwitchConfig = (name: string) => {
  postMessage({ type: 'switchConfig', name })
}

const handleOpenSettingsFolder = () => {
  postMessage({ type: 'openSettingsFolder' })
}

const handleOpenLogsFolder = () => {
  postMessage({
    type: 'openLogsFolder'
  })
}

const handleOpenFontsFolder = () => {
  postMessage({ type: 'openFontsFolder' })
}

const handleRefreshFonts = () => {
  postMessage({ type: 'refreshFonts' })
}

const handleUpdateEditorFontFamily = (family: string) => {
  editorFontFamily.value = family
  postMessage({ type: 'updateEditorFontFamily', family })
}

const handleUpdatePdfSettings = (settings: PdfSettings) => {
  postMessage({
    type: 'updatePdfSettings',
    settings
  })
}

const handleResetPdfSettings = () => {
  postMessage({
    type: 'resetPdfSettings'
  })
}

const handleGeneratePdf = () => {
  postMessage({
    type: 'generatePdf'
  })
}

// Ctrl+E exports PDF in the desktop app only; VS Code owns its own keybindings.
const handleExportShortcut = (event: KeyboardEvent) => {
  if (event.key.toLowerCase() === 'e' && (event.ctrlKey || event.metaKey) && !event.altKey && !event.shiftKey) {
    event.preventDefault()
    event.stopPropagation()
    handleGeneratePdf()
  }
}

const handleGoToLine = (line: number) => {
  postMessage({
    type: 'goToLine',
    line
  })
}

const handlePrettify = () => {
  postMessage({ type: 'prettifyDocument' })
}

const handleUpdatePrettifyIndentStyle = (style: 'tab' | 'space') => {
  prettifyIndentStyle.value = style
  postMessage({ type: 'updatePrettifyIndentStyle', value: style })
}

const handleUpdatePrettifyIndentSize = (size: number) => {
  prettifyIndentSize.value = size
  postMessage({ type: 'updatePrettifyIndentSize', value: size })
}

const handleUpdatePrettifyTrim = (enabled: boolean) => {
  prettifyTrimTrailing.value = enabled
  postMessage({ type: 'updatePrettifyTrim', value: enabled })
}

const handleApplyMetadata = (payload: { data: MetadataCommentData; settings: SettingsValues; ui?: UiDirectiveData }) => {
  if (!metadataBlock.value) return
  // One message → one atomic edit covering the metadata comment, the
  // document-level #settings directive, and the #UI directive at the cursor,
  // so the writes can't race or shift each other's line numbers.
  postMessage({
    type: 'updateMetadata',
    line: metadataBlock.value.line,
    endLine: metadataBlock.value.endLine,
    indent: metadataBlock.value.indent,
    trailingQuote: metadataBlock.value.trailingQuote,
    layout: metadataBlock.value.layout,
    isNew: metadataBlock.value.isNew,
    data: payload.data,
    settings: payload.settings,
    settingsLine: metadataBlock.value.settingsLine ?? null,
    settingsEndLine: metadataBlock.value.settingsEndLine ?? metadataBlock.value.settingsLine ?? null,
    settingsLayout: metadataBlock.value.settingsLayout,
    ui: payload.ui,
    uiLine: metadataBlock.value.uiDirective?.line ?? null,
  })
}

// Message handler
const handleMessage = (event: MessageEvent) => {
  const message = event.data

  switch (message.type) {
    case 'insertDataResponse':
      insertItems.value = message.items || []
      break
    case 'settingsResponse':
      settings.value = message.settings
      previewTheme.value = message.previewTheme || 'system'
      colorTheme.value = message.colorTheme || ''
      availableThemes.value = message.availableThemes || []
      commentFormat.value = message.commentFormat || 'auto'
      enableFormattingHotkeys.value = message.enableFormattingHotkeys !== false
      if (typeof message.enableQuickTyping === 'boolean') enableQuickTyping.value = message.enableQuickTyping
      if (typeof message.enablePreviewCursorSync === 'boolean') enablePreviewCursorSync.value = message.enablePreviewCursorSync
      if (typeof message.enableAutoRun === 'boolean') enableAutoRun.value = message.enableAutoRun
      darkBackground.value = message.darkBackground || '#1e1e1e'
      linterMinSeverity.value = message.linterMinSeverity || 'information'
      if (typeof message.maxOutputLines === 'number' && message.maxOutputLines >= 10) {
        maxOutputLines.value = message.maxOutputLines
      }
      libraryPath.value = message.libraryPath || ''
      if (message.activeConfig) activeConfig.value = message.activeConfig
      if (Array.isArray(message.availableConfigs)) availableConfigs.value = message.availableConfigs
      if (typeof message.editorFontFamily === 'string') editorFontFamily.value = message.editorFontFamily
      if (Array.isArray(message.availableFonts)) availableFonts.value = message.availableFonts
      break
    case 'editorFontFamilyChanged':
      if (typeof message.family === 'string') editorFontFamily.value = message.family
      break
    case 'availableFontsChanged':
      if (Array.isArray(message.availableFonts)) availableFonts.value = message.availableFonts
      break
    case 'saveNamedConfigError':
      window.alert(message.message || 'Failed to save settings.')
      break
    case 'settingsReset':
      settings.value = message.settings
      break
    case 'updateVariables':
      variablesData.value = message.data
      variablesLoading.value = false
      break
    case 'updateHeadings':
      tocHeadings.value = message.headings || []
      tocLoading.value = false
      break
    case 'updateConvertErrors':
      convertErrors.value = message.errors || []
      break
    case 'pdfSettingsResponse':
      pdfSettings.value = message.settings
      break
    case 'pdfSettingsReset':
      pdfSettings.value = message.settings
      break
    case 'prettifySettingsResponse':
      prettifyIndentStyle.value = message.indentStyle === 'space' ? 'space' : 'tab'
      prettifyIndentSize.value = typeof message.indentSize === 'number' ? message.indentSize : 4
      prettifyTrimTrailing.value = message.trimTrailingWhitespace !== false
      break
    case 'folderOpened':
      openedFolder.value = message.path || null
      fileTreeRoots.value = message.entries || []
      break
    case 'folderContents':
      setDirectoryChildren(message.path, message.entries || [])
      break
    case 'plotsResponse':
      plots.value = Array.isArray(message.plots) ? message.plots : []
      plotsLoading.value = false
      break
    case 'metadataContext':
      metadataBlock.value = message.block ?? null
      break
    case 'focusTab':
      if (typeof message.tab === 'string') switchTab(message.tab)
      break
  }
}

defineExpose({ switchTab, switchView })

// Initialize
onMounted(() => {
  // Listen for messages from host
  window.addEventListener('message', handleMessage)

  // Request initial data
  postMessage({ type: 'getInsertData' })
  postMessage({ type: 'getSettings' })
  postMessage({ type: 'getPdfSettings' })

  if (props.versionConfig.isDesktop) {
    postMessage({ type: 'getOpenedFolder' })
    window.addEventListener('keydown', handleExportShortcut, true)
  }

  // Debug message
  postMessage({
    type: 'debug',
    message: 'CalcpadVueApp mounted successfully'
  })
})

onUnmounted(() => {
  window.removeEventListener('message', handleMessage)
  if (props.versionConfig.isDesktop) {
    window.removeEventListener('keydown', handleExportShortcut, true)
  }
})
</script>

<style scoped>
.calcpad-vue-ui {
  /* Natural document flow inside the parent (#vue-sidebar in calcpad-desktop,
   * <body> in the VS Code webview). Avoids flex-column edge cases where a
   * wrapped .tab-container can render on top of .tab-content because the
   * column's fixed height was computed before the wrap layout pass.
   * min-height (not height) so the box grows to the full content height:
   * a fixed 100% would cap it at one viewport, and the sticky .activity-icons
   * bar would un-stick once its containing block scrolled past. */
  min-height: 100%;
  display: block;
  font-family: var(--vscode-font-family);
  font-size: var(--vscode-font-size);
  color: var(--vscode-foreground);
  background: var(--vscode-editor-background);
}

.activity-icons {
  display: flex;
  gap: 0;
  padding: 4px 4px;
  position: sticky;
  top: 0;
  z-index: 2;
  border-bottom: 1px solid var(--vscode-widget-border);
  background: var(--vscode-activityBar-background, var(--vscode-editor-background));
}

.activity-icon {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 32px;
  height: 32px;
  padding: 0;
  border: none;
  background: transparent;
  color: var(--vscode-activityBar-inactiveForeground, var(--vscode-foreground));
  cursor: pointer;
  border-radius: 3px;
  position: relative;
  opacity: 0.75;
  transition: background 0.15s ease, opacity 0.15s ease, color 0.15s ease;
}

.activity-icon:hover {
  background: var(--vscode-toolbar-hoverBackground, rgba(90, 93, 94, 0.31));
  opacity: 1;
}

.activity-icon.active {
  color: var(--vscode-activityBar-foreground, var(--vscode-foreground));
  background: var(--vscode-list-activeSelectionBackground, var(--vscode-toolbar-activeBackground, rgba(90, 93, 94, 0.5)));
  opacity: 1;
}

.activity-icon :deep(svg) {
  display: block;
}

/* Split mode: the view becomes a vertical flex column of independently
 * scrolling panes. The root switches to height:100% so panes fill the sidebar
 * where the host gives it a definite height (calcpad-web/desktop); in the VS
 * Code webview (body-scroll, no definite height) it degrades to natural flow
 * with both panes fully expanded. */
.calcpad-vue-ui.split {
  height: 100%;
  display: flex;
  flex-direction: column;
}

.calcpad-vue-ui.split .calcpad-view {
  flex: 1 1 auto;
  min-height: 0;
}

.calcpad-view.split {
  display: flex;
  flex-direction: column;
}

.calcpad-view.split .calcpad-pane {
  flex: 1 1 0;
  min-height: 0;
  overflow-y: auto;
}

.calcpad-view.split .calcpad-pane + .calcpad-pane {
  border-top: 2px solid var(--vscode-widget-border);
}

.tab-container {
  display: flex;
  flex-wrap: wrap;
  /* Sticky so the tab strip stays visible while .tab-content scrolls. */
  position: sticky;
  top: 0;
  z-index: 1;
  border-bottom: 1px solid var(--vscode-widget-border);
  background: var(--vscode-editor-background);
}

.tab {
  padding: 8px 12px;
  border: none;
  background: transparent;
  color: var(--vscode-tab-inactiveForeground);
  cursor: pointer;
  font-size: 11px;
  font-weight: normal;
  border-radius: 0;
  transition: all 0.2s ease;
}

.tab:hover {
  background: var(--vscode-tab-hoverBackground);
  color: var(--vscode-tab-activeForeground);
}

.tab.active {
  background: var(--vscode-tab-activeBackground);
  color: var(--vscode-tab-activeForeground);
  border-bottom: 2px solid var(--vscode-tab-activeBorder);
}

/* Split / close button pinned to the right end of the tab strip. */
.pane-action {
  margin-left: auto;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 28px;
  padding: 0;
  border: none;
  background: transparent;
  color: var(--vscode-tab-inactiveForeground);
  cursor: pointer;
  border-radius: 0;
  transition: all 0.2s ease;
}

.pane-action:hover {
  background: var(--vscode-tab-hoverBackground);
  color: var(--vscode-tab-activeForeground);
}

.pane-action :deep(svg) {
  display: block;
}

.tab-content {
  /* Natural-flow content area — grows with its content. The parent
   * (#vue-sidebar) handles overflow scrolling for the whole panel. */
  padding: 0;
}

.tab-placeholder {
  padding: 20px;
  text-align: center;
  color: var(--vscode-descriptionForeground);
  font-style: italic;
}
</style>
