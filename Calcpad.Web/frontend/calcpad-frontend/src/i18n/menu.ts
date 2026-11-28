import en from './messages/en'

/**
 * Native (Tauri) menu labels. The translations live in the shared catalogs;
 * this module lists the IDs the Rust menu uses and filters untranslated ones.
 */
export const MENU_KEYS = [
    'menu.noRecentFiles',
    'menu.clearRecent',
    'menu.openRecent',
    'menu.newTab',
    'menu.newWindow',
    'common.openEllipsis',
    'common.save',
    'common.saveAsEllipsis',
    'menu.closeTab',
    'menu.showServerLog',
    'menu.stopServer',
    'menu.restartServer',
    'menu.quit',
] as const

export function menuLabels(t: (key: string) => string): Record<string, string> {
    const labels: Record<string, string> = {}
    for (const id of MENU_KEYS) {
        const english = en[id] ?? id
        const translated = t(id)
        if (translated !== english) labels[english] = translated
    }
    return labels
}
