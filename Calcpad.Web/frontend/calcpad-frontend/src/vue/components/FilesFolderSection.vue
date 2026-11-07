<template>
  <div class="files-section" :class="{ collapsed }">
    <div class="files-header">
      <button class="icon files-collapse-btn" @click="$emit('toggle-collapse')" :title="collapsed ? t('Expand') : t('Collapse')">
        <span class="tree-arrow" :class="{ open: !collapsed }">&#x25B6;</span>
      </button>
      <span class="files-folder-name" :title="folder">{{ folderBasename }}</span>
      <div class="files-header-actions">
        <button class="icon" @click="$emit('refresh')" :title="t('Refresh')">
          <RefreshIcon />
        </button>
        <button class="icon" @click="expandedPaths = []" :title="t('Collapse All')">
          <svg width="14" height="14" viewBox="0 0 16 16" fill="currentColor" xmlns="http://www.w3.org/2000/svg"><path d="M4 9h11v1H4z"/></svg>
        </button>
        <button class="icon" @click="$emit('close')" :title="t('Close Folder')">
          <svg width="12" height="12" viewBox="0 0 16 16" fill="currentColor" xmlns="http://www.w3.org/2000/svg"><path d="M8 8.707l3.646 3.647.708-.707L8.707 8l3.647-3.646-.707-.708L8 7.293 4.354 3.646l-.707.708L7.293 8l-3.647 3.646.708.708L8 8.707z"/></svg>
        </button>
      </div>
    </div>
    <div v-if="!collapsed" class="file-tree" @contextmenu.prevent>
      <FileTreeNode
        v-for="node in filteredRoots"
        :key="node.path"
        :node="node"
        :expanded-paths="expandedPaths"
        @toggle="handleToggle"
        @open-file="(p) => $emit('open-file', p)"
        @context-menu="(payload) => $emit('context-menu', { ...payload, root: folder })"
      />
    </div>
  </div>
</template>

<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { ref, computed, watch } from 'vue'
import type { FileNode } from '../types'
import FileTreeNode from './FileTreeNode.vue'
import RefreshIcon from './RefreshIcon.vue'
import type { ContextMenuPayload } from './FileTreeNode.vue'

const { t } = useI18n()

export interface SectionContextMenuPayload extends ContextMenuPayload {
  root: string
}

interface Props {
  folder: string
  treeRoots: FileNode[]
  showAll: boolean
  collapsed: boolean
}

const props = defineProps<Props>()

const emit = defineEmits<{
  'open-file': [path: string]
  'expand-folder': [path: string]
  'context-menu': [payload: SectionContextMenuPayload]
  'toggle-collapse': []
  close: []
  refresh: []
}>()

const EXPANDED_KEY = 'calcpad.files.expanded.'

const expandedPaths = ref<string[]>([])

watch(() => props.folder, (folder) => {
  expandedPaths.value = JSON.parse(localStorage.getItem(EXPANDED_KEY + folder) ?? '[]')
}, { immediate: true })

watch(expandedPaths, (paths) => {
  localStorage.setItem(EXPANDED_KEY + props.folder, JSON.stringify(paths))
}, { flush: 'sync' })

// Only worksheets are shown by default; folders are always kept.
const WORKSHEET_EXTENSIONS = ['cpd', 'cpdz']

const isCalcpadFile = (name: string): boolean => {
  const dot = name.lastIndexOf('.')
  if (dot < 0) return false
  return WORKSHEET_EXTENSIONS.includes(name.substring(dot + 1).toLowerCase())
}

// Returns the original array when nothing was dropped, so Vue can skip unchanged branches.
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
    } else if (!out) {
      out = nodes.slice(0, i)
    }
  }
  return out ?? nodes
}

const filteredRoots = computed(() =>
  props.showAll ? props.treeRoots : filterNodes(props.treeRoots)
)

const folderBasename = computed(() => {
  const normalized = props.folder.replace(/\\/g, '/').replace(/\/+$/, '')
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
</script>

<style scoped>
.files-section {
  flex: 1 1 0;
  min-height: 0;
  display: flex;
  flex-direction: column;
}

.files-section.collapsed {
  flex: 0 0 auto;
}

.files-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 4px 8px 4px 4px;
  gap: 6px;
  border-bottom: 1px solid var(--vscode-widget-border);
  color: var(--vscode-sideBarSectionHeader-foreground, var(--vscode-foreground));
  background: var(--vscode-sideBarSectionHeader-background, var(--vscode-editor-background));
  flex: 0 0 auto;
}

.files-collapse-btn {
  flex-shrink: 0;
}

.tree-arrow {
  display: inline-block;
  width: 12px;
  font-size: var(--calcpad-font-size-icon-small);
  transition: transform 0.15s ease;
  transform-origin: center;
}

.tree-arrow.open {
  transform: rotate(90deg);
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

.file-tree {
  flex: 1 1 auto;
  min-height: 0;
  overflow-y: auto;
  overflow-x: hidden;
  padding: 4px 0;
}
</style>
