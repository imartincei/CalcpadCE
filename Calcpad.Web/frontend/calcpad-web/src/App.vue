<template>
  <div class="app-layout" :class="{ resizing: isAnyDividerDragging }">
    <!-- v-show, not v-if: main.ts mounts a Vue app into #vue-sidebar. -->
    <div
      v-show="sidebarVisible"
      class="sidebar-pane"
      :style="{ width: sidebarWidth + 'px' }"
    >
      <div id="vue-sidebar"></div>
    </div>
    <!-- Drag to resize when open; click to toggle. -->
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
    <!-- UI mode gives the window to the input form; only the tab strip stays.
         `.work-area` becomes a column for it, otherwise `display: contents`. -->
    <div class="work-area" :class="{ 'ui-mode': uiModeFullscreen }">
    <div class="editor-pane">
      <!-- Editor groups, stacked; two when split. -->
      <div class="editor-groups">
        <template v-for="(group, gi) in groups" :key="group.id">
          <div
            v-show="!uiModeFullscreen || group.id === activeGroupId"
            class="editor-group"
            :class="{ 'active-group': group.id === activeGroupId && isSplit && !uiModeFullscreen }"
            :style="editorGroupStyle(gi)"
            @mousedown="onGroupFocus(group.id)"
          >
            <!-- Grows taller only once the tabs overflow, to clear the scrollbar. -->
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
              <!-- First group only; the strip is inside the v-for over groups. -->
              <div v-if="gi === 0 && !uiModeFullscreen" class="tab-actions">
                <button
                  class="tab-action"
                  @click="onRunPreview"
                  title="Run preview (Ctrl+Alt+X)"
                  aria-label="Run preview"
                >
                  <svg viewBox="0 0 16 16" aria-hidden="true"><path d="M4 2.5v11l9-5.5z" fill="currentColor"/></svg>
                </button>
                <button
                  class="tab-action"
                  :class="{ active: isSplit }"
                  @click="onToggleSplit"
                  :title="isSplit ? 'Merge editor groups' : 'Split editor down (Ctrl+\\)'"
                  :aria-label="isSplit ? 'Merge editor groups' : 'Split editor down'"
                  :aria-pressed="isSplit"
                >
                  <svg viewBox="0 0 16 16" aria-hidden="true" fill="none" stroke="currentColor" stroke-width="1.2"><rect x="2.5" y="2.5" width="11" height="11" rx="1"/><line x1="2.5" y1="8" x2="13.5" y2="8"/></svg>
                </button>
                <button
                  class="tab-action"
                  :class="{ active: previewVisible }"
                  @click="togglePreview"
                  :title="previewVisible ? 'Hide the results pane' : 'Show the results pane (Ctrl+P)'"
                  :aria-label="previewVisible ? 'Hide the results pane' : 'Show the results pane'"
                  :aria-pressed="previewVisible"
                >
                  <svg viewBox="0 0 16 16" aria-hidden="true" fill="none" stroke="currentColor" stroke-width="1.2"><rect x="2.5" y="2.5" width="11" height="11" rx="1"/><line x1="9" y1="2.5" x2="9" y2="13.5"/><path d="M9.5 3h4v10h-4z" fill="currentColor" stroke="none" opacity="0.5"/></svg>
                </button>
              </div>
              <button
                v-if="isSplit && gi > 0"
                class="group-close"
                title="Close this editor group"
                @click="onCloseGroup(group.id)"
              >✕</button>
            </div>
            <!-- Anchors the reveal button to the code area, not the tab strip. -->
            <div v-show="!uiModeFullscreen" class="editor-area">
              <div class="editor-container" :ref="el => setEditorRef(group.id, el)"></div>
              <button
                v-if="!sidebarVisible && gi === 0"
                class="editor-sidebar-reveal"
                @click="toggleSidebar"
                title="Show the sidebar (Ctrl+Shift+B)"
                aria-label="Show the sidebar"
              >
                <svg viewBox="0 0 16 16" aria-hidden="true" fill="none" stroke="currentColor" stroke-width="1.2">
                  <rect x="2.5" y="2.5" width="11" height="11" rx="1"/>
                  <line x1="6.5" y1="2.5" x2="6.5" y2="13.5"/>
                  <polyline points="9.5,5.5 12,8 9.5,10.5"/>
                </svg>
              </button>
            </div>
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

      <!-- Outside .tab-strip so it isn't clipped. @mousedown.stop keeps the
           document-level closer from beating the click handler. -->
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

      <!-- Problems context menu; replaces the broken default WebView menu. -->
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

    </div>

    <!-- Editor ↔ results divider. Nothing to resize in UI mode. -->
    <div
      v-if="previewVisible && !uiModeFullscreen"
      class="pane-divider"
      :class="{ dragging: draggingPreviewDivider }"
      @mousedown="onPreviewDividerMouseDown"
      title="Drag to resize"
      role="separator"
      aria-orientation="vertical"
    ></div>

    <!-- Rows the input form and its report in UI mode; `display: contents` otherwise. -->
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
      <!-- One preview iframe per editor group. allow-same-origin is deliberately
           absent: with allow-scripts it would hand untrusted worksheet HTML this
           window's origin. postMessage is the only channel — see injectPreviewAgent. -->
      <!-- Find-in-preview; opened by Ctrl+F or the preview context menu. -->
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
          <div
            class="preview-buffers"
            :class="{ 'active-group': group.id === activeGroupId && isSplit && !uiModeFullscreen }"
            :style="previewGroupStyle(gi)"
          >
            <iframe
              v-for="slot in [0, 1]"
              :key="slot"
              class="preview-frame"
              :class="{ 'preview-frame-back': slot !== frontIndex(group.id) }"
              :inert="bufferInert(group.id, slot)"
              :ref="el => setPreviewRef(group.id, slot, el)"
              sandbox="allow-scripts"
            ></iframe>
          </div>
          <div
            v-if="gi === 0 && isSplit && !uiModeFullscreen"
            class="group-divider"
            :class="{ dragging: draggingEditorDivider }"
            @mousedown="onEditorDividerMouseDown"
            role="separator"
            aria-orientation="horizontal"
          ></div>
        </template>
        <div
          v-if="previewLoading"
          class="preview-loading-overlay"
          :class="{ 'preview-dark': previewTheme === 'dark' }"
        >
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

    <!-- Report companion to the input form: the print layout with entered values applied. -->
    <div v-if="uiModeFullscreen && uiPrintVisible" class="preview-pane ui-print-pane" :style="uiPrintPaneStyle()">
      <div class="preview-toolbar" @contextmenu.prevent>
        <span>Report</span>
        <span class="spacer"></span>
        <button class="toolbar-btn" @click="toggleUiPrint" title="Hide the report">✕</button>
      </div>
      <div class="preview-frames">
        <div v-if="activeGroup" class="preview-buffers" :style="{ flex: '1 1 0', minHeight: '0' }">
          <iframe
            v-for="slot in [0, 1]"
            :key="slot"
            class="preview-frame"
            :class="{ 'preview-frame-back': slot !== frontIndex(UI_PRINT_FRAME + activeGroup.id) }"
            :inert="bufferInert(UI_PRINT_FRAME + activeGroup.id, slot)"
            :ref="el => setUiPrintRef(activeGroup.id, slot, el)"
            sandbox="allow-scripts"
          ></iframe>
        </div>
      </div>
    </div>
    </div><!-- /.preview-area -->
    </div><!-- /.work-area -->

    <!-- Replaces the broken native WebView menu (see injectLineLinks). Viewport
         coordinates, and outside the editor pane, which UI mode hides. -->
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

    <!-- HTML modal rather than a native dialog, for web/desktop consistency. -->
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

    <!-- The target comes from the worksheet: shown as text, opened only by choice. -->
    <div v-if="openLinkState" class="modal-backdrop" @click.self="resolveOpenLink(false)">
      <div class="modal-card" role="dialog" aria-modal="true">
        <template v-if="openLinkState.mode === 'file'">
          <div class="modal-title">Open this file?</div>
          <div class="modal-message">This path comes from the worksheet. Your system will choose the application that opens it.</div>
        </template>
        <template v-else>
          <div class="modal-title">Open external link?</div>
          <div class="modal-message">This link comes from the worksheet and will open in your browser.</div>
        </template>
        <div class="modal-url">{{ openLinkState.url }}</div>
        <div class="modal-actions">
          <button class="modal-btn primary" @click="resolveOpenLink(true)">
            {{ openLinkState.mode === 'file' ? 'Open File' : 'Open in Browser' }}
          </button>
          <button class="modal-btn" @click="resolveOpenLink(false)">Cancel</button>
        </div>
      </div>
    </div>

    <!-- Single-select list modal (VS Code QuickPick analog). -->
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
    <span v-if="serverStatus !== 'connected'" class="status-server" :class="serverStatus" :title="serverStatusTitle">
      ● {{ serverStatusLabel }}
    </span>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, nextTick, watch, onMounted, onBeforeUnmount } from 'vue'
