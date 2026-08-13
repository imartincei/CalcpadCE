import { BaseMessageBridge, type ExportRequest } from 'calcpad-frontend/services/message-bridge/base';
import { getDefaultSettings } from 'calcpad-frontend/types/settings';
import type { CalcpadSettings } from 'calcpad-frontend/types/settings';
import type { ExportVariant } from 'calcpad-frontend/types/api';
import { mimeFromExtension, bytesToBase64, pdfResponseError } from 'calcpad-frontend';
import { setAppTheme, coerceAppTheme } from '../editor/app-theme';
import { getActiveDocumentKey } from '../editor/bridge';

const SETTINGS_KEY = 'calcpad-settings';

function camelToKebab(s: string): string {
    return s.replace(/([A-Z])/g, '-$1').toLowerCase();
}

function extraKey(key: string): string {
    return 'calcpad-' + camelToKebab(key);
}

function triggerBlobDownload(blob: Blob, filename: string): void {
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = filename;
    a.click();
    URL.revokeObjectURL(url);
}

/** Pop a hidden `<input type="file">` to let the user choose an image; resolve with the File. */
function pickImageViaInput(): Promise<File | null> {
    return new Promise(resolve => {
        const input = document.createElement('input');
        input.type = 'file';
        input.accept = 'image/png,image/jpeg,image/gif,image/webp,image/svg+xml';
        input.style.display = 'none';
        let settled = false;
        const cleanup = () => { if (input.parentNode) input.parentNode.removeChild(input); };
        input.onchange = () => {
            settled = true;
            const file = input.files?.[0] ?? null;
            cleanup();
            resolve(file);
        };
        // If the user cancels, no event fires reliably across browsers; clean up on next tick
        // when focus returns to the window.
        const onFocus = () => {
            window.removeEventListener('focus', onFocus);
            setTimeout(() => {
                if (!settled) { cleanup(); resolve(null); }
            }, 200);
        };
        window.addEventListener('focus', onFocus);
        document.body.appendChild(input);
        input.click();
    });
}

/**
 * In-process message bridge for the web platform. Persists settings to
 * localStorage and uses browser blob-downloads for exported files.
 */
export class MessageBridge extends BaseMessageBridge {
    constructor(serverUrl: string) {
        super(serverUrl);
        this.settings = this.loadSettings();
    }

    getExtraSetting(key: string): string | undefined {
        return localStorage.getItem(extraKey(key)) ?? undefined;
    }

    setExtraSetting(key: string, value: string): void {
        localStorage.setItem(extraKey(key), value);
    }

    protected persistSettings(settings: CalcpadSettings): void {
        localStorage.setItem(SETTINGS_KEY, JSON.stringify(settings));
    }

    protected resetSettingsBackend(): void {
        this.settings = getDefaultSettings();
        localStorage.setItem(SETTINGS_KEY, JSON.stringify(this.settings));
    }

    protected coerceColorTheme(raw: string | undefined | null): string {
        return coerceAppTheme(raw);
    }

    protected applyColorTheme(theme: string): void {
        setAppTheme(coerceAppTheme(theme));
    }

    protected async pickImageSrc(): Promise<string | null> {
        const file = await pickImageViaInput();
        if (!file) return null;
        const data = new Uint8Array(await file.arrayBuffer());
        const mimeType = file.type || mimeFromExtension(file.name);
        return `data:${mimeType};base64,${bytesToBase64(data)}`;
    }

    protected async saveExportedFile(req: ExportRequest): Promise<string | null> {
        const part: BlobPart = req.data instanceof Uint8Array
            ? new Uint8Array(req.data)
            : req.data;
        triggerBlobDownload(new Blob([part], { type: req.mime }), req.defaultName);
        return null;
    }

    protected async buildFileContext(_content: string): Promise<{ sourceFilePath?: string }> {
        return {};
    }

    protected getVariablesOrigin(): string {
        return 'web-editor';
    }

    protected getActiveDefinitions() {
        return this.definitionsService.getCachedDefinitions(getActiveDocumentKey());
    }

    /**
     * Render, then hand the HTML to the server's PDF endpoint. `/convert` has no PDF output
     * of its own — it ignores `outputFormat` and always answers with HTML — so asking it for
     * one produced a `.pdf` full of markup.
     */
    protected async generatePdfBytes(
        content: string,
        apiSettings: unknown,
        sourceFilePath: string | undefined,
        variant: ExportVariant,
    ): Promise<ArrayBuffer | null> {
        const html = await this.renderForExport(content, apiSettings, sourceFilePath, variant);
        if (html == null) return null;

        const response = await fetch(`${this.apiClient.getBaseUrl()}/api/calcpad/pdf`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json', ...this.apiClient.authHeaders() },
            body: JSON.stringify({ html, options: this.getEffectivePdfOptions(content) }),
            signal: AbortSignal.timeout(60000),
        });
        if (!response.ok) throw await pdfResponseError(response);
        return await response.arrayBuffer();
    }

    private loadSettings(): CalcpadSettings {
        const stored = localStorage.getItem(SETTINGS_KEY);
        if (stored) {
            try { return JSON.parse(stored); } catch { /* fall through */ }
        }
        return getDefaultSettings();
    }
}
