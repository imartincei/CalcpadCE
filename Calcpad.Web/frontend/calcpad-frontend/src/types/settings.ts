import defaultSettingsJson from '../defaults/settings.default.json' with { type: 'json' };
import { specForKey, validateCatalogValue } from './catalog';
import type { MetadataSettingKey, SettingOption } from './catalog';

export interface CalcpadSettings {
    math: {
        decimals: number;
        degrees: number;
        isComplex: boolean;
        substitute: boolean;
        formatEquations: boolean;
        zeroSmallMatrixElements: boolean;
        showHiddenOutput: boolean;
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

const COLOR_SCALE_OPTIONS: SettingOption[] = [
    { value: 'None', label: 'None' },
    { value: 'Gray', label: 'Grayscale' },
    { value: 'Rainbow', label: 'Rainbow' },
    { value: 'Terrain', label: 'Terrain' },
    { value: 'VioletToYellow', label: 'Violet → Yellow' },
    { value: 'GreenToYellow', label: 'Green → Yellow' },
    { value: 'Blues', label: 'Blues' },
    { value: 'BlueToYellow', label: 'Blue → Yellow' },
    { value: 'BlueToRed', label: 'Blue → Red' },
    { value: 'PurpleToYellow', label: 'Purple → Yellow' },
];

/**
 * Which renders are allowed to run `#write`/`#append`. A host setting, not a `#settings` key:
 * the backend takes a plain `write` boolean per request and this decides what to send.
 *
 * "Report" is the report layout wherever it is rendered — the preview pane's Report view as
 * much as a PDF, Word or HTML report export. Both are the same render, so both write.
 *
 * The input form never writes, in any mode: it is for filling in, and the report shown beside
 * it is the render that produces output. Unwrapped never writes either — it is a code listing,
 * so the document is not calculated for it.
 */
export type WriteMode = 'previewAndReport' | 'reportOnly' | 'manual';

export const WRITE_MODE_OPTIONS: { value: WriteMode; label: string; detail: string }[] = [
    {
        value: 'previewAndReport',
        label: 'Preview and Report',
        detail: 'The Preview and Report views both write, so the files are rewritten as you type. The input form does not.',
    },
    {
        value: 'reportOnly',
        label: 'Report Only',
        detail: 'Only a report render writes: the preview pane switched to Report, and a report export. The Preview view leaves the files alone, so typing does not rewrite them.',
    },
    {
        value: 'manual',
        label: 'Manual',
        detail: 'Nothing writes until you press Write to Disk.',
    },
];

export function coerceWriteMode(raw: string | undefined | null): WriteMode {
    return WRITE_MODE_OPTIONS.some(o => o.value === raw) ? raw as WriteMode : 'reportOnly';
}

/**
 * Whether a render may run `#write`/`#append`. The two flags are the API's own: `forPrint` is
 * the report layout, which the preview pane's Report view and a report export share, and
 * `enableUi` is the input form. They never both hold — the server clears `enableUi` for a
 * report — so the three cases below are exhaustive.
 */
export function writesAllowed(mode: WriteMode, forPrint: boolean, enableUi = false): boolean {
    if (mode === 'manual') return false;
    // A report is a report wherever it renders, form open or not.
    if (forPrint) return true;
    // The form is for filling in. Producing output is what its report half is for.
    if (enableUi) return false;
    return mode === 'previewAndReport';
}

/**
 * Recognized keys for the `settings` overrides object (the `#settings` directive).
 * Types and ranges mirror `Calcpad.Core`'s `SettingsDto` so the panel rejects the
 * same values the engine would reject. Keep in sync with `Settings.cs`.
 */
export const METADATA_SETTINGS_KEYS: MetadataSettingKey[] = [
    { key: 'decimals', label: 'Decimals', detail: 'Decimal places in output (0 to 15)', type: 'number', def: 2, min: 0, max: 15 },
    {
        key: 'degrees', label: 'Angle units', detail: 'Angle unit: 0=radians, 1=degrees, 2=gradians', type: 'enum', def: 0,
        options: [{ value: '0', label: 'Radians' }, { value: '1', label: 'Degrees' }, { value: '2', label: 'Gradians' }],
    },
    { key: 'complex', label: 'Complex numbers', detail: 'Enable complex number mode', type: 'boolean', def: false },
    { key: 'substitute', label: 'Substitute variables', detail: 'Substitute variable values into expressions', type: 'boolean', def: true },
    { key: 'formatEquations', label: 'Format equations', detail: 'Professional (checked) renders equations in stacked math form; Inline (unchecked) renders them on a single line.', type: 'boolean', def: true },
    { key: 'zeroSmallMatrixElements', label: 'Zero small matrix elements', detail: 'Display very small matrix/vector values as 0 instead of using scientific notation.', type: 'boolean', def: true },
    { key: 'showHiddenOutput', label: 'Show hidden output', detail: 'Ignore #hide so suppressed content is rendered anyway. For debugging.', type: 'boolean', def: false },
    { key: 'maxOutputCount', label: 'Max output count', detail: 'Maximum number of rows/columns shown for large matrices and vectors (5–100).', type: 'number', def: 20, min: 5, max: 100 },
    { key: 'units', label: 'Default length unit', detail: 'Default length unit used for %u placeholders in input forms.', type: 'string', def: 'm' },
    { key: 'isUs', label: 'Non-metric units', detail: 'Selects US or UK definitions for bare unit names that differ between the two systems (gal, ton, cwt, pt, qt, bbl, tonf, therm, etc.).', type: 'boolean', def: true },
    { key: 'vectorGraphics', label: 'Vector graphics', detail: 'Renders plots as SVG (scalable, sharp at any zoom) instead of raster PNG images.', type: 'boolean', def: false },
    { key: 'colorScale', label: 'Plot color scale', detail: 'Plot color scale', type: 'enum', def: 'Rainbow', options: COLOR_SCALE_OPTIONS },
    { key: 'smoothScale', label: 'Smooth color scale', detail: 'Smooth color scale transitions', type: 'boolean', def: false },
    { key: 'shadows', label: 'Plot shadows', detail: 'Enable 3-D plot shadows', type: 'boolean', def: true },
    { key: 'adaptivePlot', label: 'Adaptive plotting', detail: 'Concentrates sample points where the curve bends sharply instead of spacing them evenly. Produces smoother plots of curved functions at a lower point count; disable for a fixed dense sampling.', type: 'boolean', def: true },
    { key: 'plotWidth', label: 'Plot width', detail: 'Width of the plot area in pixels (at least 1). Can be overridden per-document with a PlotWidth = … line.', type: 'number', def: 500, min: 1 },
    { key: 'plotHeight', label: 'Plot height', detail: 'Height of the plot area in pixels (at least 1). Can be overridden per-document with a PlotHeight = … line.', type: 'number', def: 300, min: 1 },
    { key: 'plotStep', label: 'Plot mesh step', detail: 'Mesh size for map (surface) plotting in pixels; 0 lets Calcpad choose automatically. Can be overridden per-document with a PlotStep = … line.', type: 'number', def: 0, min: 0 },
    { key: 'precision', label: 'Numerical precision', detail: 'Relative precision for numerical methods such as integration and root finding (1e-15 to 1e-2). Can be overridden per-document with a Precision = … line.', type: 'number', def: 1e-14, min: 1e-15, max: 1e-2 },
    { key: 'tol', label: 'Solver tolerance', detail: 'Target tolerance for the iterative PCG solver and eigensolver (1e-15 to 1e-2). Can be overridden per-document with a Tol = … line.', type: 'number', def: 1e-6, min: 1e-15, max: 1e-2 },
];

/** Looks up the definition for a `#settings` key. */
export function settingSpec(key: string): MetadataSettingKey | undefined {
    return specForKey(METADATA_SETTINGS_KEYS, key);
}

/**
 * Returns an error message when `value` is not valid for `key`, else `null`.
 * Mirrors the type/range checks in `Calcpad.Core`'s `SettingsDto.Validate`.
 */
export function validateSettingValue(key: string, value: string | number | boolean): string | null {
    return validateCatalogValue(METADATA_SETTINGS_KEYS, key, value);
}

/**
 * Dot-path in CalcpadSettings for each METADATA_SETTINGS_KEYS entry that has
 * one. Shared with CalcpadSettingsTab.vue so the mapping exists exactly once.
 */
export const SETTINGS_PATH: Partial<Record<string, string>> = {
    decimals: 'math.decimals', degrees: 'math.degrees', complex: 'math.isComplex',
    substitute: 'math.substitute', formatEquations: 'math.formatEquations',
    zeroSmallMatrixElements: 'math.zeroSmallMatrixElements', showHiddenOutput: 'math.showHiddenOutput',
    maxOutputCount: 'math.maxOutputCount', precision: 'math.precision', tol: 'math.tol',
    units: 'units', isUs: 'isUs', vectorGraphics: 'plot.vectorGraphics', colorScale: 'plot.colorScale',
    smoothScale: 'plot.smoothScale', shadows: 'plot.shadows', adaptivePlot: 'plot.isAdaptive',
    plotWidth: 'plot.width', plotHeight: 'plot.height', plotStep: 'plot.step',
};

// The handful of CalcpadSettings fields Core doesn't expose as a #settings key
// (no SettingKey entry), so METADATA_SETTINGS_KEYS has nothing to give them.
const UNCATALOGED_CORE_DEFAULTS = {
    math: { formatString: '' },
    plot: { imagePath: '', imageUri: '', screenScaleFactor: 2, lightDirection: 'NorthWest' },
    server: { url: '', mode: 'auto' as const },
};

function setAtPath(obj: Record<string, any>, path: string, value: unknown): void {
    const parts = path.split('.');
    let cur = obj;
    for (let i = 0; i < parts.length - 1; i++) cur = cur[parts[i]];
    cur[parts[parts.length - 1]] = value;
}

function buildDefaultSettings(): CalcpadSettings {
    const out = structuredClone(UNCATALOGED_CORE_DEFAULTS) as CalcpadSettings;
    for (const [key, path] of Object.entries(SETTINGS_PATH)) {
        const spec = settingSpec(key);
        if (spec) setAtPath(out, path!, spec.def);
    }
    return out;
}

/**
 * core's defaults, generated from METADATA_SETTINGS_KEYS wherever a path
 * exists. Consumed only by generate-settings-defaults.mjs — getDefaultSettings()
 * below keeps reading settings.default.json at runtime, unchanged.
 */
export const DEFAULT_CALCPAD_SETTINGS: CalcpadSettings = buildDefaultSettings();

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

/** `core` in settings.default.json is regenerated on build from DEFAULT_CALCPAD_SETTINGS (see generate-settings-defaults.mjs) — a hand-edit there is a no-op after the next build. */
export function getDefaultSettings(): CalcpadSettings {
    return structuredClone(defaultSettingsJson.core) as CalcpadSettings;
}

/**
 * Default extras keyed as they're stored internally (strings for scalars,
 * JSON-stringified object for `pdfSettings`). `pdfSettings` is regenerated on
 * build from DEFAULT_PDF_SETTINGS (see generate-settings-defaults.mjs).
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
