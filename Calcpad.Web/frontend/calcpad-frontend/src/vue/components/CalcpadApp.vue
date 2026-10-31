<template>
  <!-- @contextmenu.prevent on the whole panel: on Linux, the WebKitGTK
       hosting can segfault when the native context menu tries to render inside
       the embedded webview. Blocking the default menu everywhere is safer than
       relying on children to opt in. -->
  <div class="calcpad-vue-ui" :class="{ split: isSplit }" @contextmenu.prevent>
    <div class="calcpad-view" :class="{ split: isSplit }">
    <div v-for="(pane, paneIndex) in panes" :key="pane.id" class="calcpad-pane">
    <div class="tab-bar">
    <div class="tab-container">
      <button
        v-for="tab in tabs"
        :key="tab.id"
        :class="['tab', { active: pane.activeTab === tab.id, unavailable: tabUnavailable(tab.id) }]"
        :title="tabUnavailable(tab.id) ? INPUT_MODE_NOTE : undefined"
        @click="activateTab(pane, tab.id)"
      >
        {{ tab.label }}
      </button>
      <!-- Grouped so the pair wraps as a unit rather than splitting across rows at
           the sidebar's minimum width. -->
      <div class="pane-actions">
        <button
          class="icon"
          :title="isSplit ? 'Close this panel' : 'Split panel'"
          :aria-label="isSplit ? 'Close this panel' : 'Split panel'"
          @click="isSplit ? closePane(paneIndex) : splitPane()"
          v-html="isSplit ? CLOSE_ICON : SPLIT_ICON"
        ></button>
        <!-- Hides the whole sidebar, unlike the button beside it, which splits or
             closes one of its inner panes. -->
        <button
          v-if="(versionConfig.isWeb || versionConfig.isDesktop) && paneIndex === 0"
          class="icon"
          :title="t('Hide the sidebar (Ctrl+Shift+B)')"
          :aria-label="t('Hide the sidebar')"
          @click="hideSidebar()"
        ><SidebarIcon /></button>
      </div>
    </div>
    </div>

    <p v-if="tabUnavailable(pane.activeTab)" class="unavailable-note">{{ INPUT_MODE_NOTE }}</p>

    <!-- `inert` rather than a per-control disabled flag: it takes the whole tab out of
         both pointer and keyboard reach in one attribute, whatever it holds. -->
    <div class="tab-content" :class="{ unavailable: tabUnavailable(pane.activeTab), 'insert-content': pane.activeTab === 'insert' }" :inert="tabUnavailable(pane.activeTab)">
      <CalcpadFilesTab
        v-if="pane.activeTab === 'files'"
        :opened-folder="openedFolder"
        :tree-roots="fileTreeRoots"
        @open-folder-request="handleOpenFolderRequest"
        @open-file="handleOpenFile"
        @expand-folder="handleExpandFolder"
        @open-containing-folder="handleOpenContainingFolder"
        @close-folder="handleCloseFolder"
        @refresh-folder="handleRefreshFolder"
      />

      <CalcpadInsertTab
        v-else-if="pane.activeTab === 'insert'"
        :ref="el => { if (paneIndex === 0) insertTab = el as InstanceType<typeof CalcpadInsertTab> | null }"
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
        :initial-enable-auto-input-mode="enableAutoInputMode"
        :initial-enable-preview-ui-overrides="enablePreviewUiOverrides"
        :initial-enable-preview-pdf="enablePreviewPdf"
        :initial-show-file-metadata="showFileMetadata"
        :initial-dark-background="darkBackground"
        :initial-linter-min-severity="linterMinSeverity"
        :initial-log-level="logLevel"
        :initial-max-output-lines="maxOutputLines"
        :initial-max-preview-size="maxPreviewSizeMB"
        :initial-max-preview-console-messages="maxPreviewConsoleMessages"
        :version-config="versionConfig"
        :initial-active-config="activeConfig"
        :initial-available-configs="availableConfigs"
        :initial-editor-font-family="editorFontFamily"
        :initial-completion-delay-ms="completionDelayMs"
        :initial-available-fonts="availableFonts"
        :initial-user-name="userName"
        :focus-request="settingFocus?.paneId === pane.id ? settingFocus : null"
        @focus-handled="settingFocus = null"
        :app-version="appVersion"
        :pdf-settings="pdfSettings"
        @update-settings="handleUpdateSettings"
        @update-pdf-settings="handleUpdatePdfSettings"
        @reset-pdf-settings="handleResetPdfSettings"
        @update-preview-theme="handleUpdatePreviewTheme"
        @update-color-theme="handleUpdateColorTheme"
        @update-quick-typing="handleUpdateQuickTyping"
        @update-comment-format="handleUpdateCommentFormat"
        @update-formatting-hotkeys="handleUpdateFormattingHotkeys"
        @update-preview-cursor-sync="handleUpdatePreviewCursorSync"
        @update-auto-run="handleUpdateAutoRun"
        @update-auto-input-mode="handleUpdateAutoInputMode"
        @update-preview-ui-overrides="handleUpdatePreviewUiOverrides"
        @update-preview-pdf="handleUpdatePreviewPdf"
        @update-show-file-metadata="handleUpdateShowFileMetadata"
        @update-dark-background="handleUpdateDarkBackground"
        @update-linter-min-severity="handleUpdateLinterMinSeverity"
        @update-log-level="handleUpdateLogLevel"
        @update-max-output-lines="handleUpdateMaxOutputLines"
        @update-max-preview-size="handleUpdateMaxPreviewSize"
        @update-max-preview-console-messages="handleUpdateMaxPreviewConsoleMessages"
        @reset-settings="handleResetSettings"
        @save-named-config="handleSaveNamedConfig"
        @switch-config="handleSwitchConfig"
        @open-settings-folder="handleOpenSettingsFolder"
        @open-logs-folder="handleOpenLogsFolder"
        @open-fonts-folder="handleOpenFontsFolder"
        @refresh-fonts="handleRefreshFonts"
        @update-editor-font-family="handleUpdateEditorFontFamily"
        @update-completion-delay-ms="handleUpdateCompletionDelayMs"
        @update-user-name="handleUpdateUserName"
      >
        <template #formatting>
          <CalcpadFormattingTab
            :source-unavailable="inputMode"
            :indent-style="prettifyIndentStyle"
            :indent-size="prettifyIndentSize"
            :trim-trailing-whitespace="prettifyTrimTrailing"
            @prettify="handlePrettify"
            @update-indent-style="handleUpdatePrettifyIndentStyle"
            @update-indent-size="handleUpdatePrettifyIndentSize"
            @update-trim-trailing="handleUpdatePrettifyTrim"
          />
        </template>
      </CalcpadSettingsTab>
      <CalcpadVariablesTab
        v-else-if="pane.activeTab === 'variables'"
        :variables-data="variablesData"
        :loading="variablesLoading"
        @insert-text="handleInsertText"
      />
      <CalcpadExportTab
        v-else-if="pane.activeTab === 'export'"
        :plots="plots"
        :loading="plotsLoading"
        :version-config="versionConfig"
        :is-compiled="activeIsCompiled"
        :write-mode="writeMode"
        :write-result="writeResult"
        @save-pdf="handleSavePdf"
        @save-html="handleSaveSourceHtml"
        @save-docx="handleSaveDocx"
        @save-compiled="handleSaveCompiled"
        @save-portable="handleSavePortable"
        @refresh-plots="handleRefreshPlots"
        @save-plot="handleSavePlot"
        @save-plots-zip="handleSavePlotsZip"
        @update-write-mode="handleUpdateWriteMode"
        @write-files-now="handleWriteFilesNow"
      />
      <CalcpadMetadataTab
        v-else-if="pane.activeTab === 'metadata'"
        :block="metadataBlock"
        :ui-controls="uiControls"
        :file-info="fileInfo"
        :saved-ui-overrides="savedUiOverrides"
        :has-document="fileInfoHasDocument"
        :doc-key="fileInfoDocKey"
        :user-name="userName"
        :source-unavailable="inputMode"
        :source-unavailable-note="INPUT_MODE_NOTE"
        @apply="handleApplyMetadata"
        @go-to-line="handleGoToLine"
        @refresh-ui-controls="handleRefreshUiControls"
        @draft-dirty="handleDraftDirty($event, 'properties')"
        @update-file-info="handleUpdateFileInfo"
        @mark-reviewed="postMessage({ type: 'markFileReviewed' })"
        @open-settings="openSetting(pane, 'user', 'userName')"
        @file-info-draft-dirty="handleDraftDirty($event, 'info')"
        @update-ui-overrides="handleUpdateUiOverrides"
        @ui-overrides-draft-dirty="handleDraftDirty($event, 'uiOverrides')"
      />
    </div>
    </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { ref, computed, nextTick, onMounted, onUnmounted } from 'vue'
