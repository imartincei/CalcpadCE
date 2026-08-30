/**
 * Platform-aware messaging service for Vue components.
 * Uses import.meta.env.VITE_PLATFORM to select the adapter at build time:
 * - 'vscode': uses acquireVsCodeApi() (VS Code webview)
 * - 'web': uses window.calcpadBridge (in-process message bridge) — this is also the
 *   build the Tauri desktop app serves
 */

export interface IMessaging {
    postMessage(message: unknown): void;
    onMessage(handler: (message: unknown) => void): void;
}

let instance: IMessaging | null = null;

/**
 * Serialize Vue reactive objects safely for postMessage.
 */
function serializeForPostMessage(obj: unknown): unknown {
    if (obj === null || obj === undefined) return obj;
    if (typeof obj === 'string' || typeof obj === 'number' || typeof obj === 'boolean') return obj;

    if (Array.isArray(obj)) {
        return obj.map(item => serializeForPostMessage(item));
    }

    if (typeof obj === 'object') {
        const serialized: Record<string, unknown> = {};
        for (const [key, value] of Object.entries(obj)) {
            serialized[key] = serializeForPostMessage(value);
        }
        return serialized;
    }

    return obj;
}

/**
 * Initialize the messaging service for the current platform.
 * Must be called before any Vue component uses postMessage().
 */
export function initMessaging(): IMessaging {
    if (instance) return instance;

    if (import.meta.env.VITE_PLATFORM === 'web') {
        // Web: use in-process bridge (set by host app on window.calcpadBridge)
        const bridge = (window as any).calcpadBridge;
        instance = {
            postMessage: (msg: unknown) => bridge.handleMessage(serializeForPostMessage(msg)),
            onMessage: (handler: (message: unknown) => void) => {
                // This window also hosts the preview iframes, which render untrusted worksheet
                // HTML and can post anything they like. The host bridge announces itself with
                // a synthetic MessageEvent carrying no source, so a null source is what tells
                // a real host message from a forged one.
                window.addEventListener('message', (e: MessageEvent) => {
                    if (e.source !== null) return;
                    handler(e.data);
                });
            },
        };
    } else {
        // VS Code webview: use acquireVsCodeApi
        const vscode = (window as any).vscode || (window as any).acquireVsCodeApi();
        (window as any).vscode = vscode;
        instance = {
            postMessage: (msg: unknown) => vscode.postMessage(serializeForPostMessage(msg)),
            onMessage: (handler: (message: unknown) => void) => {
                // No source filter here, unlike the web branch: the panel webview
                // embeds no untrusted frames (its CSP is default-src 'none' with
                // nonce'd scripts — see CalcpadVueUIProvider), and the source VS Code
                // attaches to extension-host messages is not contractual.
                window.addEventListener('message', (e: MessageEvent) => handler(e.data));
            },
        };
    }

    return instance;
}

/**
 * Get the messaging service instance.
 */
export function getMessaging(): IMessaging {
    if (!instance) throw new Error('Messaging not initialized. Call initMessaging() first.');
    return instance;
}

/**
 * Post a message to the host (VS Code extension or the web/desktop host).
 * Drop-in replacement for the previous services/vscode.ts postMessage().
 */
export function postMessage(message: unknown): void {
    getMessaging().postMessage(message);
}
