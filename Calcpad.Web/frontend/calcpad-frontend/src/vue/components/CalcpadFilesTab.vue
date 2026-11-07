<template>
  <div class="files-tab">
    <div v-if="!anyOpen" class="files-empty">
      <p class="files-empty-message">{{ t('No folder open.') }}</p>
      <button class="files-open-btn" @click="$emit('open-folder-request', 0)">{{ t('Open Folder') }}</button>
    </div>
    <template v-else>
      <label class="files-show-all">
        <input type="checkbox" v-model="showAll" />
        <span>{{ t('Show all files') }}</span>
      </label>
      <div ref="sectionsEl" class="files-sections">
        <template v-for="(folder, slot) in folders" :key="slot">
          <div
            v-if="slot > 0 && resizable"
            class="files-splitter"
            role="separator"
            aria-orientation="horizontal"
            @pointerdown.prevent="startResize"
            @dblclick="resetSplit"
          />
          <FilesFolderSection
            v-if="folder.path"
            :folder="folder.path"
            :tree-roots="folder.roots"
            :show-all="showAll"
            :collapsed="collapsed[slot]"
            :style="sectionStyle(slot)"
            @open-file="(p) => $emit('open-file', p)"
            @expand-folder="(p) => $emit('expand-folder', p)"
            @context-menu="handleContextMenu"
            @toggle-collapse="toggleCollapsed(slot)"
            @close="$emit('close-folder', slot)"
            @refresh="$emit('refresh-folder', slot)"
          />
        </template>
      </div>
      <button v-if="emptySlot !== null" class="secondary files-add-btn" @click="$emit('open-folder-request', emptySlot)">
        {{ t('Open Another Folder') }}
      </button>
    </template>
    <!-- Custom right-click menu. Rendered outside .files-tab so it can be
         positioned absolutely without being clipped by the sidebar's overflow.
         @mousedown.stop keeps the document-level closer from firing before the
         button's click handler runs. -->
    <div
      v-if="contextMenu"
      class="calcpad-context-menu"
      :style="{ left: contextMenu.x + 'px', top: contextMenu.y + 'px' }"
      @mousedown.stop
      @click.stop
    >
      <button class="context-menu" @click="onOpenContainingFolder">
        Open Containing Folder
      </button>
      <button class="context-menu" @click="onCopyFullPath">
        Copy Full Path
      </button>
      <button class="context-menu" @click="onCopyRelativePath">
        Copy Relative Path
      </button>
    </div>
  </div>
</template>

<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { ref, computed, onMounted, onBeforeUnmount } from 'vue'
import type { OpenedFolder } from '../types'
import FilesFolderSection from './FilesFolderSection.vue'
import type { SectionContextMenuPayload } from './FilesFolderSection.vue'
import { writeClipboard } from '../services/clipboard'

const { t } = useI18n()

interface Props {
  folders: OpenedFolder[]
}

const props = defineProps<Props>()

const emit = defineEmits<{
  'open-folder-request': [slot: number]
  'open-file': [path: string]
  'expand-folder': [path: string]
  'open-containing-folder': [path: string]
  'close-folder': [slot: number]
  'refresh-folder': [slot: number]
}>()

const showAll = ref(false)

const anyOpen = computed(() => props.folders.some(f => f.path))
const emptySlot = computed(() => {
  const i = props.folders.findIndex(f => !f.path)
  return i >= 0 ? i : null
})

// ---- Section collapse and resize ----

const COLLAPSED_KEY = 'calcpad.files.collapsed.'
const SPLIT_KEY = 'calcpad.files.split'
const MIN_SECTION_PX = 48

const collapsed = ref([0, 1].map(i => localStorage.getItem(COLLAPSED_KEY + i) === '1'))

const toggleCollapsed = (slot: number) => {
  collapsed.value[slot] = !collapsed.value[slot]
  localStorage.setItem(COLLAPSED_KEY + slot, collapsed.value[slot] ? '1' : '0')
}

const storedSplit = parseFloat(localStorage.getItem(SPLIT_KEY) ?? '')
const split = ref(storedSplit > 0 && storedSplit < 1 ? storedSplit : 0.5)
const sectionsEl = ref<HTMLElement | null>(null)

const resizable = computed(() =>
  props.folders.every(f => f.path) && !collapsed.value.some(Boolean)
)

const sectionStyle = (slot: number) =>
  resizable.value ? { flexGrow: slot === 0 ? split.value : 1 - split.value } : undefined

const resetSplit = () => {
  split.value = 0.5
  localStorage.setItem(SPLIT_KEY, '0.5')
}

