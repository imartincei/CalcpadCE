/**
 * A selectable value for an `enum` setting: `value` is written verbatim into the
 * `#settings` JSON (must match what Calcpad.Core parses), `label` is the friendly
 * text shown in the dropdown.
 */
export interface SettingOption {
    value: string;
    label: string;
}

/** A catalog entry generic enough to cover both `MetadataSettingKey` and `UiPropertyKey`. */
interface CatalogKeySpec {
    key: string;
    label: string;
    type: string;
    options?: SettingOption[];
    min?: number;
    max?: number;
    pattern?: RegExp;
    patternHint?: string;
}

export interface MetadataSettingKey {
    key: string;
    /** Friendly name shown in the key dropdown; `key` is what's written to JSON. */
    label: string;
    detail: string;
    type: 'number' | 'boolean' | 'string' | 'enum';
    options?: SettingOption[];
    def: string | number | boolean;
    /** Inclusive lower bound for `number` values. Mirrors Calcpad.Core's SettingsDto. */
    min?: number;
    /** Inclusive upper bound for `number` values; omit for open-ended. */
    max?: number;
    /** Shape a `string` value must match, e.g. a CSS length. */
    pattern?: RegExp;
    /** Completes "must be ...", e.g. "a CSS length such as 2cm, 0.5in, or 12mm". */
    patternHint?: string;
}

/** Looks up a key's spec in an arbitrary catalog (`METADATA_SETTINGS_KEYS`, `UI_PROPERTY_KEYS`, ...). */
export function specForKey<T extends CatalogKeySpec>(catalog: T[], key: string): T | undefined {
    return catalog.find(s => s.key === key);
}

function rangeMessage(spec: CatalogKeySpec): string {
    if (spec.min !== undefined && spec.max !== undefined) return `${spec.label} must be between ${spec.min} and ${spec.max}`;
    if (spec.min !== undefined) return `${spec.label} must be at least ${spec.min}`;
    if (spec.max !== undefined) return `${spec.label} must be at most ${spec.max}`;
    return '';
}

/**
 * Returns an error message when `value` is not valid for `key` in {@link catalog},
 * else `null`. Mirrors the type/range checks in `Calcpad.Core`'s `SettingsDto.Validate`
 * for `METADATA_SETTINGS_KEYS`; reused as-is for other catalogs (e.g. `UI_PROPERTY_KEYS`)
 * since the enum/number checks are catalog-agnostic.
 */
export function validateCatalogValue<T extends CatalogKeySpec>(
    catalog: T[],
    key: string,
    value: string | number | boolean,
): string | null {
    const spec = specForKey(catalog, key);
    if (!spec) return null;
    if (spec.type === 'enum')
        return spec.options?.some(o => o.value === String(value))
            ? null
            : `${spec.label} must be one of: ${spec.options?.map(o => o.label).join(', ')}`;
    if (spec.type === 'number') {
        const n = Number(value);
        if (String(value).trim() === '' || !Number.isFinite(n)) return `${spec.label} must be a number`;
        if (spec.min !== undefined && n < spec.min || spec.max !== undefined && n > spec.max) return rangeMessage(spec);
    }
    if (spec.pattern && !spec.pattern.test(String(value)))
        return `${spec.label} must be ${spec.patternHint}`;
    return null;
}
