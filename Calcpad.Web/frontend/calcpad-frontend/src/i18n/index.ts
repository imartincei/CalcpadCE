/**
 * UI translations for the shared CalcpadCE frontend.
 *
 * Keys are the English source strings, so a missing translation falls back to the
 * English text that used to be hard-coded in the component. Chinese wording follows
 * `Calcpad.Core/Messages.zh.resx` and the former WPF `AppMessages.zh.resx`.
 */
import { createI18n, type I18n } from 'vue-i18n'
import en from './messages/en'
import zh from './messages/zh'

export { menuLabelsFor } from './menu'

export type CalcpadLocale = 'en' | 'zh'
export type LocaleSetting = 'auto' | CalcpadLocale

const STORAGE_KEY = 'calcpad.locale'

export const LOCALE_OPTIONS: { value: LocaleSetting; label: string }[] = [
    { value: 'auto', label: 'Auto' },
    { value: 'en', label: 'English' },
    { value: 'zh', label: '中文' },
]

export function resolveLocale(setting: LocaleSetting): CalcpadLocale {
    if (setting !== 'auto') return setting
    const tag = typeof navigator !== 'undefined' ? navigator.language : 'en'
    return tag.toLowerCase().startsWith('zh') ? 'zh' : 'en'
}

export function readLocaleSetting(): LocaleSetting {
    try {
        const stored = localStorage.getItem(STORAGE_KEY)
        if (stored === 'en' || stored === 'zh' || stored === 'auto') return stored
    } catch {
        // Storage can be unavailable in sandboxed hosts; fall back to auto.
    }
    return 'auto'
}

export function writeLocaleSetting(setting: LocaleSetting): void {
    try {
        localStorage.setItem(STORAGE_KEY, setting)
    } catch {
        // Persisting is best-effort.
    }
}

export function createCalcpadI18n(setting: LocaleSetting = readLocaleSetting()): I18n {
    const locale = resolveLocale(setting)
    const i18n = createI18n({
        legacy: false,
        locale,
        fallbackLocale: 'en',
        messages: { en, zh },
        missingWarn: false,
        fallbackWarn: false,
    })
    if (typeof document !== 'undefined') {
        document.documentElement.lang = locale
    }
    return i18n as unknown as I18n
}

export function applyLocale(i18n: I18n, setting: LocaleSetting): void {
    const locale = resolveLocale(setting)
    const composer = i18n.global as unknown as { locale: { value: string } }
    composer.locale.value = locale
    writeLocaleSetting(setting)
    if (typeof document !== 'undefined') {
        document.documentElement.lang = locale
    }
}
