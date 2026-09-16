<template>
  <div class="errors-tab">
    <div class="errors-container p-3">
      <div v-if="errors.length === 0" class="no-errors">
        No errors from the last render.
      </div>
      <div v-else class="errors-list" @contextmenu.prevent="openContextMenu($event, null)">
        <div
          v-for="(err, i) in errors"
          :key="i"
          class="error-item"
          :title="`Go to line ${err.sourceLine}`"
          @click="$emit('go-to-line', err.sourceLine)"
          @contextmenu.prevent.stop="openContextMenu($event, err)"
        >
          <span class="error-icon">✕</span>
          <span class="error-source">{{ err.source }}</span>
          <span class="error-message">{{ err.message }}</span>
          <span class="error-location">Ln {{ err.sourceLine }}</span>
        </div>
      </div>
    </div>
    <div
      v-if="contextMenu"
      class="calcpad-context-menu"
      :style="{ left: contextMenu.x + 'px', top: contextMenu.y + 'px' }"
      @mousedown.stop
      @click.stop
    >
      <button
        v-if="contextMenu.error"
        class="calcpad-context-item"
        @click="onCopyError"
      >Copy</button>
      <button class="calcpad-context-item" @click="onCopyAllErrors">Copy All</button>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted, onBeforeUnmount } from 'vue'
import type { CalcpadError } from '../../types/api'
import { writeClipboard } from '../services/clipboard'

interface Props {
  errors?: CalcpadError[]
}

const props = withDefaults(defineProps<Props>(), {
  errors: () => []
})

defineEmits<{
  'go-to-line': [line: number]
}>()

interface ErrorContextMenu {
  x: number
  y: number
  error: CalcpadError | null
}

const contextMenu = ref<ErrorContextMenu | null>(null)

/** Matches the Problems panel's format. */
const formatError = (err: CalcpadError): string =>
  `[Ln ${err.sourceLine}] ${err.source}: ${err.message}`

const openContextMenu = (e: MouseEvent, error: CalcpadError | null) => {
  contextMenu.value = { x: e.clientX, y: e.clientY, error }
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

const onCopyError = () => {
  const err = contextMenu.value?.error
  closeContextMenu()
  if (err) void writeClipboard(formatError(err))
}

const onCopyAllErrors = () => {
  closeContextMenu()
  void writeClipboard(props.errors.map(formatError).join('\n'))
}
</script>

<style scoped>
.errors-tab {
  height: 100%;
  display: flex;
  flex-direction: column;
}

.errors-container {
  overflow-y: auto;
  flex: 1;
}

.no-errors {
  text-align: center;
  color: var(--vscode-descriptionForeground);
  padding: 20px;
  font-style: italic;
}

.errors-list {
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.error-item {
  display: grid;
  grid-template-columns: auto auto 1fr auto;
  align-items: baseline;
  gap: 8px;
  padding: 6px 8px;
  border-radius: 3px;
  cursor: pointer;
  font-size: var(--calcpad-font-size-md);
  color: var(--vscode-foreground);
}

.error-item:hover {
  background: var(--vscode-list-hoverBackground);
}

.error-icon {
  color: var(--vscode-errorForeground, #f14c4c);
  font-weight: 700;
}

.error-source {
  font-size: var(--calcpad-font-size-xs);
  color: var(--vscode-badge-foreground, var(--vscode-foreground));
  background: var(--vscode-badge-background);
  padding: 1px 4px;
  border-radius: 3px;
}

.error-message {
  white-space: pre-wrap;
  word-break: break-word;
}

.error-location {
  font-size: var(--calcpad-font-size-sm);
  color: var(--vscode-foreground);
  opacity: 0.7;
  white-space: nowrap;
}
</style>
