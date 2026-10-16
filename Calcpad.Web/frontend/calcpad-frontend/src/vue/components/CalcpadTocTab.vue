<template>
  <div class="toc-tab">
    <div class="toc-container p-3">
      <div v-if="loading" class="loading">
        Loading headings...
      </div>
      <div v-else-if="headings.length === 0" class="no-headings">
        No headings found. Add headings to your document using
        <code>"Title</code>, <code>'&lt;h2&gt;...&lt;/h2&gt;</code>,
        or markdown <code>'### ...</code> syntax.
      </div>
      <div v-else class="toc-list">
        <div
          v-for="(heading, index) in headings"
          v-show="isVisible(index)"
          :key="index"
          class="toc-item"
          :class="[`toc-level-${heading.level}`]"
          :style="{ paddingLeft: (heading.level - minLevel) * 16 + 'px' }"
        >
          <span
            class="toc-arrow"
            :class="{ collapsed: isCollapsed(index), leaf: !hasChildren(index) }"
            @click.stop="toggleCollapse(index)"
          >&#9654;</span>
          <span
            class="toc-level-badge"
          >H{{ heading.level }}</span>
          <span
            class="toc-text"
            :title="`Go to line ${heading.line}`"
            @click="$emit('go-to-line', heading.line)"
          >{{ heading.text }}</span>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import type { TocHeading } from '../types'

interface Props {
  headings?: TocHeading[]
  loading?: boolean
}

const props = withDefaults(defineProps<Props>(), {
  headings: () => [],
  loading: false
})

defineEmits<{
  'go-to-line': [line: number]
}>()

// The smallest heading level present — used so indentation starts at 0
const minLevel = computed(() =>
  props.headings.reduce((min, h) => Math.min(min, h.level), 6)
)

// Set of collapsed heading indices
const collapsedSet = ref<Set<number>>(new Set())

// Reset collapsed state when headings change
watch(() => props.headings, () => {
  collapsedSet.value = new Set()
})

/** Whether heading at `index` has any children (next heading with a deeper level). */
function hasChildren(index: number): boolean {
  const level = props.headings[index].level
  for (let i = index + 1; i < props.headings.length; i++) {
    if (props.headings[i].level <= level) break
    return true
  }
  return false
}

/** Whether heading at `index` is currently collapsed. */
function isCollapsed(index: number): boolean {
  return collapsedSet.value.has(index)
}

/** Whether heading at `index` should be visible (not hidden by a collapsed ancestor). */
function isVisible(index: number): boolean {
  // Walk backwards to find if any ancestor is collapsed
  const level = props.headings[index].level
  for (let i = index - 1; i >= 0; i--) {
    const ancestor = props.headings[i]
    if (ancestor.level < level) {
      // This is a parent — if collapsed, hide us
      if (collapsedSet.value.has(i)) return false
      // Continue checking grandparents at even shallower levels
    }
  }
  return true
}

function toggleCollapse(index: number): void {
  if (!hasChildren(index)) return
  const next = new Set(collapsedSet.value)
  if (next.has(index)) {
    next.delete(index)
  } else {
    next.add(index)
  }
  collapsedSet.value = next
}
</script>

<style scoped>
.toc-tab {
  height: 100%;
  display: flex;
  flex-direction: column;
}

.toc-container {
  overflow-y: auto;
  flex: 1;
}

.loading,
.no-headings {
  text-align: center;
  color: var(--vscode-descriptionForeground);
  padding: 20px;
  font-style: italic;
}

.no-headings code {
  background: var(--vscode-textCodeBlock-background);
  padding: 1px 4px;
  border-radius: 2px;
  font-size: var(--calcpad-font-size-sm);
}

.toc-list {
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.toc-item {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 6px 8px;
  border-radius: 3px;
}

.toc-arrow {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 16px;
  height: 16px;
  font-size: var(--calcpad-font-size-xs);
  color: var(--vscode-descriptionForeground);
  cursor: pointer;
  flex-shrink: 0;
  transition: transform 0.15s ease;
  transform: rotate(90deg);
  user-select: none;
}

.toc-arrow:hover {
  color: var(--vscode-foreground);
}

.toc-arrow.collapsed {
  transform: rotate(0deg);
}

.toc-arrow.leaf {
  visibility: hidden;
}

.toc-level-badge {
  font-size: var(--calcpad-font-size-xxs);
  font-weight: 600;
  color: var(--vscode-descriptionForeground);
  background: var(--vscode-badge-background);
  padding: 1px 4px;
  border-radius: 3px;
  flex-shrink: 0;
  min-width: 20px;
  text-align: center;
}

.toc-text {
  font-size: var(--calcpad-font-size-md);
  color: var(--vscode-foreground);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  cursor: pointer;
  flex: 1;
}

.toc-text:hover {
  background: var(--vscode-list-hoverBackground);
  border-radius: 2px;
}

/* Visually distinguish top-level headings */
.toc-level-1 .toc-text,
.toc-level-2 .toc-text {
  font-weight: 600;
}

.toc-level-3 .toc-text {
  font-weight: 500;
}
</style>
