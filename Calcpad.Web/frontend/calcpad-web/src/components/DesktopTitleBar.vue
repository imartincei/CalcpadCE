<template>
  <header class="desktop-titlebar" :style="{ '--titlebar-leading-width': leadingWidth + 'px' }" data-tauri-drag-region @contextmenu.prevent>
    <div ref="leading" class="titlebar-leading">
      <img class="titlebar-logo" :src="CALCPAD_ICON_URL" alt="CalcpadCE" />
      <button ref="fileButton" class="titlebar-button" :title="t('File menu (Alt+M)')" :aria-label="t('File menu')" aria-haspopup="menu" :aria-expanded="menuOpen" @mousedown.left="onMenuMouseDown" @click="onMenuClick">
        <svg viewBox="0 0 16 16" aria-hidden="true"><path d="M2 4h12M2 8h12M2 12h12"/></svg>
        <span>{{ t('File') }}</span>
      </button>
      <div class="titlebar-actions">
        <button v-for="action in actions" :key="action.id" class="titlebar-button" :title="t(action.title)" :aria-label="t(action.label)" :disabled="action.id === 'save' && !canSave" @click="$emit('command', action.id)">
          <svg viewBox="0 0 16 16" aria-hidden="true" :class="action.format">
            <path :d="action.path"/>
            <template v-if="action.format === 'pdf'">
              <rect class="format-badge" x="0.5" y="7" width="15" height="8" rx="1" />
              <text x="8" y="13" text-anchor="middle">PDF</text>
            </template>
            <template v-if="action.format === 'word'">
              <path d="M7 7h6M7 9h6M7 11h6"/>
              <rect class="format-badge" x="0.5" y="5" width="9" height="9" rx="1" />
              <text x="5" y="11.7" text-anchor="middle">W</text>
            </template>
          </svg>
        </button>
      </div>
    </div>
    <div class="titlebar-caption" data-tauri-drag-region :title="title">
      <span data-tauri-drag-region>{{ title }}</span>
    </div>
    <div class="titlebar-window-controls">
      <button class="titlebar-button" :aria-label="t('Minimize')" @click="desktopWindow.minimize()">
        <svg viewBox="0 0 16 16" aria-hidden="true"><path d="M3 8.5h10"/></svg>
      </button>
      <button class="titlebar-button" :aria-label="maximized ? t('Restore') : t('Maximize')" @click="desktopWindow.toggleMaximize()">
        <svg viewBox="0 0 16 16" aria-hidden="true"><path :d="maximized ? 'M5.5 5.5h7v7h-7zM3.5 10V3.5H10' : 'M3.5 3.5h9v9h-9z'"/></svg>
      </button>
      <button class="titlebar-button titlebar-close" :aria-label="t('Close window')" @click="desktopWindow.close()">
        <svg viewBox="0 0 16 16" aria-hidden="true"><path d="m3.5 3.5 9 9m0-9-9 9"/></svg>
      </button>
    </div>
  </header>
  <div v-if="linux && menuOpen" ref="fileMenu" class="tab-context-menu desktop-file-menu" :style="{ left: menuLeft + 'px' }" role="menu" :aria-label="t('File')" @mousedown.stop @keydown="onMenuKeyDown">
    <template v-for="entry in menuEntries" :key="entry.id">
      <div v-if="!entry.text" class="tab-context-sep" role="separator"></div>
      <details v-else-if="entry.children.length" class="recent-menu">
        <summary role="menuitem" aria-haspopup="menu">{{ entry.text }}<span aria-hidden="true">›</span></summary>
        <div role="menu">
          <template v-for="child in entry.children" :key="child.id">
            <div v-if="!child.text" class="tab-context-sep" role="separator"></div>
            <button v-else class="context-menu" role="menuitem" :disabled="!child.enabled" :title="child.text" @click="runMenuCommand(child.id)">{{ child.text }}</button>
          </template>
        </div>
      </details>
      <button v-else class="context-menu" role="menuitem" :disabled="!entry.enabled" @click="runMenuCommand(entry.id)">
        <span>{{ entry.text }}</span><span class="menu-shortcut">{{ shortcuts[entry.id] }}</span>
      </button>
    </template>
  </div>
</template>

<script setup lang="ts">
import { nextTick, onBeforeUnmount, onMounted, ref } from 'vue'
import { invoke } from '@tauri-apps/api/core'
import { getCurrentWindow } from '@tauri-apps/api/window'
import { platform } from '@tauri-apps/plugin-os'
import { CALCPAD_ICON_URL } from 'calcpad-frontend/vue/calcpad-icon'
import { useI18n } from 'vue-i18n'

