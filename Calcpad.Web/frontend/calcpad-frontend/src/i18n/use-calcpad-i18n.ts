import { useI18n } from 'vue-i18n'
import { resolveMessageKey } from './keys'

/**
 * Same shape as vue-i18n's useI18n, but t() accepts legacy English keys while
 * the catalogs move to stable message IDs.
 */
export function useCalcpadI18n() {
    const i18n = useI18n()
    const t = ((key: string, ...args: unknown[]) =>
        (i18n.t as unknown as (key: string, ...args: unknown[]) => string)(
            resolveMessageKey(key),
            ...args,
        )) as typeof i18n.t
    return { ...i18n, t, tt: t }
}
