<template>
  <div class="hotkey-recorder">
    <div class="hotkey-recorder-row">
      <input
        ref="input"
        class="hotkey-recorder-input"
        :class="{ 'input-invalid': !!error }"
        data-hotkey-recorder
        readonly
        :value="pending ? formatHotkey(pending) : ''"
        :placeholder="t('hotkeys.pressAKeyCombinationEllipsis')"
        :aria-label="t('hotkeys.pressAKeyCombinationEllipsis')"
        @keydown="onKeydown"
      />
      <button :disabled="!pending || !!error" @click="emit('save', toPortableHotkey(pending))">{{ t('common.save') }}</button>
      <button v-if="current" @click="emit('save', '')">{{ t('common.remove') }}</button>
      <button
        v-if="defaultValue && current !== defaultValue"
        :title="formatHotkey(normalizeHotkey(defaultValue) ?? defaultValue)"
        @click="emit('save', defaultValue)"
      >{{ t('common.reset') }}</button>
      <button @click="emit('cancel')">{{ t('common.cancel') }}</button>
    </div>
    <span v-if="error" class="hotkey-recorder-error">{{ error }}</span>
    <span v-else-if="hint" class="hotkey-recorder-hint">{{ hint }}</span>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useI18n } from '../../i18n'
import { formatHotkey, hotkeyFromEvent, isMacPlatform, normalizeHotkey, toPortableHotkey, validateHotkey } from '../../services/hotkeys'
import { findKeybindingConflict } from '../../services/keybindings'
import type { HotkeyBinding } from '../../services/hotkeys'

const props = defineProps<{
  bindingId: string
  current: string
  defaultValue?: string
  keybindings: Record<string, string>
  extraReserved?: HotkeyBinding[]
  labelFor: (id: string) => string
  hint?: string
}>()

const emit = defineEmits<{
  save: [hotkey: string]
  cancel: []
}>()

const { t } = useI18n()
const input = ref<HTMLInputElement | null>(null)
const pending = ref('')

onMounted(() => input.value?.focus())

const onKeydown = (event: KeyboardEvent) => {
  if (event.key === 'Tab' && !event.ctrlKey && !event.altKey && !event.metaKey) return
  event.preventDefault()
  event.stopPropagation()
  if (event.key === 'Escape' && !event.ctrlKey && !event.altKey && !event.metaKey && !event.shiftKey) {
    emit('cancel')
    return
  }
  if (event.key === 'Enter' && pending.value && !error.value && !event.ctrlKey && !event.altKey && !event.metaKey && !event.shiftKey) {
    emit('save', toPortableHotkey(pending.value))
    return
  }
  const hotkey = hotkeyFromEvent(event)
  if (hotkey) pending.value = hotkey
}

const error = computed(() => {
  if (!pending.value) return ''
  const invalid = validateHotkey(pending.value)
  if (invalid) return t(invalid, { meta: isMacPlatform() ? 'Cmd' : 'Meta' })
  const conflict = findKeybindingConflict(pending.value, props.bindingId, props.keybindings, props.extraReserved)
  if (!conflict) return ''
  const name = conflict.kind === 'reserved' ? t(conflict.label) : props.labelFor(conflict.id)
  return t('hotkeys.keyIsAlreadyUsedByName', { key: formatHotkey(pending.value), name })
})
</script>

<style scoped>
.hotkey-recorder {
  display: flex;
  flex-direction: column;
  gap: 4px;
  margin-top: 6px;
}

.hotkey-recorder-row {
  display: flex;
  flex-wrap: wrap;
  gap: 4px;
  align-items: center;
}

.hotkey-recorder-input {
  flex: 1 1 120px;
  min-width: 0;
  padding: 4px 6px;
  background: var(--vscode-input-background);
  border: 1px solid var(--vscode-focusBorder);
  color: var(--vscode-input-foreground);
  border-radius: 3px;
  font-family: monospace;
}

.input-invalid {
  border-color: var(--vscode-inputValidation-errorBorder, #f14c4c);
}

.hotkey-recorder-error {
  color: var(--vscode-errorForeground, #f48771);
}

.hotkey-recorder-hint {
  color: var(--vscode-descriptionForeground);
}
</style>
