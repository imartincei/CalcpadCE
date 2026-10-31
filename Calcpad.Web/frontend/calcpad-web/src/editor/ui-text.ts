/**
 * Picks between an English and a Chinese literal using the persisted UI locale.
 * Editor actions are registered before the Vue i18n instance exists, so they cannot
 * use `t()`; this mirrors the choice Monaco's built-in strings get at startup.
 */
export function uiText(en: string, zh: string): string {
    try {
        const stored = localStorage.getItem('calcpad.locale')
        if (stored === 'en') return en
        if (stored === 'zh') return zh
    } catch {
        // Storage unavailable; fall back to the browser language.
    }
    return typeof navigator !== 'undefined' && navigator.language.toLowerCase().startsWith('zh')
        ? zh
        : en
}
