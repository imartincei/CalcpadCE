import { resolveVersion } from '../../../tools/version.mjs';
import { defineConfig } from 'vite';
import vue from '@vitejs/plugin-vue';
import path from 'path';
import fs from 'node:fs';

// Monaco's npm ESM build inlines English and ignores the NLS global table, so each
// localize() message is wrapped in a tiny runtime chooser instead. The map pairs the
// English fallbacks in the min build with the matching zh-cn NLS entries.
const monacoZh = JSON.parse(
    fs.readFileSync(path.resolve(__dirname, 'src/editor/monaco-zh-map.json'), 'utf8'),
) as Record<string, string>;

const MONACO_NLS_HELPER =
    "const __calcpadTr=(en,zh)=>{try{const l=localStorage.getItem('calcpad.locale');" +
    "if(l==='en')return en;if(l==='zh')return zh}catch(e){}" +
    "return typeof navigator!=='undefined'&&navigator.language.toLowerCase().startsWith('zh')?zh:en};";

function translateMonacoLiterals(code: string): string {
    const doubleQuoted = /(\blocalize2?\s*\(\s*(?:'[^'\\]*(?:\\.[^'\\]*)*'|"[^"\\]*(?:\\.[^"\\]*)*")\s*,\s*)"((?:[^"\\]|\\.)*)"/g;
    const singleQuoted = /(\blocalize2?\s*\(\s*(?:'[^'\\]*(?:\\.[^'\\]*)*'|"[^"\\]*(?:\\.[^"\\]*)*")\s*,\s*)'((?:[^'\\]|\\.)*)'/g;
    const swap = (match: string, head: string, body: string) => {
        let text: string;
        try {
            text = JSON.parse('"' + body.replace(/\\'/g, "'") + '"');
        } catch {
            text = body;
        }
        const zh = monacoZh[text];
        return zh ? head + `__calcpadTr(${JSON.stringify(text)}, ${JSON.stringify(zh)})` : match;
    };
    const out = code.replace(doubleQuoted, swap).replace(singleQuoted, swap);
    return out === code ? code : MONACO_NLS_HELPER + '\n' + out;
}

function monacoChineseNls() {
    return {
        name: 'monaco-chinese-nls',
        enforce: 'pre' as const,
        transform(code: string, id: string) {
            if (!id.includes('monaco-editor/esm/') || !id.endsWith('.js')) return null;
            if (!code.includes('localize(') && !code.includes('localize2(')) return null;
            const out = translateMonacoLiterals(code);
            return out === code ? null : { code: out, map: null };
        },
    };
}

// Tauri picks up frontendDist from tauri.conf.json (../../calcpad-web/dist),
// so the desktop build just needs vite's default outDir. VITE_SERVER_URL
// still overrides the dev proxy target when developers point at an already-
// running Calcpad.Server.
export default defineConfig(() => ({
    plugins: [monacoChineseNls(), vue()],
    define: {
    'import.meta.env.VITE_APP_VERSION': JSON.stringify(resolveVersion()),
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
