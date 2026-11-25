import type { InjectionKey } from 'vue'
import type { HotkeyBinding } from '../../services/hotkeys'

export interface SettingRowContext {
  rowVisible(section: string, row: string): boolean
  isFavorite(id: string): boolean
  toggleFavorite(id: string): void
  favoriteSlotSelector(id: string): string
  hotkeyFor(id: string): string
  setHotkey(id: string, hotkey: string): void
  keybindings(): Record<string, string>
  extraReserved(): HotkeyBinding[]
  labelFor(bindingId: string): string
}

export const SETTING_ROW_CONTEXT: InjectionKey<SettingRowContext> = Symbol('settingRowContext')