const startResize = (e: PointerEvent) => {
  const container = sectionsEl.value
  const handle = e.currentTarget as HTMLElement
  if (!container) return
  handle.setPointerCapture(e.pointerId)

  const onMove = (ev: PointerEvent) => {
    const rect = container.getBoundingClientRect()
    const available = rect.height - handle.offsetHeight
    if (available <= 0) return
    const min = Math.min(0.5, MIN_SECTION_PX / available)
    const ratio = (ev.clientY - rect.top - handle.offsetHeight / 2) / available
    split.value = Math.min(1 - min, Math.max(min, ratio))
  }
  const onEnd = () => {
    handle.removeEventListener('pointermove', onMove)
    handle.removeEventListener('pointerup', onEnd)
    handle.removeEventListener('pointercancel', onEnd)
    localStorage.setItem(SPLIT_KEY, String(split.value))
  }
  handle.addEventListener('pointermove', onMove)
  handle.addEventListener('pointerup', onEnd)
  handle.addEventListener('pointercancel', onEnd)
}

// ---- Context menu ----

const contextMenu = ref<SectionContextMenuPayload | null>(null)

const handleContextMenu = (payload: SectionContextMenuPayload) => {
  contextMenu.value = payload
}

const closeContextMenu = () => {
  contextMenu.value = null
}

const onDocumentInteraction = (e: MouseEvent | KeyboardEvent) => {
  if (!contextMenu.value) return
  if (e instanceof KeyboardEvent && e.key !== 'Escape') return
  closeContextMenu()
}

onMounted(() => {
  document.addEventListener('mousedown', onDocumentInteraction)
  document.addEventListener('keydown', onDocumentInteraction)
})

onBeforeUnmount(() => {
  document.removeEventListener('mousedown', onDocumentInteraction)
  document.removeEventListener('keydown', onDocumentInteraction)
})

const relativePathFor = (fullPath: string, root: string): string => {
  const rootNorm = root.replace(/[\\/]+$/, '')
  const rootWithSep = rootNorm + (rootNorm.includes('\\') ? '\\' : '/')
  if (fullPath.startsWith(rootWithSep)) {
    return fullPath.substring(rootWithSep.length)
  }
  if (fullPath === rootNorm) return ''
  return fullPath
}

const onOpenContainingFolder = () => {
  const menu = contextMenu.value
  if (!menu) return
  emit('open-containing-folder', menu.node.path)
  closeContextMenu()
}

const onCopyFullPath = async () => {
  const menu = contextMenu.value
  if (!menu) return
  await writeClipboard(menu.node.path)
  closeContextMenu()
}

const onCopyRelativePath = async () => {
  const menu = contextMenu.value
  if (!menu) return
  await writeClipboard(relativePathFor(menu.node.path, menu.root))
  closeContextMenu()
}
</script>

<style scoped>
.files-tab {
  height: 100%;
  min-height: 0;
  display: flex;
  flex-direction: column;
  padding: 4px 0;
}

.files-empty {
  flex: 0 0 auto;
  padding: 16px;
  text-align: center;
  color: var(--vscode-descriptionForeground);
}

.files-empty-message {
  margin: 0 0 12px 0;
}

.files-open-btn {
  padding: 6px 14px;
  background: var(--vscode-button-background);
  color: var(--vscode-button-foreground);
  border: none;
  border-radius: 2px;
  cursor: pointer;
}

.files-open-btn:hover {
  background: var(--vscode-button-hoverBackground);
}

.files-show-all {
  flex: 0 0 auto;
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 4px 10px;
  color: var(--vscode-descriptionForeground);
  cursor: pointer;
  user-select: none;
  border-bottom: 1px solid var(--vscode-widget-border);
}

.files-show-all input {
  margin: 0;
  cursor: pointer;
}

.files-sections {
  flex: 1 1 auto;
  min-height: 0;
  display: flex;
  flex-direction: column;
}

.files-splitter {
  flex: 0 0 4px;
  position: relative;
  z-index: 2;
  cursor: row-resize;
  touch-action: none;
  background: transparent;
  transition: background 0.1s ease 0.1s;
}

/* Wider hit area than the visible bar */
.files-splitter::before {
  content: '';
  position: absolute;
  inset: -4px 0;
}

.files-splitter:hover,
.files-splitter:active {
  background: var(--vscode-sash-hoverBorder, var(--vscode-focusBorder));
}

.files-add-btn {
  flex: 0 0 auto;
  margin: 6px 8px;
}
</style>
