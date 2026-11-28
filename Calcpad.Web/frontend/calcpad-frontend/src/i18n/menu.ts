/**
 * Native (Tauri) menu labels. The translations live in the shared catalogs;
 * this module lists the keys the Rust menu uses and filters untranslated ones.
 */
export const MENU_KEYS = [
    'No Recent Files',
    'Clear Recent',
    'Open Recent',
    'New Tab',
    'New Window',
    'Open...',
    'Save',
    'Save As...',
    'Close Tab',
    'Show Server Log',
    'Stop Server',
    'Restart Server',
    'Quit',
] as const

export function menuLabels(t: (key: string) => string): Record<string, string> {
    const labels: Record<string, string> = {}
    for (const key of MENU_KEYS) {
        const translated = t(key)
        if (translated !== key) labels[key] = translated
    }
    return labels
}
