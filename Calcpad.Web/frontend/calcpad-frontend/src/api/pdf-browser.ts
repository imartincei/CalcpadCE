/**
 * Client half of the PDF browser contract.
 *
 * The server no longer downloads a headless Chromium behind the user's back: when
 * it finds no Chromium-family browser it answers `/api/calcpad/pdf` with 503 and
 * `code: "BROWSER_NOT_FOUND"`. Hosts turn that into a prompt and, if the user
 * accepts, call `installPdfBrowser` before retrying the export.
 */

export const BROWSER_NOT_FOUND = 'BROWSER_NOT_FOUND';

export interface PdfBrowserStatus {
    available: boolean;
    source: 'configured' | 'system' | 'downloaded' | 'none';
    path: string | null;
    downloadAllowed: boolean;
    downloadSizeMb: number;
}

/** Thrown when PDF export failed only because no browser was usable. */
export class BrowserNotFoundError extends Error {
    public readonly code = BROWSER_NOT_FOUND;
    public readonly downloadSizeMb: number;

    constructor(message: string, downloadSizeMb = 0) {
        super(message);
        this.name = 'BrowserNotFoundError';
        this.downloadSizeMb = downloadSizeMb;
    }
}

export function isBrowserNotFound(err: unknown): err is BrowserNotFoundError {
    return err instanceof BrowserNotFoundError
        || (typeof err === 'object' && err !== null && (err as { code?: string }).code === BROWSER_NOT_FOUND);
}

/**
 * Converts a non-OK `/api/calcpad/pdf` response into the error to throw, so every
 * host recognizes the missing-browser case the same way.
 */
export async function pdfResponseError(response: Response): Promise<Error> {
    let body: { code?: string; message?: string; error?: string; downloadSizeMb?: number } | null = null;
    try {
        body = await response.json();
    } catch {
        /* non-JSON error body */
    }

    if (body?.code === BROWSER_NOT_FOUND) {
        return new BrowserNotFoundError(
            body.message || 'No Chromium-family browser is available for PDF export.',
            body.downloadSizeMb ?? 0,
        );
    }

    const detail = body?.message || body?.error;
    return new Error(detail
        ? `PDF endpoint returned ${response.status}: ${detail}`
        : `PDF endpoint returned ${response.status}`);
}

/**
 * @param headers Auth headers from `CalcpadApiClient.authHeaders()`. The local
 *   server answers 401 without them.
 */
export async function fetchPdfBrowserStatus(
    baseUrl: string,
    headers: Record<string, string> = {},
): Promise<PdfBrowserStatus | null> {
    try {
        const response = await fetch(`${baseUrl}/api/calcpad/pdf/browser`, {
            headers,
            signal: AbortSignal.timeout(10000),
        });
        if (!response.ok) return null;
        return await response.json() as PdfBrowserStatus;
    } catch {
        return null;
    }
}

/**
 * Downloads the bundled headless Chromium. Only call this after the user has agreed — it is a
 * multi-hundred-megabyte download, which is also why there is no timeout.
 */
export async function installPdfBrowser(
    baseUrl: string,
    headers: Record<string, string> = {},
): Promise<string> {
    const response = await fetch(`${baseUrl}/api/calcpad/pdf/browser/install`, { method: 'POST', headers });
    if (!response.ok) {
        let message = `Install endpoint returned ${response.status}`;
        try {
            const body = await response.json() as { message?: string };
            if (body?.message) message = body.message;
        } catch {
            /* non-JSON error body */
        }
        throw new Error(message);
    }
    const body = await response.json() as { path?: string };
    return body?.path ?? '';
}