import {
  extractBodyHtml,
  isCompiledPath,
  parseScrollState,
  previewDiagnosticsScript,
  scrollAnchorScript,
  consoleRelayGuardScript,
  shouldLog,
  DISPLAY_LOG_LEVEL,
  truncateForOutput,
  BACK_BUFFER_CLEAR_CHARS,
  MAX_HTML_MIRROR_CHARS,
  DEFAULT_CONSOLE_MESSAGES_PER_DOCUMENT,
  MIN_CONSOLE_MESSAGES_PER_DOCUMENT,
  type PreviewScrollState,
  type ServerStatus,
  type DisplayLogLevel,
} from 'calcpad-frontend'
import type { ResultMode, WorkspaceLayout } from './services/workspace-state'

const props = withDefaults(defineProps<{ isDesktop?: boolean }>(), { isDesktop: false })

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

// Result mode is shared across groups; onGotoProblem targets the active one.
const onGotoProblem = ref<((problem: ProblemItem) => void) | null>(null)
const onPreviewToggled = ref<((visible: boolean) => void) | null>(null)
const onResultModeChanged = ref<((mode: ResultMode) => void) | null>(null)
const resultMode = ref<ResultMode>('preview')

// #UI values entered but not yet written into the document source.
const uiOverridesDirty = ref(false)
const onSaveUiOverridesRequest = ref<(() => void) | null>(null)
// Asked before the input form closes; false keeps it open.
const onExitUiModeRequest = ref<(() => Promise<boolean>) | null>(null)
// "Print PDF" — the host runs the export.
const onPrintReportRequest = ref<(() => void) | null>(null)