defineProps<{ title: string; canSave: boolean }>()
const { t } = useI18n()
const emit = defineEmits<{ command: [id: string] }>()

const desktopWindow = getCurrentWindow()
const maximized = ref(false)
const menuOpen = ref(false)
const linux = platform() === 'linux'
const fileButton = ref<HTMLButtonElement>()
const fileMenu = ref<HTMLElement>()
const menuLeft = ref(0)
const leading = ref<HTMLElement>()
const leadingWidth = ref(0)
const leadingObserver = new ResizeObserver(([entry]) => { leadingWidth.value = entry.borderBoxSize[0].inlineSize })
interface MenuEntry { id: string; text: string; enabled: boolean; children: MenuEntry[] }
const menuEntries = ref<MenuEntry[]>([])
const shortcuts: Record<string, string> = {
  new: 'Ctrl+N', 'new-window': 'Ctrl+Alt+W', open: 'Ctrl+O',
  save: 'Ctrl+S', 'save-as': 'Ctrl+Shift+S', 'close-tab': 'Ctrl+W',
}
const actions = [
  { id: 'save', label: 'Save document', title: 'Save document (Ctrl+S)', path: 'M1 1h11l3 3v11H1zM4 1v5h7V1M4 15V9h8v6' },
  { id: 'open', label: 'Open document', title: 'Open document (Ctrl+O)', path: 'M1 7V2h5l2 2h7v11H1zM1 7h14' },
  { id: 'export-pdf', label: 'Export to PDF', title: 'Export report to PDF and open (Ctrl+E)', format: 'pdf', path: 'M10 1H3v13h11V5zM10 1v4h4' },
  { id: 'export-docx', label: 'Export to Word', title: 'Export report to Word and open', format: 'word', path: 'M10 1H4v13h11V5zM10 1v4h5' },
]
let unlistenResize: (() => void) | undefined

async function showMenu(): Promise<void> {
  if (linux) {
    if (menuOpen.value) { closeMenu(); return }
    menuLeft.value = fileButton.value!.getBoundingClientRect().left
    menuEntries.value = await invoke<MenuEntry[]>('get_app_menu')
    menuOpen.value = true
    await nextTick()
    fileMenu.value!.querySelector<HTMLElement>('button:not(:disabled), summary')!.focus()
    return
  }
  if (menuOpen.value) return
  const button = fileButton.value!.getBoundingClientRect()
  menuOpen.value = true
  try {
    await invoke('show_app_menu', { x: button.left, y: button.bottom })
  } finally {
    menuOpen.value = false
  }
}

function onMenuMouseDown(event: MouseEvent): void {
  if (!linux) { event.preventDefault(); void showMenu() }
}

function onMenuClick(event: MouseEvent): void {
  if (linux || event.detail === 0) void showMenu()
}

function closeMenu(): void {
  menuOpen.value = false
}

function runMenuCommand(id: string): void {
  closeMenu()
  emit('command', id)
}

function onOutsidePointerDown(event: PointerEvent): void {
  if (linux && !fileButton.value!.contains(event.target as Node) && !fileMenu.value?.contains(event.target as Node)) closeMenu()
}

function onMenuKeyDown(event: KeyboardEvent): void {
  const current = document.activeElement as HTMLElement
  const recent = current.closest('details')
  if (event.key === 'Escape' || event.key === 'Tab') {
    closeMenu()
    fileButton.value!.focus()
    if (event.key === 'Escape') event.preventDefault()
  } else if (event.key === 'ArrowRight' && current.tagName === 'SUMMARY') {
    recent!.open = true
    recent!.querySelector<HTMLButtonElement>('button:not(:disabled)')?.focus()
    event.preventDefault()
  } else if (event.key === 'ArrowLeft' && recent) {
    recent.open = false
    recent.querySelector('summary')!.focus()
    event.preventDefault()
  } else if (['ArrowDown', 'ArrowUp', 'Home', 'End'].includes(event.key)) {
    const items = [...fileMenu.value!.querySelectorAll<HTMLElement>('button:not(:disabled), summary')].filter(item => item.checkVisibility())
    const index = items.indexOf(current)
    const next = event.key === 'Home' ? 0 : event.key === 'End' ? items.length - 1
      : (index + (event.key === 'ArrowUp' ? -1 : 1) + items.length) % items.length
    items[next].focus()
    event.preventDefault()
  }
}

function onKeyDown(event: KeyboardEvent): void {
  if (event.altKey && event.key.toLowerCase() === 'm' && !event.ctrlKey && !event.metaKey && !event.shiftKey) {
    event.preventDefault()
    void showMenu()
  }
}

