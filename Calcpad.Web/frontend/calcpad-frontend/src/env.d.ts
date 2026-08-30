/// <reference types="vite/client" />

interface ImportMetaEnv {
    readonly VITE_PLATFORM: 'vscode' | 'web';
}

interface ImportMeta {
    readonly env: ImportMetaEnv;
}
