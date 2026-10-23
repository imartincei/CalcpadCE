<template>
  <svg width="16" height="16" viewBox="0 0 16 16" aria-hidden="true" fill="none" stroke="currentColor" stroke-width="1.2">
    <g :transform="side === 'right' ? 'translate(16 0) scale(-1 1)' : undefined">
      <rect x="2.5" y="2.5" width="11" height="11" rx="1"/>
      <line x1="6" y1="2.5" x2="6" y2="13.5"/>
      <polyline :points="collapsed ? '8.5,5.5 11,8 8.5,10.5' : '11,5.5 8.5,8 11,10.5'"/>
    </g>
  </svg>
</template>

<script setup lang="ts">
withDefaults(defineProps<{
  side?: 'left' | 'right'
  collapsed?: boolean
}>(), {
  side: 'left',
  collapsed: false,
})
</script>
