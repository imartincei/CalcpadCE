<template>
  <div class="modal-backdrop agent-diff-backdrop">
    <div class="modal-card agent-diff-card" role="dialog" aria-modal="true">
      <div class="modal-title">{{ t('Proposed edit') }}: {{ title }}</div>
      <div v-if="summary" class="modal-message">{{ summary }}</div>
      <div ref="host" class="agent-diff-host"></div>
      <div class="modal-actions">
        <button class="modal-btn" @click="emit('resolve', true)">{{ t('Accept') }}</button>
        <button class="modal-btn secondary" @click="emit('resolve', false)">{{ t('Reject') }}</button>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import * as monaco from 'monaco-editor'

const props = defineProps<{ title: string; summary: string; original: string; modified: string }>()
const emit = defineEmits<{ resolve: [accept: boolean] }>()
const { t } = useI18n()

const host = ref<HTMLElement | null>(null)
let diffEditor: monaco.editor.IStandaloneDiffEditor | null = null
const models: monaco.editor.ITextModel[] = []

function onKey(e: KeyboardEvent): void {
  if (e.key === 'Escape') emit('resolve', false)
}

onMounted(() => {
  if (!host.value) return
  const original = monaco.editor.createModel(props.original, 'calcpad')
  const modified = monaco.editor.createModel(props.modified, 'calcpad')
  models.push(original, modified)
  diffEditor = monaco.editor.createDiffEditor(host.value, {
    readOnly: true,
    originalEditable: false,
    automaticLayout: true,
    renderSideBySide: host.value.clientWidth > 900,
    minimap: { enabled: false },
    scrollBeyondLastLine: false,
  })
  diffEditor.setModel({ original, modified })
  window.addEventListener('keydown', onKey)
})

onBeforeUnmount(() => {
  window.removeEventListener('keydown', onKey)
  diffEditor?.dispose()
  for (const model of models) model.dispose()
})
</script>

<style scoped>
.agent-diff-card {
  display: flex;
  flex-direction: column;
  width: min(1200px, 92vw);
  max-width: none;
  height: min(800px, 85vh);
}
.agent-diff-host {
  flex: 1;
  min-height: 0;
  margin: 8px 0;
  border: 1px solid var(--vscode-widget-border, #2b2b2b);
}
</style>
