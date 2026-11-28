import { specForKey, validateCatalogValue } from './catalog';
import type { MetadataSettingKey, SettingOption } from './catalog';

export interface PdfSettings {
  format: string;
  orientation: string;
  marginTop: string;
  marginBottom: string;
  marginLeft: string;
  marginRight: string;
  /** Header title, from the file info's title rather than a setting; see {@link resolveEffectivePdfSettings}. */
  documentTitle?: string;
  showPageNumbers: boolean;
  showDate: boolean;
  showTitle: boolean;
  dateTimeFormat: string;
  headerFooterLines: string;
  lineColor: string;
}

const PAPER_FORMAT_OPTIONS: SettingOption[] = [
  { value: 'Letter', label: 'catalog.letter8511In' },
  { value: 'Legal', label: 'catalog.legal8514In' },
  { value: 'Tabloid', label: 'catalog.tabloid1117In' },
  { value: 'Ledger', label: 'catalog.ledger1711In' },
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

const HEX_COLOR = /^#[0-9a-f]{6}$/i;

/**
 * Recognized keys for the `pdf` object of a metadata comment — the PDF export settings a
 * document can pin for itself, curated rather than exhaustive. Keep in sync with
 * `Calcpad.Highlighter`'s `PdfSettingsDto` and the backend's
 * `PdfSettingsDto`/`PdfSettingsDefaults`.
 */
export const PDF_SETTING_KEYS: MetadataSettingKey[] = [
  { key: 'format', label: 'catalog.paperSize', detail: 'catalog.pageSize', type: 'enum', def: 'Letter', options: PAPER_FORMAT_OPTIONS },
  {
    key: 'orientation', label: 'catalog.orientation', detail: 'catalog.pageOrientation', type: 'enum', def: 'portrait',
    options: [{ value: 'portrait', label: 'catalog.portrait' }, { value: 'landscape', label: 'catalog.landscape' }],
  },
  { key: 'marginTop', label: 'catalog.topMargin', detail: `Top page margin — ${CSS_LENGTH_HINT}`, type: 'string', def: '0.75in', pattern: CSS_LENGTH, patternHint: CSS_LENGTH_HINT },
  { key: 'marginBottom', label: 'catalog.bottomMargin', detail: `Bottom page margin — ${CSS_LENGTH_HINT}`, type: 'string', def: '0.75in', pattern: CSS_LENGTH, patternHint: CSS_LENGTH_HINT },
  { key: 'marginLeft', label: 'catalog.leftMargin', detail: `Left page margin — ${CSS_LENGTH_HINT}`, type: 'string', def: '0.5in', pattern: CSS_LENGTH, patternHint: CSS_LENGTH_HINT },
  { key: 'marginRight', label: 'catalog.rightMargin', detail: `Right page margin — ${CSS_LENGTH_HINT}`, type: 'string', def: '0.5in', pattern: CSS_LENGTH, patternHint: CSS_LENGTH_HINT },
  { key: 'showPageNumbers', label: 'catalog.pageNumbers', detail: 'catalog.showPageNOfMInTheFooter', type: 'boolean', def: true },
  { key: 'showDate', label: 'catalog.date', detail: 'catalog.showTheTimestampInTheHeader', type: 'boolean', def: true },
  { key: 'showTitle', label: 'common.title', detail: "Show the file's Info title (or the file name) in the header", type: 'boolean', def: true },
  { key: 'dateTimeFormat', label: 'pdfsettings.timestampFormat', detail: '.NET date/time format string, e.g. M/d/yyyy h:mm tt', type: 'string', def: 'M/d/yyyy h:mm tt' },
  {
    key: 'headerFooterLines', label: 'pdfsettings.showHeaderFooterLines', detail: 'pdfsettings.whichSeparatorLineSToDraw', type: 'enum', def: 'both',
    options: [
      { value: 'both', label: 'pdfsettings.both' }, { value: 'header', label: 'pdfsettings.headerOnly' },
      { value: 'footer', label: 'pdfsettings.footerOnly' }, { value: 'none', label: 'common.none' },
    ],
  },
  { key: 'lineColor', label: 'pdfsettings.headerFooterLineColor', detail: 'pdfsettings.headerFooterSeparatorLineColorAsAHex', type: 'string', def: '#b3b3b3', pattern: HEX_COLOR, patternHint: 'a hex color, e.g. #b3b3b3' },
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

// Sourced from PDF_SETTING_KEYS so the literals aren't hand-copied in both files.
export const DEFAULT_PDF_SETTINGS: Readonly<PdfSettings> = PDF_SETTING_KEYS.reduce(
  (defaults, spec) => ({ ...defaults, [spec.key]: spec.def }),
  {} as Record<string, unknown>,
) as Readonly<PdfSettings>;

/**
 * Defaults filled in for whatever `stored` doesn't set. A stored `documentTitle` is dropped:
 * the title is per file now (its file info), and one host-wide title never fit every file.
 */
export function resolveStoredPdfSettings(stored: Partial<PdfSettings> | null | undefined): PdfSettings {
  const { documentTitle: _hostWideTitle, ...rest } = stored ?? {};
  return { ...DEFAULT_PDF_SETTINGS, ...rest };
}

/**
 * The PDF options an export should use: `stored` with `documentOverrides` layered over
 * it key by key, titled `documentTitle` (see `pdfDocumentTitle`).
 */
export function resolveEffectivePdfSettings(
  stored: Partial<PdfSettings> | null | undefined,
  documentOverrides: Partial<PdfSettings> | undefined,
  documentTitle: string,
): PdfSettings {
  return { ...resolveStoredPdfSettings(stored), ...documentOverrides, documentTitle };
}
