<template>
  <div
    :class="{ 'active-group': active }"
    @mousedown="emit('focus', groupId)"
    class="tab-strip"
    :data-tab-group="groupId"
    @contextmenu.prevent
  >
    <div
      ref="tabList"
      class="tab-list"
      :class="{ 'overflow-left': overflowLeft, 'overflow-right': overflowRight }"
      role="tablist"
      @wheel="emit('wheel', $event)"
      @scroll="updateOverflow"
    >
      <div
        v-for="tab in tabs"
        :key="tab.id"
        class="tab"
        :data-tab-id="tab.id"
        :class="{ active: tab.isActive, dirty: tab.dirty }"
        role="tab"
        :aria-selected="tab.isActive"
        :title="tab.filePath || tab.title"
        @mousedown.left="emit('activate', groupId, tab.id)"
        @mousedown.middle.prevent="emit('close', groupId, tab.id)"
        @contextmenu.prevent="emit('contextmenu', $event, groupId, tab.id)"
      >
        <span class="tab-title">{{ tab.title }}</span>
        <span v-if="tab.dirty" class="tab-dirty-dot" :title="'Unsaved changes'">●</span>
        <button
          class="icon tab-close"
          :disabled="tabCount === 1 && tab.filePath === null"
          :title="tab.dirty ? 'Close (unsaved changes)' : 'Close'"
          @mousedown.stop
          @click.stop="emit('close', groupId, tab.id)"
        >
          ✕
        </button>
      </div>
      <button class="icon tab-new" title="New tab (Ctrl+T)" aria-label="New tab" @click="emit('new', groupId)">
        <svg viewBox="0 0 16 16" aria-hidden="true" fill="none" stroke="currentColor" stroke-width="1.2">
          <path d="M8 4v8M4 8h8" />
        </svg>
      </button>
    </div>
  </div>
</template>

<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, useTemplateRef, watch } from 'vue'
import type { TabUiState } from '../App.vue'

const props = defineProps<{
  groupId: string
  tabs: TabUiState[]
  tabCount: number
  active: boolean
}>()

const tabList = useTemplateRef<HTMLDivElement>('tabList')
const overflowLeft = ref(false)
const overflowRight = ref(false)
let resizeObserver: ResizeObserver

function updateOverflow(): void {
  const list = tabList.value!
  overflowLeft.value = list.scrollLeft > 0
  overflowRight.value = list.scrollLeft + list.clientWidth < list.scrollWidth - 1
}

onMounted(() => {
  resizeObserver = new ResizeObserver(updateOverflow)
  resizeObserver.observe(tabList.value!)
  updateOverflow()
})
onBeforeUnmount(() => resizeObserver.disconnect())
watch(() => props.tabs, updateOverflow, { deep: true, flush: 'post' })

const emit = defineEmits<{
  focus: [groupId: string]
  activate: [groupId: string, tabId: string]
  close: [groupId: string, tabId: string]
  new: [groupId: string]
  contextmenu: [event: MouseEvent, groupId: string, tabId: string]
  wheel: [event: WheelEvent]
}>()
</script>
