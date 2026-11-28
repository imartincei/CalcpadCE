/**
 * UI translations for the shared CalcpadCE frontend.
 *
 * Keys are the English source strings, so a missing translation falls back to the
 * English text. "auto" follows the current culture, then the host language; a
 * manual choice overrides both.
 */
import { createI18n, type I18n } from 'vue-i18n'
import cs from './messages/cs'
import de from './messages/de'
import en from './messages/en'
import es from './messages/es'
import fr from './messages/fr'
import it from './messages/it'
import ja from './messages/ja'
import ko from './messages/ko'
import pl from './messages/pl'
import ptBR from './messages/pt-BR'
import ru from './messages/ru'
import tr from './messages/tr'
import zh from './messages/zh'
import zhHant from './messages/zh-Hant'
import { resolveMessageKey } from './keys'
import { LOCALES, LOCALE_LABELS, SUPPORTED_LOCALES, type CalcpadLocale } from './locales.generated'

export { menuLabels, MENU_KEYS } from './menu'
export { LOCALE_LABELS, SUPPORTED_LOCALES }
export { formatMessage } from './format'
export { resolveMessageKey }
export { useCalcpadI18n as useI18n } from './use-calcpad-i18n'
export type { CalcpadLocale }

export type LocaleSetting = 'auto' | CalcpadLocale

export const LOCALE_STORAGE_KEY = 'calcpad.locale'
/** Fired after a locale change so hosts can rebuild the editor. */
export const LOCALE_CHANGED_EVENT = 'calcpad:locale-changed'

export const LOCALE_OPTIONS: { value: LocaleSetting; label: string; translatable: boolean }[] = [
    { value: 'auto', label: 'Auto', translatable: true },
    ...SUPPORTED_LOCALES.map(value => ({ value, label: LOCALE_LABELS[value], translatable: false })),
]

let hostLocale: string | undefined
let currentCulture: string | undefined
let activeI18n: I18n | null = null
let activeLocale: CalcpadLocale = 'en'

export function setHostLocale(locale: string | undefined): void {
    hostLocale = locale
}

export function setCurrentCulture(locale: string | undefined): void {
    currentCulture = locale?.trim() || undefined
}

export function matchLocale(tag: string): CalcpadLocale | null {
    const normalized = tag.replace('_', '-').toLowerCase()
    for (const locale of LOCALES) {
        if (locale.tag.toLowerCase() === normalized) return locale.tag
        if (locale.aliases.some(alias => alias.toLowerCase() === normalized)) return locale.tag
    }
    const language = normalized.split('-')[0]
    if (language === 'zh') {
        // Unlisted Chinese variants default to Simplified; Traditional regions do not.
        const traditional = /(?:^|-)hant(?:-|$)/.test(normalized)
            || /(?:^|-)(?:tw|hk|mo)(?:-|$)/.test(normalized)
        return traditional ? 'zh-Hant' : 'zh'
    }
    if (language === 'pt') {
        // Bare "pt" is ambiguous (Portugal vs Brazil); require an explicit pt-BR tag.
        return normalized === 'pt-br' || normalized.startsWith('pt-br-') ? 'pt-BR' : null
    }
    const fallback = LOCALES.find(locale => locale.languageFallback && locale.tag.toLowerCase() === language)
    return fallback?.tag ?? null
}

function browserLocaleTags(): string[] {
    if (typeof navigator === 'undefined') return []
    const tags = [...(navigator.languages ?? [])]
    if (navigator.language && !tags.includes(navigator.language)) tags.push(navigator.language)
    return tags
}

export function resolveLocale(setting: LocaleSetting, fallbackTag?: string): CalcpadLocale {
    if (setting !== 'auto') return setting
    let tags: string[]
    if (fallbackTag) tags = [fallbackTag]
    else if (currentCulture) tags = [currentCulture]
    else if (hostLocale) tags = [hostLocale]
    else tags = browserLocaleTags()
    for (const tag of tags) {
        const locale = matchLocale(tag)
        if (locale) return locale
    }
    return 'en'
}

export function readLocaleSetting(): LocaleSetting {
    try {
        const stored = localStorage.getItem(LOCALE_STORAGE_KEY)
        if (stored === 'auto' || (SUPPORTED_LOCALES as readonly string[]).includes(stored ?? ''))
            return stored as LocaleSetting
    } catch {
        // Storage can be unavailable in sandboxed hosts; fall back to auto.
    }
    return 'auto'
}

export function writeLocaleSetting(setting: LocaleSetting): void {
    try {
        localStorage.setItem(LOCALE_STORAGE_KEY, setting)
    } catch {
        // Persisting is best-effort.
    }
}

export function getActiveLocale(): CalcpadLocale {
    return activeLocale
}

/** Translate a key with the active catalog; missing keys return the key. */
export function translate(key: string, named?: Record<string, unknown>): string {
    if (!activeI18n) return key
    const composer = activeI18n.global as unknown as {
        t: (key: string, named?: Record<string, unknown>) => string
    }
    return composer.t(resolveMessageKey(key), named)
}

function notifyLocaleChanged(setting: LocaleSetting, locale: CalcpadLocale): void {
    if (typeof window === 'undefined') return
    window.dispatchEvent(new CustomEvent(LOCALE_CHANGED_EVENT, { detail: { setting, locale } }))
}

function applyResolvedLocale(i18n: I18n, setting: LocaleSetting): CalcpadLocale {
    const locale = resolveLocale(setting)
    activeLocale = locale
    activeI18n = i18n
    const composer = i18n.global as unknown as { locale: { value: string } }
    composer.locale.value = locale
    writeLocaleSetting(setting)
    if (typeof document !== 'undefined') document.documentElement.lang = locale
    notifyLocaleChanged(setting, locale)
    return locale
}

export function createCalcpadI18n(setting: LocaleSetting = readLocaleSetting()): I18n {
    const locale = resolveLocale(setting)
    const i18n = createI18n({
        legacy: false,
        locale,
        fallbackLocale: 'en',
        messages: {
            en,
            cs,
            de,
            es,
            fr,
            it,
            ja,
            ko,
            pl,
            'pt-BR': ptBR,
            ru,
            tr,
            zh,
            'zh-Hant': zhHant,
        },
        missingWarn: false,
        fallbackFormat: true,
        fallbackWarn: false,
    }) as unknown as I18n
    activeI18n = i18n
    activeLocale = locale
    if (typeof document !== 'undefined') document.documentElement.lang = locale
    return i18n
}

export function applyLocale(i18n: I18n, setting: LocaleSetting): CalcpadLocale {
    return applyResolvedLocale(i18n, setting)
}

export function setLocale(setting: LocaleSetting): CalcpadLocale {
    if (!activeI18n) {
        const locale = resolveLocale(setting)
        activeLocale = locale
        writeLocaleSetting(setting)
        if (typeof document !== 'undefined') document.documentElement.lang = locale
        notifyLocaleChanged(setting, locale)
        return locale
    }
    return applyLocale(activeI18n, setting)
}
