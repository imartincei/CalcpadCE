import { resolveVersion } from '../../../tools/version.mjs';
import { defineConfig } from 'vite';
import vue from '@vitejs/plugin-vue';
import path from 'path';
import fs from 'node:fs';

// Monaco's ESM build calls localize('key', 'English') with a string key, so the
// numeric NLS table used by its AMD build is never consulted. The maps carry the
// same (module, key) pairs from the pinned VS Code language packs.
interface LocaleEntry {
    tag: string;
    aliases: string[];
    languageFallback: boolean;
    monaco: boolean;
}

type NlsMap = Record<string, Record<string, string>>;

const monacoNlsDir = path.resolve(__dirname, 'node_modules/.cache/calcpad-monaco-nls/maps');
const monacoOverridesDir = path.resolve(__dirname, 'node_modules/.cache/calcpad-monaco-nls/overrides');
const localeConfig = JSON.parse(
    fs.readFileSync(path.resolve(__dirname, '../calcpad-frontend/src/i18n/locales.json'), 'utf8'),
) as { locales: LocaleEntry[] };

if (!fs.existsSync(monacoNlsDir))
    console.warn('[i18n] Monaco cache missing; run: node ../scripts/i18n/i18n.mjs --monaco-cache');

function loadMonacoNls(file: string): NlsMap {
    const fullPath = path.join(monacoNlsDir, file);
    return fs.existsSync(fullPath)
        ? JSON.parse(fs.readFileSync(fullPath, 'utf8'))
        : {};
}

function loadMonacoOverrides(locale: string): NlsMap {
    const fullPath = path.join(monacoOverridesDir, `${locale}.json`);
    return fs.existsSync(fullPath)
        ? JSON.parse(fs.readFileSync(fullPath, 'utf8'))
        : {};
}

function mergeMonacoNls(base: NlsMap, overrides: NlsMap): NlsMap {
    for (const [module, entries] of Object.entries(overrides))
        base[module] = { ...(base[module] ?? {}), ...entries };
    return base;
}

const monacoNls: Record<string, NlsMap> = {};
for (const locale of localeConfig.locales.filter(entry => entry.monaco)) {
    const base = loadMonacoNls(`${locale.tag}.json`);
    monacoNls[locale.tag] = mergeMonacoNls(base, loadMonacoOverrides(locale.tag));
}

const translatedKeys = new Set<string>();
for (const map of Object.values(monacoNls)) {
    for (const [module, entries] of Object.entries(map)) {
        for (const key of Object.keys(entries))
            translatedKeys.add(`${module}::${key}`);
    }
}

const localeMatches = localeConfig.locales.map(locale => ({
    tag: locale.tag,
    aliases: locale.aliases,
    languageFallback: locale.languageFallback,
}));

const MONACO_NLS_MODULE = `const __calcpadMaps = ${JSON.stringify(monacoNls)};
const __calcpadLocales = ${JSON.stringify(localeMatches)};
const __calcpadTraditional = /(?:^|-)hant(?:-|$)|(?:^|-)(?:tw|hk|mo)(?:-|$)/;
let __calcpadResolved;
const __calcpadEnglishById = Object.create(null);
const __calcpadEnglishIndex = new Map();
let __calcpadReverse;
function __calcpadMatch(tag) {
    if (!tag) return null;
    const n = String(tag).replace('_', '-').toLowerCase();
    for (const locale of __calcpadLocales) {
        if (locale.tag.toLowerCase() === n) return locale.tag;
        for (const alias of locale.aliases)
            if (alias.toLowerCase() === n) return locale.tag;
    }
    const language = n.split('-')[0];
    if (language === 'zh') return __calcpadTraditional.test(n) ? 'zh-Hant' : 'zh';
    if (language === 'pt') return n === 'pt-br' || n.startsWith('pt-br-') ? 'pt-BR' : null;
    const fallback = __calcpadLocales.find(locale => locale.languageFallback && locale.tag.toLowerCase() === language);
    return fallback ? fallback.tag : null;
}
function __calcpadLocale() {
    if (__calcpadResolved) return __calcpadResolved;
    let tag;
    try { tag = localStorage.getItem('calcpad.locale'); } catch {}
    if (tag && tag !== 'auto') __calcpadResolved = __calcpadMatch(tag);
    if (!__calcpadResolved && typeof window !== 'undefined') {
        __calcpadResolved = __calcpadMatch(window.__CALCPAD_CURRENT_CULTURE__)
            || __calcpadMatch(window.__CALCPAD_HOST_LOCALE__);
    }
    if (!__calcpadResolved && typeof navigator !== 'undefined') {
        for (const tag of [...(navigator.languages || []), navigator.language]) {
            __calcpadResolved = __calcpadMatch(tag);
            if (__calcpadResolved) break;
        }
    }
    return __calcpadResolved || (__calcpadResolved = 'en');
}
function __calcpadAddReverse(text, id) {
    let ids = __calcpadReverse.get(text);
    if (!ids) __calcpadReverse.set(text, ids = new Set());
    ids.add(id);
}
function __calcpadReverseIndex() {
    if (__calcpadReverse) return __calcpadReverse;
    __calcpadReverse = new Map();
    for (const map of Object.values(__calcpadMaps)) {
        for (const [module, entries] of Object.entries(map)) {
            for (const [key, value] of Object.entries(entries))
                __calcpadAddReverse(value, module + '::' + key);
        }
    }
    for (const [english, ids] of __calcpadEnglishIndex) {
        for (const id of ids) __calcpadAddReverse(english, id);
    }
    return __calcpadReverse;
}
export function __calcpadSetLocale(tag) {
    __calcpadResolved = __calcpadMatch(tag) || 'en';
}
export function __calcpadTranslate(text, target) {
    const ids = __calcpadReverseIndex().get(text);
    if (!ids) return undefined;
    const values = new Set();
    for (const id of ids) {
        const cut = id.indexOf('::');
        const module = id.slice(0, cut);
        const key = id.slice(cut + 2);
        const translated = __calcpadMaps[target]?.[module]?.[key] ?? __calcpadEnglishById[id];
        if (typeof translated === 'string' && translated) values.add(translated);
    }
    return values.size === 1 ? [...values][0] : undefined;
}
export function __calcpadTr(module, key, english) {
    const id = module + '::' + key;
    if (!(id in __calcpadEnglishById)) __calcpadEnglishById[id] = english;
    let ids = __calcpadEnglishIndex.get(english);
    if (!ids) __calcpadEnglishIndex.set(english, ids = new Set());
    if (!ids.has(id)) {
        ids.add(id);
        if (__calcpadReverse) __calcpadAddReverse(english, id);
    }
    const map = __calcpadMaps[__calcpadLocale()];
    return map?.[module]?.[key] || english;
}
`;

