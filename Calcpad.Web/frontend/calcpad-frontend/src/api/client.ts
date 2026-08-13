import type { ILogger } from '../types/interfaces';
import type {
    LintRequest,
    LintResponse,
    HighlightRequest,
    HighlightResponse,
    HighlightToken,
    DefinitionsRequest,
    DefinitionsResponse,
    SymbolAtPositionRequest,
    SymbolAtPositionResponse,
    PrettifyRequest,
    PrettifyResponse,
    CalcpadError,
    ConvertResult,
    UiConvertOptions,
    CpdzDecodeResponse,
    CpdzEncodeResponse,
    PortableBundleResult,
    PortablePackageResult,
} from '../types/api';
import type { SnippetsResponse } from '../types/snippets';

/** Request header carrying the server's per-launch token. Must match the backend's constant. */
export const API_TOKEN_HEADER = 'X-Calcpad-Token';

/** Mirrors the backend's own loopback test (`Program.IsLoopbackHost`). */
function isLoopbackUrl(url: string): boolean {
    try {
        const host = new URL(url).hostname.replace(/^\[|\]$/g, '');
        return host === 'localhost' || host === '::1' || /^127\./.test(host);
    } catch {
        return false;
    }
}

/**
 * Unified fetch-based API client for the CalcPad server.
 * Replaces scattered axios calls across the extension codebase.
 * Works in Node.js 18+, Electron, and browsers.
 */
export class CalcpadApiClient {
    private baseUrl: string;
    private logger?: ILogger;
    private authToken: string | null = null;

    // Per-key "latest wins" bookkeeping. A caller passes `key` (e.g. an editor
    // group id) to mean "only the newest request for this key still matters"
    // — an older request sharing the same key is aborted once a newer one for
    // that key starts, instead of running to completion long after it stopped
    // mattering. Requests with no key run independently with no supersession.
    private keySeq = new Map<string, number>();
    private keyAbort = new Map<string, AbortController>();

    constructor(baseUrl: string, logger?: ILogger) {
        this.baseUrl = baseUrl;
        this.logger = logger;
    }

    public setBaseUrl(url: string): void {
        this.baseUrl = url;
    }

    public getBaseUrl(): string {
        return this.baseUrl;
    }

    /**
     * Sets the per-launch token the local server requires on every `/api` route.
     * Pass `null` for a server that runs without one (a remote URL, or a
     * development launch that never had `CALCPAD_API_TOKEN` set).
     */
    public setAuthToken(token: string | null): void {
        this.authToken = token || null;
    }

    /**
     * Auth headers for a request this client doesn't make itself. Spread into
     * the `headers` of any direct `fetch` against the same server — a bare
     * request now comes back 401.
     *
     * Withheld for a non-loopback base URL. The token belongs to a server this
     * machine launched; `setBaseUrl` can be pointed at a configured remote one
     * (a preset carrying `server.url`), and that host has no business seeing it.
     */
    public authHeaders(): Record<string, string> {
        if (!this.authToken || !isLoopbackUrl(this.baseUrl)) return {};
        return { [API_TOKEN_HEADER]: this.authToken };
    }

    private jsonHeaders(): Record<string, string> {
        return { 'Content-Type': 'application/json', ...this.authHeaders() };
    }

    /**
     * Runs `task` immediately. If `key` is given, a later call sharing that
     * key aborts this one's signal instead of letting it run to completion
     * after it stops mattering. A superseded call resolves to `null` — the
     * same outcome callers already handle for a failed or non-OK response, so
     * no caller needs to special-case it.
     */
    private withSupersession<T>(key: string | undefined, task: (signal: AbortSignal) => Promise<T>): Promise<T | null> {
        if (!key) return task(new AbortController().signal);

        const mySeq = (this.keySeq.get(key) ?? 0) + 1;
        this.keySeq.set(key, mySeq);
        this.keyAbort.get(key)?.abort();
        const controller = new AbortController();
        this.keyAbort.set(key, controller);

        return (async (): Promise<T | null> => {
            try {
                return await task(controller.signal);
            } finally {
                if (this.keyAbort.get(key) === controller) this.keyAbort.delete(key);
            }
        })();
    }

    public async lint(content: string, sourceFilePath?: string, opts?: { key?: string }): Promise<LintResponse | null> {
        const request: LintRequest = { content, sourceFilePath };
        return this.post<LintResponse>('/api/calcpad/lint', request, 'Lint', opts?.key);
    }

    public async highlight(content: string, includeText: boolean = false, sourceFilePath?: string, opts?: { key?: string }): Promise<HighlightToken[] | null> {
        const request: HighlightRequest = { content, includeText, sourceFilePath };
        const response = await this.post<HighlightResponse>('/api/calcpad/highlight', request, 'Highlight', opts?.key);
        return response?.tokens ?? null;
    }

