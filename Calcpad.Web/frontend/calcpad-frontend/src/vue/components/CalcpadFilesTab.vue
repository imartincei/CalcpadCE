<template>
  <div class="files-tab">
    <div v-if="!openedFolder" class="files-empty">
      <p class="files-empty-message">{{ t('No folder open.') }}</p>
      <button class="files-open-btn" @click="$emit('open-folder-request')">{{ t('Open Folder') }}</button>
    </div>
    <template v-else>
      <div class="files-header">
        <button class="icon files-close-btn" @click="$emit('close-folder')" :title="t('Close Folder')">
          <svg width="12" height="12" viewBox="0 0 16 16" fill="currentColor" xmlns="http://www.w3.org/2000/svg"><path d="M8 8.707l3.646 3.647.708-.707L8.707 8l3.647-3.646-.707-.708L8 7.293 4.354 3.646l-.707.708L7.293 8l-3.647 3.646.708.708L8 8.707z"/></svg>
        </button>
        <span class="files-folder-name" :title="openedFolder">{{ folderBasename }}</span>
        <div class="files-header-actions">
          <button class="icon" @click="onRefresh" :title="t('Refresh')">
            <RefreshIcon />
          </button>
          <button class="icon" @click="collapseAll" :title="t('Collapse All')">
            <svg width="14" height="14" viewBox="0 0 16 16" fill="currentColor" xmlns="http://www.w3.org/2000/svg"><path d="M4 9h11v1H4z"/></svg>
          </button>
          <button class="secondary files-change-btn" @click="$emit('open-folder-request')" :title="t('Open a different folder')">{{ t('Change Folder') }}</button>
        </div>
      </div>
      <label class="files-show-all">
        <input type="checkbox" v-model="showAll" />
        <span>{{ t('Show all files') }}</span>
      </label>
      <div class="file-tree" @contextmenu.prevent>
        <FileTreeNode
          v-for="node in filteredRoots"
          :key="node.path"
          :node="node"
          :expanded-paths="expandedPaths"
          @toggle="handleToggle"
          @open-file="(p) => $emit('open-file', p)"
          @context-menu="handleContextMenu"
        />
      </div>
    </template>
    <!-- Custom right-click menu. Rendered outside .files-tab so it can be
         positioned absolutely without being clipped by the sidebar's overflow.
         @mousedown.stop keeps the document-level closer from firing before the
         button's click handler runs. -->
    <div
      v-if="contextMenu"
      class="calcpad-context-menu"
      :style="{ left: contextMenu.x + 'px', top: contextMenu.y + 'px' }"
      @mousedown.stop
      @click.stop
    >
      <button class="context-menu" @click="onOpenContainingFolder">
        Open Containing Folder
      </button>
      <button class="context-menu" @click="onCopyFullPath">
        Copy Full Path
      </button>
      <button class="context-menu" @click="onCopyRelativePath">
        Copy Relative Path
      </button>
    </div>
  </div>
</template>

<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { ref, computed, watch, onMounted, onBeforeUnmount } from 'vue'
import type { FileNode } from '../types'
import FileTreeNode from './FileTreeNode.vue'
import RefreshIcon from './RefreshIcon.vue'
import type { ContextMenuPayload } from './FileTreeNode.vue'
import { writeClipboard } from '../services/clipboard'

const { t } = useI18n()

interface Props {
  openedFolder: string | null
  treeRoots: FileNode[]
}

const props = defineProps<Props>()

const emit = defineEmits<{
  'open-folder-request': []
  'open-file': [path: string]
  'expand-folder': [path: string]
  'open-containing-folder': [path: string]
  'close-folder': []
  'refresh-folder': []
}>()

const expandedPaths = ref<string[]>([])
const showAll = ref(false)
const STORAGE_KEY = 'calcpad.files.expanded.'

watch(() => props.openedFolder, (folder) => {
  expandedPaths.value = folder
    ? JSON.parse(localStorage.getItem(STORAGE_KEY + folder) ?? '[]')
    : []
}, { immediate: true })

watch(expandedPaths, (paths) => {
  if (props.openedFolder) {
    localStorage.setItem(STORAGE_KEY + props.openedFolder, JSON.stringify(paths))
  }
}, { flush: 'sync' })

// Extension filter: only worksheets — editable .cpd and compiled .cpdz — are
// surfaced by default. Folders are always kept so the user can drill in.
const WORKSHEET_EXTENSIONS = ['cpd', 'cpdz']

const isCalcpadFile = (name: string): boolean => {
  const dot = name.lastIndexOf('.')
  if (dot < 0) return false
  return WORKSHEET_EXTENSIONS.includes(name.substring(dot + 1).toLowerCase())
}

// Structural sharing: return the original array when no children were
// filtered out and no directory needed to be re-cloned. Big win on large
// trees — Vue can skip re-rendering entire branches when their reference
// identity is unchanged.
const filterNodes = (nodes: FileNode[]): FileNode[] => {
  let out: FileNode[] | null = null
  for (let i = 0; i < nodes.length; i++) {
    const n = nodes[i]
    if (n.isDirectory) {
      const original = n.children
      const filtered = original ? filterNodes(original) : original
      if (filtered === original) {
        if (out) out.push(n)
      } else {
        if (!out) out = nodes.slice(0, i)
        out.push({ ...n, children: filtered })
      }
    } else if (isCalcpadFile(n.name)) {
      if (out) out.push(n)
    } else {
      if (!out) out = nodes.slice(0, i)
      // drop it
    }
  }
  return out ?? nodes
}

