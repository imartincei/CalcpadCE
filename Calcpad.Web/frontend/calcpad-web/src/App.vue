<template>
  <div class="app-layout">
    <!-- Use v-show (not v-if) so #vue-sidebar stays in the DOM and the
         Vue app mounted to it in main.ts isn't orphaned across collapses. -->
    <div
      v-show="sidebarVisible"
      class="sidebar-pane"
      :style="{ width: sidebarWidth + 'px' }"
    >
      <div id="vue-sidebar"></div>
    </div>
    <!-- Doubles as a drag-to-resize handle (when sidebar is open) and a
         click-to-toggle collapse/expand button. -->
    <div
      class="resize-handle"
      :class="{ collapsed: !sidebarVisible, dragging: isResizing }"
      @mousedown="onSidebarHandleMouseDown"
      @dblclick="toggleSidebar"
      :title="sidebarVisible ? 'Drag to resize · double-click to collapse (Ctrl+Shift+B)' : 'Click to expand sidebar (Ctrl+Shift+B)'"
      role="separator"
      :aria-orientation="'vertical'"
      :aria-expanded="sidebarVisible"
    ></div>
    <div class="editor-pane">
      <div class="editor-toolbar" @contextmenu.prevent>
        <span class="spacer"></span>
        <button
          class="toolbar-btn"
          @click="onRunPreview"
          title="Run preview (Ctrl+Alt+X)"
        >
          ▶ Run
        </button>
        <button
          class="toolbar-btn"
          @click="onToggleSplit"
          :title="isSplit ? 'Merge editor groups' : 'Split editor down (Ctrl+\\)'"
        >
          {{ isSplit ? 'Unsplit' : 'Split ⬓' }}
        </button>
        <button class="toolbar-btn" @click="togglePreview" title="Preview HTML">
          {{ previewVisible ? 'Hide Preview' : 'Preview' }}
        </button>
      </div>

      <!-- Editor groups, stacked top/bottom. One group normally; two when split. -->
      <div class="editor-groups">
        <template v-for="(group, gi) in groups" :key="group.id">
          <div
            class="editor-group"
            :class="{ 'active-group': group.id === activeGroupId && isSplit }"
            :style="editorGroupStyle(gi)"
            @mousedown="onGroupFocus(group.id)"
          >
            <!-- Tab strip (VS Code-style). Hidden until at least one tab is registered. -->
            <div v-if="group.tabs.length > 0" class="tab-strip" role="tablist" @contextmenu.prevent>
              <div
                v-for="tab in group.tabs"
                :key="tab.id"
                class="tab"
                :class="{ active: tab.isActive, dirty: tab.dirty }"
                role="tab"
                :aria-selected="tab.isActive"
                :title="tab.filePath || tab.title"
                @mousedown.left="onTabClick(group.id, tab.id)"
                @mousedown.middle.prevent="onTabClose(group.id, tab.id)"
                @contextmenu.prevent="onTabContextMenu($event, group.id, tab.id)"
              >
                <span class="tab-title">{{ tab.title }}</span>
                <span v-if="tab.dirty" class="tab-dirty-dot" :title="'Unsaved changes'">●</span>
                <button
                  class="tab-close"
                  :title="tab.dirty ? 'Close (unsaved changes)' : 'Close'"
                  @mousedown.stop
                  @click.stop="onTabClose(group.id, tab.id)"
                >
                  ✕
                </button>
              </div>
              <button class="tab-new" title="New tab (Ctrl+T)" @click="onNewTab(group.id)">+</button>
              <span class="spacer"></span>
              <button
                v-if="isSplit && gi > 0"
                class="group-close"
                title="Close this editor group"
                @click="onCloseGroup(group.id)"
              >✕</button>
            </div>
            <div class="editor-container" :ref="el => setEditorRef(group.id, el)"></div>
          </div>
          <!-- Horizontal divider between the two stacked groups. -->
          <div
            v-if="gi === 0 && isSplit"
            class="group-divider"
            :class="{ dragging: draggingEditorDivider }"
            @mousedown="onEditorDividerMouseDown"
            role="separator"
            aria-orientation="horizontal"
          ></div>
        </template>
      </div>

      <!-- Right-click context menu for tabs. Rendered outside .tab-strip so it
           can be positioned absolutely without being clipped. @mousedown.stop
           keeps the document-level closer from firing before the button's
           click handler runs. -->
      <div
        v-if="tabContextMenu"
        class="tab-context-menu"
        :style="{ left: tabContextMenu.x + 'px', top: tabContextMenu.y + 'px' }"
        @mousedown.stop
        @click.stop
      >
        <button class="tab-context-item" @click="onContextClose">Close</button>
        <button
          class="tab-context-item"
          @click="onContextCloseOthers"
        >Close Others</button>
        <button class="tab-context-item" @click="onContextCloseAll">Close All</button>
        <template v-if="tabContextMenu.filePath">
          <div class="tab-context-sep" role="separator"></div>
          <button class="tab-context-item" @click="onContextOpenContainingFolder">
            Open Containing Folder
          </button>
          <button class="tab-context-item" @click="onContextCopyFullPath">
            Copy Full Path
          </button>
          <button class="tab-context-item" @click="onContextCopyRelativePath">
            Copy Relative Path
          </button>
        </template>
      </div>

      <!-- Problems context menu. Replaces the broken default WebView menu
           (back/forward/stop/reload) with clipboard actions. -->
      <div
        v-if="problemsContextMenu"
        class="tab-context-menu"
        :style="{ left: problemsContextMenu.x + 'px', top: problemsContextMenu.y + 'px' }"
        @mousedown.stop
        @click.stop
      >
        <button
          v-if="problemsContextMenu.problem"
          class="tab-context-item"
          @click="onCopyProblem"
        >Copy</button>
        <button
          class="tab-context-item"
          :disabled="problems.length === 0"
          @click="onCopyAllProblems"
        >Copy All</button>
      </div>

      <!-- Output context menu — same copy mechanism as the Problems panel. -->
      <div
        v-if="outputContextMenu"
        class="tab-context-menu"
        :style="{ left: outputContextMenu.x + 'px', top: outputContextMenu.y + 'px' }"
        @mousedown.stop
        @click.stop
      >
        <button
          v-if="outputContextMenu.line"
          class="tab-context-item"
          @click="onCopyOutputLine"
        >Copy</button>
        <button
          class="tab-context-item"
          :disabled="filteredOutputLines.length === 0"
          @click="onCopyAllOutput"
        >Copy All</button>
      </div>

      <!-- Preview context menu. Layered over the iframe in place of the broken
           native WebView menu (see injectLineLinks). -->
      <div
        v-if="previewContextMenu"
        class="tab-context-menu"
        :style="{ left: previewContextMenu.x + 'px', top: previewContextMenu.y + 'px' }"
        @mousedown.stop
        @click.stop
      >
        <button
          class="tab-context-item"
          :disabled="!previewContextMenu.selection"
          @click="onCopyPreviewSelection"
        >Copy</button>
        <button class="tab-context-item" @click="onFindInPreview">Find… (Ctrl+F)</button>
        <button v-if="onOpenFullHtmlRequest" class="tab-context-item" @click="onOpenFullHtml">Open Full HTML</button>
      </div>

      <!-- Bottom panel (Problems / Output) — reflects the ACTIVE group. -->
      <div v-if="bottomPanelOpen" class="bottom-panel">
        <div class="bottom-panel-header">
          <button
            class="panel-tab"
            :class="{ active: activeBottomTab === 'problems' }"
            @click="activeBottomTab = 'problems'"
          >
            Problems
            <span v-if="errorCount + warningCount + infoCount > 0" class="problems-badge" :class="errorCount > 0 ? 'error' : warningCount > 0 ? 'warning' : 'info'">
              {{ errorCount + warningCount + infoCount }}
            </span>
          </button>
          <button
            class="panel-tab"
            :class="{ active: activeBottomTab === 'output' }"
            @click="activeBottomTab = 'output'"
          >
            Output
          </button>
          <select
            v-if="activeBottomTab === 'output'"
            class="output-channel-select"
            v-model="activeOutputChannel"
            title="Output channel"
          >
            <option v-for="ch in (['app', 'preview', 'server', 'html'] as OutputChannel[])" :key="ch" :value="ch">
              {{ OUTPUT_CHANNEL_LABELS[ch] }}
            </option>
          </select>
          <span v-if="isSplit" class="panel-scope" :title="'Showing the active editor group'">
            {{ activeGroupLabel }}
          </span>
          <span class="spacer"></span>
          <button v-if="activeBottomTab === 'output'" class="toolbar-btn" @click="clearOutput" title="Clear Output">⌫</button>
          <button class="toolbar-btn" @click="bottomPanelOpen = false">✕</button>
        </div>
        <!-- Problems tab -->
        <div
          v-show="activeBottomTab === 'problems'"
          class="problems-list"
          ref="problemsList"
          @contextmenu.prevent="onProblemsContextMenu($event, null)"
        >
          <div
            v-for="(problem, i) in problems"
            :key="i"
            class="problem-row"
            @click="gotoProblem(problem)"
            @contextmenu.prevent.stop="onProblemsContextMenu($event, problem)"
          >
            <span class="problem-icon" :class="problem.severityClass">{{ problem.icon }}</span>
            <span class="problem-message">{{ problem.message }}</span>
            <span v-if="problem.code" class="problem-code">{{ problem.code }}</span>
            <span class="problem-location">[Ln {{ problem.startLineNumber }}, Col {{ problem.startColumn }}]</span>
          </div>
          <div v-if="problems.length === 0" class="problems-empty">No problems detected.</div>
        </div>
        <!-- Output tab -->
        <div
          v-show="activeBottomTab === 'output'"
          class="output-list"
          ref="outputList"
          @contextmenu.prevent="onOutputContextMenu($event, null)"
        >
          <div
            v-for="(line, i) in filteredOutputLines"
            :key="i"
            class="output-row"
            :class="line.level"
            @contextmenu.prevent.stop="onOutputContextMenu($event, line)"
          >
            <span class="output-timestamp">{{ line.time }}</span>
            <span class="output-level">{{ line.label }}</span>
            <span class="output-message">{{ line.message }}</span>
          </div>
          <div v-if="filteredOutputLines.length === 0" class="problems-empty">No output on this channel yet.</div>
        </div>
      </div>
      <!-- Status bar -->
      <div class="status-bar" @contextmenu.prevent>
        <span class="status-problems" @click="openBottomTab('problems')">
          <svg class="status-icon lintError" viewBox="0 0 16 16" aria-hidden="true">
            <path d="M16 8A8 8 0 1 1 0 8a8 8 0 0 1 16 0zM5.354 4.646a.5.5 0 1 0-.708.708L7.293 8l-2.647 2.646a.5.5 0 0 0 .708.708L8 8.707l2.646 2.647a.5.5 0 0 0 .708-.708L8.707 8l2.647-2.646a.5.5 0 0 0-.708-.708L8 7.293z"/>
          </svg> {{ errorCount }}
          <svg class="status-icon warning" viewBox="0 0 16 16" aria-hidden="true">
            <path d="M8.982 1.566a1.13 1.13 0 0 0-1.964 0L.165 13.233c-.457.778.091 1.767.982 1.767h13.706c.891 0 1.44-.99.982-1.767zM8 5c.535 0 .954.462.9.995l-.35 3.507a.552.552 0 0 1-1.1 0L7.1 5.995A.905.905 0 0 1 8 5m.002 6a1 1 0 1 1 0 2 1 1 0 0 1 0-2"/>
          </svg> {{ warningCount }}
          <svg class="status-icon info" viewBox="0 0 16 16" aria-hidden="true">
            <path d="M8 16A8 8 0 1 0 8 0a8 8 0 0 0 0 16m.93-9.412-1 4.705c-.07.34.029.533.304.533.194 0 .487-.07.686-.246l-.088.416c-.287.346-.92.598-1.465.598-.703 0-1.002-.422-.808-1.319l.738-3.468c.064-.293.006-.399-.287-.47l-.451-.081.082-.381 2.29-.287zM8 5.5a1 1 0 1 1 0-2 1 1 0 0 1 0 2"/>
          </svg> {{ infoCount }}
        </span>
        <span class="status-output" @click="openBottomTab('output')">Output</span>
        <span class="spacer"></span>
        <span
          class="status-server"
          :class="{ connected: serverConnected, disconnected: !serverConnected }"
          :title="serverConnected ? 'Server connected' : 'Server disconnected'"
        >
          ● {{ serverConnected ? 'Connected' : 'Disconnected' }}
        </span>
      </div>
    </div>

    <div v-if="previewVisible" class="preview-pane">
      <div class="preview-toolbar" @contextmenu.prevent>
        <span>Preview</span>
        <div class="preview-mode-group">
          <button
            class="toolbar-btn"
            :class="{ active: previewMode === 'wrapped' }"
            @click="setPreviewMode('wrapped')"
            title="Full HTML document"
          >Wrapped</button>
          <button
            class="toolbar-btn"
            :class="{ active: previewMode === 'unwrapped' }"
            @click="setPreviewMode('unwrapped')"
            title="Body markup only"
          >Unwrapped</button>
        </div>
        <span class="spacer"></span>
        <button class="toolbar-btn" @click="togglePreview">✕</button>
      </div>
      <!-- One preview iframe per editor group, stacked to mirror the editor
           split. allow-scripts is required so the injected console-interception
           script (and any user #HTML script) actually runs in the iframe. -->
      <!-- Find-in-preview widget (VS Code style). Opened via Ctrl+F while the
           preview is focused, or the preview context menu. -->
      <div v-if="previewFind" class="preview-find" @contextmenu.prevent>
        <input
          ref="previewFindInput"
          class="preview-find-input"
          type="text"
          placeholder="Find in preview"
          v-model="previewFind.query"
          @input="applyPreviewSearch"
          @keydown.enter.exact.prevent="previewFindStep(1)"
          @keydown.shift.enter.prevent="previewFindStep(-1)"
          @keydown.esc.prevent="closePreviewFind"
        />
        <span class="preview-find-count">
          {{ previewFind.total > 0 ? `${previewFind.current + 1}/${previewFind.total}` : (previewFind.query ? '0/0' : '') }}
        </span>
        <button class="preview-find-btn" :disabled="previewFind.total === 0" title="Previous match (Shift+Enter)" @click="previewFindStep(-1)">↑</button>
        <button class="preview-find-btn" :disabled="previewFind.total === 0" title="Next match (Enter)" @click="previewFindStep(1)">↓</button>
        <button class="preview-find-btn" title="Close (Esc)" @click="closePreviewFind">✕</button>
      </div>

      <div class="preview-frames">
        <template v-for="(group, gi) in groups" :key="'pv-' + group.id">
          <iframe
            class="preview-frame"
            :class="{ 'active-group': group.id === activeGroupId && isSplit }"
            :style="editorGroupStyle(gi)"
            :ref="el => setPreviewRef(group.id, el)"
            sandbox="allow-same-origin allow-scripts"
          ></iframe>
          <div
            v-if="gi === 0 && isSplit"
            class="group-divider"
            :class="{ dragging: draggingEditorDivider }"
            @mousedown="onEditorDividerMouseDown"
            role="separator"
            aria-orientation="horizontal"
          ></div>
        </template>
        <div v-if="previewLoading" class="preview-loading-overlay">
          <div class="preview-spinner"></div>
          <span>Calculating…</span>
        </div>
      </div>
    </div>

    <!-- Confirm dialog. HTML modal instead of a native dialog for cross-platform
         consistency between web and desktop. -->
    <div v-if="confirmState" class="modal-backdrop" @click.self="resolveConfirm('cancel')">
      <div class="modal-card" role="dialog" aria-modal="true">
        <div class="modal-title">{{ confirmState.title }}</div>
        <div class="modal-message">{{ confirmState.message }}</div>
        <div class="modal-actions">
          <button class="modal-btn primary" @click="resolveConfirm('yes')">{{ confirmState.yesLabel }}</button>
          <button class="modal-btn" @click="resolveConfirm('no')">{{ confirmState.noLabel }}</button>
          <button class="modal-btn" @click="resolveConfirm('cancel')">Cancel</button>
        </div>
      </div>
    </div>

    <!-- Quick-pick dialog. A single-select list modal (VS Code QuickPick
         analog) used e.g. by the image-storage prompt. -->
    <div v-if="quickPickState" class="modal-backdrop" @click.self="resolveQuickPick(null)">
      <div class="modal-card quick-pick-card" role="dialog" aria-modal="true">
        <div class="modal-title">{{ quickPickState.title }}</div>
        <div v-if="quickPickState.placeholder" class="modal-message">{{ quickPickState.placeholder }}</div>
        <div class="quick-pick-list">
          <button
            v-for="(opt, i) in quickPickState.options"
            :key="i"
            class="quick-pick-option"
            @click="resolveQuickPick(i)"
          >
            <div class="quick-pick-option-label">{{ opt.label }}</div>
            <div v-if="opt.detail" class="quick-pick-option-detail">{{ opt.detail }}</div>
          </button>
        </div>
        <div class="modal-actions">
          <button class="modal-btn" @click="resolveQuickPick(null)">Cancel</button>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, nextTick, onMounted, onBeforeUnmount } from 'vue'
