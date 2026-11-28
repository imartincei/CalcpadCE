/// <reference types="vite/client" />

declare module '*.vue' {
    import type { DefineComponent } from 'vue';
    const component: DefineComponent<object, object, unknown>;
    export default component;
}

declare module 'virtual:calcpad-monaco-nls' {
    export function __calcpadSetLocale(tag: string): void;
    export function __calcpadTranslate(text: string, target: string): string | undefined;
}

declare module 'monaco-editor/esm/vs/platform/actions/common/actions.js' {
    export const MenuId: { readonly CommandPalette: unknown };
    export const MenuRegistry: unknown;
}

interface ImportMetaEnv {
    readonly VITE_APP_VERSION: string;
    readonly VITE_PLATFORM: 'web';
    readonly VITE_SERVER_URL?: string;
}

interface ImportMeta {
    readonly env: ImportMetaEnv;
}