const filteredRoots = computed(() =>
  showAll.value ? props.treeRoots : filterNodes(props.treeRoots)
)

const folderBasename = computed(() => {
  if (!props.openedFolder) return ''
  const normalized = props.openedFolder.replace(/\\/g, '/').replace(/\/+$/, '')
  const idx = normalized.lastIndexOf('/')
  return idx >= 0 ? normalized.substring(idx + 1) : normalized
})

const handleToggle = (path: string, isExpanding: boolean) => {
  if (isExpanding) {
    if (!expandedPaths.value.includes(path)) {
      expandedPaths.value = [...expandedPaths.value, path]
    }
  } else {
    expandedPaths.value = expandedPaths.value.filter(p => p !== path)
  }
}

const requestedDirectories = new WeakSet<FileNode>()
const loadExpandedDirectories = (nodes: FileNode[]) => {
  for (const node of nodes) {
    if (!node.isDirectory || !expandedPaths.value.includes(node.path)) continue
    if (!node.loaded) {
      if (!requestedDirectories.has(node)) {
        requestedDirectories.add(node)
        emit('expand-folder', node.path)
      }
    } else if (node.children) {
      loadExpandedDirectories(node.children)
    }
  }
}

watch([() => props.treeRoots, expandedPaths], () => {
  loadExpandedDirectories(props.treeRoots)
}, { immediate: true, deep: true })

const collapseAll = () => {
  expandedPaths.value = []
}

const onRefresh = () => {
  emit('refresh-folder')
}

// ---- Context menu ----

interface ContextMenuState {
  node: FileNode
  x: number
  y: number
}
const contextMenu = ref<ContextMenuState | null>(null)

const handleContextMenu = (payload: ContextMenuPayload) => {
  contextMenu.value = { node: payload.node, x: payload.x, y: payload.y }
}

const closeContextMenu = () => {
  contextMenu.value = null
}

const onDocumentInteraction = (e: MouseEvent | KeyboardEvent) => {
  if (!contextMenu.value) return
  if (e instanceof KeyboardEvent && e.key !== 'Escape') return
  closeContextMenu()
}

onMounted(() => {
  document.addEventListener('mousedown', onDocumentInteraction)
  document.addEventListener('keydown', onDocumentInteraction)
})

onBeforeUnmount(() => {
  document.removeEventListener('mousedown', onDocumentInteraction)
  document.removeEventListener('keydown', onDocumentInteraction)
})

const relativePathFor = (fullPath: string): string => {
  if (!props.openedFolder) return fullPath
  const rootNorm = props.openedFolder.replace(/[\\/]+$/, '')
  const rootWithSep = rootNorm + (rootNorm.includes('\\') ? '\\' : '/')
  if (fullPath.startsWith(rootWithSep)) {
    return fullPath.substring(rootWithSep.length)
  }
  if (fullPath === rootNorm) return ''
  return fullPath
}

const onOpenContainingFolder = () => {
  const menu = contextMenu.value
  if (!menu) return
  emit('open-containing-folder', menu.node.path)
  closeContextMenu()
}

const onCopyFullPath = async () => {
  const menu = contextMenu.value
  if (!menu) return
  await writeClipboard(menu.node.path)
  closeContextMenu()
}

const onCopyRelativePath = async () => {
  const menu = contextMenu.value
  if (!menu) return
  await writeClipboard(relativePathFor(menu.node.path))
  closeContextMenu()
}
</script>

<style scoped>
.files-tab {
  height: 100%;
  min-height: 0;
  display: flex;
  flex-direction: column;
  padding: 4px 0;
}

.files-empty {
  flex: 0 0 auto;
  padding: 16px;
  text-align: center;
  color: var(--vscode-descriptionForeground);
}

.files-empty-message {
  margin: 0 0 12px 0;
}

.files-open-btn {
  padding: 6px 14px;
  background: var(--vscode-button-background);
  color: var(--vscode-button-foreground);
  border: none;
  border-radius: 2px;
  cursor: pointer;
}

.files-open-btn:hover {
  background: var(--vscode-button-hoverBackground);
}

.files-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 4px 8px;
  gap: 6px;
  border-bottom: 1px solid var(--vscode-widget-border);
  text-transform: uppercase;
  color: var(--vscode-sideBarSectionHeader-foreground, var(--vscode-foreground));
  background: var(--vscode-sideBarSectionHeader-background, var(--vscode-editor-background));
  flex: 0 0 auto;
}

.files-folder-name {
  font-weight: 600;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  flex: 1;
  min-width: 0;
}

.files-header-actions {
  display: flex;
  align-items: center;
  gap: 2px;
  flex-shrink: 0;
}

.files-close-btn {
  margin-right: 2px;
  flex-shrink: 0;
}

.files-change-btn {
  text-transform: none;
}

.files-show-all {
  flex: 0 0 auto;
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 4px 10px;
  color: var(--vscode-descriptionForeground);
  cursor: pointer;
  user-select: none;
  /* Stick right below the header — z-index just under so the header still
     covers it if the sticky offsets ever overlap. */
  position: sticky;
  top: 24px;
  z-index: 1;
  background: var(--vscode-editor-background);
  border-bottom: 1px solid var(--vscode-widget-border);
}

.files-show-all input {
  margin: 0;
  cursor: pointer;
}

.file-tree {
  flex: 1 1 auto;
  min-height: 0;
  overflow-y: auto;
  overflow-x: hidden;
  padding: 4px 0;
}
</style>
