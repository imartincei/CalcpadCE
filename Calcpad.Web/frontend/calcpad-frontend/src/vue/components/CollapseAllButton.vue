<template>
  <button
    type="button"
    class="icon"
    :title="expand ? 'Expand all' : 'Collapse all'"
    @click="$emit('click')"
  >
    <svg width="16" height="16" viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.2" aria-hidden="true">
      <path d="M5 2h9v9M2 5h9v9H2zM4 9.5h5" />
      <path v-if="expand" d="M6.5 7v5" />
    </svg>
  </button>
</template>

<script setup lang="ts">
defineProps<{
  expand?: boolean
}>()

defineEmits<{
  click: []
}>()
</script>
