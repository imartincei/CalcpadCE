// A non-English locale only full-ICU builds support.
const ICU_PROBE_LOCALE = 'pt-BR'

let uiLocale: string | undefined
let detectedCulture: string | undefined

export function currentCulture(): string {
    let intl = 'en-US'
    try {
        intl = Intl.DateTimeFormat().resolvedOptions().locale
        const fullIcu = Intl.DateTimeFormat.supportedLocalesOf([ICU_PROBE_LOCALE]).length > 0
        // Full ICU resolves non-English locales; en-US stays open to an explicit
        // LC_ALL/LANG override. Small-ICU reports en-US for every locale.
        if (fullIcu && intl && intl !== 'en-US') return intl
    } catch {
        // Fall through to the environment.
    }
    const env = process.env.LC_ALL || process.env.LC_MESSAGES || process.env.LANG
    if (env) {
        const tag = env.split('.')[0].replace('_', '-')
        if (tag && !/^(C|POSIX)$/i.test(tag)) return tag
    }
    return intl
}

export function setUiLocale(locale: string | undefined): void {
    uiLocale = locale?.trim() || undefined
}

/** The webview's resolved locale, with the OS culture as the pre-webview fallback. */
export function getUiLocale(): string {
    if (!detectedCulture) detectedCulture = currentCulture()
    return uiLocale || detectedCulture
}
