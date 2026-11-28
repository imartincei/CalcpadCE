import type { CalcpadSettings } from '../types/settings';
import { SETTINGS_PATH } from '../types/settings';
import type { PdfSettings } from '../types/pdf-settings';

/**
 * Setting ids are `<section>.<row>`, matching the Settings tab. A hotkey on one of these
 * toggles it; a hotkey on any other setting opens it in the Settings tab.
 */
type SettingToggle =
    | { kind: 'extra'; label: string; extra: string; message: string; defaultValue: boolean }
    | { kind: 'core'; label: string; key: string }
    | { kind: 'pdf'; label: string; key: 'showPageNumbers' | 'showDate' | 'showTitle' };

const extra = (label: string, extraKey: string, message: string, defaultValue: boolean): SettingToggle =>
    ({ kind: 'extra', label, extra: extraKey, message, defaultValue });

export const SETTING_TOGGLES: Record<string, SettingToggle> = {
    'math.returnAngleUnits': { kind: 'core', label: 'Return Angle Units', key: 'returnAngleUnits' },
    'math.complex': { kind: 'core', label: 'Complex Numbers', key: 'complex' },
    'math.substitute': { kind: 'core', label: 'Substitute Variables', key: 'substitute' },
    'math.formatEquations': { kind: 'core', label: 'Format Equations', key: 'formatEquations' },
    'math.zeroSmallMatrixElements': { kind: 'core', label: 'Zero Small Matrix Elements', key: 'zeroSmallMatrixElements' },
    'math.showHiddenOutput': { kind: 'core', label: 'Show Hidden Output', key: 'showHiddenOutput' },
    'math.calculatePost': { kind: 'core', label: 'service.calculateHashPostInInputForm', key: 'calculatePost' },
    'plot.adaptivePlot': { kind: 'core', label: 'Adaptive Plotting', key: 'adaptivePlot' },
    'plot.vectorGraphics': { kind: 'core', label: 'Vector Graphics', key: 'vectorGraphics' },
    'plot.smoothScale': { kind: 'core', label: 'Smooth Scale', key: 'smoothScale' },
    'plot.shadows': { kind: 'core', label: 'Shadows', key: 'shadows' },
    'units.isUs': { kind: 'core', label: 'US Customary Units', key: 'isUs' },
    'pdf.showPageNumbers': { kind: 'pdf', label: 'PDF Page Numbers', key: 'showPageNumbers' },
    'pdf.showDate': { kind: 'pdf', label: 'PDF Date', key: 'showDate' },
    'pdf.showTitle': { kind: 'pdf', label: 'PDF Title', key: 'showTitle' },
    'editor.quickTyping': extra('Quick Typing', 'quickTyping', 'updateQuickTyping', true),
    'editor.formattingHotkeys': extra('Formatting Hotkeys', 'formattingHotkeys', 'updateFormattingHotkeys', true),
    'editor.previewCursorSync': extra('settings.syncPreviewToCursorLine', 'previewCursorSync', 'updatePreviewCursorSync', false),
    'editor.autoRun': extra('service.autoRunPreview', 'autoRun', 'updateAutoRun', true),
    'editor.autoInputMode': extra('settings.openHashUiDocumentsInInputMode', 'autoInputMode', 'updateAutoInputMode', true),
    'editor.previewUiOverrides': extra('service.applyHashUiValuesInPreview', 'previewUiOverrides', 'updatePreviewUiOverrides', false),
    'editor.previewPdf': extra('settings.renderPreviewAsPdf', 'previewPdf', 'updatePreviewPdf', false),
    'editor.showFileMetadata': extra('settings.showFileMetadata', 'showFileMetadata', 'updateShowFileMetadata', false),
    'editor.showUiOverrideHints': extra('settings.showUiOverrideHints', 'uiOverrideHints', 'updateShowUiOverrideHints', false),
    'editor.autoSyncUiValues': extra('settings.autoSyncHashUiValues', 'autoSyncUiValues', 'updateAutoSyncUiValues', false),
};

export function isToggleSetting(id: string): boolean {
    return id in SETTING_TOGGLES;
}

export interface SettingToggleContext {
    getExtra(key: string): string | undefined;
    settings: CalcpadSettings;
    pdfSettings: PdfSettings;
}

export interface SettingToggleResult {
    /** The panel message that applies the new value, dispatched through the host's own handler. */
    message: Record<string, unknown>;
    label: string;
    enabled: boolean;
}

export function buildSettingToggle(id: string, ctx: SettingToggleContext): SettingToggleResult | null {
    const toggle = SETTING_TOGGLES[id];
    if (!toggle) return null;
    if (toggle.kind === 'extra') {
        const raw = ctx.getExtra(toggle.extra);
        const current = raw === 'true' ? true : raw === 'false' ? false : toggle.defaultValue;
        return { message: { type: toggle.message, enabled: !current }, label: toggle.label, enabled: !current };
    }
    if (toggle.kind === 'pdf') {
        const enabled = !ctx.pdfSettings[toggle.key];
        return {
            message: { type: 'updatePdfSettings', settings: { ...ctx.pdfSettings, [toggle.key]: enabled } },
            label: toggle.label,
            enabled,
        };
    }
    const settings = structuredClone(ctx.settings) as unknown as Record<string, Record<string, unknown>>;
    const [a, b] = SETTINGS_PATH[toggle.key]!.split('.');
    const holder = b ? settings[a] : (settings as unknown as Record<string, unknown>);
    const field = b ?? a;
    const enabled = !holder[field];
    holder[field] = enabled;
    return { message: { type: 'updateSettings', settings }, label: toggle.label, enabled };
}