import { extractBodyHtml } from 'calcpad-frontend'

export interface ProblemItem {
  severity: number
  severityClass: string
  icon: string
  message: string
  code: string
  startLineNumber: number
  startColumn: number
  endLineNumber: number
  endColumn: number
}

export type PreviewMode = 'wrapped' | 'unwrapped'

// Preview mode is shared across both groups. `onGotoProblem` targets the
// active group's editor (main.ts resolves it).
const onGotoProblem = ref<((problem: ProblemItem) => void) | null>(null)
const onPreviewToggled = ref<((visible: boolean) => void) | null>(null)
const onPreviewModeChanged = ref<((mode: PreviewMode) => void) | null>(null)
const previewMode = ref<PreviewMode>('wrapped')

// ---- Tab strip / editor groups ----
export interface TabUiState {
  id: string
  title: string
  filePath: string | null
  dirty: boolean
  isActive: boolean
}

interface GroupUi {
  id: string
  tabs: TabUiState[]
  problems: ProblemItem[]
  errorCount: number
  warningCount: number
  infoCount: number
}

function emptyGroup(id: string): GroupUi {
  return { id, tabs: [], problems: [], errorCount: 0, warningCount: 0, infoCount: 0 }
}

// Seed with the primary group. main.ts adds a second on split.
const groups = ref<GroupUi[]>([emptyGroup('g0')])
const activeGroupId = ref<string>('g0')