/** UI mode hands the whole window to the input form; only the tab strip stays. */
const uiModeFullscreen = computed(() => previewVisible.value && resultMode.value === 'ui')

// The report pane beside the input form.
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

/** A compiled worksheet has no readable source, so input is the only mode offered. */
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

// DOM registries main.ts reads to build each group's editor and preview.
const editorEls = new Map<string, HTMLElement>()
// Two stacked iframes: a render lands in the back one and comes forward once painted.
type FramePair = [HTMLIFrameElement | null, HTMLIFrameElement | null]
const previewEls = new Map<string, FramePair>()
// Iframes of the report pane shown beside the input form in UI mode.
const uiPrintEls = new Map<string, FramePair>()
const frontBuffer = ref<Record<string, 0 | 1>>({})
const loadingBuffer = ref<Record<string, 0 | 1>>({})
// Last full (unstripped) HTML rendered per group, kept for "Open Full HTML".
const previewHtmlByGroup = new Map<string, string>()
// Position within each frame's #UI form. Held here because srcdoc gives every render a
// fresh browsing context, with no sessionStorage on an opaque origin.
const uiPositionByFrame = new Map<string, unknown>()
// Scroll position per frame *and* document, so a re-render returns but a tab switch
// starts at the top. Anchor-based rather than an offset — see scroll-anchor.ts.
const scrollByFrameDoc = new Map<string, PreviewScrollState>()
const docKeyByFrame = new Map<string, string>()

function scrollKey(frameId: string, docKey: string): string {
  return frameId + ' ' + docKey
}

function setEditorRef(id: string, el: unknown): void {
  if (el instanceof HTMLElement) editorEls.set(id, el)
  else editorEls.delete(id)
}

// Which tab strips overflow, so only those grow taller for the scrollbar. A
// ResizeObserver catches both causes: window resizes and tabs coming and going.
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
function setFrameRef(map: Map<string, FramePair>, id: string, slot: number, el: unknown): void {
  const pair = map.get(id) ?? [null, null] as FramePair
  pair[slot === 0 ? 0 : 1] = el instanceof HTMLIFrameElement ? el : null
  if (pair[0] || pair[1]) map.set(id, pair)
  else map.delete(id)
}
function setPreviewRef(id: string, slot: number, el: unknown): void {
  setFrameRef(previewEls, id, slot, el)
}
function setUiPrintRef(id: string, slot: number, el: unknown): void {
  setFrameRef(uiPrintEls, id, slot, el)
}

function framePair(frameId: string): FramePair | undefined {
  return frameId.startsWith(UI_PRINT_FRAME)
    ? uiPrintEls.get(frameId.slice(UI_PRINT_FRAME.length))
    : previewEls.get(frameId)
}
function frontIndex(frameId: string): 0 | 1 {
  return frontBuffer.value[frameId] ?? 0
}
function frontFrame(frameId: string): HTMLIFrameElement | null {
  return framePair(frameId)?.[frontIndex(frameId)] ?? null
}
// Keeps Tab out of a buffer the user has been moved off. The one mid-render is left
// alone — inert would swallow the #UI script's focus restore.
function bufferInert(frameId: string, slot: number): boolean {
  return slot !== frontIndex(frameId) && slot !== loadingBuffer.value[frameId]
}
function getEditorContainer(id: string): HTMLElement | null {
  return editorEls.get(id) ?? null
}

// ---- Split ratio (top group's fraction of the stack height) ----
const editorSplitRatio = ref<number>(0.5)
const draggingEditorDivider = ref(false)

