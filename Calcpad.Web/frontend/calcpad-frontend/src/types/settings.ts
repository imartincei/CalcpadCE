import defaultSettingsJson from '../defaults/settings.default.json';

export interface CalcpadSettings {
    math: {
        decimals: number;
        degrees: number;
        isComplex: boolean;
        substitute: boolean;
        formatEquations: boolean;
        zeroSmallMatrixElements: boolean;
        maxOutputCount: number;
        formatString: string;
        precision: number;
        tol: number;
    };
    plot: {
        isAdaptive: boolean;
        screenScaleFactor: number;
        /** @deprecated WPF-only, to be removed later. */
        imagePath: string;
        imageUri: string;
        vectorGraphics: boolean;
        colorScale: string;
        smoothScale: boolean;
        shadows: boolean;
        lightDirection: string;
        width: number;
        height: number;
        step: number;
    };
    server: {
        url: string;
        mode: 'auto' | 'local' | 'remote';
    };
    units: string;
    isUs: boolean;
}

/**
 * Extras dict — flat key/value store for settings that don't fit the core
 * CalcpadSettings shape (preview theme, comment format, prettify options,
 * pdfSettings, etc.). Values are strings; `pdfSettings` is a JSON-stringified
 * object for backward compat with existing storage layers.
 */
export type CalcpadExtras = Record<string, string>;

/**
 * Full on-disk / exported settings blob. `core` maps to CalcpadSettings;
 * `extras` holds typed values (booleans as booleans, numbers as numbers,
 * pdfSettings as a nested object) so the file is human-editable.
 */
export interface CalcpadSettingsBlob {
    core: CalcpadSettings;
    extras: Record<string, unknown>;
}

export function getDefaultSettings(): CalcpadSettings {
    return structuredClone(defaultSettingsJson.core) as CalcpadSettings;
}

/**
 * Default extras keyed as they're stored internally (strings for scalars,
 * JSON-stringified object for `pdfSettings`).
 */
export function getDefaultExtras(): CalcpadExtras {
    return typedExtrasToInternal(defaultSettingsJson.extras);
}

/** Full default blob as it would appear on disk / when exported. */
export function getDefaultSettingsBlob(): CalcpadSettingsBlob {
    return structuredClone(defaultSettingsJson) as CalcpadSettingsBlob;
}

/**
 * Convert the on-disk / imported blob to the internal (settings, extras)
 * pair. Missing keys fall back to defaults so newly added default keys are
 * picked up automatically on old settings files.
 */
export function deserializeSettingsBlob(
    blob: unknown
): { settings: CalcpadSettings; extras: CalcpadExtras } {
    const defaults = getDefaultSettingsBlob();
    const b = (blob ?? {}) as Partial<CalcpadSettingsBlob>;
    const core = deepMerge(defaults.core, b.core ?? {}) as CalcpadSettings;
    const rawExtras = { ...defaults.extras, ...(b.extras ?? {}) };
    return { settings: core, extras: typedExtrasToInternal(rawExtras) };
}

/**
 * Convert the internal (settings, extras) pair back to a typed blob suitable
 * for JSON serialization. Uses the default extras as a schema to decide
 * which keys are booleans / numbers / objects.
 */
export function serializeSettingsBlob(
    settings: CalcpadSettings,
    extras: CalcpadExtras
): CalcpadSettingsBlob {
    const typedExtras: Record<string, unknown> = {};
    const schema = defaultSettingsJson.extras as Record<string, unknown>;
    for (const key of Object.keys(schema)) {
        const stored = extras[key];
        const defaultValue = schema[key];
        typedExtras[key] = stored === undefined
            ? defaultValue
            : coerceToDefaultType(stored, defaultValue);
    }
    return { core: structuredClone(settings), extras: typedExtras };
}

function typedExtrasToInternal(typed: Record<string, unknown>): CalcpadExtras {
    const out: CalcpadExtras = {};
    for (const [key, value] of Object.entries(typed)) {
        if (value === null || value === undefined) out[key] = '';
        else if (typeof value === 'object') out[key] = JSON.stringify(value);
        else out[key] = String(value);
    }
    return out;
}

function coerceToDefaultType(stored: string, defaultValue: unknown): unknown {
    if (typeof defaultValue === 'boolean') return stored === 'true';
    if (typeof defaultValue === 'number') {
        const n = Number(stored);
        return Number.isFinite(n) ? n : defaultValue;
    }
    if (typeof defaultValue === 'object' && defaultValue !== null) {
        try { return JSON.parse(stored); } catch { return defaultValue; }
    }
    return stored;
}

function deepMerge<T>(base: T, override: Partial<T>): T {
    if (base === null || typeof base !== 'object' || Array.isArray(base)) {
        return (override ?? base) as T;
    }
    const out: any = Array.isArray(base) ? [...(base as any)] : { ...base };
    for (const key of Object.keys(override ?? {})) {
        const b = (base as any)[key];
        const o = (override as any)[key];
        if (b && typeof b === 'object' && !Array.isArray(b) && o && typeof o === 'object' && !Array.isArray(o)) {
            out[key] = deepMerge(b, o);
        } else if (o !== undefined) {
            out[key] = o;
        }
    }
    return out as T;
}

const COLOR_SCALE_MAP: Record<string, number> = {
    'Rainbow': 0,
    'Grayscale': 1,
    'Hot': 2,
    'Cool': 3,
    'Jet': 4,
    'Parula': 5
};

const LIGHT_DIRECTION_MAP: Record<string, number> = {
    'NorthWest': 0,
    'North': 1,
    'NorthEast': 2,
    'West': 3,
    'East': 4,
    'SouthWest': 5,
    'South': 6,
    'SouthEast': 7
};

export function colorScaleToEnum(colorScale: string): number {
    return COLOR_SCALE_MAP[colorScale] ?? 0;
}

export function lightDirectionToEnum(direction: string): number {
    return LIGHT_DIRECTION_MAP[direction] ?? 0;
}

// ---- Extras runtime accessors ----
// Extras are stored as `Record<string, string>` at runtime; these helpers do
// the type coercion callers need on read. Kept as free functions so both the
// VS Code settings manager and the Tauri bridge can share them.

export function getExtraString(
    extras: CalcpadExtras,
    key: string,
    defaultValue: string = '',
): string {
    const v = extras[key];
    return v === undefined || v === '' ? defaultValue : v;
}

export function getExtraBool(
    extras: CalcpadExtras,
    key: string,
    defaultValue: boolean,
): boolean {
    const v = extras[key];
    if (v === undefined) return defaultValue;
    if (v === 'true') return true;
    if (v === 'false') return false;
    return defaultValue;
}

export function getExtraNumber(
    extras: CalcpadExtras,
    key: string,
    defaultValue: number,
): number {
    const v = extras[key];
    if (v === undefined || v === '') return defaultValue;
    const n = Number(v);
    return Number.isFinite(n) ? n : defaultValue;
}

export function getExtraObject<T>(
    extras: CalcpadExtras,
    key: string,
    defaultValue: T,
): T {
    const v = extras[key];
    if (!v) return defaultValue;
    try { return JSON.parse(v) as T; } catch { return defaultValue; }
}

export function buildApiSettings(settings: CalcpadSettings): unknown {
    return {
        math: { ...settings.math },
        plot: {
            ...settings.plot,
            colorScale: colorScaleToEnum(settings.plot.colorScale),
            lightDirection: lightDirectionToEnum(settings.plot.lightDirection)
        },
        units: settings.units,
        isUs: settings.isUs
    };
}