import CalcpadInsertTab from './CalcpadInsertTab.vue'
import SidebarIcon from './SidebarIcon.vue'
import CalcpadTocTab from './CalcpadTocTab.vue'
import CalcpadSettingsTab from './CalcpadSettingsTab.vue'
import CalcpadVariablesTab from './CalcpadVariablesTab.vue'
import CalcpadFormattingTab from './CalcpadFormattingTab.vue'
import CalcpadExportTab from './CalcpadExportTab.vue'
import CalcpadFilesTab from './CalcpadFilesTab.vue'
import CalcpadMetadataTab from './CalcpadMetadataTab.vue'
import { postMessage } from '../services/messaging'
import type { FileInfo } from '../../services/file-info'
import type { MetadataCommentBlock, MetadataCommentData, SettingsValues } from '../../text/metadata-comment'
import { discardMetadataDraft } from '../metadata-drafts'
import type { UiDirectiveData } from '../../text/ui-directive'
import type { UiControl } from '../../services/ui-overrides'
import type { Tab, InsertItem, Settings, VariablesData, PdfSettings, TocHeading, ThemeInfo, FileNode, VersionConfig } from '../types'
import { DEFAULT_VERSION_CONFIG } from '../types'
import type { ExportVariant } from '../../types/api'
import type { WriteMode } from '../../types/settings'
import { coerceWriteMode, DEFAULT_COMPLETION_DELAY_MS } from '../../types/settings'
import { DEFAULT_PDF_SETTINGS } from '../types'