const MONACO_NLS_ID = 'virtual:calcpad-monaco-nls';
const MONACO_NLS_RESOLVED = '\0' + MONACO_NLS_ID;

const MONACO_LOCALIZE =
    /(\blocalize2?\s*\(\s*(?:'((?:[^'\\]|\\.)*)'|"((?:[^"\\]|\\.)*)")\s*,\s*)(?:'((?:[^'\\]|\\.)*)'|"((?:[^"\\]|\\.)*)")/g;

function decodeString(raw: string): string {
    try {
        return JSON.parse('"' + raw.replace(/\\'/g, "'") + '"');
    } catch {
        return raw;
    }
}

function monacoModule(id: string): string | null {
    const normalized = id.replace(/\\/g, '/').split('?')[0].split('#')[0];
    const marker = 'monaco-editor/esm/';
    const start = normalized.indexOf(marker);
    return start < 0 ? null : normalized.slice(start + marker.length).replace(/\.js$/, '');
}

function translateMonacoLiterals(code: string, module: string): string {
    let changed = false;
    const out = code.replace(
        MONACO_LOCALIZE,
        (match, head, keySingle, keyDouble, bodySingle, bodyDouble) => {
            const key = decodeString(keySingle ?? keyDouble);
            if (!translatedKeys.has(`${module}::${key}`)) return match;
            const english = decodeString(bodySingle ?? bodyDouble);
            changed = true;
            return `${head}__calcpadTr(${JSON.stringify(module)}, ${JSON.stringify(key)}, ${JSON.stringify(english)})`;
        },
    );
    return changed ? `import { __calcpadTr } from ${JSON.stringify(MONACO_NLS_ID)};\n${out}` : code;
}

function monacoTransform(code: string, id: string) {
    const cleanId = id.split('?')[0].split('#')[0];
    if (!cleanId.includes('monaco-editor/esm/') || !cleanId.endsWith('.js')) return null;
    if (!code.includes('localize(') && !code.includes('localize2(')) return null;
    const module = monacoModule(cleanId);
    if (!module) return null;
    const out = translateMonacoLiterals(code, module);
    return out === code ? null : { code: out, map: null };
}

function monacoLanguagePacks() {
    return {
        name: 'monaco-language-packs',
        enforce: 'pre' as const,
        resolveId(id: string) {
            return id === MONACO_NLS_ID ? MONACO_NLS_RESOLVED : null;
        },
        load(id: string) {
            return id === MONACO_NLS_RESOLVED ? MONACO_NLS_MODULE : null;
        },
        transform: monacoTransform,
    };
}

/** The dev dep optimizer serves Monaco pre-bundled, bypassing the Vite transform. */
function monacoPrebundle() {
    return {
        name: 'calcpad-monaco-nls-prebundle',
        resolveId(id: string) {
            return id === MONACO_NLS_ID ? MONACO_NLS_RESOLVED : null;
        },
        load(id: string) {
            return id === MONACO_NLS_RESOLVED ? MONACO_NLS_MODULE : null;
        },
        transform: monacoTransform,
    };
}

// Tauri picks up frontendDist from tauri.conf.json (../../calcpad-web/dist),
// so the desktop build just needs vite's default outDir. VITE_SERVER_URL
// still overrides the dev proxy target when developers point at an already-
// running Calcpad.Server.
export default defineConfig(() => ({
    plugins: [monacoLanguagePacks(), vue()],
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
        rolldownOptions: {
            plugins: [monacoPrebundle()],
        },
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