const isSplit = computed(() => groups.value.length > 1)
const activeGroup = computed(() => groups.value.find(g => g.id === activeGroupId.value) ?? groups.value[0])
const problems = computed(() => activeGroup.value?.problems ?? [])
const errorCount = computed(() => activeGroup.value?.errorCount ?? 0)
const warningCount = computed(() => activeGroup.value?.warningCount ?? 0)
const infoCount = computed(() => activeGroup.value?.infoCount ?? 0)
const activeGroupLabel = computed(() => {
  const i = groups.value.findIndex(g => g.id === activeGroupId.value)
  return i === 0 ? 'Top' : 'Bottom'
})

// DOM element registries (function refs). main.ts reads these to create the
// Monaco editor / write preview HTML for each group.
const editorEls = new Map<string, HTMLElement>()
const previewEls = new Map<string, HTMLIFrameElement>()
// Last full (unstripped) HTML rendered per group, kept for "Open Full HTML".
const previewHtmlByGroup = new Map<string, string>()

function setEditorRef(id: string, el: unknown): void {
  if (el instanceof HTMLElement) editorEls.set(id, el)
  else editorEls.delete(id)
}
function setPreviewRef(id: string, el: unknown): void {
  if (el instanceof HTMLIFrameElement) previewEls.set(id, el)
  else previewEls.delete(id)
}
function getEditorContainer(id: string): HTMLElement | null {
  return editorEls.get(id) ?? null
}

// ---- Split ratio (top group's fraction of the stack height) ----
const editorSplitRatio = ref<number>(0.5)
const draggingEditorDivider = ref(false)

function editorGroupStyle(index: number): Record<string, string> {
  if (!isSplit.value) return { flex: '1 1 0', minHeight: '0' }
  if (index === 0) return { flex: `0 0 ${editorSplitRatio.value * 100}%`, minHeight: '0' }
  return { flex: '1 1 0', minHeight: '0' }
}