    public async definitions(content: string, sourceFilePath?: string, opts?: { key?: string }): Promise<DefinitionsResponse | null> {
        const request: DefinitionsRequest = { content, sourceFilePath };
        return this.post<DefinitionsResponse>('/api/calcpad/definitions', request, 'Definitions', opts?.key);
    }

    /**
     * Resolve a cursor position to the user-defined symbol at that point and
     * return every occurrence of it. Server-side replacement for the legacy
     * client-side overlap test that powers go-to-definition, find-all-references,
     * and rename across all editor integrations.
     */
    public async symbolAtPosition(
        content: string,
        line: number,
        column: number,
        sourceFilePath?: string,
        opts?: { key?: string },
    ): Promise<SymbolAtPositionResponse | null> {
        const request: SymbolAtPositionRequest = { content, line, column, sourceFilePath };
        return this.post<SymbolAtPositionResponse>('/api/calcpad/symbol-at-position', request, 'SymbolAtPosition', opts?.key);
    }

    /**
     * Decodes a compiled `.cpdz` worksheet to its source text. `composite` marks the
     * archive form that bundles images: its bytes must be handed back to
     * {@link encodeCpdz} on save or those images are lost.
     */
    public async decodeCpdz(data: Uint8Array): Promise<CpdzDecodeResponse | null> {
        return this.post<CpdzDecodeResponse>(
            '/api/calcpad/cpdz/decode', { data: toBase64(data) }, 'DecodeCpdz');
    }

    /**
     * Encodes source text as a `.cpdz` worksheet. Pass the bytes of the file being
     * overwritten as `original` so a composite archive keeps its other entries.
     */
    public async encodeCpdz(content: string, original?: Uint8Array): Promise<Uint8Array | null> {
        const result = await this.post<CpdzEncodeResponse>('/api/calcpad/cpdz/encode',
            { content, original: original ? toBase64(original) : undefined }, 'EncodeCpdz');
        return result ? fromBase64(result.data) : null;
    }

    /**
     * Rewrites a worksheet into the self-contained form a compiled `.cpdz` needs: macros and
     * `#include`d files expanded, `#read` data inlined, an included file's image paths made
     * absolute so the host can embed them. Reports what stands in the way instead of
     * returning a worksheet that would still read files beside it — which is why this has
     * its own request rather than going through {@link post}, whose errors are only logged.
     */
    public bundlePortable(
        content: string,
        sourceFilePath?: string,
    ): Promise<PortableBundleResult> {
        return (async () => {
            try {
                const response = await fetch(`${this.baseUrl}/api/calcpad/portable/bundle`, {
                    method: 'POST',
                    headers: this.jsonHeaders(),
                    body: JSON.stringify({ content, sourceFilePath }),
                    signal: AbortSignal.timeout(30000),
                });
                const body = await response.json().catch(() => null);
                if (response.ok && typeof body?.content === 'string') return { content: body.content, errors: [] };

                this.logger?.appendLine(`[BundlePortable] Server returned ${response.status}`);
                const messages: unknown = body?.messages;
                return {
                    errors: Array.isArray(messages) && messages.length
                        ? messages.map(String)
                        : [body?.message ?? body?.error ?? `The server returned ${response.status}`],
                };
            } catch (error) {
                this.logError('BundlePortable', error);
                return { errors: [error instanceof Error ? error.message : String(error)] };
            }
        })();
    }

    /**
     * Packs a worksheet and the files it references into a ZIP that stays text — the document
     * with its directives intact, and a folder beside it holding what they name. Reports what
     * stands in the way instead of writing a package that is missing a file, so like
     * {@link bundlePortable} it has its own request rather than going through {@link post}.
     */
    public packagePortable(
        content: string,
        sourceFilePath?: string,
    ): Promise<PortablePackageResult> {
        return (async () => {
            try {
                const response = await fetch(`${this.baseUrl}/api/calcpad/portable/package`, {
                    method: 'POST',
                    headers: this.jsonHeaders(),
                    body: JSON.stringify({ content, sourceFilePath }),
                    signal: AbortSignal.timeout(60000),
                });
                const body = await response.json().catch(() => null);
                if (response.ok && typeof body?.data === 'string') return {
                    zip: fromBase64(body.data),
                    name: String(body.name ?? 'worksheet.zip'),
                    refsFolder: String(body.refsFolder ?? ''),
                    bundled: Array.isArray(body.bundled) ? body.bundled.map(String) : [],
                    errors: [],
                };

                this.logger?.appendLine(`[PackagePortable] Server returned ${response.status}`);
                const messages: unknown = body?.messages;
                return {
                    bundled: [],
                    errors: Array.isArray(messages) && messages.length
                        ? messages.map(String)
                        : [body?.message ?? body?.error ?? `The server returned ${response.status}`],
                };
            } catch (error) {
                this.logError('PackagePortable', error);
                return { bundled: [], errors: [error instanceof Error ? error.message : String(error)] };
            }
        })();
    }