onMounted(async () => {
  leadingWidth.value = leading.value!.offsetWidth
  leadingObserver.observe(leading.value!)
  maximized.value = await desktopWindow.isMaximized()
  unlistenResize = await desktopWindow.onResized(async () => {
    maximized.value = await desktopWindow.isMaximized()
  })
  window.addEventListener('keydown', onKeyDown)
  if (linux) {
    document.addEventListener('pointerdown', onOutsidePointerDown, true)
    window.addEventListener('blur', closeMenu)
  }
})

onBeforeUnmount(() => {
  leadingObserver.disconnect()
  unlistenResize?.()
  window.removeEventListener('keydown', onKeyDown)
  document.removeEventListener('pointerdown', onOutsidePointerDown, true)
  window.removeEventListener('blur', closeMenu)
})
</script>

<style scoped>
.desktop-file-menu {
  top: 36px;
  min-width: 240px;
  max-width: calc(100vw - 48px);
  max-height: calc(100vh - 44px);
  overflow-y: auto;
  z-index: 10001;
  font-size: var(--vscode-font-size);
}
.desktop-file-menu button,
.desktop-file-menu summary {
  cursor: default;
  white-space: nowrap;
}
.desktop-file-menu button {
  overflow: hidden;
  text-overflow: ellipsis;
}
.desktop-file-menu summary {
  display: flex;
  justify-content: space-between;
  padding: 5px 14px;
  list-style: none;
}
.desktop-file-menu summary::-webkit-details-marker { display: none; }
.desktop-file-menu summary:hover,
.desktop-file-menu [role="menuitem"]:focus-visible {
  background: var(--vscode-list-hoverBackground);
  outline: none;
}
.recent-menu > div { padding-left: 12px; }
.menu-shortcut {
  margin-left: auto;
  padding-left: 24px;
  opacity: 0.7;
}
.desktop-titlebar {
  position: relative;
  display: grid;
  grid-template-columns: minmax(var(--titlebar-leading-width), 1fr) minmax(0, auto) minmax(var(--titlebar-leading-width), 1fr);
  flex: 0 0 36px;
  height: 36px;
  min-height: 36px;
  min-width: 0;
  align-items: stretch;
  background: var(--vscode-titleBar-activeBackground, var(--vscode-editorGroupHeader-tabsBackground));
  color: var(--vscode-titleBar-activeForeground, var(--vscode-foreground));
  border-bottom: 1px solid var(--vscode-widget-border);
  user-select: none;
}
.titlebar-leading {
  display: flex;
  z-index: 1;
  justify-self: start;
}
.titlebar-logo {
  width: 24px;
  height: 24px;
  align-self: center;
  margin: 0 8px;
}
.titlebar-caption {
  flex: 1;
  min-width: 0;
  padding: 0 12px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: var(--vscode-font-size);
  text-align: center;
}
.titlebar-caption span {
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
}
.titlebar-actions,
.titlebar-window-controls {
  display: flex;
  flex-shrink: 0;
}
.titlebar-window-controls {
  justify-self: end;
}
.titlebar-button {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 5px;
  min-width: 36px;
  padding: 0 9px;
  color: inherit;
  background: transparent;
  border: 0;
  font: inherit;
  font-size: var(--vscode-font-size);
  line-height: 1;
  align-self: stretch;
  cursor: default;
}
.titlebar-button svg {
  width: 16px;
  height: 16px;
  fill: none;
  stroke: currentColor;
  stroke-width: 1.2;
  stroke-linecap: round;
  stroke-linejoin: round;
}
.titlebar-leading > .titlebar-button span {
  transform: translateY(-0.5px);
}
.titlebar-actions .titlebar-button {
  min-width: 36px;
}
.titlebar-actions svg {
  width: 18px;
  height: 18px;
}
.titlebar-button:hover:not(:disabled) {
  background: var(--vscode-toolbar-hoverBackground);
}
.titlebar-button .format-badge {
  fill: currentColor;
  stroke: none;
}
.titlebar-button svg text {
  fill: var(--vscode-titleBar-activeBackground, var(--vscode-editorGroupHeader-tabsBackground));
  stroke: none;
  font: bold 6px sans-serif;
}
.titlebar-button svg.word text { font-size: 7px; }
.titlebar-button:focus-visible {
  outline: 1px solid var(--vscode-focusBorder);
  outline-offset: -3px;
}
.titlebar-close {
  border-radius: 0;
}
.titlebar-close:hover:not(:disabled) {
  background: #c42b1c;
  color: white;
}
@media (max-width: 520px) {
  .titlebar-button { min-width: 24px; padding: 0 4px; }
  .titlebar-actions .titlebar-button { min-width: 28px; padding: 0 3px; }
  .titlebar-logo { margin: 0 4px; }
}
</style>