function onEditorDividerMouseDown(e: MouseEvent): void {
  e.preventDefault()
  draggingEditorDivider.value = true
  const container = (e.currentTarget as HTMLElement).parentElement
  if (!container) return
  const rect = container.getBoundingClientRect()
  const onMove = (ev: MouseEvent) => {
    const frac = (ev.clientY - rect.top) / rect.height
    editorSplitRatio.value = Math.min(0.85, Math.max(0.15, frac))
  }
  const onUp = () => {
    draggingEditorDivider.value = false
    window.removeEventListener('mousemove', onMove)
    window.removeEventListener('mouseup', onUp)
  }
  window.addEventListener('mousemove', onMove)
  window.addEventListener('mouseup', onUp)
}

// ---- Group lifecycle (driven by main.ts) ----
const onSplitRequest = ref<(() => void) | null>(null)
const onCloseGroupRequest = ref<((groupId: string) => void) | null>(null)
const onGroupFocusRequest = ref<((groupId: string) => void) | null>(null)
const onRunRequest = ref<(() => void) | null>(null)

function onRunPreview(): void {
  onRunRequest.value?.()
}

function addGroup(id: string): void {
  if (groups.value.some(g => g.id === id)) return
  groups.value.push(emptyGroup(id))
}
function removeGroup(id: string): void {
  const idx = groups.value.findIndex(g => g.id === id)
  if (idx < 0) return
  groups.value.splice(idx, 1)
  editorEls.delete(id)
  previewEls.delete(id)
  if (activeGroupId.value === id) {
    activeGroupId.value = groups.value[0]?.id ?? 'g0'
  }
}
function setActiveGroup(id: string): void {
  if (groups.value.some(g => g.id === id)) activeGroupId.value = id
}
function groupIds(): string[] {
  return groups.value.map(g => g.id)
}

function onToggleSplit(): void {
  if (isSplit.value) {
    // Merge: always close the bottom group; the top (primary) is preserved
    // and the bottom is created fresh on each split (main.ts prompts for
    // dirty tabs before closing).
    const bottom = groups.value[groups.value.length - 1]
    if (bottom) onCloseGroupRequest.value?.(bottom.id)
  } else {
    onSplitRequest.value?.()
  }
}
function onCloseGroup(groupId: string): void {
  onCloseGroupRequest.value?.(groupId)
}
function onGroupFocus(groupId: string): void {
  if (activeGroupId.value !== groupId) onGroupFocusRequest.value?.(groupId)
}

// ---- Tab-strip callbacks (per group) ----
const onTabActivate = ref<((groupId: string, id: string) => void) | null>(null)
const onTabCloseRequest = ref<((groupId: string, id: string) => void) | null>(null)
const onNewTabRequest = ref<((groupId: string) => void) | null>(null)
const onTabCloseOthersRequest = ref<((groupId: string, id: string) => void) | null>(null)
const onTabCloseAllRequest = ref<((groupId: string) => void) | null>(null)
const onTabOpenContainingFolderRequest = ref<((groupId: string, id: string) => void) | null>(null)
const onTabCopyFullPathRequest = ref<((groupId: string, id: string) => void) | null>(null)
const onTabCopyRelativePathRequest = ref<((groupId: string, id: string) => void) | null>(null)

// Generic clipboard write. Set by the host (main.ts) to route through Tauri's
// native clipboard on desktop; falls back to the Web Clipboard API otherwise.
const onCopyTextRequest = ref<((text: string) => void) | null>(null)

// Opens the full rendered HTML as raw text in a new (unsaved) editor tab in
// the group the preview belongs to — mirrors vscode-calcpad's "View Webview
// Source". Left null (button hidden) when the host doesn't wire it up.
const onOpenFullHtmlRequest = ref<((groupId: string, html: string) => void) | null>(null)

interface TabContextMenuState {
  x: number
  y: number
  groupId: string
  tabId: string
  filePath: string | null
}
const tabContextMenu = ref<TabContextMenuState | null>(null)

function setTabs(groupId: string, next: TabUiState[]): void {
  const g = groups.value.find(g => g.id === groupId)
  if (g) g.tabs = next
}

function onTabClick(groupId: string, id: string): void {
  onTabActivate.value?.(groupId, id)
}

function onTabClose(groupId: string, id: string): void {
  onTabCloseRequest.value?.(groupId, id)
}

function onNewTab(groupId: string): void {
  onNewTabRequest.value?.(groupId)
}

function onTabContextMenu(e: MouseEvent, groupId: string, tabId: string): void {
  const tab = groups.value.find(g => g.id === groupId)?.tabs.find(t => t.id === tabId)
  tabContextMenu.value = {
    x: e.clientX,
    y: e.clientY,
    groupId,
    tabId,
    filePath: tab?.filePath ?? null,
  }
}

function closeTabContextMenu(): void {
  tabContextMenu.value = null
}

function onContextClose(): void {
  const m = tabContextMenu.value
  if (!m) return
  onTabCloseRequest.value?.(m.groupId, m.tabId)
  closeTabContextMenu()
}

function onContextCloseOthers(): void {
  const m = tabContextMenu.value
  if (!m) return
  onTabCloseOthersRequest.value?.(m.groupId, m.tabId)
  closeTabContextMenu()
}

function onContextCloseAll(): void {
  const m = tabContextMenu.value
  if (!m) return
  onTabCloseAllRequest.value?.(m.groupId)
  closeTabContextMenu()
}

function onContextOpenContainingFolder(): void {
  const m = tabContextMenu.value
  if (!m) return
  onTabOpenContainingFolderRequest.value?.(m.groupId, m.tabId)
  closeTabContextMenu()
}

function onContextCopyFullPath(): void {
  const m = tabContextMenu.value
  if (!m) return
  onTabCopyFullPathRequest.value?.(m.groupId, m.tabId)
  closeTabContextMenu()
}

function onContextCopyRelativePath(): void {
  const m = tabContextMenu.value
  if (!m) return
  onTabCopyRelativePathRequest.value?.(m.groupId, m.tabId)
  closeTabContextMenu()
}

interface ProblemsContextMenuState {
  x: number
  y: number
  problem: ProblemItem | null
}
const problemsContextMenu = ref<ProblemsContextMenuState | null>(null)

function onProblemsContextMenu(e: MouseEvent, problem: ProblemItem | null): void {
  problemsContextMenu.value = { x: e.clientX, y: e.clientY, problem }
}

function closeProblemsContextMenu(): void {
  problemsContextMenu.value = null
}

const SEVERITY_LABELS: Record<number, string> = { 8: 'Error', 4: 'Warning', 2: 'Info' }

function formatProblem(p: ProblemItem): string {
  const label = SEVERITY_LABELS[p.severity] ?? 'Info'
  const code = p.code ? ` (${p.code})` : ''
  return `[Ln ${p.startLineNumber}, Col ${p.startColumn}] ${label}: ${p.message}${code}`
}