    public async snippets(): Promise<SnippetsResponse | null> {
        return this.get<SnippetsResponse>('/api/calcpad/snippets', 'Snippets');
    }

    public async prettify(
        content: string,
        indentUnit?: string,
        trimTrailingWhitespace?: boolean
    ): Promise<PrettifyResponse | null> {
        const request: PrettifyRequest = { content, indentUnit, trimTrailingWhitespace };
        return this.post<PrettifyResponse>('/api/calcpad/prettify', request, 'Prettify');
    }

    /**
     * @param ui Interactive `#UI` mode. `enableUi` renders `#UI` lines as controls and
     *   hides `#post` content; `uiOverrides` replaces the right hand side of annotated
     *   assignments and applies in both modes, so a report reflects entered values;
     *   `hideErrorLines` drops the "on line [N]" reference and defaults to `enableUi`.
     * @param includeLineAnchors Per-line anchors and error boxes for in-preview line links.
     *   Defaults server-side to `!forPrint`; pass it to break that pairing — `true` for the
     *   on-screen report, `false` for anything being written to a file.
     */
    public async convert(
        content: string,
        settings: unknown,
        outputFormat: string = 'html',
        forPrint: boolean = false,
        sourceFilePath?: string,
        theme?: 'light' | 'dark',
        ui?: UiConvertOptions,
        includeLineAnchors?: boolean,
        opts?: { key?: string },
    ): Promise<ArrayBuffer | ConvertResult | null> {
        return this.withSupersession(opts?.key, async (signal) => {
            const url = this.baseUrl + '/api/calcpad/convert';
            try {
                const response = await fetch(url, {
                    method: 'POST',
                    headers: this.jsonHeaders(),
                    body: JSON.stringify({
                        content, settings, outputFormat, forPrint, sourceFilePath, theme,
                        enableUi: ui?.enableUi ?? false,
                        uiOverrides: ui?.uiOverrides,
                        includeLineAnchors,
                        hideErrorLines: ui?.hideErrorLines,
                    }),
                    signal: combineSignals(AbortSignal.timeout(60000), signal),
                });
                if (!response.ok) return null;

                if (outputFormat === 'pdf') {
                    return response.arrayBuffer();
                }
                const html = await response.text();
                return { html, errors: parseConvertErrorHeader(response) };
            } catch (error) {
                this.logError('Convert', error);
                return null;
            }
        });
    }

    /**
     * Convert calcpad → DOCX (Word). Backend renders to HTML internally,
     * then runs the Calcpad.OpenXml writer over it. Returns the .docx
     * bytes, or null on failure.
     *
     * @param opts `forPrint` defaults to true — a Word export is a report unless the caller
     *   asks for the preview layout. `uiOverrides` makes the report show entered `#UI` values.
     */
    public async convertDocx(
        content: string,
        settings: unknown,
        sourceFilePath?: string,
        opts?: { forPrint?: boolean; uiOverrides?: Record<string, string> },
    ): Promise<ArrayBuffer | null> {
        return (async () => {
            const url = this.baseUrl + '/api/calcpad/docx';
            try {
                const response = await fetch(url, {
                    method: 'POST',
                    headers: this.jsonHeaders(),
                    body: JSON.stringify({
                        content,
                        settings,
                        sourceFilePath,
                        forPrint: opts?.forPrint ?? true,
                        uiOverrides: opts?.uiOverrides,
                    }),
                    signal: AbortSignal.timeout(60000),
                });
                if (!response.ok) return null;
                return response.arrayBuffer();
            } catch (error) {
                this.logError('ConvertDocx', error);
                return null;
            }
        })();
    }

    /**
     * Convert calcpad to "unwrapped" HTML — server returns just the body markup
     * without the document chrome. Used for preview-pane rendering.
     */
    public async convertUnwrapped(
        content: string,
        settings: unknown,
        sourceFilePath?: string,
        theme?: 'light' | 'dark',
        opts?: { key?: string },
    ): Promise<ConvertResult | null> {
        return this.withSupersession(opts?.key, async (signal) => {
            const url = this.baseUrl + '/api/calcpad/convert?unwrap=true';
            try {
                const response = await fetch(url, {
                    method: 'POST',
                    headers: this.jsonHeaders(),
                    body: JSON.stringify({ content, settings, sourceFilePath, theme }),
                    signal: combineSignals(AbortSignal.timeout(60000), signal),
                });
                if (!response.ok) return null;
                const html = await response.text();
                return { html, errors: parseConvertErrorHeader(response) };
            } catch (error) {
                this.logError('ConvertUnwrapped', error);
                return null;
            }
        });
    }