/** UI mode renders only the active group: the input form is a single-document view. */
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

// Drives `.app-layout.resizing`, which kills pointer-events on the iframes: a cursor
// crossing into one mid-drag would otherwise steal the mousemove/mouseup.
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

// Measured against `.app-layout`: `.work-area` is `display: contents` and has no box.
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
  delete frontBuffer.value[id]
  delete frontBuffer.value[UI_PRINT_FRAME + id]
  delete loadingBuffer.value[id]
  delete loadingBuffer.value[UI_PRINT_FRAME + id]
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
    // Merge closes the bottom group; the top is the primary one.
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

// Set by the host to use Tauri's native clipboard; falls back to the Web API.
const onCopyTextRequest = ref<((text: string) => void) | null>(null)
// Desktop host only. Its presence marks a WebView whose frames must handle their
// own Ctrl+C/X/V (see injectPreviewAgent).
const onClipboardReadRequest = ref<(() => Promise<string>) | null>(null)

// Opens the rendered HTML as raw text in a new tab. Null hides the button.
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

// Frames are addressed by group; the report pane shares its group's id, hence the prefix.
const UI_PRINT_FRAME = 'ui-print:'

function clipboardFrame(frameId: string): HTMLIFrameElement | null {
  return frontFrame(frameId)
}

async function readClipboardText(): Promise<string> {
  if (onClipboardReadRequest.value) return await onClipboardReadRequest.value()
  try { return await navigator.clipboard.readText() } catch { return '' }
}

/** Sends a command to a preview frame's injected agent. See injectPreviewAgent. */
function postToPreviewFrame(frameId: string, message: Record<string, unknown>): void {
  // An opaque origin leaves '*' as the only targetOrigin. Safe outbound: these
  // commands carry no secrets; the inbound check is in onPreviewWindowMessage.
  clipboardFrame(frameId)?.contentWindow?.postMessage(message, '*')
}

/**
 * Clipboard action against whatever the frame has focused, which the desktop WebView
 * otherwise leaves inert. The frame does the work; reaching its DOM from here would
 * need allow-same-origin.
 */
async function runPreviewClipboardAction(frameId: string, action: PreviewClipboardAction): Promise<void> {
  const text = action === 'paste' ? await readClipboardText() : undefined
  if (action === 'paste' && !text) return
  if (action !== 'paste') armClipboardCopy(frameId)
  postToPreviewFrame(frameId, { type: 'cpdClipboardExec', action, text })
}

// A copy/cut reply is only honoured just after the host asked for one — otherwise a
// worksheet could post previewClipboardText on a timer and own the clipboard.
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

// One Ctrl+V can arrive twice (frame key handler + host accelerator). Only the
// key-driven routes are deduplicated; a context-menu click is always meant.
let lastPreviewClipboard = { action: '', at: 0 }

function isRepeatedPreviewClipboard(action: PreviewClipboardAction): boolean {
  const at = performance.now()
  const repeated = action === lastPreviewClipboard.action && at - lastPreviewClipboard.at < 250
  lastPreviewClipboard = { action, at }
  return repeated
}

/** Routes the host's Edit menu into a focused preview/report frame; false if none is. */
function runFocusedPreviewClipboardAction(action: PreviewClipboardAction): boolean {
  const focused = document.activeElement
  for (const groupId of previewEls.keys()) {
    if (frontFrame(groupId) !== focused) continue
    if (!isRepeatedPreviewClipboard(action)) void runPreviewClipboardAction(groupId, action)
    return true
  }
  for (const groupId of uiPrintEls.keys()) {
    const frameId = UI_PRINT_FRAME + groupId
    if (frontFrame(frameId) !== focused) continue
    if (!isRepeatedPreviewClipboard(action)) void runPreviewClipboardAction(frameId, action)
    return true
  }
  return false
}

