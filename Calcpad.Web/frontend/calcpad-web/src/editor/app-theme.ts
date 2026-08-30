import { setCalcpadEditorTheme } from './setup';

/**
 * App theme labels shown in the Color Theme dropdown, where "System" follows the OS
 * `prefers-color-scheme`. These must match the entries the bridges send in `availableThemes`,
 * plus the static "System" option in CalcpadSettingsTab.vue.
 */
export const APP_THEME_LABELS = ['System', 'Dark', 'Light'] as const;
export type AppThemeLabel = typeof APP_THEME_LABELS[number];
export type ResolvedTheme = 'light' | 'dark';

const SYSTEM_QUERY = '(prefers-color-scheme: light)';

function resolve(label: AppThemeLabel): ResolvedTheme {
    if (label === 'System') {
        return window.matchMedia(SYSTEM_QUERY).matches ? 'light' : 'dark';
    }
    return label === 'Light' ? 'light' : 'dark';
}

function apply(resolved: ResolvedTheme): void {
    document.documentElement.dataset.theme = resolved;
    setCalcpadEditorTheme(resolved);
}

let mql: MediaQueryList | null = null;
let mqlListener: ((e: MediaQueryListEvent) => void) | null = null;
let current: AppThemeLabel = 'System';

/**
 * Apply the given color theme label, subscribing to OS color-scheme changes when called with
 * "System". Any subsequent call replaces the previous subscription.
 */
export function setAppTheme(label: AppThemeLabel): void {
    current = label;
    apply(resolve(label));

    if (mql && mqlListener) {
        mql.removeEventListener('change', mqlListener);
        mql = null;
        mqlListener = null;
    }
    if (label === 'System') {
        mql = window.matchMedia(SYSTEM_QUERY);
        mqlListener = () => apply(resolve(current));
        mql.addEventListener('change', mqlListener);
    }
}

/** Normalize an arbitrary string (from storage / IPC) into a valid label. */
export function coerceAppTheme(raw: string | undefined | null): AppThemeLabel {
    return raw === 'Dark' || raw === 'Light' ? raw : 'System';
}
