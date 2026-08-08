import type { CalcpadApiClient } from 'calcpad-frontend/api/client';
import type { CalcpadDefinitionsService } from 'calcpad-frontend/services/definitions';
import type { CalcpadSnippetService } from 'calcpad-frontend/services/snippets';
import type { CalcpadSettings } from 'calcpad-frontend/types/settings';

/**
 * Shape both `MessageBridge` (web) and `TauriMessageBridge` (desktop) must satisfy
 * so editor providers can be written once and consume either.
 */
export interface EditorBridge {
    api: CalcpadApiClient;
    snippets: CalcpadSnippetService;
    definitions: CalcpadDefinitionsService;
    getSettings(): CalcpadSettings;
    getExtraSetting(key: string): string | undefined;
    setExtraSetting(key: string, value: string): void;
    /** Desktop overrides this with Tauri's native clipboard (WebKitGTK workaround); web falls back to the browser API. */
    readClipboardText?(): Promise<string>;
}

/**
 * Indirection so editor providers (hover, definitions, etc.) can ask "what
 * document am I in?" at call time. With multi-tab editing, the answer
 * changes when the user switches tabs. main.ts installs the resolver after
 * the TabManager is wired up.
 */
let activeDocumentKeyResolver: () => string = () => 'calcpad-editor';

export function getActiveDocumentKey(): string {
    return activeDocumentKeyResolver();
}

export function setActiveDocumentKeyResolver(resolver: () => string): void {
    activeDocumentKeyResolver = resolver;
}

/** @deprecated Use getActiveDocumentKey(). Kept for callers that haven't migrated. */
export const EDITOR_DOCUMENT_KEY = 'calcpad-editor';
