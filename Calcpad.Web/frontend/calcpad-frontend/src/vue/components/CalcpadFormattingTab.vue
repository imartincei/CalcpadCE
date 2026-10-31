<template>
  <div class="formatting-tab">
    <div class="formatting-container p-3">
      <h3 class="section-title">{{ t('Prettify') }}</h3>
      <p class="section-desc">{{ t('Auto-indents the active document. Content is not modified outside of whitespace.') }}</p>

      <div class="setting-group">
        <label for="indentStyle">{{ t('Indent style:') }}</label>
        <select id="indentStyle" :value="indentStyle" @change="onIndentStyleChange">
          <option value="tab">{{ t('Tab') }}</option>
          <option value="space">{{ t('Space') }}</option>
        </select>
      </div>

      <div class="setting-group" v-if="indentStyle === 'space'">
        <label for="indentSize">{{ t('Spaces per level:') }}</label>
        <input
          id="indentSize"
          type="number"
          min="1"
          max="16"
          :value="indentSize"
          @change="onIndentSizeChange"
        />
      </div>

      <div class="setting-group">
        <label>
          <input
            type="checkbox"
            :checked="trimTrailingWhitespace"
            @change="onTrimChange"
          />{{ t('Trim trailing whitespace') }}</label>
      </div>

      <div class="actions">
        <button @click="onPrettify" :disabled="sourceUnavailable" :title="sourceUnavailable ? t('Unavailable in input mode') : undefined">
          {{ t('Prettify Document') }}
        </button>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { useI18n } from 'vue-i18n'

const { t } = useI18n()
interface Props {
  sourceUnavailable?: boolean
  indentStyle?: 'tab' | 'space'
  indentSize?: number
  trimTrailingWhitespace?: boolean
}

withDefaults(defineProps<Props>(), {
  sourceUnavailable: false,
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
  display: flex;
  flex-direction: column;
}

.formatting-container {
  overflow-y: auto;
  flex: 1;
  padding: 0;
}

.section-title {
  margin: 0 0 8px 0;
  font-weight: 600;
  color: var(--vscode-foreground);
}

.section-desc {
  color: var(--vscode-descriptionForeground);
  margin: 0 0 16px 0;
  line-height: 1.5;
}

.section-desc code {
  background: var(--vscode-textCodeBlock-background);
  padding: 1px 4px;
  border-radius: 2px;
}

.actions {
  margin-top: 8px;
}

</style>
