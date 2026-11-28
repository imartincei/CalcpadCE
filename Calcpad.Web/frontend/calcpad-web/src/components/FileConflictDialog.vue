<template>
  <div class="modal-backdrop" @click.self="emit('resolve', null)">
    <div class="modal-card file-conflict-card" role="dialog" aria-modal="true" :aria-label="t('fileConflict.fileChangedOnDisk')">
      <div class="modal-title">{{ t('fileConflict.fileChangedOnDisk') }}: {{ title }}</div>
      <div class="modal-message">{{ t('fileConflict.anotherProgramChangedThisFileWhileYouHad') }}</div>
      <div class="file-conflict-path">{{ filePath }}</div>
      <div class="file-conflict-labels">
        <span>{{ t('fileConflict.onDiskTheirs') }}</span>
        <span>{{ t('fileConflict.yourEditsMine') }}</span>
      </div>
      <div ref="diffHost" class="file-conflict-diff"></div>
      <div class="modal-actions">
        <button ref="mineButton" class="modal-btn" :title="t('fileConflict.keepYourEditsSavingWillOverwriteTheFile')" @click="emit('resolve', 'mine')">{{ t('fileConflict.keepMine') }}</button>
        <button class="modal-btn secondary" :title="t('fileConflict.discardYourEditsAndLoadTheFileFrom')" @click="emit('resolve', 'theirs')">{{ t('fileConflict.keepTheirs') }}</button>
        <button class="modal-btn secondary" :title="t('fileConflict.saveEachVersionAsACopyNextTo')" @click="emit('resolve', 'both')">{{ t('fileConflict.keepBoth') }}</button>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import * as monaco from 'monaco-editor'
import { onBeforeUnmount, onMounted, ref } from 'vue'
import { useI18n } from 'calcpad-frontend/i18n'
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