function copyText(text: string): void {
  if (!text) return
  if (onCopyTextRequest.value) onCopyTextRequest.value(text)
  else void navigator.clipboard?.writeText(text)
}

function onCopyProblem(): void {
  const p = problemsContextMenu.value?.problem
  if (p) copyText(formatProblem(p))
  closeProblemsContextMenu()
}

function onCopyAllProblems(): void {
  copyText(problems.value.map(formatProblem).join('\n'))
  closeProblemsContextMenu()
}

interface OutputContextMenuState {
  x: number
  y: number
  line: OutputLine | null
}
const outputContextMenu = ref<OutputContextMenuState | null>(null)

function onOutputContextMenu(e: MouseEvent, line: OutputLine | null): void {
  outputContextMenu.value = { x: e.clientX, y: e.clientY, line }
}

function closeOutputContextMenu(): void {
  outputContextMenu.value = null
}

function formatOutputLine(l: OutputLine): string {
  return `${l.time} ${l.label} ${l.message}`
}

function onCopyOutputLine(): void {
  const l = outputContextMenu.value?.line
  if (l) copyText(formatOutputLine(l))
  closeOutputContextMenu()
}

function onCopyAllOutput(): void {
  copyText(filteredOutputLines.value.map(formatOutputLine).join('\n'))
  closeOutputContextMenu()
}

// ---- Preview context menu + find-in-preview ----
interface PreviewContextMenuState {
  x: number
  y: number
  groupId: string
  selection: string
}
const previewContextMenu = ref<PreviewContextMenuState | null>(null)

function closePreviewContextMenu(): void {
  previewContextMenu.value = null
}

function onCopyPreviewSelection(): void {
  const m = previewContextMenu.value
  if (m?.selection) copyText(m.selection)
  closePreviewContextMenu()
}

function onFindInPreview(): void {
  const groupId = previewContextMenu.value?.groupId
  closePreviewContextMenu()
  openPreviewFind(groupId ?? activeGroupId.value)
}

function onOpenFullHtml(): void {
  const groupId = previewContextMenu.value?.groupId
  closePreviewContextMenu()
  if (!groupId) return
  const html = previewHtmlByGroup.get(groupId)
  if (!html) return
  onOpenFullHtmlRequest.value?.(groupId, html)
}

interface PreviewFindState {
  groupId: string
  query: string
  total: number
  current: number
}
const previewFind = ref<PreviewFindState | null>(null)
const previewFindInput = ref<HTMLInputElement | null>(null)

function openPreviewFind(groupId: string): void {
  const existing = previewFind.value
  previewFind.value = {
    groupId,
    query: existing?.groupId === groupId ? existing.query : '',
    total: 0,
    current: 0,
  }
  void nextTick(() => {
    previewFindInput.value?.focus()
    previewFindInput.value?.select()
    applyPreviewSearch()
  })
}

function closePreviewFind(): void {
  const f = previewFind.value
  if (f) clearPreviewMarks(f.groupId)
  previewFind.value = null
}

function previewDoc(groupId: string): Document | null {
  return previewEls.get(groupId)?.contentDocument ?? null
}

function clearPreviewMarks(groupId: string): void {
  const doc = previewDoc(groupId)
  if (!doc) return
  doc.querySelectorAll('mark.cpd-find').forEach(m => {
    const parent = m.parentNode
    if (!parent) return
    parent.replaceChild(doc.createTextNode(m.textContent ?? ''), m)
    parent.normalize()
  })
}

function applyPreviewSearch(): void {
  const f = previewFind.value
  if (!f) return
  clearPreviewMarks(f.groupId)
  const doc = previewDoc(f.groupId)
  const query = f.query
  if (!doc || !query) {
    f.total = 0
    f.current = 0
    return
  }
  const needle = query.toLowerCase()
  const walker = doc.createTreeWalker(doc.body, NodeFilter.SHOW_TEXT, {
    acceptNode(node) {
      const p = node.parentElement
      if (!node.nodeValue || !p) return NodeFilter.FILTER_REJECT
      const tag = p.tagName
      if (tag === 'SCRIPT' || tag === 'STYLE') return NodeFilter.FILTER_REJECT
      return node.nodeValue.toLowerCase().includes(needle)
        ? NodeFilter.FILTER_ACCEPT
        : NodeFilter.FILTER_REJECT
    },
  })
  const targets: Text[] = []
  let n = walker.nextNode()
  while (n) {
    targets.push(n as Text)
    n = walker.nextNode()
  }
  let count = 0
  for (const node of targets) {
    const text = node.nodeValue ?? ''
    const hay = text.toLowerCase()
    const frag = doc.createDocumentFragment()
    let last = 0
    let idx = hay.indexOf(needle)
    while (idx !== -1) {
      if (idx > last) frag.appendChild(doc.createTextNode(text.slice(last, idx)))
      const mark = doc.createElement('mark')
      mark.className = 'cpd-find'
      mark.textContent = text.slice(idx, idx + query.length)
      frag.appendChild(mark)
      count++
      last = idx + query.length
      idx = hay.indexOf(needle, last)
    }
    if (last < text.length) frag.appendChild(doc.createTextNode(text.slice(last)))
    node.parentNode?.replaceChild(frag, node)
  }
  f.total = count
  f.current = 0
  highlightCurrentMatch()
}

function highlightCurrentMatch(): void {
  const f = previewFind.value
  const doc = f && previewDoc(f.groupId)
  if (!f || !doc) return
  const marks = doc.querySelectorAll('mark.cpd-find')
  marks.forEach(m => m.classList.remove('cpd-find-current'))
  const target = marks[f.current] as HTMLElement | undefined
  if (target) {
    target.classList.add('cpd-find-current')
    target.scrollIntoView({ block: 'center' })
  }
}

function previewFindStep(dir: number): void {
  const f = previewFind.value
  if (!f || f.total === 0) return
  f.current = (f.current + dir + f.total) % f.total
  highlightCurrentMatch()
}

function onDocumentInteractionForTabMenu(e: MouseEvent | KeyboardEvent): void {
  if (e instanceof KeyboardEvent && e.key !== 'Escape') return
  closeTabContextMenu()
  closeProblemsContextMenu()
  closeOutputContextMenu()
  closePreviewContextMenu()
}

function onPreviewWindowMessage(e: MessageEvent): void {
  const data = e.data
  if (!data || typeof data.type !== 'string') return
  if (data.type === 'previewContextMenuDismiss') {
    closePreviewContextMenu()
    return
  }
  if (data.type === 'previewContextMenu') {
    const iframe = previewEls.get(data.groupId)
    if (!iframe) return
    const rect = iframe.getBoundingClientRect()
    previewContextMenu.value = {
      x: rect.left + (Number(data.x) || 0),
      y: rect.top + (Number(data.y) || 0),
      groupId: data.groupId,
      selection: typeof data.selection === 'string' ? data.selection : '',
    }
    return
  }
  if (data.type === 'previewFindOpen') {
    openPreviewFind(data.groupId ?? activeGroupId.value)
  }
}