/** Opens the find bar when a preview frame holds focus. Returns false if none does. */
function openFindInFocusedPreview(): boolean {
  const focused = document.activeElement
  for (const groupId of previewEls.keys()) {
    if (frontFrame(groupId) !== focused) continue
    openPreviewFind(groupId)
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
  // Nothing to search over for a frame with no preview iframe (the report pane).
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

// Find runs inside the frame (see injectPreviewAgent); reaching its DOM from here
// would mean allow-same-origin. The host keeps only the counts.
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
 * The frame a message came from, or null. `e.origin` is "null" for every sandboxed frame,
 * so window identity is the only meaningful check. Both buffers of a pair count.
 */
function senderFrameId(source: MessageEventSource | null): string | null {
  if (!source) return null
  for (const [groupId, pair] of previewEls) {
    if (pair.some(el => el?.contentWindow === source)) return groupId
  }
  for (const [groupId, pair] of uiPrintEls) {
    if (pair.some(el => el?.contentWindow === source)) return UI_PRINT_FRAME + groupId
  }
  return null
}

/** Exposed so main.ts's own message listener can apply the same check. */
function isPreviewFrameSource(source: MessageEventSource | null): boolean {
  return senderFrameId(source) !== null
}

function onPreviewWindowMessage(e: MessageEvent): void {
  const data = e.data
  if (!data || typeof data.type !== 'string') return
  const frameId = senderFrameId(e.source)
  if (!frameId) return
  // A frame may only speak for itself; a trusted id would let one preview drive another's.
  const groupId = frameId.startsWith(UI_PRINT_FRAME)
    ? frameId.slice(UI_PRINT_FRAME.length)
    : frameId

  if (data.type === 'cpdFrameReady') {
    resolveFrameReady(e.source)
    return
  }
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
      // Only the frame can see what it has focused.
      editable: data.editable === true,
    }
    return
  }
  if (data.type === 'previewClipboardAction') {
    const action = data.action as PreviewClipboardAction
    if (!isRepeatedPreviewClipboard(action)) void runPreviewClipboardAction(frameId, action)
    return
  }
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
    // Only the front buffer speaks for where the user is; a demoted one still settles
    // and would overwrite the new position with its own.
    if (frontFrame(frameId)?.contentWindow !== e.source) return
    const docKey = docKeyByFrame.get(frameId)
    if (docKey === undefined) return
    const state = parseScrollState(data)
    if (state) scrollByFrameDoc.set(scrollKey(frameId, docKey), state)
    return
  }
  if (data.type === 'previewFindOpen') {
    openPreviewFind(groupId)
  }
}

function gotoProblem(problem: ProblemItem): void {
  onGotoProblem.value?.(problem)
}

// Pushed in by main.ts's ConnectionMonitor; this component never probes the server itself.
const serverStatus = ref<ServerStatus>('connecting')
const serverStatusLabel = computed(() =>
  serverStatus.value === 'connected' ? 'Connected'
    : serverStatus.value === 'connecting' ? 'Starting…'
      : 'Disconnected')
// Only the desktop build has the Server menu these name.
const serverStatusTitle = computed(() => {
  if (serverStatus.value === 'connected') return 'Server connected'
  if (serverStatus.value === 'connecting') return 'Server starting…'
  if (!props.isDesktop) return 'Server disconnected — reload the page to reconnect'
  return [
    'Server disconnected',
    'Server ▸ Restart Server — start it again',
    'Server ▸ Show Server Log — see why it stopped',
    'Ctrl+Alt+X — refresh once it is back',
  ].join('\n')
})

function setServerStatus(status: ServerStatus): void {
  serverStatus.value = status
}

const sidebarVisible = ref(true)
const previewVisible = ref(true)
// Groups with an in-flight preview render; drives the "Calculating…" overlay.
const previewLoadingGroups = ref(new Set<string>())
const previewLoading = computed(() => previewLoadingGroups.value.size > 0)
// The rendered document's own theme (main.ts:resolvePreviewTheme); the overlay follows it.
const previewTheme = ref<'light' | 'dark'>('light')
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

// 'preview'/'html' are per-group, so filter by the active one; 'app'/'server' are global.
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

// Cap on retained output lines per channel; older lines are dropped.
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
// Baked into each render's injected script, so a change only takes effect on the next.
const maxPreviewConsoleMessages = ref<number>(DEFAULT_CONSOLE_MESSAGES_PER_DOCUMENT)
function setMaxPreviewConsoleMessages(n: number): void {
  if (!Number.isFinite(n) || n < MIN_CONSOLE_MESSAGES_PER_DOCUMENT) return
  maxPreviewConsoleMessages.value = Math.floor(n)
}

function setMaxOutputLines(n: number): void {
  if (!Number.isFinite(n) || n < 10) return
  maxOutputLinesPerChannel.value = Math.floor(n)
  for (const ch of ['app', 'preview', 'server', 'html'] as OutputChannel[]) trimChannel(ch)
}

