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
} from '../types/api';
import type { SnippetsResponse } from '../types/snippets';

/**
 * Unified fetch-based API client for the CalcPad server.
 * Replaces scattered axios calls across the extension codebase.
 * Works in Node.js 18+, Electron, and browsers.
 */
export class CalcpadApiClient {
    private baseUrl: string;
    private logger?: ILogger;

    // Serializes every parser-touching request. Calcpad.Core parses through a
    // process-global `MacroParser.Macros` dictionary that is cleared and
    // repopulated per request, so two in-flight requests corrupt each other's
    // macro state (and can crash the server). Chaining requests through this
    // promise guarantees at most one runs at a time — this matters now that the
    // desktop app can drive two editor groups (two previews + two linters).
    private requestQueue: Promise<unknown> = Promise.resolve();

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
     * Run `task` only after every previously-queued request has settled, so
     * parser requests never overlap. A task's failure never breaks the chain.
     */
    private serialize<T>(task: () => Promise<T>): Promise<T> {
        const run = this.requestQueue.then(task, task);
        this.requestQueue = run.then(() => undefined, () => undefined);
        return run;
    }

    public async lint(content: string, sourceFilePath?: string): Promise<LintResponse | null> {
        const request: LintRequest = { content, sourceFilePath };
        return this.post<LintResponse>('/api/calcpad/lint', request, 'Lint');
    }

    public async highlight(content: string, includeText: boolean = false, sourceFilePath?: string): Promise<HighlightToken[] | null> {
        const request: HighlightRequest = { content, includeText, sourceFilePath };
        const response = await this.post<HighlightResponse>('/api/calcpad/highlight', request, 'Highlight');
        return response?.tokens ?? null;
    }

    public async definitions(content: string, sourceFilePath?: string): Promise<DefinitionsResponse | null> {
        const request: DefinitionsRequest = { content, sourceFilePath };
        return this.post<DefinitionsResponse>('/api/calcpad/definitions', request, 'Definitions');
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
    ): Promise<SymbolAtPositionResponse | null> {
        const request: SymbolAtPositionRequest = { content, line, column, sourceFilePath };
        return this.post<SymbolAtPositionResponse>('/api/calcpad/symbol-at-position', request, 'SymbolAtPosition');
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
     *   assignments and applies in both modes, so a report reflects entered values.
     */
    public async convert(
        content: string,
        settings: unknown,
        outputFormat: string = 'html',
        forPrint: boolean = false,
        sourceFilePath?: string,
        theme?: 'light' | 'dark',
        ui?: UiConvertOptions
    ): Promise<ArrayBuffer | ConvertResult | null> {
        return this.serialize(async () => {
            const url = this.baseUrl + '/api/calcpad/convert';
            try {
                const response = await fetch(url, {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({
                        content, settings, outputFormat, forPrint, sourceFilePath, theme,
                        enableUi: ui?.enableUi ?? false,
                        uiOverrides: ui?.uiOverrides,
                    }),
                    signal: AbortSignal.timeout(60000),
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
     */
    public async convertDocx(
        content: string,
        settings: unknown,
        sourceFilePath?: string,
    ): Promise<ArrayBuffer | null> {
        return this.serialize(async () => {
            const url = this.baseUrl + '/api/calcpad/docx';
            try {
                const response = await fetch(url, {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({
                        content,
                        settings,
                        sourceFilePath,
                        forPrint: true,
                    }),
                    signal: AbortSignal.timeout(60000),
                });
                if (!response.ok) return null;
                return response.arrayBuffer();
            } catch (error) {
                this.logError('ConvertDocx', error);
                return null;
            }
        });
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
    ): Promise<ConvertResult | null> {
        return this.serialize(async () => {
            const url = this.baseUrl + '/api/calcpad/convert?unwrap=true';
            try {
                const response = await fetch(url, {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ content, settings, sourceFilePath, theme }),
                    signal: AbortSignal.timeout(60000),
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

    public async checkHealth(): Promise<boolean> {
        try {
            const response = await fetch(this.baseUrl + '/api/calcpad/snippets', {
                signal: AbortSignal.timeout(5000),
            });
            return response.ok;
        } catch {
            return false;
        }
    }

    private post<T>(endpoint: string, body: unknown, tag: string): Promise<T | null> {
        // Serialized: every POST endpoint runs the Calcpad.Core parser, whose
        // static macro state cannot be shared by concurrent requests.
        return this.serialize(async () => {
            const url = this.baseUrl + endpoint;
            try {
                this.logger?.appendLine(`[${tag}] Sending request to server...`);
                const response = await fetch(url, {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify(body),
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
        });
    }

    private async get<T>(endpoint: string, tag: string): Promise<T | null> {
        const url = this.baseUrl + endpoint;
        try {
            this.logger?.appendLine(`[${tag}] Sending request to server...`);
            const response = await fetch(url, {
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
