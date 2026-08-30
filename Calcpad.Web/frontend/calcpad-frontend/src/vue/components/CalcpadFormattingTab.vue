<template>
  <div class="formatting-tab">
    <div class="formatting-container p-3">
      <h3 class="section-title">Prettify</h3>
      <p class="section-desc">
        Auto-indents the active document. Content is not modified outside of whitespace.
      </p>

      <div class="form-row">
        <label for="indentStyle">Indent style</label>
        <select id="indentStyle" :value="indentStyle" @change="onIndentStyleChange">
          <option value="tab">Tab</option>
          <option value="space">Space</option>
        </select>
      </div>

      <div class="form-row" v-if="indentStyle === 'space'">
        <label for="indentSize">Spaces per level</label>
        <input
          id="indentSize"
          type="number"
          min="1"
          max="16"
          :value="indentSize"
          @change="onIndentSizeChange"
        />
      </div>

      <div class="form-row">
        <label class="checkbox-label">
          <input
            type="checkbox"
            :checked="trimTrailingWhitespace"
            @change="onTrimChange"
          />
          Trim trailing whitespace
        </label>
      </div>

      <div class="actions">
        <button class="primary-button" @click="onPrettify">
          Prettify Document
        </button>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
interface Props {
  indentStyle?: 'tab' | 'space'
  indentSize?: number
  trimTrailingWhitespace?: boolean
}

withDefaults(defineProps<Props>(), {
  indentStyle: 'tab',
  indentSize: 4,
  trimTrailingWhitespace: true
})

const emit = defineEmits<{
  'prettify': []
  'update-indent-style': [style: 'tab' | 'space']
  'update-indent-size': [size: number]
  'update-trim-trailing': [enabled: boolean]
}>()

function onIndentStyleChange(e: Event) {
  const val = (e.target as HTMLSelectElement).value as 'tab' | 'space'
  emit('update-indent-style', val)
}

function onIndentSizeChange(e: Event) {
  const val = parseInt((e.target as HTMLInputElement).value, 10)
  if (Number.isFinite(val) && val >= 1 && val <= 16) {
    emit('update-indent-size', val)
  }
}

function onTrimChange(e: Event) {
  emit('update-trim-trailing', (e.target as HTMLInputElement).checked)
}

function onPrettify() {
  emit('prettify')
}
</script>

<style scoped>
.formatting-tab {
  height: 100%;
  display: flex;
  flex-direction: column;
}

.formatting-container {
  overflow-y: auto;
  flex: 1;
  padding: 12px;
}

.section-title {
  margin: 0 0 8px 0;
  font-size: 13px;
  font-weight: 600;
  color: var(--vscode-foreground);
}

.section-desc {
  font-size: 12px;
  color: var(--vscode-descriptionForeground);
  margin: 0 0 16px 0;
  line-height: 1.5;
}

.section-desc code {
  background: var(--vscode-textCodeBlock-background);
  padding: 1px 4px;
  border-radius: 2px;
  font-size: 11px;
}

.form-row {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 12px;
  font-size: 12px;
  color: var(--vscode-foreground);
}

.form-row label {
  min-width: 140px;
}

.form-row select,
.form-row input[type="number"] {
  background: var(--vscode-input-background);
  color: var(--vscode-input-foreground);
  border: 1px solid var(--vscode-input-border, transparent);
  padding: 4px 6px;
  font-size: 12px;
  font-family: var(--vscode-font-family);
  border-radius: 2px;
}

.form-row input[type="number"] {
  width: 70px;
}

.checkbox-label {
  display: flex;
  align-items: center;
  gap: 6px;
  cursor: pointer;
  min-width: 0;
}

.checkbox-label input[type="checkbox"] {
  margin: 0;
}

.actions {
  margin-top: 8px;
}

.primary-button {
  background: var(--vscode-button-background);
  color: var(--vscode-button-foreground);
  border: none;
  padding: 6px 14px;
  font-size: 12px;
  font-family: var(--vscode-font-family);
  cursor: pointer;
  border-radius: 2px;
}

.primary-button:hover {
  background: var(--vscode-button-hoverBackground);
}
</style>