function appendOutput(
  level: DisplayLogLevel,
  message: string,
  channel: OutputChannel = 'app',
  groupId?: string,
): void {
  // Only diagnostic channels are filtered; 'preview'/'html' carry the worksheet's own output.
  const diagnostic = channel === 'app' || channel === 'server'
  if (diagnostic && !shouldLog(DISPLAY_LOG_LEVEL[level])) return
  const now = new Date()
  const time = now.toLocaleTimeString('en-US', { hour12: false, hour: '2-digit', minute: '2-digit', second: '2-digit' })
  const labels: Record<string, string> = { info: 'INFO', warn: 'WARN', error: 'ERROR', debug: 'DEBUG' }
  // Sampled before the push, or the new line's height masks a user scroll-up.
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
  // Per-channel clear, as in VS Code.
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

const onLayoutChanged = ref<((layout: WorkspaceLayout) => void) | null>(null)

// Watched, not reported per toggle: several controls flip these directly.
watch([sidebarVisible, previewVisible, bottomPanelOpen, activeBottomTab], () => {
  onLayoutChanged.value?.({
    sidebarVisible: sidebarVisible.value,
    previewVisible: previewVisible.value,
    bottomPanelOpen: bottomPanelOpen.value,
    activeBottomTab: activeBottomTab.value,
  })
})

/** Restores a persisted layout, bypassing `togglePreview`'s input-mode guard. */
function setLayout(layout: WorkspaceLayout): void {
  sidebarVisible.value = layout.sidebarVisible
  previewVisible.value = layout.previewVisible
  bottomPanelOpen.value = layout.bottomPanelOpen
  activeBottomTab.value = layout.activeBottomTab
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
  // Collapsed: a click opens it instead of starting a drag.
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
  // Guarded here rather than on the buttons, so every caller is covered.
  if (!resultModeAvailable(mode)) return
  if (resultMode.value === 'ui' && !await confirmExitUiMode()) return
  resultMode.value = mode
  onResultModeChanged.value?.(mode)
}

/** The host's leave-input-mode prompt; false when the user cancelled. */
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

function setPreviewTheme(theme: 'light' | 'dark'): void {
  previewTheme.value = theme
}

const PREVIEW_READY_TIMEOUT_MS = 30000
const frameReadyWaiters = new Map<HTMLIFrameElement, () => void>()

/**
 * Resolves on the ping injectPreviewAgent posts; the element's own load event fires for
 * the initial about:blank too. Timed out so a stuck document still reaches the screen.
 */
function awaitFrameReady(frame: HTMLIFrameElement): Promise<void> {
  frameReadyWaiters.get(frame)?.()
  return new Promise(resolve => {
    const finish = () => {
      if (frameReadyWaiters.get(frame) !== finish) return
      frameReadyWaiters.delete(frame)
      window.clearTimeout(timer)
      resolve()
    }
    const timer = window.setTimeout(finish, PREVIEW_READY_TIMEOUT_MS)
    frameReadyWaiters.set(frame, finish)
  })
}

function resolveFrameReady(source: MessageEventSource | null): void {
  for (const [frame, done] of frameReadyWaiters) {
    if (frame.contentWindow === source) {
      done()
      return
    }
  }
}

/**
 * Writes into the back buffer and brings it forward once painted. The front index is
 * set, not toggled, so racing renders cannot flip back to the stale slot.
 */
function commitToBuffer(frameId: string, html: string): Promise<void> {
  const slot: 0 | 1 = frontIndex(frameId) === 0 ? 1 : 0
  const pair = framePair(frameId)
  const frame = pair?.[slot]
  if (!frame) return Promise.resolve()
  const demoted = pair?.[frontIndex(frameId)]
  loadingBuffer.value = { ...loadingBuffer.value, [frameId]: slot }
  const ready = awaitFrameReady(frame)
  frame.srcdoc = html
  return ready.then(() => {
    frontBuffer.value = { ...frontBuffer.value, [frameId]: slot }
    // The demoted buffer is only overwritten by the next render, so a large one is
    // emptied rather than left holding a second copy.
    if (demoted && demoted !== frame && html.length > BACK_BUFFER_CLEAR_CHARS) demoted.srcdoc = ''
  })
}

// srcdoc forces a fresh browsing context; reusing one document via doc.open()/write()
// desynced WebKit's scrolling state and lost the preview's scrollbar.
function setPreviewHtml(groupId: string, html: string, scrollToLine?: number, docKey = ''): Promise<void> {
  if (!previewEls.has(groupId)) return Promise.resolve()
  const isUi = resultMode.value === 'ui'
  docKeyByFrame.set(groupId, docKey)
  // An explicit line target outranks restoring the old position.
  const seed = scrollToLine === undefined ? scrollByFrameDoc.get(scrollKey(groupId, docKey)) : undefined
  let out = injectPreviewAgent(
    injectLineLinks(html, scrollToLine, groupId, undefined, !isUi),
    groupId,
    !!onClipboardReadRequest.value,
    seed,
  )
  out = injectPreviewConsole(out, groupId)
  out = injectUiPosition(out, groupId)
  previewHtmlByGroup.set(groupId, html)
  setPreviewHtmlOutput(groupId, html)
  return commitToBuffer(groupId, out)
}

/**
 * Writes the report beside the input form. No controls and no logging, so it skips the #UI
 * event script and console interception, and the line links whose editor is hidden.
 */
function setUiPrintHtml(groupId: string, html: string, docKey = ''): Promise<void> {
  if (!uiPrintEls.has(groupId)) return Promise.resolve()
  const frameId = UI_PRINT_FRAME + groupId
  docKeyByFrame.set(frameId, docKey)
  const out = injectPreviewAgent(
    injectLineLinks(html, undefined, groupId, frameId, false),
    frameId,
    !!onClipboardReadRequest.value,
    scrollByFrameDoc.get(scrollKey(frameId, docKey)),
  )
  return commitToBuffer(frameId, out)
}

function isUiPrintVisible(): boolean {
  return uiPrintVisible.value
}

function toggleUiPrint(): void {
  uiPrintVisible.value = !uiPrintVisible.value
  onUiPrintToggled.value?.(uiPrintVisible.value)
}

// Mirrors the last render's body into the 'html' channel, replacing the group's prior
// line. Clipped, since the cap counts lines and not their length.
function setPreviewHtmlOutput(groupId: string, html: string): void {
  outputLines.value = outputLines.value.filter(l => !(l.channel === 'html' && l.groupId === groupId))
  appendOutput('info', truncateForOutput(extractBodyHtml(html), MAX_HTML_MIRROR_CHARS), 'html', groupId)
}

// Editor/TOC -> preview sync, moving the report pane with it. `exact` drops the
// nearest-preceding-line fallback, so a hidden heading jumps nowhere.
function scrollPreviewToSourceLine(groupId: string, line: number, exact = false): void {
  const msg = { type: 'scrollPreviewToLine', line, exact }
  frontFrame(groupId)?.contentWindow?.postMessage(msg, '*')
  frontFrame(UI_PRINT_FRAME + groupId)?.contentWindow?.postMessage(msg, '*')
}

// Per-render line-link behaviour ported from vscode-calcpad; its CSS lives in the
// backend's template.html. `lineLinks` turns off just the hover arrows, which the input
// form and its report drop along with the editor they navigate to.
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
    // Replaces WebKitGTK's broken native menu. pointerdown dismisses and
    // contextmenu reopens, so a right-click nets an open menu.
    "  var postMenu = function(type, e) {",
    "    var sel = ''; try { sel = String(window.getSelection() || ''); } catch (_e) {}",
    // Only this side can see what is focused; injectPreviewAgent publishes the probe.
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
    // A sandboxed frame may navigate itself, so no activation reaches the default action.
    // Known schemes go to the host, which prompts. auxclick covers middle-click.
    "  function onLinkActivate(e) {",
    "    if (e.type === 'auxclick' && e.button !== 1) return;",
    "    var a = e.target && e.target.closest ? e.target.closest('a[href]') : null;",
    "    if (!a || a.hasAttribute('data-text')) return;",
    "    var href = a.getAttribute('href') || '';",
    "    if (!href) return;",
    "    e.preventDefault();",
    // srcdoc has no document URL: the default action blanks the frame.
    "    if (href.charAt(0) === '#') { scrollToFragment(href.slice(1)); return; }",
    "    if (!/^(https?|file):/i.test(href)) return;",
    "    try { window.parent.postMessage({ type: 'openExternal', url: href, groupId: FRAME_ID }, '*'); } catch (_e) {}",
    "  }",
    "  function scrollToFragment(raw) {",
    "    var id = raw;",
    "    try { id = decodeURIComponent(raw); } catch (_e) {}",
    "    var t = id ? (document.getElementById(id) || document.getElementsByName(id)[0]) : document.body;",
    "    if (!t) return;",
    "    if (window.__calcpadReleaseScroll) window.__calcpadReleaseScroll();",
    "    t.scrollIntoView({ block: 'start' });",
    "  }",
    "  document.addEventListener('click', onLinkActivate);",
    "  document.addEventListener('auxclick', onLinkActivate);",
    ...(lineLinks ? [
    "  function hideAllLineLinks() {",
    "    document.querySelectorAll('.lineLink').forEach(function(l) { l.style.display = 'none'; });",
    "  }",
    // The arrow sits at left:-3em inside .line, so inline ones are skipped.
    "  document.querySelectorAll('.line').forEach(function(el) {",
    "    if (el.closest('a[href]') || getComputedStyle(el).display === 'inline') return;",
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
    // Release the scroll agent first, or its restore pulls the user back.
    "  function goTo(target, block) {",
    "    if (!target) return;",
    '    if (window.__calcpadReleaseScroll) window.__calcpadReleaseScroll();',
    "    target.scrollIntoView({ block: block });",
    "  }",
    "  document.querySelectorAll('.roundBox').forEach(function(box) {",
    "    box.addEventListener('click', function() {",
    "      var errId = box.getAttribute('data-error');",
    "      var target = errId ? document.getElementById(errId) : null;",
    "      if (!target) {",
    "        var line = box.getAttribute('data-line');",
    "        target = line ? document.getElementById('line-' + line) : null;",
    "      }",
    "      goTo(target, 'start');",
    "    });",
    "  });",
    "  var scrollToLine = " + scrollTarget + ";",
    "  if (scrollToLine !== null) goTo(document.getElementById('line-' + scrollToLine), 'center');",
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
    "    goTo(target, 'center');",
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
 * Seeds the pre-render position for the backend's #UI script to pick up. Consumed once, and
 * in <head> so it runs before that script at </body>.
 */
function injectUiPosition(html: string, frameId: string): string {
  const state = uiPositionByFrame.get(frameId)
  if (state === undefined) return html
  uiPositionByFrame.delete(frameId)
  // The state carries a key from the document, so close any tag it could open.
  const json = JSON.stringify(state).replace(/</g, '\\u003c')
  return insertHeadScript(html, 'window.__calcpadUiPosition = ' + json + ';')
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
 * The frame's half of find-in-preview and the clipboard bridge. Done in the frame so it can
 * keep an opaque origin; from the host it would need `allow-same-origin`. `interceptClipboard`
 * takes Ctrl+C/X/V in the capture phase, ahead of the datagrid library.
 */
function injectPreviewAgent(
  html: string,
  frameId: string,
  interceptClipboard: boolean,
  scroll: PreviewScrollState | undefined,
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
    // A datagrid keeps its position in the library, so its own inputs take the sheet path.
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
    // A programmatic edit raises neither the 'input' nor the 'change' the #UI script
    // needs. A non-numeric edit is left uncommitted rather than reverted.
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
    '    if (window.__calcpadReleaseScroll) window.__calcpadReleaseScroll();',
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
    // srcdoc replaces the document every render, so the position is handed to the host
    // and seeded back. The #UI script restores focus and caret, but not the position.
    scrollAnchorScript(
      "function(s) { send({ type: 'cpdScrollState', x: s.x, y: s.y, atEnd: s.atEnd, anchor: s.anchor }); }",
      scroll,
    ),
    '',
    // ---- readiness ----
    // Tells the host to bring this buffer forward; the iframe's own load event also fires
    // for about:blank. Held, with a cap, until the scroll agent has applied its position.
    "  window.addEventListener('load', function() {",
    "    var ready = function() { send({ type: 'cpdFrameReady' }); };",
    '    if (window.__calcpadScrollSettled) window.__calcpadScrollSettled(ready);',
    '    else ready();',
    '  });',
    '',
    // ---- command channel ----
    // Only the host embeds this document, so window.parent is the only valid sender.
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

// Forwards iframe console.* and uncaught errors to the host, tagged with groupId so the
// Output panel can split them by editor group.
function injectPreviewConsole(html: string, groupId: string): string {
  const maxMessages = maxPreviewConsoleMessages.value
  const gid = JSON.stringify(groupId)
  const body = [
    // Clipped and counted inside the frame, or a logging loop pushes its whole heap
    // across the boundary.
    consoleRelayGuardScript(maxMessages),
    '(function() {',
    // Before the patch guard so it is always set; the #UI script tags messages with it.
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
    '    var line = window.__calcpadRelayLine(msg);',
    '    if (line === null) return;',
    "    try { window.parent.postMessage({ type: 'previewConsole', level: level, message: line, groupId: GROUP_ID }, '*'); } catch (e) {}",
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
    // CSP violations and resource load failures: a refused fetch never throws, and a
    // resource error does not bubble to the window listener.
    previewDiagnosticsScript('function(level, message) { post(level, [message]); }', maxMessages),
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
  // A prompt still up answers cancel.
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

// ---- External-link prompt ----
interface OpenLinkState {
  url: string
  mode: 'browser' | 'file'
  resolve: (ok: boolean) => void
}

const openLinkState = ref<OpenLinkState | null>(null)

function showOpenLink(url: string, mode: OpenLinkState['mode'] = 'browser'): Promise<boolean> {
  openLinkState.value?.resolve(false)
  return new Promise(resolve => {
    openLinkState.value = { url, mode, resolve }
  })
}

function resolveOpenLink(ok: boolean): void {
  const state = openLinkState.value
  if (!state) return
  openLinkState.value = null
  state.resolve(ok)
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
  // A prompt still up counts as dismissed.
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
  onLayoutChanged,
  setLayout,
  isPreviewVisible,
  setPreviewHtml,
  setPreviewLoading,
  setServerStatus,
  setPreviewTheme,
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
  setMaxPreviewConsoleMessages,
  showConfirm,
  showOpenLink,
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
  openFindInFocusedPreview,
  onOpenFullHtmlRequest,
})
</script>