const { t } = useI18n()
import {
  DEFAULT_PREVIEW_SIZE_MB, MIN_PREVIEW_SIZE_MB,
  DEFAULT_CONSOLE_MESSAGES_PER_DOCUMENT, MIN_CONSOLE_MESSAGES_PER_DOCUMENT,
} from '../../services/preview-limits'

// Props
interface Props {
  versionConfig?: VersionConfig
  appVersion?: string
}
const props = withDefaults(defineProps<Props>(), {
  versionConfig: () => ({ ...DEFAULT_VERSION_CONFIG }),
  appVersion: '',
})

// Files tab state
const openedFolder = ref<string | null>(null)
const fileTreeRoots = ref<FileNode[]>([])

// Split icon (two stacked rows) and close icon for the per-pane action button;
// hide-sidebar collapses the panel itself.
const SPLIT_ICON = '<svg width="16" height="16" viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.2" xmlns="http://www.w3.org/2000/svg"><rect x="2.5" y="2.5" width="11" height="11" rx="1"/><line x1="2.5" y1="8" x2="13.5" y2="8"/></svg>'
const CLOSE_ICON = '<svg width="16" height="16" viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.3" xmlns="http://www.w3.org/2000/svg"><line x1="4" y1="4" x2="12" y2="12"/><line x1="12" y1="4" x2="4" y2="12"/></svg>'

// The side panel can be split into two panes, each with its own active tab.
// A pane's `id` is a stable key for v-for so switching a pane's tab doesn't
// remount the other pane.
interface Pane { id: number; activeTab: string }
let paneSeq = 0
const panes = ref<Pane[]>([{ id: paneSeq++, activeTab: 'insert' }])
const insertTab = ref<InstanceType<typeof CalcpadInsertTab> | null>(null)
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
const enableAutoInputMode = ref(true)
const enablePreviewUiOverrides = ref(false)
const enablePreviewPdf = ref(false)
const showFileMetadata = ref(false)
const darkBackground = ref('#1e1e1e')
const linterMinSeverity = ref('information')
const logLevel = ref('warning')
const maxOutputLines = ref(1000)
const maxPreviewSizeMB = ref(DEFAULT_PREVIEW_SIZE_MB)
const maxPreviewConsoleMessages = ref(DEFAULT_CONSOLE_MESSAGES_PER_DOCUMENT)
const activeConfig = ref('default')
const availableConfigs = ref<string[]>(['default'])
const editorFontFamily = ref('JuliaMono')
const completionDelayMs = ref(DEFAULT_COMPLETION_DELAY_MS)
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

