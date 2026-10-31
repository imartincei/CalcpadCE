<template>
  <div class="search-field">
    <input
      ref="searchInput"
      v-model="model"
      type="text"
      :placeholder="placeholder"
      :aria-label="placeholder"
      class="search-input"
      @keydown.enter.exact="submitSearch"
    />
    <button
      v-if="model"
      type="button"
      class="icon search-clear-btn"
      :title="t('Clear search')"
      @click="clearSearch"
    >
      <svg width="16" height="16" viewBox="0 0 16 16" aria-hidden="true">
        <path
          d="M7.5 3.5h6m-6 3h6m-11 3h11m-11 3h11M.5 1.5l5 5m0-5-5 5"
          fill="none"
          stroke="currentColor"
          stroke-linecap="round"
        />
      </svg>
    </button>
  </div>
</template>

<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { ref } from 'vue'

const { t } = useI18n()

defineProps<{
  placeholder: string
}>()

const model = defineModel<string>({ required: true })
const emit = defineEmits<{ submit: [] }>()
const searchInput = ref<HTMLInputElement | null>(null)

const submitSearch = (event: KeyboardEvent) => {
  if (event.isComposing) return
  event.preventDefault()
  event.stopPropagation()
  if (!event.repeat) emit('submit')
}

const focus = () => {
  searchInput.value!.focus()
  searchInput.value!.select()
}

defineExpose({ focus })

const clearSearch = () => {
  model.value = ''
  searchInput.value!.focus()
}
</script>

<style scoped>
.search-field {
  position: relative;
  display: flex;
  min-width: 0;
}

.search-input {
  width: 100%;
  min-width: 0;
  padding: 8px 36px 8px 8px;
  background: var(--vscode-input-background);
  border: 1px solid var(--vscode-input-border);
  color: var(--vscode-input-foreground);
  border-radius: 3px;
  font-family: var(--vscode-font-family);
}

.search-input:focus {
  outline: none;
  border-color: var(--vscode-focusBorder);
}

.search-clear-btn {
  position: absolute;
  top: 50%;
  right: 6px;
  transform: translateY(-50%);
}

.search-clear-btn:focus-visible {
  outline: 1px solid var(--vscode-focusBorder);
}
</style>