    /**
     * Runs an arbitrary request with the same per-key supersession as the
     * built-in methods above, for a caller whose request body isn't shaped
     * like any of them (e.g. an endpoint-specific extra field none of the
     * typed methods carry). `task` gets an `AbortSignal` that fires when it's
     * superseded — pass it as the `fetch` call's `signal` (combined with any
     * timeout signal of the caller's own) so a superseded request actually
     * aborts instead of running to completion unseen.
     */
    public runWithSupersession<T>(task: (signal: AbortSignal) => Promise<T>, opts?: { key?: string }): Promise<T | null> {
        return this.withSupersession(opts?.key, task);
    }

    public async checkHealth(): Promise<boolean> {
        try {
            const response = await fetch(this.baseUrl + '/api/calcpad/snippets', {
                headers: this.authHeaders(),
                signal: AbortSignal.timeout(5000),
            });
            return response.ok;
        } catch {
            return false;
        }
    }

    private post<T>(endpoint: string, body: unknown, tag: string, key?: string): Promise<T | null> {
        return this.withSupersession(key, async (signal) => {
            const url = this.baseUrl + endpoint;
            try {
                this.logger?.appendLine(`[${tag}] Sending request to server...`);
                const response = await fetch(url, {
                    method: 'POST',
                    headers: this.jsonHeaders(),
                    body: JSON.stringify(body),
                    signal: combineSignals(AbortSignal.timeout(30000), signal),
                });
                if (!response.ok) {
                    this.logger?.appendLine(`[${tag}] Server returned ${response.status}`);
                    return null;
                }
                const data: T = await response.json();
                return data;
            } catch (error) {
                this.logError(tag, error);
                return null;
            }
        });
    }

    private async get<T>(endpoint: string, tag: string): Promise<T | null> {
        const url = this.baseUrl + endpoint;
        try {
            this.logger?.appendLine(`[${tag}] Sending request to server...`);
            const response = await fetch(url, {
                headers: this.authHeaders(),
                signal: AbortSignal.timeout(30000),
            });
            if (!response.ok) {
                this.logger?.appendLine(`[${tag}] Server returned ${response.status}`);
                return null;
            }
            const data: T = await response.json();
            return data;
        } catch (error) {
            this.logError(tag, error);
            return null;
        }
    }

    private logError(tag: string, error: unknown): void {
        if (!this.logger) return;
        if (error instanceof DOMException && error.name === 'AbortError') {
            this.logger.appendLine(`[${tag}] Request timed out`);
        } else if (error instanceof TypeError && error.message.includes('fetch')) {
            this.logger.appendLine(`[${tag}] Server connection refused`);
        } else {
            this.logger.appendLine(`[${tag}] Error: ${error instanceof Error ? error.message : String(error)}`);
        }
    }
}

export function parseConvertErrorHeader(response: Response): CalcpadError[] {
    const raw = response.headers.get('X-Calcpad-Errors');
    if (!raw) return [];
    try {
        const parsed = JSON.parse(decodeURIComponent(raw));
        return Array.isArray(parsed) ? parsed : [];
    } catch {
        return [];
    }
}

/**
 * Aborts when either input does. Hand-rolled instead of `AbortSignal.any()`
 * since that's Node 20+ only and this client's contract is Node 18+.
 */
export function combineSignals(a: AbortSignal, b: AbortSignal): AbortSignal {
    if (a.aborted) return a;
    if (b.aborted) return b;
    const controller = new AbortController();
    a.addEventListener('abort', () => controller.abort(a.reason), { once: true });
    b.addEventListener('abort', () => controller.abort(b.reason), { once: true });
    return controller.signal;
}

/**
 * Base64 helpers for the binary `.cpdz` endpoints. Chunked so a large worksheet
 * doesn't blow the argument limit of String.fromCharCode.
 */
function toBase64(bytes: Uint8Array): string {
    let binary = '';
    const chunk = 0x8000;
    for (let i = 0; i < bytes.length; i += chunk) {
        binary += String.fromCharCode(...bytes.subarray(i, i + chunk));
    }
    return btoa(binary);
}

function fromBase64(data: string): Uint8Array {
    const binary = atob(data);
    const bytes = new Uint8Array(binary.length);
    for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
    return bytes;
}
