<template>
  <div class="modal-backdrop" @click.self="emit('resolve', null)">
    <div class="modal-card file-conflict-card" role="dialog" aria-modal="true" :aria-label="t('File changed on disk')">
      <div class="modal-title">{{ t('File changed on disk') }}: {{ title }}</div>
      <div class="modal-message">{{ t('Another program changed this file while you had unsaved edits. Choose which version to keep.') }}</div>
      <div class="file-conflict-path">{{ filePath }}</div>
      <div class="file-conflict-labels">
        <span>{{ t('On disk (theirs)') }}</span>
        <span>{{ t('Your edits (mine)') }}</span>
      </div>
      <div ref="diffHost" class="file-conflict-diff"></div>
      <div class="modal-actions">
        <button ref="mineButton" class="modal-btn" :title="t('Keep your edits; saving will overwrite the file on disk')" @click="emit('resolve', 'mine')">{{ t('Keep Mine') }}</button>
        <button class="modal-btn secondary" :title="t('Discard your edits and load the file from disk')" @click="emit('resolve', 'theirs')">{{ t('Keep Theirs') }}</button>
        <button class="modal-btn secondary" :title="t('Save each version as a copy next to the file')" @click="emit('resolve', 'both')">{{ t('Keep Both') }}</button>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import * as monaco from 'monaco-editor'
import { onBeforeUnmount, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import type { FileConflictChoice } from '../services/file-change-tracker'

const props = defineProps<{
  title: string
  filePath: string
  mine: string
  theirs: string
}>()

const emit = defineEmits<{
  resolve: [choice: FileConflictChoice | null]
}>()

const { t } = useI18n()
const diffHost = ref<HTMLElement>()
const mineButton = ref<HTMLButtonElement>()
let diffEditor: monaco.editor.IStandaloneDiffEditor | null = null
let models: monaco.editor.ITextModel[] = []

function onKeydown(e: KeyboardEvent): void {
  if (e.key === 'Escape') {
    e.preventDefault()
    e.stopPropagation()
    emit('resolve', null)
  }
}

onMounted(() => {
  const original = monaco.editor.createModel(props.theirs, 'calcpad')
  const modified = monaco.editor.createModel(props.mine, 'calcpad')
  models = [original, modified]
  diffEditor = monaco.editor.createDiffEditor(diffHost.value!, {
    readOnly: true,
    originalEditable: false,
    automaticLayout: true,
    renderSideBySide: true,
    minimap: { enabled: false },
    scrollBeyondLastLine: false,
  })
  diffEditor.setModel({ original, modified })
  window.addEventListener('keydown', onKeydown, true)
  mineButton.value?.focus()
})

onBeforeUnmount(() => {
  window.removeEventListener('keydown', onKeydown, true)
  diffEditor?.dispose()
  for (const m of models) m.dispose()
})
</script>

<style scoped>
.modal-card.file-conflict-card {
  width: min(1100px, 92vw);
  max-width: none;
  display: flex;
  flex-direction: column;
}
.file-conflict-path {
  font-family: var(--vscode-editor-font-family, monospace);
  font-size: 12px;
  opacity: 0.8;
  overflow-wrap: anywhere;
  margin-bottom: 10px;
}
.file-conflict-labels {
  display: grid;
  grid-template-columns: 1fr 1fr;
  font-size: 12px;
  font-weight: 600;
  margin-bottom: 4px;
}
.file-conflict-diff {
  height: min(60vh, 600px);
  border: 1px solid var(--vscode-editorWidget-border, #454545);
  margin-bottom: 12px;
}
</style>