function gotoProblem(problem: ProblemItem): void {
  onGotoProblem.value?.(problem)
}

const serverConnected = ref(false)
const sidebarVisible = ref(true)
const previewVisible = ref(false)
// Groups with an in-flight preview render; drives the "Calculating…" overlay.
const previewLoadingGroups = ref(new Set<string>())
const previewLoading = computed(() => previewLoadingGroups.value.size > 0)
const bottomPanelOpen = ref(false)
const activeBottomTab = ref<'problems' | 'output'>('problems')

export type OutputChannel = 'app' | 'preview' | 'server' | 'html'

export interface OutputLine {
  time: string
  level: string
  label: string
  message: string
  channel: OutputChannel
  /** For the per-group 'preview'/'html' channels: which group emitted it. */
  groupId?: string
}

const OUTPUT_CHANNEL_LABELS: Record<OutputChannel, string> = {
  app: 'CalcpadCE',
  preview: 'Preview Console',
  server: 'Server',
  html: 'HTML Preview Output',
}

const outputLines = ref<OutputLine[]>([])
const outputList = ref<HTMLElement | null>(null)
const activeOutputChannel = ref<OutputChannel>('app')

// The 'preview'/'html' channels are per-group (each split preview has its own
// console/rendered HTML), so filter them by the active group. 'app' / 'server'
// are global.
const filteredOutputLines = computed(() =>
  outputLines.value.filter(l =>
    l.channel === activeOutputChannel.value &&
    ((l.channel !== 'preview' && l.channel !== 'html') || !l.groupId || l.groupId === activeGroupId.value)
  )
)

function openBottomTab(tab: 'problems' | 'output'): void {
  if (bottomPanelOpen.value && activeBottomTab.value === tab) {
    bottomPanelOpen.value = false
  } else {
    activeBottomTab.value = tab
    bottomPanelOpen.value = true
  }
}

// User-configurable cap on retained output lines per channel. Older lines in
// that channel are dropped once the cap is exceeded — a lower value helps
// performance when large log volumes accumulate.
const maxOutputLinesPerChannel = ref<number>(1000)
function trimChannel(channel: OutputChannel): void {
  const cap = maxOutputLinesPerChannel.value
  let excess = 0
  for (const l of outputLines.value) if (l.channel === channel) excess++
  excess -= cap
  if (excess <= 0) return
  let removed = 0
  outputLines.value = outputLines.value.filter(l => {
    if (removed < excess && l.channel === channel) { removed++; return false }
    return true
  })
}
function setMaxOutputLines(n: number): void {
  if (!Number.isFinite(n) || n < 10) return
  maxOutputLinesPerChannel.value = Math.floor(n)
  for (const ch of ['app', 'preview', 'server', 'html'] as OutputChannel[]) trimChannel(ch)
}

function appendOutput(
  level: 'info' | 'warn' | 'error' | 'debug',
  message: string,
  channel: OutputChannel = 'app',
  groupId?: string,
): void {
  const now = new Date()
  const time = now.toLocaleTimeString('en-US', { hour12: false, hour: '2-digit', minute: '2-digit', second: '2-digit' })
  const labels: Record<string, string> = { info: 'INFO', warn: 'WARN', error: 'ERROR', debug: 'DEBUG' }
  // Sample scroll position BEFORE mutating outputLines so the new line's
  // height doesn't inflate scrollHeight and mask a user-initiated scroll-up.
  const el = outputList.value
  const wasAtBottom = el
    ? (el.scrollHeight - el.scrollTop - el.clientHeight) <= 4
    : true
  outputLines.value.push({ time, level, label: labels[level] ?? level, message, channel, groupId })
  trimChannel(channel)
  const visible = channel === activeOutputChannel.value &&
    ((channel !== 'preview' && channel !== 'html') || !groupId || groupId === activeGroupId.value)
  if (wasAtBottom && visible) {
    nextTick(() => {
      const target = outputList.value
      if (target) target.scrollTop = target.scrollHeight
    })
  }
}

function clearOutput(): void {
  // Only clear the currently visible channel — match VS Code's per-channel clear.
  outputLines.value = outputLines.value.filter(l => l.channel !== activeOutputChannel.value)
}

function showOutput(channel: OutputChannel = 'app'): void {
  activeOutputChannel.value = channel
  activeBottomTab.value = 'output'
  bottomPanelOpen.value = true
}

function toggleSidebar(): void {
  sidebarVisible.value = !sidebarVisible.value
}

// ---- Sidebar drag-to-resize ----
const SIDEBAR_MIN = 180
const SIDEBAR_MAX = 600
const SIDEBAR_DEFAULT = 320
const sidebarWidth = ref<number>(loadSidebarWidth())
const isResizing = ref(false)

function loadSidebarWidth(): number {
  const raw = parseInt(localStorage.getItem('calcpad.sidebarWidth') ?? '', 10)
  if (!Number.isFinite(raw)) return SIDEBAR_DEFAULT
  return Math.min(SIDEBAR_MAX, Math.max(SIDEBAR_MIN, raw))
}

function onSidebarHandleMouseDown(e: MouseEvent): void {
  // If the sidebar is currently collapsed, a single click opens it instead
  // of starting a resize drag — there's nothing to resize yet.
  if (!sidebarVisible.value) {
    e.preventDefault()
    sidebarVisible.value = true
    return
  }
  e.preventDefault()
  isResizing.value = true
  const startX = e.clientX
  const startWidth = sidebarWidth.value
  let moved = false

  const onMove = (ev: MouseEvent) => {
    const dx = ev.clientX - startX
    if (!moved && Math.abs(dx) > 2) moved = true
    const next = Math.min(SIDEBAR_MAX, Math.max(SIDEBAR_MIN, startWidth + dx))
    sidebarWidth.value = next
  }
  const onUp = () => {
    isResizing.value = false
    window.removeEventListener('mousemove', onMove)
    window.removeEventListener('mouseup', onUp)
    if (moved) {
      localStorage.setItem('calcpad.sidebarWidth', String(sidebarWidth.value))
    }
  }
  window.addEventListener('mousemove', onMove)
  window.addEventListener('mouseup', onUp)
}

function togglePreview(): void {
  previewVisible.value = !previewVisible.value
  onPreviewToggled.value?.(previewVisible.value)
}

function isPreviewVisible(): boolean {
  return previewVisible.value
}

function setPreviewMode(mode: PreviewMode): void {
  if (previewMode.value === mode) return
  previewMode.value = mode
  onPreviewModeChanged.value?.(mode)
}

function getPreviewMode(): PreviewMode {
  return previewMode.value
}

function setPreviewLoading(groupId: string, loading: boolean): void {
  if (loading) previewLoadingGroups.value.add(groupId)
  else previewLoadingGroups.value.delete(groupId)
}

