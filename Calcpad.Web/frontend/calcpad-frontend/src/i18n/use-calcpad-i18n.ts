import { useI18n } from 'vue-i18n'

/** vue-i18n's useI18n with `tt` kept as an alias for `t`. */
export function useCalcpadI18n() {
    const i18n = useI18n()
    return { ...i18n, tt: i18n.t }
}