const userName = ref('')
const fileInfo = ref<FileInfo | null>(null)
const savedUiOverrides = ref<Record<string, string>>({})
const fileInfoHasDocument = ref(false)
const fileInfoDocKey = ref('')

const metadataBlock = ref<MetadataCommentBlock | null>(null)
// #UI controls of the last input-form render, null until the host resolves them.
const uiControls = ref<UiControl[] | null>(null)

const tabs = computed<Tab[]>(() => {
  const base: Tab[] = [
    { id: 'insert', label: t('Insert') },
    { id: 'toc', label: t('TOC') },
    { id: 'settings', label: t('Settings') },
    { id: 'variables', label: t('Variables') },
    { id: 'export', label: t('Export') }
  ]
  if (props.versionConfig.isDesktop) base.unshift({ id: 'files', label: t('Files') })
  // The metadata comment editor is driven by editor cursor tracking, available
  // in the VS Code webview and the desktop app (both host a real editor).
  // File info is stamped on save, which only those two hosts have.
  if (props.versionConfig.isVSCode || props.versionConfig.isDesktop) {
    base.push({ id: 'metadata', label: t('Properties') })
  }
  return base
})

// The first pane reopens on the tab it was last left on; a new install starts on the first tab.
const START_TAB_KEY = 'calcpad.sidebar.activeTab'
const storedStartTab = (): string => {
  let stored: string | null = null
  try {
    stored = localStorage.getItem(START_TAB_KEY)
  } catch {
    // ignore unavailable storage
  }
  const tabId = stored === 'formatting' ? 'settings' : stored
  return tabs.value.find(t => t.id === tabId)?.id ?? 'insert'
}
panes.value[0].activeTab = storedStartTab()

/**
 * Input mode fills a worksheet in rather than editing it — the host hides the editor,
 * and for a compiled worksheet there is no source behind it at all. The tabs that act
 * on source have nothing to act on there, so their content is shown inert: TOC, Export,
 * Settings and Properties' File Info still work, since a filled-in worksheet is still
 * navigated, exported and reviewed.
 */
const SOURCE_TABS = ['insert', 'variables']
const INPUT_MODE_NOTE = 'Unavailable in input mode — this acts on the document source, which the input form does not edit.'
const inputMode = ref(false)
const tabUnavailable = (tabId: string) => inputMode.value && SOURCE_TABS.includes(tabId)
// A compiled worksheet has no source at all, so the Export tab drops the exports
// and re-packaging options that would need it.
const activeIsCompiled = ref(false)

interface PlotSummary { index: number; ext: 'png' | 'svg'; dataUri: string; sizeBytes: number }
const plots = ref<PlotSummary[]>([])
const plotsLoading = ref(false)

const writeMode = ref<WriteMode>('reportOnly')
const writeResult = ref<{ ok: boolean; message: string } | null>(null)

// Methods
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

