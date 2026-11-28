<template>
  <!-- A favorited row is also rendered into the Favorites section. -->
  <Teleport
    v-for="place in places"
    :key="place"
    defer
    :to="place === 'favorites' ? ctx.favoriteSlotSelector(id) : 'body'"
    :disabled="place === 'base'"
  >
    <div
      v-show="ctx.rowVisible(section, row)"
      :ref="place === 'favorites' ? setCopyEl : undefined"
      class="setting-row"
      :class="rowClass"
      :data-setting-id="id"
      :data-favorite-copy="place === 'favorites' ? '' : undefined"
    >
      <div class="setting-row-line">
        <div class="setting-row-main"><slot /></div>
        <div class="setting-row-actions">
          <button
            class="setting-row-action hotkey"
            :class="{ active: !!hotkey }"
            :title="hotkeyTitle"
            :aria-label="hotkeyTitle"
            @click="recordingIn = recordingIn === place ? null : place"
          >{{ hotkey ? formatHotkey(normalizeHotkey(hotkey) ?? hotkey) : '⌨' }}</button>
          <button
            class="setting-row-action star"
            :class="{ active: favorite }"
            :title="favorite ? t('insert.removeFromFavorites') : t('insert.addToFavorites')"
            :aria-label="favorite ? t('insert.removeFromFavorites') : t('insert.addToFavorites')"
            :aria-pressed="favorite"
            @click="ctx.toggleFavorite(id)"
          >{{ favorite ? '★' : '☆' }}</button>
        </div>
      </div>
      <HotkeyRecorder
        v-if="recordingIn === place"
        :binding-id="SETTING_KEYBINDING_PREFIX + id"
        :current="hotkey"
        :keybindings="ctx.keybindings()"
        :extra-reserved="ctx.extraReserved()"
        :label-for="ctx.labelFor"
        :hint="isToggleSetting(id) ? t('settings.theHotkeyTurnsThisSettingOnAndOff') : t('settings.theHotkeyOpensThisSetting')"
        @save="save"
        @cancel="recordingIn = null"
      />
    </div>
  </Teleport>
</template>

<script setup lang="ts">
import { computed, inject, onBeforeUnmount, ref } from 'vue'
import type { ComponentPublicInstance } from 'vue'
import { useI18n } from '../../i18n'
import HotkeyRecorder from './HotkeyRecorder.vue'
import { SETTING_ROW_CONTEXT } from './setting-row-context'
import { formatHotkey, normalizeHotkey } from '../../services/hotkeys'
import { SETTING_KEYBINDING_PREFIX } from '../../services/keybindings'
import { isToggleSetting } from '../../services/setting-actions'

const props = withDefaults(defineProps<{
  section: string
  row: string
  rowClass?: string
}>(), { rowClass: 'setting-group' })

const { t } = useI18n()
const ctx = inject(SETTING_ROW_CONTEXT)!
const id = computed(() => props.section + '.' + props.row)
const favorite = computed(() => ctx.isFavorite(id.value))
const hotkey = computed(() => ctx.hotkeyFor(id.value))
const places = computed(() => favorite.value ? ['base', 'favorites'] as const : ['base'] as const)
const recordingIn = ref<'base' | 'favorites' | null>(null)

// The copy's ids get a suffix so labels and id lookups still reach the original row.
const COPY_ID_SUFFIX = '--favorite'
const suffixIds = (root: Element) => {
  root.querySelectorAll('[id]').forEach(el => {
    if (!el.id.endsWith(COPY_ID_SUFFIX)) el.id += COPY_ID_SUFFIX
  })
  root.querySelectorAll<HTMLLabelElement>('label[for]').forEach(label => {
    if (!label.htmlFor.endsWith(COPY_ID_SUFFIX)) label.htmlFor += COPY_ID_SUFFIX
  })
}
let copyObserver: MutationObserver | null = null
const setCopyEl = (el: Element | ComponentPublicInstance | null) => {
  copyObserver?.disconnect()
  copyObserver = null
  if (!(el instanceof Element)) return
  suffixIds(el)
  copyObserver = new MutationObserver(() => suffixIds(el))
  copyObserver.observe(el, { childList: true, subtree: true, attributes: true, attributeFilter: ['id', 'for'] })
}
onBeforeUnmount(() => copyObserver?.disconnect())

const hotkeyTitle = computed(() => {
  const action = isToggleSetting(id.value) ? t('settings.setAHotkeyToToggleThisSetting') : t('settings.setAHotkeyToOpenThisSetting')
  return hotkey.value ? `${action} (${formatHotkey(normalizeHotkey(hotkey.value) ?? hotkey.value)})` : action
})

const save = (value: string) => {
  ctx.setHotkey(id.value, value)
  recordingIn.value = null
}
</script>

<style scoped>
.setting-row.setting-group {
  margin-bottom: 12px;
}

.setting-row.settings-actions {
  margin-top: 16px;
}

/* The first line shares a row with the buttons; everything after it spans the full width. */
.setting-row-line {
  display: grid;
  grid-template-columns: minmax(0, 1fr) auto;
  align-items: start;
  column-gap: 4px;
}

.setting-row-main {
  display: contents;
}

.setting-row-main > :deep(*) {
  grid-column: 1 / -1;
  min-width: 0;
}

.setting-row-main > :deep(:first-child) {
  grid-column: 1;
  grid-row: 1;
}

.setting-row.settings-actions .setting-row-main {
  display: flex;
  gap: 8px;
  grid-column: 1;
  grid-row: 1;
}

.setting-row-actions {
  display: flex;
  grid-column: 2;
  grid-row: 1;
  gap: 2px;
}

.setting-row-action {
  min-width: 22px;
  height: 22px;
  padding: 0 4px;
  border: none;
  background: transparent;
  color: var(--vscode-descriptionForeground);
  cursor: pointer;
  font-size: 12px;
  line-height: 1;
  border-radius: 3px;
}

.setting-row-action:hover {
  background: var(--vscode-toolbar-hoverBackground, var(--vscode-list-hoverBackground));
  color: var(--vscode-foreground);
}

.setting-row-action.hotkey.active {
  font-family: monospace;
  border: 1px solid var(--vscode-widget-border);
}

.setting-row-action.star.active {
  color: var(--vscode-editorWarning-foreground, #cca700);
}
</style>
