<template>
  <span>
    <template v-for="(part, index) in parts" :key="index">
      <mark v-if="index % 2" class="search-match">{{ part }}</mark>
      <template v-else>{{ part }}</template>
    </template>
  </span>
</template>

<script setup lang="ts">
import { computed } from 'vue'

const props = defineProps<{
  text: string
  query: string
}>()

const parts = computed(() => {
  if (!props.query.trim()) return [props.text]
  const escapedQuery = props.query.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
  return props.text.split(new RegExp(`(${escapedQuery})`, 'gi'))
})
</script>

<style scoped>
.search-match {
  color: inherit;
  background: var(--vscode-editor-findMatchHighlightBackground, #ea5c0055);
  outline: 1px solid var(--vscode-editor-findMatchHighlightBorder, transparent);
  border-radius: 2px;
}
</style>