function setPreviewHtml(groupId: string, html: string, scrollToLine?: number): void {
  const frame = previewEls.get(groupId)
  if (!frame) return
  const doc = frame.contentDocument
  if (!doc) return
  doc.open()
  doc.write(injectPreviewConsole(injectLineLinks(html, scrollToLine, groupId), groupId))
  doc.close()
  previewHtmlByGroup.set(groupId, html)
  setPreviewHtmlOutput(groupId, html)
}

// Mirrors the last rendered preview into the 'html' output channel (body only,
// so it matches what the print/export path also strips out) for debugging.
// Each render replaces the group's prior line rather than appending, since old
// output is immediately stale.
function setPreviewHtmlOutput(groupId: string, html: string): void {
  outputLines.value = outputLines.value.filter(l => !(l.channel === 'html' && l.groupId === groupId))
  appendOutput('info', extractBodyHtml(html), 'html', groupId)
}

// Scroll a group's preview to a source line (editor -> preview sync). Posts to
// the listener injected by injectLineLinks; no-op if that preview isn't shown.
function scrollPreviewToSourceLine(groupId: string, line: number): void {
  const frame = previewEls.get(groupId)
  frame?.contentWindow?.postMessage({ type: 'scrollPreviewToLine', line }, '*')
}

// Inject the line-link behaviour ported from vscode-calcpad. Posted messages
// carry `groupId` so main.ts routes navigation to the group that owns this
// preview (see App.vue's per-group iframes). The scrollbar/line-focus/find-
// highlight CSS these scripts rely on now lives in the backend's template.html
// since it's static and applies to the print/export path too; only the
// per-render behaviour (which needs groupId/scrollToLine) is injected here.
function injectLineLinks(html: string, scrollToLine: number | undefined, groupId: string): string {
  const scrollTarget = typeof scrollToLine === 'number' ? String(scrollToLine) : 'null'
  const gid = JSON.stringify(groupId)
  const body = [
    "document.addEventListener('DOMContentLoaded', function() {",
    "  var GROUP_ID = " + gid + ";",
    "  var post = function(line, lineType) {",
    "    try { window.parent.postMessage({ type: 'navigateToLine', line: line, lineType: lineType, groupId: GROUP_ID }, '*'); } catch (e) {}",
    "  };",
    // Replace WebKitGTK's broken native menu with the parent's custom menu.
    // pointerdown posts a dismiss first, then contextmenu reopens, so a
    // right-click nets an open menu and any other click dismisses it.
    "  var postMenu = function(type, e) {",
    "    var sel = ''; try { sel = String(window.getSelection() || ''); } catch (_e) {}",
    "    try { window.parent.postMessage({ type: type, x: e ? e.clientX : 0, y: e ? e.clientY : 0, selection: sel, groupId: GROUP_ID }, '*'); } catch (_e2) {}",
    "  };",
    "  document.addEventListener('contextmenu', function(e) { e.preventDefault(); postMenu('previewContextMenu', e); });",
    "  document.addEventListener('pointerdown', function() { postMenu('previewContextMenuDismiss', null); });",
    "  document.addEventListener('keydown', function(e) {",
    "    if ((e.ctrlKey || e.metaKey) && (e.key === 'f' || e.key === 'F')) {",
    "      e.preventDefault();",
    "      try { window.parent.postMessage({ type: 'previewFindOpen', groupId: GROUP_ID }, '*'); } catch (_e) {}",
    "    }",
    "  });",
    "  var isCodeView = !!document.querySelector('.line-num');",
    "  document.querySelectorAll('a[data-text]').forEach(function(link) {",
    "    link.addEventListener('click', function(e) {",
    "      e.preventDefault();",
    "      var n = link.getAttribute('data-text');",
    "      if (!n) return;",
    "      var lineType = (link.classList.contains('line-num') || isCodeView) ? 'source' : 'output';",
    "      post(parseInt(n, 10), lineType);",
    "    });",
    "  });",
    "  function hideAllLineLinks() {",
    "    document.querySelectorAll('.lineLink').forEach(function(l) { l.style.display = 'none'; });",
    "  }",
    "  document.querySelectorAll('.line').forEach(function(el) {",
    "    var id = el.id || '';",
    "    var n = id.indexOf('line-') === 0 ? id.slice(5) : '';",
    "    var src = el.getAttribute('data-source-line') || n;",
    "    if (!src) return;",
    "    var link = document.createElement('a');",
    "    link.className = 'lineLink';",
    "    link.href = '#0';",
    "    link.setAttribute('data-text', src);",
    "    link.title = 'Source line ' + src;",
    "    link.textContent = '\\u2190';",
    "    link.style.display = 'none';",
    "    link.addEventListener('click', function(e) {",
    "      e.preventDefault();",
    "      post(parseInt(src, 10), 'source');",
    "    });",
    "    el.appendChild(link);",
    "    el.addEventListener('mouseenter', function() {",
    "      hideAllLineLinks();",
    "      link.style.display = 'inline-block';",
    "    });",
    "  });",
    "  window.addEventListener('scroll', hideAllLineLinks);",
    "  document.querySelectorAll('.roundBox').forEach(function(box) {",
    "    box.addEventListener('click', function() {",
    "      var errId = box.getAttribute('data-error');",
    "      var target = errId ? document.getElementById(errId) : null;",
    "      if (!target) {",
    "        var line = box.getAttribute('data-line');",
    "        target = line ? document.getElementById('line-' + line) : null;",
    "      }",
    "      if (target) target.scrollIntoView({ block: 'start' });",
    "    });",
    "  });",
    "  var scrollToLine = " + scrollTarget + ";",
    "  if (scrollToLine !== null) {",
    "    var target = document.getElementById('line-' + scrollToLine);",
    "    if (target) target.scrollIntoView({ block: 'center' });",
    "  }",
    "  var focusTimer = null;",
    "  function focusPreviewLine(line) {",
    "    if (typeof line !== 'number' || isNaN(line)) return;",
    "    var target = document.querySelector('[data-source-line=\"' + line + '\"]');",
    "    if (!target) {",
    "      var anchor = document.querySelector('a.line-num[data-text=\"' + line + '\"]');",
    "      if (anchor) target = anchor.closest('.line-text') || anchor;",
    "    }",
    "    if (!target) {",
    "      var best = null, bestSrc = -1;",
    "      document.querySelectorAll('[data-source-line]').forEach(function(el) {",
    "        var s = parseInt(el.getAttribute('data-source-line'), 10);",
    "        if (!isNaN(s) && s <= line && s > bestSrc) { bestSrc = s; best = el; }",
    "      });",
    "      target = best;",
    "    }",
    "    if (!target) return;",
    "    target.scrollIntoView({ block: 'center' });",
    "    document.querySelectorAll('.cpd-line-focus').forEach(function(el) { el.classList.remove('cpd-line-focus'); });",
    "    target.classList.add('cpd-line-focus');",
    "    if (focusTimer) clearTimeout(focusTimer);",
    "    focusTimer = setTimeout(function() { target.classList.remove('cpd-line-focus'); }, 1200);",
    "  }",
    "  window.addEventListener('message', function(e) {",
    "    var d = e.data;",
    "    if (d && d.type === 'scrollPreviewToLine') focusPreviewLine(d.line);",
    "  });",
    "});",
  ].join('\n')
  const script = '<' + 'script>' + body + '</' + 'script>'
  const headIdx = html.indexOf('<head>')
  if (headIdx >= 0) {
    return html.slice(0, headIdx + 6) + script + html.slice(headIdx + 6)
  }
  return script + html
}