const handleRefreshFolder = () => {
  postMessage({ type: 'getOpenedFolder' })
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

const hideSidebar = () => {
  postMessage({ type: 'hideSidebar' })
}

const activateTab = (pane: Pane, tabId: string) => {
  pane.activeTab = tabId
  if (pane === panes.value[0]) {
    try {
      localStorage.setItem(START_TAB_KEY, tabId)
    } catch {
      // ignore unavailable storage
    }
  }
  // Nothing below is worth asking the host for while the tab is inert.
  if (tabUnavailable(tabId)) return

  if (tabId === 'files' && !openedFolder.value) {
    postMessage({ type: 'getOpenedFolder' })
  }

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

  // Load formatting preferences with Settings.
  if (tabId === 'settings') {
    postMessage({ type: 'getPrettifySettings' })
  }

  if (tabId === 'metadata') {
    postMessage({ type: 'getMetadataContext' })
    postMessage({ type: 'getDocumentMetadata' })
  }

  // Fetch the current document's plots the first time the Export tab is opened.
  // Users can refresh manually after that.
  if (tabId === 'export' && plots.value.length === 0 && !plotsLoading.value) {
    plotsLoading.value = true
    postMessage({ type: 'getPlots' })
  }
}

// A setting another tab links to, focused by the Settings tab in that pane once it shows.
const settingFocus = ref<{ paneId: number; section: string; id: string } | null>(null)

const openSetting = (pane: Pane, section: string, id: string) => {
  settingFocus.value = { paneId: pane.id, section, id }
  activateTab(pane, 'settings')
}

// Host-facing entry point (focusTab message, defineExpose): always targets the
// first pane so external callers don't need to know about the split.
const switchTab = (tabId: string) => {
  activateTab(panes.value[0], tabId)
}

const focusInsertSearch = async () => {
  if (tabUnavailable('insert')) return
  switchTab('insert')
  await nextTick()
  insertTab.value!.focusSearch()
}

const handleInsertSearchShortcut = (event: KeyboardEvent) => {
  if (event.key.toLowerCase() === 'i' && event.altKey && !event.ctrlKey && !event.metaKey && !event.shiftKey) {
    event.preventDefault()
    event.stopPropagation()
    void focusInsertSearch()
  }
}

const handleSavePdf = (variant: ExportVariant) => {
  postMessage({ type: 'generatePdf', variant })
}

const handleSaveSourceHtml = (variant: ExportVariant) => {
  postMessage({ type: 'saveSourceHtml', variant })
}

const handleSaveDocx = (variant: ExportVariant) => {
  postMessage({ type: 'saveDocx', variant })
}

const handleSaveCompiled = () => {
  postMessage({ type: 'saveCompiled' })
}

const handleSavePortable = () => {
  postMessage({ type: 'savePortable' })
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

const handleUpdateWriteMode = (mode: WriteMode) => {
  writeMode.value = mode
  writeResult.value = null
  postMessage({ type: 'updateWriteMode', mode })
}

const handleWriteFilesNow = () => {
  writeResult.value = null
  postMessage({ type: 'writeFilesNow' })
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

const handleUpdateAutoInputMode = (enabled: boolean) => {
  enableAutoInputMode.value = enabled
  postMessage({ type: 'updateAutoInputMode', enabled })
}

const handleUpdatePreviewUiOverrides = (enabled: boolean) => {
  enablePreviewUiOverrides.value = enabled
  postMessage({ type: 'updatePreviewUiOverrides', enabled })
}

const handleUpdatePreviewPdf = (enabled: boolean) => {
  enablePreviewPdf.value = enabled
  postMessage({ type: 'updatePreviewPdf', enabled })
}

const handleUpdateShowFileMetadata = (enabled: boolean) => {
  showFileMetadata.value = enabled
  postMessage({ type: 'updateShowFileMetadata', enabled })
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

const handleUpdateLogLevel = (level: string) => {
  logLevel.value = level
  postMessage({ type: 'updateLogLevel', level })
}

const handleUpdateMaxOutputLines = (value: number) => {
  maxOutputLines.value = value
  postMessage({ type: 'updateMaxOutputLines', value })
}

const handleUpdateMaxPreviewSize = (value: number) => {
  maxPreviewSizeMB.value = value
  postMessage({ type: 'updateMaxPreviewSize', value })
}

const handleUpdateMaxPreviewConsoleMessages = (value: number) => {
  maxPreviewConsoleMessages.value = value
  postMessage({ type: 'updateMaxPreviewConsoleMessages', value })
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

const handleUpdateCompletionDelayMs = (delayMs: number) => {
  completionDelayMs.value = delayMs
  postMessage({ type: 'updateCompletionDelayMs', completionDelayMs: delayMs })
}

const handleUpdateUserName = (value: string) => {
  userName.value = value
  postMessage({ type: 'updateUserName', value })
}

const handleUpdateFileInfo = (patch: Partial<FileInfo>) => {
  postMessage({ type: 'updateFileInfo', patch })
}

const handleUpdateUiOverrides = (overrides: Record<string, string>) => {
  postMessage({ type: 'updateUiOverrides', overrides })
}

const handleUpdatePdfSettings = (settings: PdfSettings) => {
  pdfSettings.value = { ...settings }
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

// Re-resolving renders the document, so the stale result is dropped first: the panel
// withholds its used/unused verdicts while nothing is resolved rather than showing an
// answer from before the edit that prompted the refresh.
const handleRefreshUiControls = () => {
  uiControls.value = null
  postMessage({ type: 'getUiControls' })
}

// The panel's own block, not the last one pushed: an unsaved form stays on its target
// while the cursor moves, so the two differ.
const handleApplyMetadata = (payload: { data: MetadataCommentData; settings: SettingsValues; ui?: UiDirectiveData; block: MetadataCommentBlock }) => {
  const block = payload.block
  if (!block) return
  // One message → one atomic edit covering the metadata comment, the
  // document-level #settings directive, and the #UI directive at the cursor,
  // so the writes can't race or shift each other's line numbers.
  postMessage({
    type: 'updateMetadata',
    line: block.line,
    endLine: block.endLine,
    indent: block.indent,
    trailingQuote: block.trailingQuote,
    layout: block.layout,
    isNew: block.isNew,
    data: payload.data,
    settings: payload.settings,
    settingsLine: block.settingsLine ?? null,
    settingsEndLine: block.settingsEndLine ?? block.settingsLine ?? null,
    settingsLayout: block.settingsLayout,
    ui: payload.ui,
    uiLine: block.uiDirective?.line ?? null,
  })
}

const handleDraftDirty = (payload: { docKey: string; dirty: boolean }, form: 'properties' | 'info' | 'uiOverrides') => {
  postMessage({ type: 'metadataDraftDirty', docKey: payload.docKey, dirty: payload.dirty, form })
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
      if (typeof message.enableAutoInputMode === 'boolean') enableAutoInputMode.value = message.enableAutoInputMode
      if (typeof message.enablePreviewUiOverrides === 'boolean') enablePreviewUiOverrides.value = message.enablePreviewUiOverrides
      if (typeof message.enablePreviewPdf === 'boolean') enablePreviewPdf.value = message.enablePreviewPdf
      if (typeof message.showFileMetadata === 'boolean') showFileMetadata.value = message.showFileMetadata
      darkBackground.value = message.darkBackground || '#1e1e1e'
      linterMinSeverity.value = message.linterMinSeverity || 'information'
      logLevel.value = message.logLevel || 'warning'
      writeMode.value = coerceWriteMode(message.writeMode)
      if (typeof message.maxOutputLines === 'number' && message.maxOutputLines >= 10) {
        maxOutputLines.value = message.maxOutputLines
      }
      if (typeof message.maxPreviewSizeMB === 'number' && message.maxPreviewSizeMB >= MIN_PREVIEW_SIZE_MB) {
        maxPreviewSizeMB.value = message.maxPreviewSizeMB
      }
      if (typeof message.maxPreviewConsoleMessages === 'number'
        && message.maxPreviewConsoleMessages >= MIN_CONSOLE_MESSAGES_PER_DOCUMENT) {
        maxPreviewConsoleMessages.value = message.maxPreviewConsoleMessages
      }
      if (message.activeConfig) activeConfig.value = message.activeConfig
      if (Array.isArray(message.availableConfigs)) availableConfigs.value = message.availableConfigs
      if (typeof message.editorFontFamily === 'string') editorFontFamily.value = message.editorFontFamily
      completionDelayMs.value = message.completionDelayMs
      if (Array.isArray(message.availableFonts)) availableFonts.value = message.availableFonts
      if (typeof message.userName === 'string') userName.value = message.userName
      break
    case 'documentMetadata':
      fileInfo.value = message.info ?? null
      savedUiOverrides.value = message.uiOverrides && typeof message.uiOverrides === 'object' ? message.uiOverrides : {}
      fileInfoHasDocument.value = !!message.hasDocument
      fileInfoDocKey.value = String(message.docKey ?? '')
      break
    case 'editorFontFamilyChanged':
      if (typeof message.family === 'string') editorFontFamily.value = message.family
      break
    case 'completionDelayMsChanged':
      completionDelayMs.value = message.completionDelayMs
      break
    case 'availableFontsChanged':
      if (Array.isArray(message.availableFonts)) availableFonts.value = message.availableFonts
      break
    case 'saveNamedConfigError':
      postMessage({
        type: 'showAlert',
        title: 'Settings',
        message: message.message || 'Failed to save settings.',
        kind: 'error',
      })
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
    case 'writeModeChanged':
      writeMode.value = coerceWriteMode(message.mode)
      break
    case 'writeFilesResult':
      writeResult.value = { ok: message.ok !== false, message: String(message.message ?? '') }
      break
    case 'metadataContext':
      metadataBlock.value = message.block ?? null
      break
    case 'metadataDraftDiscard':
      discardMetadataDraft(String(message.docKey ?? ''))
      break
    case 'uiControls':
      uiControls.value = Array.isArray(message.controls) ? message.controls : null
      break
    case 'inputModeChanged':
      inputMode.value = !!message.active
      activeIsCompiled.value = !!message.compiled
      break
    case 'focusTab':
      if (typeof message.tab === 'string') switchTab(message.tab)
      break
    case 'focusInsertSearch':
      void focusInsertSearch()
      break
  }
}

defineExpose({ switchTab, focusInsertSearch })

// Initialize
onMounted(() => {
  // Listen for messages from host
  window.addEventListener('message', handleMessage)
  if (props.versionConfig.isVSCode) {
    window.addEventListener('keydown', handleInsertSearchShortcut, true)
  }

  // Request initial data
  postMessage({ type: 'getInsertData' })
  postMessage({ type: 'getSettings' })
  postMessage({ type: 'getPdfSettings' })
  // The starting tab never gets a click, so its data is asked for here.
  activateTab(panes.value[0], panes.value[0].activeTab)

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
  if (props.versionConfig.isVSCode) {
    window.removeEventListener('keydown', handleInsertSearchShortcut, true)
  }
  if (props.versionConfig.isDesktop) {
    window.removeEventListener('keydown', handleExportShortcut, true)
  }
})
</script>

<style scoped>
.calcpad-vue-ui {
  height: 100%;
  min-height: 0;
  display: flex;
  flex-direction: column;
  font-family: var(--vscode-font-family);
  color: var(--vscode-foreground);
  background: var(--vscode-editor-background);
}

.calcpad-view {
  flex: 1 1 auto;
  min-height: 0;
  display: flex;
  flex-direction: column;
}

.calcpad-pane {
  flex: 1 1 0;
  min-height: 0;
  display: flex;
  flex-direction: column;
}

.calcpad-view.split .calcpad-pane + .calcpad-pane {
  border-top: 2px solid var(--vscode-widget-border);
}

/* Block wrapper, not a flex item: a wrapping flex container is measured at
 * max-content as a column flex item, so it gets a one-row height and clips the
 * rows it wraps onto. CSS Grid tracks size it the same wrong way. */
.tab-bar {
  flex: 0 0 auto;
}

.tab-container {
  display: flex;
  flex-wrap: wrap;
  border-bottom: 1px solid var(--vscode-widget-border);
  background: var(--vscode-editor-background);
}

.tab {
  padding: 8px 12px;
  border: none;
  border-bottom: 2px solid transparent;
  background: transparent;
  color: var(--vscode-tab-inactiveForeground);
  cursor: pointer;
  font-weight: normal;
  border-radius: 0;
  transition: background-color 0.2s ease, color 0.2s ease, border-bottom-color 0.2s ease;
}

.tab.active {
  background: var(--vscode-tab-activeBackground);
  color: var(--vscode-tab-activeForeground);
  border-bottom-color: var(--vscode-tab-activeBorder);
}

.tab:hover {
  background: var(--vscode-tab-hoverBackground);
  color: var(--vscode-tab-activeForeground);
}

/* Still clickable while unavailable: opening the tab is how the note explaining
 * why its content is inert gets read. */
.tab.unavailable {
  opacity: 0.5;
}

/* Split / close and hide-sidebar buttons, pinned to the right end of the tab strip. */
.pane-actions {
  margin-left: auto;
  display: flex;
  flex-shrink: 0;
}

.pane-actions button {
  height: auto;
}

.tab-content {
  flex: 1 1 auto;
  min-height: 0;
  overflow-y: auto;
  overflow-x: hidden;
  padding: 0;
}

.tab-content.unavailable {
  opacity: 0.45;
}

.tab-content.insert-content {
  display: flex;
  overflow: hidden;
}

/* Sits outside .tab-content so it stays legible while the tab itself is dimmed. */
.unavailable-note {
  flex: 0 0 auto;
  margin: 0;
  padding: 8px 12px;
  font-style: italic;
  color: var(--vscode-descriptionForeground);
  border-bottom: 1px solid var(--vscode-widget-border);
}

.tab-placeholder {
  padding: 20px;
  text-align: center;
  color: var(--vscode-descriptionForeground);
  font-style: italic;
}
</style>
