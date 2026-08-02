import { specForKey, validateCatalogValue } from './catalog';
import type { MetadataSettingKey, SettingOption } from './catalog';

export interface PdfSettings {
  format: string;
  orientation: string;
  marginTop: string;
  marginBottom: string;
  marginLeft: string;
  marginRight: string;
  documentTitle: string;
  showPageNumbers: boolean;
  showDate: boolean;
  dateTimeFormat: string;
}

const PAPER_FORMAT_OPTIONS: SettingOption[] = [
  { value: 'Letter', label: 'Letter (8.5 × 11 in)' },
  { value: 'Legal', label: 'Legal (8.5 × 14 in)' },
  { value: 'Tabloid', label: 'Tabloid (11 × 17 in)' },
  { value: 'Ledger', label: 'Ledger (17 × 11 in)' },
  { value: 'A0', label: 'A0' },
  { value: 'A1', label: 'A1' },
  { value: 'A2', label: 'A2' },
  { value: 'A3', label: 'A3' },
  { value: 'A4', label: 'A4' },
  { value: 'A5', label: 'A5' },
  { value: 'A6', label: 'A6' },
];

/**
 * A CSS length as the headless browser accepts it for a page margin: a number
 * with a unit, e.g. `2cm`, `0.5in`, `12mm`, `36pt`, `96px`. A bare number is
 * rejected because Puppeteer treats it as pixels, which is never what someone
 * setting a print margin means.
 */
const CSS_LENGTH = /^\d*\.?\d+(cm|mm|in|pt|pc|px)$/i;
const CSS_LENGTH_HINT = 'a length with a unit, e.g. 2cm, 0.5in, or 12mm';

/**
 * Recognized keys for the `pdf` object of a metadata comment — the PDF export
 * settings a document can pin for itself. Curated rather than exhaustive: only
 * options that demonstrably affect the output are offered. Keep in sync with
 * `Calcpad.Highlighter`'s `PdfSettingsDto` (which validates the same payload) and
 * with the backend's `PdfSettingsDto`/`PdfSettingsDefaults`.
 */
export const PDF_SETTING_KEYS: MetadataSettingKey[] = [
  { key: 'format', label: 'Paper size', detail: 'Page size', type: 'enum', def: 'Letter', options: PAPER_FORMAT_OPTIONS },
  {
    key: 'orientation', label: 'Orientation', detail: 'Page orientation', type: 'enum', def: 'portrait',
    options: [{ value: 'portrait', label: 'Portrait' }, { value: 'landscape', label: 'Landscape' }],
  },
  { key: 'marginTop', label: 'Top margin', detail: `Top page margin — ${CSS_LENGTH_HINT}`, type: 'string', def: '0.75in', pattern: CSS_LENGTH, patternHint: CSS_LENGTH_HINT },
  { key: 'marginBottom', label: 'Bottom margin', detail: `Bottom page margin — ${CSS_LENGTH_HINT}`, type: 'string', def: '0.75in', pattern: CSS_LENGTH, patternHint: CSS_LENGTH_HINT },
  { key: 'marginLeft', label: 'Left margin', detail: `Left page margin — ${CSS_LENGTH_HINT}`, type: 'string', def: '0.5in', pattern: CSS_LENGTH, patternHint: CSS_LENGTH_HINT },
  { key: 'marginRight', label: 'Right margin', detail: `Right page margin — ${CSS_LENGTH_HINT}`, type: 'string', def: '0.5in', pattern: CSS_LENGTH, patternHint: CSS_LENGTH_HINT },
  { key: 'showPageNumbers', label: 'Page numbers', detail: 'Show "Page n of m" in the footer', type: 'boolean', def: true },
  { key: 'showDate', label: 'Date', detail: 'Show the timestamp in the header', type: 'boolean', def: true },
  { key: 'documentTitle', label: 'Document title', detail: 'Header title (defaults to the file name)', type: 'string', def: '' },
  { key: 'dateTimeFormat', label: 'Timestamp format', detail: '.NET date/time format string, e.g. M/d/yyyy h:mm tt', type: 'string', def: 'M/d/yyyy h:mm tt' },
];

/** Looks up a `pdf` key's spec. */
export function pdfSpec(key: string): MetadataSettingKey | undefined {
  return specForKey(PDF_SETTING_KEYS, key);
}

/**
 * Returns an error message when `value` is not valid for a `pdf` key, else `null`.
 * Mirrors the checks in `Calcpad.Highlighter`'s `PdfSettingsDto.Validate`.
 */
export function validatePdfValue(key: string, value: string | number | boolean): string | null {
  return validateCatalogValue(PDF_SETTING_KEYS, key, value);
}

// Sourced from PDF_SETTING_KEYS so the 10 literals aren't hand-copied in both files.
export const DEFAULT_PDF_SETTINGS: Readonly<PdfSettings> = PDF_SETTING_KEYS.reduce(
  (defaults, spec) => ({ ...defaults, [spec.key]: spec.def }),
  {} as Record<string, unknown>,
) as Readonly<PdfSettings>;

/** Defaults filled in for whatever `stored` doesn't set. */
export function resolveStoredPdfSettings(stored: Partial<PdfSettings> | null | undefined): PdfSettings {
  return { ...DEFAULT_PDF_SETTINGS, ...stored };
}

/**
 * The PDF options an export should use: `stored` with `documentOverrides` layered over
 * it key by key. `fallbackTitle` — e.g. the file name — is used only when neither
 * source sets a non-empty `documentTitle`.
 */
export function resolveEffectivePdfSettings(
  stored: Partial<PdfSettings> | null | undefined,
  documentOverrides: Partial<PdfSettings> | undefined,
  fallbackTitle?: string,
): PdfSettings {
  const merged = { ...resolveStoredPdfSettings(stored), ...documentOverrides };
  if (fallbackTitle !== undefined)
    merged.documentTitle = documentOverrides?.documentTitle || stored?.documentTitle || fallbackTitle;
  return merged;
}
