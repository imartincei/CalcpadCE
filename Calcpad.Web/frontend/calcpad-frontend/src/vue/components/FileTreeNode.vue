<template>
  <div class="tree-node">
    <div
      class="tree-row"
      :class="{ directory: node.isDirectory, file: !node.isDirectory }"
      :style="{ paddingLeft: (8 + depth * 12) + 'px' }"
      @click="handleClick"
      @contextmenu.prevent.stop="handleContextMenu"
    >
      <span v-if="node.isDirectory" class="tree-arrow" :class="{ open: isExpanded }">&#x25B6;</span>
      <span v-else class="tree-arrow-spacer"></span>
      <span class="tree-icon">{{ node.isDirectory ? '📁' : '📄' }}</span>
      <span class="tree-label" :title="node.path">{{ node.name }}</span>
    </div>
    <div v-if="node.isDirectory && isExpanded && node.children && node.children.length > 0">
      <FileTreeNode
        v-for="child in node.children"
        :key="child.path"
        :node="child"
        :depth="depth + 1"
        :expanded-paths="expandedPaths"
        @toggle="(p, e) => $emit('toggle', p, e)"
        @open-file="(p) => $emit('open-file', p)"
        @context-menu="(payload) => $emit('context-menu', payload)"
      />
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import type { FileNode } from '../types'

interface Props {
  node: FileNode
  expandedPaths: string[]
  depth?: number
}

const props = withDefaults(defineProps<Props>(), { depth: 0 })

export interface ContextMenuPayload {
  node: FileNode
  x: number
  y: number
}

const emit = defineEmits<{
  toggle: [path: string, isExpanding: boolean]
  'open-file': [path: string]
  'context-menu': [payload: ContextMenuPayload]
}>()

const isExpanded = computed(() => props.expandedPaths.includes(props.node.path))

const handleClick = () => {
  if (props.node.isDirectory) {
    emit('toggle', props.node.path, !isExpanded.value)
  } else {
    emit('open-file', props.node.path)
  }
}

const handleContextMenu = (e: MouseEvent) => {
  // Preventing the default menu avoids the WebKitGTK context-menu segfault
  // on Linux — the native GTK context menu doesn't play well with the
  // embedded webview process.
  emit('context-menu', { node: props.node, x: e.clientX, y: e.clientY })
}
</script>

<style scoped>
.tree-node {
  user-select: none;
}

.tree-row {
  display: flex;
  align-items: center;
  padding: 2px 8px 2px 0;
  cursor: pointer;
  font-size: var(--calcpad-font-size-lg);
  color: var(--vscode-foreground);
  gap: 4px;
  white-space: nowrap;
  overflow: hidden;
}

.tree-row:hover {
  background: var(--vscode-list-hoverBackground);
}

.tree-arrow {
  display: inline-block;
  width: 12px;
  font-size: var(--calcpad-font-size-xxs);
  color: var(--vscode-icon-foreground, var(--vscode-foreground));
  transition: transform 0.15s ease;
  transform-origin: center;
}

.tree-arrow.open {
  transform: rotate(90deg);
}

.tree-arrow-spacer {
  display: inline-block;
  width: 12px;
}

.tree-icon {
  font-size: var(--calcpad-font-size-md);
  flex-shrink: 0;
}

.tree-label {
  flex: 1;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
}
</style>
