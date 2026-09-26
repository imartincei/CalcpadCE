import { defineConfig } from 'vite';
import vue from '@vitejs/plugin-vue';
import path from 'path';

// Tauri picks up frontendDist from tauri.conf.json (../../calcpad-web/dist),
// so the desktop build just needs vite's default outDir. VITE_SERVER_URL
// still overrides the dev proxy target when developers point at an already-
// running Calcpad.Server.
export default defineConfig(() => ({
    plugins: [vue()],
    define: {
        'import.meta.env.VITE_PLATFORM': JSON.stringify('web'),
    },
    resolve: {
        alias: {
            'calcpad-frontend': path.resolve(__dirname, '../calcpad-frontend/src'),
        },
    },
    build: {
        outDir: 'dist',
        emptyOutDir: true,
        // rolldown drops /*! @license */ banners by default. DOMPurify's is the
        // only attribution monaco ships for it, so losing it breaches Apache-2.0.
        rolldownOptions: {
            output: {
                comments: { legal: true },
            },
        },
    },
    // Monaco pulls in ~100 language files, and without pre-bundling vite's dep-optimizer runs
    // esbuild in parallel with browser transform requests, where esbuild 0.21.5's worker races
    // itself and dies with "callback is not a function". Naming the top-level Monaco
    // entrypoints forces vite to bundle them once at startup.
    optimizeDeps: {
        include: [
            'monaco-editor',
            'monaco-editor/esm/vs/editor/editor.worker',
            'monaco-editor/esm/vs/language/typescript/ts.worker',
            'monaco-editor/esm/vs/language/json/json.worker',
            'monaco-editor/esm/vs/language/html/html.worker',
            'monaco-editor/esm/vs/language/css/css.worker',
            'vue',
        ],
    },
    // Tauri consumes JS on the same port during `tauri dev`; keep the port
    // stable so vite.config's port matches tauri.conf.json's devUrl.
    server: {
        port: 5173,
        strictPort: true,
        proxy: {
            '/api': {
                target: process.env.VITE_SERVER_URL || 'http://localhost:9420',
                changeOrigin: true,
            },
        },
    },
}));