// Forward iframe console.* + uncaught errors to the parent window via
// postMessage, tagged with groupId so the Output panel's "Preview Console"
// channel can be split by the active editor group.
function injectPreviewConsole(html: string, groupId: string): string {
  const gid = JSON.stringify(groupId)
  const body = [
    '(function() {',
    '  if (window.__calcpadConsolePatched) return;',
    '  window.__calcpadConsolePatched = true;',
    '  var GROUP_ID = ' + gid + ';',
    '  var post = function(level, args) {',
    '    var msg = Array.from(args).map(function(a) {',
    '      if (a instanceof Error) return a.stack || a.message;',
    "      if (typeof a === 'object') { try { return JSON.stringify(a); } catch (e) { return String(a); } }",
    '      return String(a);',
    "    }).join(' ');",
    "    try { window.parent.postMessage({ type: 'previewConsole', level: level, message: msg, groupId: GROUP_ID }, '*'); } catch (e) {}",
    '  };',
    "  ['log','info','debug','warn','error'].forEach(function(level) {",
    '    var orig = console[level];',
    '    console[level] = function() { try { orig.apply(console, arguments); } catch (e) {} post(level, arguments); };',
    '  });',
    "  window.addEventListener('error', function(e) {",
    "    post('error', ['[Uncaught] ' + (e.message || '') + ' (' + (e.filename || '') + ':' + (e.lineno || 0) + ':' + (e.colno || 0) + ')']);",
    '  });',
    "  window.addEventListener('unhandledrejection', function(e) {",
    '    var r = e.reason; var d = r && (r.stack || r.message) || String(r);',
    "    post('error', ['[Unhandled Rejection] ' + d]);",
    '  });',
    "  console.log('CalcpadCE preview console interception initialized');",
    '})();',
  ].join('\n')
  const open = '<' + 'script>'
  const close = '</' + 'script>'
  const script = open + body + close
  const headIdx = html.indexOf('<head>')
  if (headIdx >= 0) {
    return html.slice(0, headIdx + 6) + script + html.slice(headIdx + 6)
  }
  return script + html
}

function setProblems(groupId: string, markers: ProblemItem[]): void {
  const g = groups.value.find(g => g.id === groupId)
  if (!g) return
  g.problems = markers
  g.errorCount = markers.filter(m => m.severity === 8).length
  g.warningCount = markers.filter(m => m.severity === 4).length
  g.infoCount = markers.filter(m => m.severity === 2).length
}

onMounted(async () => {
  const checkHealth = async () => {
    try {
      const bridge = (window as any).calcpadBridge
      if (bridge) {
        serverConnected.value = await bridge.api.checkHealth()
      }
    } catch {
      serverConnected.value = false
    }
  }

  setTimeout(checkHealth, 1000)
  setInterval(checkHealth, 30000)

  document.addEventListener('mousedown', onDocumentInteractionForTabMenu)
  document.addEventListener('keydown', onDocumentInteractionForTabMenu)
  window.addEventListener('message', onPreviewWindowMessage)
})

onBeforeUnmount(() => {
  document.removeEventListener('mousedown', onDocumentInteractionForTabMenu)
  document.removeEventListener('keydown', onDocumentInteractionForTabMenu)
  window.removeEventListener('message', onPreviewWindowMessage)
})

// ---- In-app confirm dialog ----
export type ConfirmChoice = 'yes' | 'no' | 'cancel'

interface ConfirmState {
  title: string
  message: string
  yesLabel: string
  noLabel: string
  resolve: (c: ConfirmChoice) => void
}

const confirmState = ref<ConfirmState | null>(null)

function showConfirm(opts: {
  title: string
  message: string
  yesLabel?: string
  noLabel?: string
}): Promise<ConfirmChoice> {
  // If a previous prompt is still up, treat its answer as cancel.
  confirmState.value?.resolve('cancel')
  return new Promise(resolve => {
    confirmState.value = {
      title: opts.title,
      message: opts.message,
      yesLabel: opts.yesLabel ?? 'Yes',
      noLabel: opts.noLabel ?? 'No',
      resolve,
    }
  })
}

function resolveConfirm(choice: ConfirmChoice): void {
  const state = confirmState.value
  if (!state) return
  confirmState.value = null
  state.resolve(choice)
}

// ---- In-app quick-pick dialog ----
interface QuickPickOptionUi {
  label: string
  detail?: string
}

interface QuickPickState {
  title: string
  placeholder?: string
  options: QuickPickOptionUi[]
  resolve: (index: number | null) => void
}

const quickPickState = ref<QuickPickState | null>(null)

/** Show a single-select list; resolves with the chosen option index, or null if dismissed. */
function showQuickPick(opts: {
  title: string
  placeholder?: string
  options: QuickPickOptionUi[]
}): Promise<number | null> {
  // If a previous prompt is still up, treat it as dismissed.
  quickPickState.value?.resolve(null)
  return new Promise(resolve => {
    quickPickState.value = {
      title: opts.title,
      placeholder: opts.placeholder,
      options: opts.options,
      resolve,
    }
  })
}

function resolveQuickPick(index: number | null): void {
  const state = quickPickState.value
  if (!state) return
  quickPickState.value = null
  state.resolve(index)
}

defineExpose({
  // group lifecycle
  addGroup,
  removeGroup,
  setActiveGroup,
  groupIds,
  getEditorContainer,
  onSplitRequest,
  onCloseGroupRequest,
  onGroupFocusRequest,
  onRunRequest,
  // panels / preview
  toggleSidebar,
  togglePreview,
  isPreviewVisible,
  setPreviewHtml,
  setPreviewLoading,
  scrollPreviewToSourceLine,
  setProblems,
  onGotoProblem,
  onPreviewToggled,
  onPreviewModeChanged,
  setPreviewMode,
  getPreviewMode,
  appendOutput,
  clearOutput,
  showOutput,
  setMaxOutputLines,
  showConfirm,
  showQuickPick,
  // tabs
  setTabs,
  onTabActivate,
  onTabCloseRequest,
  onNewTabRequest,
  onTabCloseOthersRequest,
  onTabCloseAllRequest,
  onTabOpenContainingFolderRequest,
  onTabCopyFullPathRequest,
  onTabCopyRelativePathRequest,
  onCopyTextRequest,
  onOpenFullHtmlRequest,
})
</script>
