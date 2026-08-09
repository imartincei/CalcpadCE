<template>
  <div class="app-layout" :class="{ resizing: isAnyDividerDragging }">
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
    <!-- UI mode is a data-entry view, so the preview takes the whole window and the
         editor steps aside until the user exits it. The tab strip stays, though —
         filling in one worksheet and moving to the next is the point of the mode, and
         without tabs there is no way to switch. `.work-area` turns into a column so
         that strip spans the window above the form; outside UI mode it is
         `display: contents` and the panes lay out as direct siblings.
         The wrapper and the panes share an indent level so adding it left the rest of
         the template untouched. -->
    <div class="work-area" :class="{ 'ui-mode': uiModeFullscreen }">
    <div class="editor-pane">
      <div v-show="!uiModeFullscreen" class="editor-toolbar" @contextmenu.prevent>
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
        <template v-for="(group, gi) in visibleGroups" :key="group.id">
          <div
            class="editor-group"
            :class="{ 'active-group': group.id === activeGroupId && isSplit && !uiModeFullscreen }"
            :style="editorGroupStyle(gi)"
            @mousedown="onGroupFocus(group.id)"
          >
            <!-- Tab strip (VS Code-style). Hidden until at least one tab is registered.
                 Grows a little taller only once its tabs actually overflow (tab-strip-overflowing),
                 so the horizontal scrollbar that then appears has room and doesn't sit over the
                 tabs' close buttons. Stays compact the rest of the time. -->
            <div
              v-if="group.tabs.length > 0"
              class="tab-strip"
              :class="{ 'tab-strip-overflowing': tabStripOverflowIds.has(group.id) }"
              role="tablist"
              :ref="el => setTabStripRef(group.id, el)"
              @contextmenu.prevent
            >
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
            <div v-show="!uiModeFullscreen" class="editor-container" :ref="el => setEditorRef(group.id, el)"></div>
          </div>
          <!-- Horizontal divider between the two stacked groups. -->
          <div
            v-if="gi === 0 && isSplit && !uiModeFullscreen"
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

      <!-- Bottom panel (Problems / Output) — reflects the ACTIVE group. -->
      <div v-if="bottomPanelOpen && !uiModeFullscreen" class="bottom-panel">
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
      <div v-show="!uiModeFullscreen" class="status-bar" @contextmenu.prevent>
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

    <!-- Draggable divider between the editor and the results pane. Hidden in UI mode,
         where the editor side collapses to just the tab strip and isn't resizable. -->
    <div
      v-if="previewVisible && !uiModeFullscreen"
      class="pane-divider"
      :class="{ dragging: draggingPreviewDivider }"
      @mousedown="onPreviewDividerMouseDown"
      title="Drag to resize"
      role="separator"
      aria-orientation="vertical"
    ></div>

    <!-- Holds the input form and the report beside it, so UI mode can lay them out as a
         row underneath the tab strip. Dissolved by `display: contents` otherwise. -->
    <div class="preview-area">
    <div v-if="previewVisible" class="preview-pane" :class="{ fullscreen: uiModeFullscreen }" :style="previewPaneStyle()">
      <div class="preview-toolbar" @contextmenu.prevent>
        <span>Results</span>
        <div class="preview-mode-group">
          <button
            v-if="resultModeAvailable('preview')"
            class="toolbar-btn"
            :class="{ active: resultMode === 'preview' }"
            @click="setResultMode('preview')"
            title="The document as written — #pre and #post both shown, entered #UI values ignored"
          >Preview</button>
          <button
            v-if="resultModeAvailable('unwrapped')"
            class="toolbar-btn"
            :class="{ active: resultMode === 'unwrapped' }"
            @click="setResultMode('unwrapped')"
            title="The source listing, with macros and includes resolved"
          >Unwrapped</button>
          <button
            class="toolbar-btn"
            :class="{ active: resultMode === 'ui' }"
            @click="setResultMode('ui')"
            title="#UI input form — #post content is hidden"
          >Input</button>
          <button
            v-if="resultModeAvailable('report')"
            class="toolbar-btn"
            :class="{ active: resultMode === 'report' }"
            @click="setResultMode('report')"
            title="The print layout — #pre hidden, entered #UI values applied"
          >Report</button>
        </div>
        <span class="spacer"></span>
        <button
          v-if="uiModeFullscreen"
          class="toolbar-btn"
          :class="{ active: uiPrintVisible }"
          @click="toggleUiPrint"
          title="Show the report the entered values produce"
        >Report</button>
        <button
          v-if="resultMode === 'report' || resultMode === 'ui'"
          class="toolbar-btn"
          @click="onPrintReport"
          title="Export the report — #pre hidden, entered #UI values applied — as a PDF"
        >Print PDF</button>
        <button
          v-if="resultMode === 'ui' && uiOverridesDirty"
          class="toolbar-btn"
          @click="onSaveUiOverrides"
          title="Write the entered values into the document so they survive a reload"
        >Save values</button>
        <template v-if="!activeTabIsCompiled">
          <button
            v-if="uiModeFullscreen"
            class="toolbar-btn"
            @click="exitUiMode"
            title="Return to the editor"
          >Exit input mode</button>
          <button v-else class="toolbar-btn" @click="togglePreview">✕</button>
        </template>
      </div>
      <!-- One preview iframe per editor group, stacked to mirror the editor
           split. allow-scripts is required so the injected console-interception
           script (and any user #HTML script) actually runs in the iframe.
           allow-same-origin is deliberately absent: paired with allow-scripts it
           would leave the frame holding this window's origin, so script in a
           #HTML block of an untrusted worksheet could walk window.parent into the
           app and, on desktop, into the Tauri IPC behind it. An opaque origin
           makes postMessage the only channel — see injectPreviewAgent. It also
           denies the frame localStorage/indexedDB, which throw on an opaque
           origin. -->
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
        <template v-for="(group, gi) in visibleGroups" :key="'pv-' + group.id">
          <iframe
            class="preview-frame"
            :class="{ 'active-group': group.id === activeGroupId && isSplit && !uiModeFullscreen }"
            :style="previewGroupStyle(gi)"
            :ref="el => setPreviewRef(group.id, el)"
            sandbox="allow-scripts"
          ></iframe>
          <div
            v-if="gi === 0 && isSplit && !uiModeFullscreen"
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

    <!-- Draggable divider between the input form and its report companion. -->
    <div
      v-if="uiModeFullscreen && uiPrintVisible"
      class="pane-divider"
      :class="{ dragging: draggingUiPrintDivider }"
      @mousedown="onUiPrintDividerMouseDown"
      title="Drag to resize"
      role="separator"
      aria-orientation="vertical"
    ></div>

    <!-- Report companion to the input form: the print layout of the document
         with the entered values applied, so the effect of each entry is visible
         while filling it in. Toggled from the input toolbar. -->
    <div v-if="uiModeFullscreen && uiPrintVisible" class="preview-pane ui-print-pane" :style="uiPrintPaneStyle()">
      <div class="preview-toolbar" @contextmenu.prevent>
        <span>Report</span>
        <span class="spacer"></span>
        <button class="toolbar-btn" @click="toggleUiPrint" title="Hide the report">✕</button>
      </div>
      <div class="preview-frames">
        <iframe
          v-if="activeGroup"
          class="preview-frame"
          :style="{ flex: '1 1 0', minHeight: '0' }"
          :ref="el => setUiPrintRef(activeGroup.id, el)"
          sandbox="allow-scripts"
        ></iframe>
      </div>
    </div>
    </div><!-- /.preview-area -->
    </div><!-- /.work-area -->

    <!-- Preview context menu. Layered over the iframe in place of the broken
         native WebView menu (see injectLineLinks). Positioned in viewport
         coordinates and kept outside the editor pane, which is hidden while the
         input form is fullscreen. -->
    <div
      v-if="previewContextMenu"
      class="tab-context-menu"
      :style="{ left: previewContextMenu.x + 'px', top: previewContextMenu.y + 'px' }"
      @mousedown.stop
      @click.stop
    >
      <button
        v-if="previewContextMenu.editable"
        class="tab-context-item"
        @click="onPreviewClipboard('cut')"
      >Cut (Ctrl+X)</button>
      <button
        class="tab-context-item"
        :disabled="!previewContextMenu.selection && !previewContextMenu.editable"
        @click="onPreviewClipboard('copy')"
      >Copy (Ctrl+C)</button>
      <button
        v-if="previewContextMenu.editable"
        class="tab-context-item"
        @click="onPreviewClipboard('paste')"
      >Paste (Ctrl+V)</button>
      <button class="tab-context-item" @click="onFindInPreview">Find… (Ctrl+F)</button>
      <button v-if="onOpenFullHtmlRequest" class="tab-context-item" @click="onOpenFullHtml">Open Full HTML</button>
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
import { extractBodyHtml, isCompiledPath } from 'calcpad-frontend'

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

/**
 * What the results pane shows.
 *
 * - 'preview' — the document as written: `#pre` and `#post` both rendered, and the
 *   values in the source rather than anything entered in the form.
 * - 'unwrapped' — the source listing, macros and includes resolved.
 * - 'ui' — `#UI` lines as interactive controls, `#post` hidden. Takes over the window
 *   and can show the report alongside (see `uiPrintVisible`).
 * - 'report' — the print layout: `#pre` hidden and the entered `#UI` values applied,
 *   without leaving the editor for the form.
 */
export type ResultMode = 'preview' | 'unwrapped' | 'ui' | 'report'

// Result mode is shared across both groups. `onGotoProblem` targets the
// active group's editor (main.ts resolves it).
const onGotoProblem = ref<((problem: ProblemItem) => void) | null>(null)
const onPreviewToggled = ref<((visible: boolean) => void) | null>(null)
const onResultModeChanged = ref<((mode: ResultMode) => void) | null>(null)
const resultMode = ref<ResultMode>('preview')

// True while the active document holds #UI values that aren't in its source yet.
const uiOverridesDirty = ref(false)
const onSaveUiOverridesRequest = ref<(() => void) | null>(null)
// Asked before the input form closes; false keeps it open (the user cancelled).
const onExitUiModeRequest = ref<(() => Promise<boolean>) | null>(null)
// "Print PDF" on the report/input toolbar; the host runs the report PDF export.
const onPrintReportRequest = ref<(() => void) | null>(null)

/** UI mode hands the whole window to the input form; only the tab strip stays. */
const uiModeFullscreen = computed(() => previewVisible.value && resultMode.value === 'ui')

// The report pane beside the input form, toggled like the preview pane itself.
const uiPrintVisible = ref(false)
const onUiPrintToggled = ref<((visible: boolean) => void) | null>(null)

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

/**
 * A compiled worksheet has no readable source: it is distributed to be filled in, and
 * the editor holding it is locked. Filling in the form is the only thing to do with
 * one, so input is the only mode offered — the buttons for the others are left out
 * rather than shown disabled. The report is still reachable: it renders beside the
 * form (see `uiPrintVisible`), which is where it belongs for a worksheet being
 * filled in.
 */
const COMPILED_RESULT_MODES: ResultMode[] = ['ui']
const activeTabIsCompiled = computed(() => {
  const filePath = activeGroup.value?.tabs.find(t => t.isActive)?.filePath
  return !!filePath && isCompiledPath(filePath)
})
function resultModeAvailable(mode: ResultMode): boolean {
  return !activeTabIsCompiled.value || COMPILED_RESULT_MODES.includes(mode)
}
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
// Iframes of the report pane shown beside the input form in UI mode.
const uiPrintEls = new Map<string, HTMLIFrameElement>()
// Last full (unstripped) HTML rendered per group, kept for "Open Full HTML".
const previewHtmlByGroup = new Map<string, string>()
// Where the user was in each frame's #UI form — focused control, caret, datagrid
// cell, scroll offsets. Held here because the frame cannot keep it itself: a
// re-render assigns srcdoc, and the fresh browsing context that creates has
// neither the previous window nor, on an opaque origin, sessionStorage. Posted by
// the backend's #UI script and seeded back into the next render.
const uiPositionByFrame = new Map<string, unknown>()
// Scroll offsets per frame *and* document, so re-rendering a document lands back
// where the user was while switching tabs still starts at the top. Covers the
// frames the backend's #UI script does not: preview, unwrapped and report modes,
// plus the report companion beside the input form. The input form itself keeps
// using that script, which restores the focused control and caret as well.
const scrollByFrameDoc = new Map<string, { x: number; y: number }>()
const docKeyByFrame = new Map<string, string>()

function scrollKey(frameId: string, docKey: string): string {
  return frameId + ' ' + docKey
}

function setEditorRef(id: string, el: unknown): void {
  if (el instanceof HTMLElement) editorEls.set(id, el)
  else editorEls.delete(id)
}

// Tracks which tab strips actually overflow horizontally, so only those grow taller
// to make room for the scrollbar (see the .tab-strip-overflowing CSS). A ResizeObserver
// catches both causes of a strip's overflow changing: the window resizing and tabs being
// added/closed/renamed (each changes scrollWidth/clientWidth without necessarily firing
// any other event we already listen for).
const tabStripEls = new Map<string, HTMLElement>()
const tabStripElIds = new WeakMap<Element, string>()
const tabStripOverflowIds = ref<Set<string>>(new Set())
let tabStripResizeObserver: ResizeObserver | null = null

function setTabStripOverflow(id: string, overflowing: boolean): void {
  if (tabStripOverflowIds.value.has(id) === overflowing) return
  const next = new Set(tabStripOverflowIds.value)
  if (overflowing) next.add(id)
  else next.delete(id)
  tabStripOverflowIds.value = next
}

function checkTabStripOverflow(id: string): void {
  const el = tabStripEls.get(id)
  if (!el) return
  setTabStripOverflow(id, el.scrollWidth > el.clientWidth + 1)
}

function setTabStripRef(id: string, el: unknown): void {
  const prev = tabStripEls.get(id)
  if (prev && prev !== el) {
    tabStripResizeObserver?.unobserve(prev)
    tabStripElIds.delete(prev)
  }
  if (el instanceof HTMLElement) {
    tabStripEls.set(id, el)
    tabStripElIds.set(el, id)
    tabStripResizeObserver?.observe(el)
    checkTabStripOverflow(id)
  } else {
    tabStripEls.delete(id)
    setTabStripOverflow(id, false)
  }
}
function setPreviewRef(id: string, el: unknown): void {
  if (el instanceof HTMLIFrameElement) previewEls.set(id, el)
  else previewEls.delete(id)
}
function setUiPrintRef(id: string, el: unknown): void {
  if (el instanceof HTMLIFrameElement) uiPrintEls.set(id, el)
  else uiPrintEls.delete(id)
}
function getEditorContainer(id: string): HTMLElement | null {
  return editorEls.get(id) ?? null
}

// ---- Split ratio (top group's fraction of the stack height) ----
const editorSplitRatio = ref<number>(0.5)
const draggingEditorDivider = ref(false)

/**
 * The groups the editor side and the preview side each render. The input form is a
 * single-document view: a split would stack two forms, and with the same file open in
 * both they would share one set of entered values and fight over it. So UI mode renders
 * only the active group — one form, and one tab strip above it.
 */
const visibleGroups = computed(() =>
  uiModeFullscreen.value ? [activeGroup.value].filter(Boolean) : groups.value)

function previewGroupStyle(index: number): Record<string, string> {
  if (uiModeFullscreen.value) return { flex: '1 1 0', minHeight: '0' }
  return editorGroupStyle(index)
}

function editorGroupStyle(index: number): Record<string, string> {
  // Only the tab strip is left in UI mode, so the group hugs it instead of filling.
  if (uiModeFullscreen.value) return { flex: '0 0 auto', minHeight: '0' }
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

// ---- Pane split ratios (editor ↔ results, and input form ↔ report) ----
const PANE_RATIO_MIN = 0.15
const PANE_RATIO_MAX = 0.85

function loadPaneRatio(key: string, fallback: number): number {
  const raw = parseFloat(localStorage.getItem(key) ?? '')
  if (!Number.isFinite(raw)) return fallback
  return Math.min(PANE_RATIO_MAX, Math.max(PANE_RATIO_MIN, raw))
}

const previewWidthRatio = ref<number>(loadPaneRatio('calcpad.previewWidthRatio', 0.45))
const draggingPreviewDivider = ref(false)
const uiPrintWidthRatio = ref<number>(loadPaneRatio('calcpad.uiPrintWidthRatio', 0.5))
const draggingUiPrintDivider = ref(false)

// True while any drag-to-resize is in progress. Drives `.app-layout.resizing`, which
// disables pointer-events on the preview/report iframes: without it, the moment the
// cursor crosses into an iframe mid-drag the mousemove/mouseup listeners below — bound
// to the outer `window` — stop receiving events (they fire in the iframe's own window
// instead), so the drag never sees its mouseup and the pane appears stuck mid-resize.
const isAnyDividerDragging = computed(() =>
  isResizing.value || draggingEditorDivider.value || draggingPreviewDivider.value || draggingUiPrintDivider.value)

function previewPaneStyle(): Record<string, string> | undefined {
  if (uiModeFullscreen.value) {
    return uiPrintVisible.value ? { flex: `0 0 ${(1 - uiPrintWidthRatio.value) * 100}%` } : undefined
  }
  return { width: `${previewWidthRatio.value * 100}%` }
}

function uiPrintPaneStyle(): Record<string, string> {
  return { flex: `0 0 ${uiPrintWidthRatio.value * 100}%` }
}

// Dragged against `.app-layout`'s box rather than the divider's own parent
// (`.work-area`), which is `display: contents` and so has no box of its own.
function onPreviewDividerMouseDown(e: MouseEvent): void {
  e.preventDefault()
  draggingPreviewDivider.value = true
  const container = (e.currentTarget as HTMLElement).closest('.app-layout') as HTMLElement | null
  if (!container) return
  const rect = container.getBoundingClientRect()
  let moved = false
  const onMove = (ev: MouseEvent) => {
    moved = true
    const frac = (rect.right - ev.clientX) / rect.width
    previewWidthRatio.value = Math.min(PANE_RATIO_MAX, Math.max(PANE_RATIO_MIN, frac))
  }
  const onUp = () => {
    draggingPreviewDivider.value = false
    window.removeEventListener('mousemove', onMove)
    window.removeEventListener('mouseup', onUp)
    if (moved) localStorage.setItem('calcpad.previewWidthRatio', String(previewWidthRatio.value))
  }
  window.addEventListener('mousemove', onMove)
  window.addEventListener('mouseup', onUp)
}

function onUiPrintDividerMouseDown(e: MouseEvent): void {
  e.preventDefault()
  draggingUiPrintDivider.value = true
  const container = (e.currentTarget as HTMLElement).parentElement
  if (!container) return
  const rect = container.getBoundingClientRect()
  let moved = false
  const onMove = (ev: MouseEvent) => {
    moved = true
    const frac = (rect.right - ev.clientX) / rect.width
    uiPrintWidthRatio.value = Math.min(PANE_RATIO_MAX, Math.max(PANE_RATIO_MIN, frac))
  }
  const onUp = () => {
    draggingUiPrintDivider.value = false
    window.removeEventListener('mousemove', onMove)
    window.removeEventListener('mouseup', onUp)
    if (moved) localStorage.setItem('calcpad.uiPrintWidthRatio', String(uiPrintWidthRatio.value))
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
  uiPositionByFrame.delete(id)
  uiPositionByFrame.delete(UI_PRINT_FRAME + id)
  docKeyByFrame.delete(id)
  docKeyByFrame.delete(UI_PRINT_FRAME + id)
  for (const key of [...scrollByFrameDoc.keys()]) {
    if (key.startsWith(id + ' ') || key.startsWith(UI_PRINT_FRAME + id + ' ')) {
      scrollByFrameDoc.delete(key)
    }
  }
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
// Read counterpart, set by the desktop host only. Its presence is what marks a
// WebView whose frames have no usable native clipboard, so the preview takes
// over its own Ctrl+C/X/V (see injectPreviewAgent).
const onClipboardReadRequest = ref<(() => Promise<string>) | null>(null)

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
  /** Owning editor group — what find and "Open Full HTML" act on. */
  groupId: string
  /** The frame actually clicked; differs from groupId for a report pane. */
  frameId: string
  selection: string
  editable: boolean
}
const previewContextMenu = ref<PreviewContextMenuState | null>(null)

function closePreviewContextMenu(): void {
  previewContextMenu.value = null
}

function onPreviewClipboard(action: PreviewClipboardAction): void {
  const frameId = previewContextMenu.value?.frameId
  closePreviewContextMenu()
  if (frameId) void runPreviewClipboardAction(frameId, action)
}

// ---- Clipboard inside the preview (#UI input form) ----
type PreviewClipboardAction = 'cut' | 'copy' | 'paste'

// Clipboard-capable frames are addressed by group, with the report pane beside
// the input form distinguished by a prefix since it shares its group's id.
const UI_PRINT_FRAME = 'ui-print:'

function clipboardFrame(frameId: string): HTMLIFrameElement | null {
  return frameId.startsWith(UI_PRINT_FRAME)
    ? uiPrintEls.get(frameId.slice(UI_PRINT_FRAME.length)) ?? null
    : previewEls.get(frameId) ?? null
}

async function readClipboardText(): Promise<string> {
  if (onClipboardReadRequest.value) return await onClipboardReadRequest.value()
  try { return await navigator.clipboard.readText() } catch { return '' }
}

/** Sends a command to a preview frame's injected agent. See injectPreviewAgent. */
function postToPreviewFrame(frameId: string, message: Record<string, unknown>): void {
  // The frame is sandboxed to an opaque origin, so '*' is the only targetOrigin
  // that can address it. That is safe in this direction: these commands carry no
  // secrets, and the frame is the untrusted party — the boundary being defended
  // is the other way round, in onPreviewWindowMessage.
  clipboardFrame(frameId)?.contentWindow?.postMessage(message, '*')
}

/**
 * Runs a clipboard action against whatever the frame is focused on — a value
 * field, a datagrid selection, or the document selection for a plain copy.
 * Needed because the desktop WebView leaves the frame's own clipboard inert.
 *
 * The frame does the work: reaching into its DOM from here would require
 * allow-same-origin on the iframe, which would also hand any script in an
 * untrusted worksheet the run of this window (and, on desktop, the Tauri IPC).
 * Paste text is resolved first because only the host can read the clipboard.
 */
async function runPreviewClipboardAction(frameId: string, action: PreviewClipboardAction): Promise<void> {
  const text = action === 'paste' ? await readClipboardText() : undefined
  if (action === 'paste' && !text) return
  if (action !== 'paste') armClipboardCopy(frameId)
  postToPreviewFrame(frameId, { type: 'cpdClipboardExec', action, text })
}

// A copy/cut reply is only honoured just after the host asked for one. The frame
// supplies the text, so without this an untrusted worksheet could post
// previewClipboardText on a timer and quietly own the user's clipboard.
const clipboardCopyArmed = new Map<string, number>()
const CLIPBOARD_REPLY_WINDOW_MS = 2000

function armClipboardCopy(frameId: string): void {
  clipboardCopyArmed.set(frameId, performance.now())
}

function takeClipboardCopyArmed(frameId: string): boolean {
  const at = clipboardCopyArmed.get(frameId)
  clipboardCopyArmed.delete(frameId)
  return at !== undefined && performance.now() - at < CLIPBOARD_REPLY_WINDOW_MS
}

// One Ctrl+V can reach here twice — from the frame's key handler and from the
// host's menu accelerator — depending on whether the WebView consumed the key,
// and a paste that lands twice inserts the text twice. Only the key-driven
// routes are deduplicated; a context-menu click is always meant.
let lastPreviewClipboard = { action: '', at: 0 }

function isRepeatedPreviewClipboard(action: PreviewClipboardAction): boolean {
  const at = performance.now()
  const repeated = action === lastPreviewClipboard.action && at - lastPreviewClipboard.at < 250
  lastPreviewClipboard = { action, at }
  return repeated
}

/**
 * Routes a host's native Edit menu into a focused preview / report frame.
 * Returns false when neither holds focus, leaving the host to handle the action
 * itself.
 */
function runFocusedPreviewClipboardAction(action: PreviewClipboardAction): boolean {
  const focused = document.activeElement
  for (const [groupId, el] of previewEls) {
    if (el !== focused) continue
    if (!isRepeatedPreviewClipboard(action)) void runPreviewClipboardAction(groupId, action)
    return true
  }
  for (const [groupId, el] of uiPrintEls) {
    if (el !== focused) continue
    if (!isRepeatedPreviewClipboard(action)) void runPreviewClipboardAction(UI_PRINT_FRAME + groupId, action)
    return true
  }
  return false
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
  // Search runs against a group's preview iframe, so a frame without one — the report
  // pane beside the input form — has nothing to open the widget over.
  if (!previewEls.has(groupId)) return
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

// Find runs inside the frame (see injectPreviewAgent): the marking walk needs the
// preview's DOM, and reaching it from here would mean allow-same-origin on a frame
// that renders untrusted worksheet HTML. The host keeps only the counts, which the
// frame reports back as cpdFindResult.
function clearPreviewMarks(groupId: string): void {
  postToPreviewFrame(groupId, { type: 'cpdFindClear' })
}

function applyPreviewSearch(): void {
  const f = previewFind.value
  if (!f) return
  postToPreviewFrame(f.groupId, { type: 'cpdFindApply', query: f.query })
}

function previewFindStep(dir: number): void {
  const f = previewFind.value
  if (!f || f.total === 0) return
  postToPreviewFrame(f.groupId, { type: 'cpdFindStep', dir })
}

/** Applies the match counts a frame reports after running a find command. */
function onPreviewFindResult(groupId: string, total: number, current: number): void {
  const f = previewFind.value
  if (!f || f.groupId !== groupId) return
  f.total = total
  f.current = current
}

function onDocumentInteractionForTabMenu(e: MouseEvent | KeyboardEvent): void {
  if (e instanceof KeyboardEvent && e.key !== 'Escape') return
  closeTabContextMenu()
  closeProblemsContextMenu()
  closeOutputContextMenu()
  closePreviewContextMenu()
}

/**
 * The frame a message came from, or null if it was not one of ours. Preview
 * frames are sandboxed to an opaque origin, so `e.origin` is the string "null"
 * for all of them and cannot tell them apart from any other opaque sender —
 * window identity is the check that means something. Anything else reaching this
 * listener (an ad frame in an embedded page, a popup that kept a handle on this
 * window) is dropped before it can drive the clipboard or the context menu.
 */
function senderFrameId(source: MessageEventSource | null): string | null {
  if (!source) return null
  for (const [groupId, el] of previewEls) {
    if (el.contentWindow === source) return groupId
  }
  for (const [groupId, el] of uiPrintEls) {
    if (el.contentWindow === source) return UI_PRINT_FRAME + groupId
  }
  return null
}

/**
 * True if a message came from one of this app's preview frames. Exposed so the
 * host's own listener in main.ts can apply the same check to the frame-originated
 * messages it handles (previewConsole, navigateToLine, uiValueChange).
 */
function isPreviewFrameSource(source: MessageEventSource | null): boolean {
  return senderFrameId(source) !== null
}

function onPreviewWindowMessage(e: MessageEvent): void {
  const data = e.data
  if (!data || typeof data.type !== 'string') return
  const frameId = senderFrameId(e.source)
  if (!frameId) return
  // A frame may only speak for itself: the ids it sends are used to route
  // clipboard and find commands, so taking them on trust would let one preview
  // drive another's.
  const groupId = frameId.startsWith(UI_PRINT_FRAME)
    ? frameId.slice(UI_PRINT_FRAME.length)
    : frameId

  if (data.type === 'previewContextMenuDismiss') {
    closePreviewContextMenu()
    return
  }
  if (data.type === 'previewContextMenu') {
    const iframe = clipboardFrame(frameId)
    if (!iframe) return
    const rect = iframe.getBoundingClientRect()
    previewContextMenu.value = {
      x: rect.left + (Number(data.x) || 0),
      y: rect.top + (Number(data.y) || 0),
      groupId,
      frameId,
      selection: typeof data.selection === 'string' ? data.selection : '',
      // Reported by the frame, which is the only side that can see what it has
      // focused now that the host cannot reach into its document.
      editable: data.editable === true,
    }
    return
  }
  if (data.type === 'previewClipboardAction') {
    const action = data.action as PreviewClipboardAction
    if (!isRepeatedPreviewClipboard(action)) void runPreviewClipboardAction(frameId, action)
    return
  }
  // Copy/cut text the frame extracted, on its way to the host-owned clipboard.
  // Only accepted as the reply to a copy the host just requested.
  if (data.type === 'previewClipboardText') {
    if (!takeClipboardCopyArmed(frameId)) return
    if (typeof data.text === 'string') copyText(data.text)
    return
  }
  if (data.type === 'cpdFindResult') {
    onPreviewFindResult(groupId, Number(data.total) || 0, Number(data.current) || 0)
    return
  }
  if (data.type === 'cpdUiState') {
    if (data.state && typeof data.state === 'object') uiPositionByFrame.set(frameId, data.state)
    return
  }
  if (data.type === 'cpdScrollState') {
    const docKey = docKeyByFrame.get(frameId)
    if (docKey === undefined) return
    scrollByFrameDoc.set(scrollKey(frameId, docKey), {
      x: Number(data.x) || 0,
      y: Number(data.y) || 0,
    })
    return
  }
  if (data.type === 'previewFindOpen') {
    openPreviewFind(groupId)
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

async function togglePreview(): Promise<void> {
  if (previewVisible.value && resultMode.value === 'ui' && !await confirmExitUiMode()) return
  previewVisible.value = !previewVisible.value
  onPreviewToggled.value?.(previewVisible.value)
}

function isPreviewVisible(): boolean {
  return previewVisible.value
}

async function setResultMode(mode: ResultMode): Promise<void> {
  if (resultMode.value === mode) return
  // Guarded here rather than only on the buttons, so the native View menu, the
  // restored-on-startup mode, and the auto-switch to unwrapped are all covered.
  if (!resultModeAvailable(mode)) return
  if (resultMode.value === 'ui' && !await confirmExitUiMode()) return
  resultMode.value = mode
  onResultModeChanged.value?.(mode)
}

/**
 * Runs the host's leave-input-mode handler, which prompts for unsaved values and
 * then discards them. Returns false when the user cancelled and the form should
 * stay open.
 */
async function confirmExitUiMode(): Promise<boolean> {
  return await onExitUiModeRequest.value?.() ?? true
}

function getResultMode(): ResultMode {
  return resultMode.value
}

function setUiOverridesDirty(dirty: boolean): void {
  uiOverridesDirty.value = dirty
}

function onSaveUiOverrides(): void {
  onSaveUiOverridesRequest.value?.()
}

function onPrintReport(): void {
  onPrintReportRequest.value?.()
}

/** Leaves the fullscreen input form for the report view, bringing the editor back. */
function exitUiMode(): void {
  void setResultMode('report')
}

function setPreviewLoading(groupId: string, loading: boolean): void {
  if (loading) previewLoadingGroups.value.add(groupId)
  else previewLoadingGroups.value.delete(groupId)
}

// Assigning srcdoc forces a real navigation (a fresh browsing context) rather than
// rewriting the existing document in place. Reusing the same document via
// doc.open()/write()/close() on every render left WebKit's per-frame scrolling state
// prone to desync after certain content swaps (e.g. a resultMode change) -- once that
// happened, the preview's scrollbar was gone for good until the iframe itself was
// recreated. A fresh navigation each render sidesteps that entirely.
function setPreviewHtml(groupId: string, html: string, scrollToLine?: number, docKey = ''): void {
  const frame = previewEls.get(groupId)
  if (!frame) return
  // In input mode the backend's #UI script owns the position, restoring the focused
  // control and caret along with the offset; elsewhere it is not injected at all,
  // so the agent carries the scroll.
  const isUi = resultMode.value === 'ui'
  docKeyByFrame.set(groupId, docKey)
  let out = injectPreviewAgent(
    injectLineLinks(html, scrollToLine, groupId, undefined, !isUi),
    groupId,
    !!onClipboardReadRequest.value,
    !isUi,
  )
  out = injectPreviewConsole(out, groupId)
  out = injectUiPosition(out, groupId)
  // An explicit line target is a navigation the user asked for, and outranks
  // returning them to where they were.
  if (!isUi && scrollToLine === undefined) out = injectSavedScroll(out, groupId, docKey)
  frame.srcdoc = out
  previewHtmlByGroup.set(groupId, html)
  setPreviewHtmlOutput(groupId, html)
}

/**
 * Writes the report that accompanies the input form. It carries no controls, so it needs
 * no #UI event script, and nothing in it logs, so it needs no console interception. It does
 * get the clipboard bridge — copying a result out of it is otherwise dead — but not the
 * hover line links: input mode hides the editor they would navigate to. The report shown
 * in report mode, where the editor is on screen, keeps them.
 */
function setUiPrintHtml(groupId: string, html: string, docKey = ''): void {
  const frame = uiPrintEls.get(groupId)
  if (!frame) return
  const frameId = UI_PRINT_FRAME + groupId
  docKeyByFrame.set(frameId, docKey)
  frame.srcdoc = injectSavedScroll(
    injectPreviewAgent(
      injectLineLinks(html, undefined, groupId, frameId, false),
      frameId,
      !!onClipboardReadRequest.value,
      true,
    ),
    frameId,
    docKey,
  )
}

function isUiPrintVisible(): boolean {
  return uiPrintVisible.value
}

function toggleUiPrint(): void {
  uiPrintVisible.value = !uiPrintVisible.value
  onUiPrintToggled.value?.(uiPrintVisible.value)
}

// Mirrors the last rendered preview into the 'html' output channel (body only,
// so it matches what the print/export path also strips out) for debugging.
// Each render replaces the group's prior line rather than appending, since old
// output is immediately stale.
function setPreviewHtmlOutput(groupId: string, html: string): void {
  outputLines.value = outputLines.value.filter(l => !(l.channel === 'html' && l.groupId === groupId))
  appendOutput('info', extractBodyHtml(html), 'html', groupId)
}

// Scroll a group's results to a source line (editor/TOC -> preview sync). Posts to
// the listener injected by injectLineLinks; no-op if that frame isn't shown. The
// report beside the input form gets it too, so navigating in input mode moves both
// panes together -- it keeps the sync listener even though it drops the hover arrows.
// `exact` disables the nearest-preceding-line fallback: a TOC heading that fell inside
// a hidden #pre/#post block has no element to land on, and the fallback would jump to
// an unrelated line, so TOC navigation should do nothing rather than land somewhere odd.
function scrollPreviewToSourceLine(groupId: string, line: number, exact = false): void {
  const msg = { type: 'scrollPreviewToLine', line, exact }
  previewEls.get(groupId)?.contentWindow?.postMessage(msg, '*')
  uiPrintEls.get(groupId)?.contentWindow?.postMessage(msg, '*')
}

// Inject the line-link behaviour ported from vscode-calcpad. Posted messages
// carry `groupId` so main.ts routes navigation to the group that owns this
// preview (see App.vue's per-group iframes). The scrollbar/line-focus/find-
// highlight CSS these scripts rely on now lives in the backend's template.html
// since it's static and applies to the print/export path too; only the
// per-render behaviour (which needs groupId/scrollToLine) is injected here.
//
// `frameId` addresses the iframe itself and only differs for the report pane beside
// the input form, which shares its group's id: navigation still targets the group's
// editor, while the menu and find widget belong to whichever frame was clicked.
//
// `lineLinks` turns just the hover arrows off; the context menu, find, error chips and
// editor->preview sync stay. Both the input form and the report shown beside it drop
// them — the editor they navigate to isn't on screen in input mode.
function injectLineLinks(
  html: string,
  scrollToLine: number | undefined,
  groupId: string,
  frameId: string = groupId,
  lineLinks: boolean = true,
): string {
  const scrollTarget = typeof scrollToLine === 'number' ? String(scrollToLine) : 'null'
  const gid = JSON.stringify(groupId)
  const fid = JSON.stringify(frameId)
  const body = [
    "document.addEventListener('DOMContentLoaded', function() {",
    "  var GROUP_ID = " + gid + ";",
    "  var FRAME_ID = " + fid + ";",
    "  var post = function(line, lineType) {",
    "    try { window.parent.postMessage({ type: 'navigateToLine', line: line, lineType: lineType, groupId: GROUP_ID }, '*'); } catch (e) {}",
    "  };",
    // Replace WebKitGTK's broken native menu with the parent's custom menu.
    // pointerdown posts a dismiss first, then contextmenu reopens, so a
    // right-click nets an open menu and any other click dismisses it.
    "  var postMenu = function(type, e) {",
    "    var sel = ''; try { sel = String(window.getSelection() || ''); } catch (_e) {}",
    // Whether the menu offers cut/paste depends on what this frame has focused,
    // which only this side can see. injectPreviewAgent publishes the probe.
    "    var editable = false;",
    "    try { editable = !!(window.__calcpadPreviewEditable && window.__calcpadPreviewEditable()); } catch (_e1) {}",
    "    try { window.parent.postMessage({ type: type, x: e ? e.clientX : 0, y: e ? e.clientY : 0, selection: sel, editable: editable, groupId: FRAME_ID }, '*'); } catch (_e2) {}",
    "  };",
    // Datagrids bring their own menu, so a right-click inside one is left alone.
    "  document.addEventListener('contextmenu', function(e) {",
    "    var t = e.target;",
    "    if (t && t.closest && t.closest('.jss_container, .calcpad-ui-datagrid')) return;",
    "    e.preventDefault(); postMenu('previewContextMenu', e);",
    "  });",
    "  document.addEventListener('pointerdown', function() { postMenu('previewContextMenuDismiss', null); });",
    "  document.addEventListener('keydown', function(e) {",
    "    if ((e.ctrlKey || e.metaKey) && (e.key === 'f' || e.key === 'F')) {",
    "      e.preventDefault();",
    "      try { window.parent.postMessage({ type: 'previewFindOpen', groupId: FRAME_ID }, '*'); } catch (_e) {}",
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
    ...(lineLinks ? [
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
    ] : []),
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
    "  function focusPreviewLine(line, exact) {",
    "    if (typeof line !== 'number' || isNaN(line)) return;",
    "    var target = document.querySelector('[data-source-line=\"' + line + '\"]');",
    "    if (!target) {",
    "      var anchor = document.querySelector('a.line-num[data-text=\"' + line + '\"]');",
    "      if (anchor) target = anchor.closest('.line-text') || anchor;",
    "    }",
    "    if (!target && !exact) {",
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
    "    if (d && d.type === 'scrollPreviewToLine') focusPreviewLine(d.line, d.exact);",
    "  });",
    "});",
  ].join('\n')
  return insertHeadScript(html, body)
}

/**
 * Seeds the position the frame reported before this render into the new document,
 * where the backend's #UI script picks it up as the fallback its readState()
 * already looks for. Consumed once, matching that script's own semantics: a stale
 * position must not steal focus when a document is opened fresh rather than
 * re-rendered. Goes in <head>, so it runs before the #UI script at </body>.
 */
function injectUiPosition(html: string, frameId: string): string {
  const state = uiPositionByFrame.get(frameId)
  if (state === undefined) return html
  uiPositionByFrame.delete(frameId)
  // The state carries a control key taken from the document, so close any tag the
  // serialization could otherwise open.
  const json = JSON.stringify(state).replace(/</g, '\\u003c')
  return insertHeadScript(html, 'window.__calcpadUiPosition = ' + json + ';')
}

/**
 * Seeds the offset this frame last reported for this document, so the agent can
 * scroll back once the replacement has laid out. Unlike the #UI position this is
 * not consumed on read: a render that follows one the user did not scroll through
 * should still land where they were.
 */
function injectSavedScroll(html: string, frameId: string, docKey: string): string {
  const pos = scrollByFrameDoc.get(scrollKey(frameId, docKey))
  if (!pos || (pos.x === 0 && pos.y === 0)) return html
  return insertHeadScript(html, 'window.__calcpadScrollPosition = ' + JSON.stringify(pos) + ';')
}

function insertHeadScript(html: string, body: string): string {
  const script = '<' + 'script>' + body + '</' + 'script>'
  const headIdx = html.indexOf('<head>')
  if (headIdx >= 0) {
    return html.slice(0, headIdx + 6) + script + html.slice(headIdx + 6)
  }
  return script + html
}

/**
 * The frame's half of find-in-preview and the clipboard bridge.
 *
 * Both used to run in the host, reaching through `iframe.contentDocument`. That
 * needs `allow-same-origin`, which — combined with the `allow-scripts` the
 * rendered report requires — is not a sandbox at all: the frame keeps this
 * window's origin, so script in a `#HTML` block of an untrusted worksheet could
 * walk `window.parent` into the app, and on desktop into the Tauri IPC. Keeping
 * the DOM work on this side lets the frame hold an opaque origin, leaving
 * postMessage as the only channel across.
 *
 * `interceptClipboard` mirrors the old injection condition: only a host that has
 * offered a clipboard to route through takes the frame's Ctrl+C/X/V, since in a
 * browser the frame's own handling is the better one. Capture phase with
 * propagation stopped, so the datagrid library never sees the keys either — its
 * Ctrl+X would clear the cells before the copy could read them, and its paste
 * depends on an event that never fires.
 */
function injectPreviewAgent(
  html: string,
  frameId: string,
  interceptClipboard: boolean,
  ownScroll: boolean,
): string {
  const id = JSON.stringify(frameId)
  const body = [
    '(function() {',
    '  var FRAME_ID = ' + id + ';',
    '  if (window.__calcpadAgentReady) return;',
    '  window.__calcpadAgentReady = true;',
    "  var send = function(msg) { msg.frameId = FRAME_ID; try { window.parent.postMessage(msg, '*'); } catch (_e) {} };",
    '',
    // ---- focus probes ----
    // A datagrid keeps its position in the library rather than in the focused
    // element, so its own inputs (the hidden copy textarea, an open cell editor)
    // belong to the sheet path instead.
    '  function activeInput() {',
    '    var el = document.activeElement;',
    "    if (!el || (el.tagName !== 'INPUT' && el.tagName !== 'TEXTAREA')) return null;",
    "    if (el.closest && el.closest('.calcpad-ui-datagrid')) return null;",
    '    return el;',
    '  }',
    '  function activeSheet() {',
    '    var js = window.jspreadsheet;',
    '    var sheet = js && js.current;',
    '    return sheet && sheet.selectedCell ? sheet : null;',
    '  }',
    // Read by the context-menu post in injectLineLinks, which runs later.
    '  window.__calcpadPreviewEditable = function() {',
    '    return !!(activeInput() || activeSheet());',
    '  };',
    '',
    // ---- clipboard ----
    // The #UI script filters keystrokes on 'input' and commits the field on
    // 'change'; a programmatic edit raises neither. An edit that has not produced
    // a number is left uncommitted rather than reverted, so the text stays put.
    '  var UI_NUMBER = /^[-+]?(\\d+\\.?\\d*|\\.\\d+)$/;',
    '  function commit(input) {',
    "    input.dispatchEvent(new Event('input', { bubbles: true }));",
    "    if (UI_NUMBER.test(input.value.trim())) input.dispatchEvent(new Event('change', { bubbles: true }));",
    '  }',
    '  function runClipboard(action, text) {',
    '    var input = activeInput();',
    '    if (input) {',
    '      var start = input.selectionStart || 0;',
    '      var end = input.selectionEnd == null ? start : input.selectionEnd;',
    "      if (action === 'paste') {",
    "        var t = (text || '').trim();",
    '        if (!t) return;',
    "        input.setRangeText(t, start, end, 'end');",
    '        commit(input);',
    '        return;',
    '      }',
    '      if (end === start) return;',
    "      send({ type: 'previewClipboardText', text: input.value.substring(start, end) });",
    "      if (action === 'cut') { input.setRangeText('', start, end, 'end'); commit(input); }",
    '      return;',
    '    }',
    '    var sheet = activeSheet();',
    '    if (sheet) {',
    '      var sel = sheet.selectedCell;',
    '      var x1 = Math.min(sel[0], sel[2]), x2 = Math.max(sel[0], sel[2]);',
    '      var y1 = Math.min(sel[1], sel[3]), y2 = Math.max(sel[1], sel[3]);',
    "      if (action === 'paste') { if (text) sheet.paste(x1, y1, text); return; }",
    '      var data = sheet.getData();',
    '      var rows = [];',
    "      for (var r = y1; r <= y2; r++) rows.push(data[r].slice(x1, x2 + 1).join('\\t'));",
    "      send({ type: 'previewClipboardText', text: rows.join('\\n') });",
    // Every cell is an element of a matrix literal, so a cleared one is a zero.
    "      if (action === 'cut')",
    '        for (var r2 = y1; r2 <= y2; r2++)',
    "          for (var c = x1; c <= x2; c++) sheet.setValueFromCoords(c, r2, '0');",
    '      return;',
    '    }',
    "    if (action === 'paste') return;",
    "    var selection = '';",
    "    try { selection = String(window.getSelection() || ''); } catch (_e) {}",
    "    if (selection) send({ type: 'previewClipboardText', text: selection });",
    '  }',
    ...(interceptClipboard ? [
    "  var ACTIONS = { c: 'copy', x: 'cut', v: 'paste' };",
    "  document.addEventListener('keydown', function(e) {",
    '    if ((!e.ctrlKey && !e.metaKey) || e.altKey || e.shiftKey) return;',
    "    var action = ACTIONS[(e.key || '').toLowerCase()];",
    '    if (!action) return;',
    '    e.preventDefault();',
    '    e.stopImmediatePropagation();',
    // The host resolves paste text and echoes the action back as cpdClipboardExec.
    "    send({ type: 'previewClipboardAction', action: action });",
    '  }, true);',
    ] : []),
    '',
    // ---- find ----
    '  var matches = [];',
    '  var current = 0;',
    '  function clearMarks() {',
    "    var marks = document.querySelectorAll('mark.cpd-find');",
    '    for (var i = 0; i < marks.length; i++) {',
    '      var m = marks[i];',
    '      var parent = m.parentNode;',
    '      if (!parent) continue;',
    "      parent.replaceChild(document.createTextNode(m.textContent || ''), m);",
    '      parent.normalize();',
    '    }',
    '    matches = [];',
    '    current = 0;',
    '  }',
    '  function highlight() {',
    '    for (var i = 0; i < matches.length; i++) matches[i].classList.remove(\'cpd-find-current\');',
    '    var target = matches[current];',
    '    if (!target) return;',
    "    target.classList.add('cpd-find-current');",
    "    target.scrollIntoView({ block: 'center' });",
    '  }',
    '  function applyFind(query) {',
    '    clearMarks();',
    '    if (!query || !document.body) { send({ type: \'cpdFindResult\', total: 0, current: 0 }); return; }',
    '    var needle = query.toLowerCase();',
    '    var walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT, {',
    '      acceptNode: function(node) {',
    '        var p = node.parentElement;',
    '        if (!node.nodeValue || !p) return NodeFilter.FILTER_REJECT;',
    "        if (p.tagName === 'SCRIPT' || p.tagName === 'STYLE') return NodeFilter.FILTER_REJECT;",
    '        return node.nodeValue.toLowerCase().indexOf(needle) !== -1',
    '          ? NodeFilter.FILTER_ACCEPT',
    '          : NodeFilter.FILTER_REJECT;',
    '      }',
    '    });',
    '    var targets = [];',
    '    var n = walker.nextNode();',
    '    while (n) { targets.push(n); n = walker.nextNode(); }',
    '    for (var i = 0; i < targets.length; i++) {',
    '      var node = targets[i];',
    "      var text = node.nodeValue || '';",
    '      var hay = text.toLowerCase();',
    '      var frag = document.createDocumentFragment();',
    '      var last = 0;',
    '      var idx = hay.indexOf(needle);',
    '      while (idx !== -1) {',
    '        if (idx > last) frag.appendChild(document.createTextNode(text.slice(last, idx)));',
    "        var mark = document.createElement('mark');",
    "        mark.className = 'cpd-find';",
    '        mark.textContent = text.slice(idx, idx + query.length);',
    '        frag.appendChild(mark);',
    '        matches.push(mark);',
    '        last = idx + query.length;',
    '        idx = hay.indexOf(needle, last);',
    '      }',
    '      if (last < text.length) frag.appendChild(document.createTextNode(text.slice(last)));',
    '      if (node.parentNode) node.parentNode.replaceChild(frag, node);',
    '    }',
    '    current = 0;',
    '    highlight();',
    "    send({ type: 'cpdFindResult', total: matches.length, current: current });",
    '  }',
    '  function stepFind(dir) {',
    '    if (!matches.length) return;',
    '    current = (current + dir + matches.length) % matches.length;',
    '    highlight();',
    "    send({ type: 'cpdFindResult', total: matches.length, current: current });",
    '  }',
    '',
    // ---- scroll ----
    // Every render replaces the document (srcdoc forces a fresh browsing context),
    // so the offset has to be handed to the host and seeded back. Skipped for the
    // input form, whose #UI script restores scroll along with focus and caret.
    ...(ownScroll ? [
    '  var startAt = window.__calcpadScrollPosition || null;',
    '  window.__calcpadScrollPosition = null;',
    '  if (startAt) {',
    '    var scrollBack = function() { window.scrollTo(startAt.x || 0, startAt.y || 0); };',
    "    document.addEventListener('DOMContentLoaded', scrollBack);",
    // Late-loading images reflow the page and clamp the offset, so the position is
    // only final once everything has laid out.
    "    window.addEventListener('load', scrollBack);",
    '  }',
    '  var scrollQueued = false;',
    "  window.addEventListener('scroll', function() {",
    '    if (scrollQueued) return;',
    '    scrollQueued = true;',
    '    requestAnimationFrame(function() {',
    '      scrollQueued = false;',
    "      send({ type: 'cpdScrollState', x: window.scrollX, y: window.scrollY });",
    '    });',
    '  });',
    ] : []),
    '',
    // ---- command channel ----
    // Only the host embeds this document, so window.parent is the one sender that
    // can reach here; commands from anywhere else are ignored.
    "  window.addEventListener('message', function(e) {",
    '    if (e.source !== window.parent) return;',
    '    var d = e.data;',
    "    if (!d || typeof d.type !== 'string') return;",
    "    if (d.type === 'cpdFindApply') applyFind(String(d.query || ''));",
    "    else if (d.type === 'cpdFindStep') stepFind(Number(d.dir) || 0);",
    "    else if (d.type === 'cpdFindClear') clearMarks();",
    "    else if (d.type === 'cpdClipboardExec') runClipboard(d.action, d.text);",
    '  });',
    '})();',
  ].join('\n')
  return insertHeadScript(html, body)
}

// Forward iframe console.* + uncaught errors to the parent window via
// postMessage, tagged with groupId so the Output panel's "Preview Console"
// channel can be split by the active editor group.
function injectPreviewConsole(html: string, groupId: string): string {
  const gid = JSON.stringify(groupId)
  const body = [
    '(function() {',
    // Published before the patch guard so it is always set: the backend's #UI
    // event script reads it to tag its messages with the owning group.
    '  window.__calcpadGroupId = ' + gid + ';',
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

  tabStripResizeObserver = new ResizeObserver(entries => {
    for (const entry of entries) {
      const id = tabStripElIds.get(entry.target)
      if (id) checkTabStripOverflow(id)
    }
  })
  for (const el of tabStripEls.values()) tabStripResizeObserver.observe(el)
})

onBeforeUnmount(() => {
  document.removeEventListener('mousedown', onDocumentInteractionForTabMenu)
  document.removeEventListener('keydown', onDocumentInteractionForTabMenu)
  window.removeEventListener('message', onPreviewWindowMessage)
  tabStripResizeObserver?.disconnect()
  tabStripResizeObserver = null
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
  isPreviewFrameSource,
  setProblems,
  onGotoProblem,
  onPreviewToggled,
  onResultModeChanged,
  setResultMode,
  getResultMode,
  resultModeAvailable,
  setUiOverridesDirty,
  onSaveUiOverridesRequest,
  onExitUiModeRequest,
  onPrintReportRequest,
  setUiPrintHtml,
  isUiPrintVisible,
  onUiPrintToggled,
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
  onClipboardReadRequest,
  runFocusedPreviewClipboardAction,
  onOpenFullHtmlRequest,
})
</script>
