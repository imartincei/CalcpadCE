// CI-only check: the English text at every vscode.index.json module/key must
// match the Calcpad English key. Monaco entries come from the installed editor;
// shared-UI entries are fetched from the pinned VS Code source and cached under
// node_modules/.cache. No source file is committed.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const frontendRoot = path.resolve(here, '../..');
const index = JSON.parse(fs.readFileSync(path.join(here, 'data/vscode.index.json'), 'utf8'));
const vscodeTag = index.vscode?.tag;
if (!vscodeTag) throw new Error('vscode.index.json has no vscode.tag');

const cacheDir = path.join(
    frontendRoot,
    'calcpad-web/node_modules/.cache/calcpad-vscode-source',
    vscodeTag,
);

function decode(raw) {
    try {
        return JSON.parse('"' + raw.replace(/\\'/g, "'") + '"');
    } catch {
        return raw;
    }
}

function parseLocalize(source) {
    const found = new Map();
    const pattern = /\blocalize2?\s*\(\s*(?:'((?:[^'\\]|\\.)*)'|"((?:[^"\\]|\\.)*)")\s*,\s*(?:'((?:[^'\\]|\\.)*)'|"((?:[^"\\]|\\.)*)")/g;
    let match;
    while ((match = pattern.exec(source))) {
        const key = decode(match[1] ?? match[2]);
        const english = decode(match[3] ?? match[4]);
        if (!found.has(key)) found.set(key, english);
    }
    return found;
}

function walk(directory, output = []) {
    for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
        const full = path.join(directory, entry.name);
        if (entry.isDirectory()) walk(full, output);
        else if (entry.name.endsWith('.js')) output.push(full);
    }
    return output;
}

function monacoCalls() {
    const root = [
        path.join(frontendRoot, 'calcpad-web/node_modules/monaco-editor/esm'),
        path.join(frontendRoot, 'calcpad-frontend/node_modules/monaco-editor/esm'),
    ].find(candidate => fs.existsSync(candidate));
    if (!root) throw new Error('monaco-editor is not installed; run npm ci in calcpad-web');
    const calls = new Map();
    for (const file of walk(root)) {
        const module = path.relative(root, file).replace(/\\/g, '/').replace(/\.js$/, '');
        for (const [key, english] of parseLocalize(fs.readFileSync(file, 'utf8')))
            calls.set(`${module}::${key}`, english);
    }
    return calls;
}

async function fetchText(url) {
    let lastError;
    for (let attempt = 1; attempt <= 3; attempt++) {
        try {
            const response = await fetch(url, { signal: AbortSignal.timeout(30_000) });
            if (response.ok) return response.text();
            lastError = new Error(`${response.status} ${response.statusText}`);
            if (response.status < 500) break;
        } catch (error) {
            lastError = error;
        }
        if (attempt < 3) await new Promise(resolve => setTimeout(resolve, attempt * 1000));
    }
    throw new Error(`${url}: ${lastError?.message ?? lastError}`);
}

async function loadSharedSource(module) {
    const relative = path.join(cacheDir, `${module}.ts`);
    if (fs.existsSync(relative)) return fs.readFileSync(relative, 'utf8');
    const tsx = path.join(cacheDir, `${module}.tsx`);
    if (fs.existsSync(tsx)) return fs.readFileSync(tsx, 'utf8');
    for (const extension of ['ts', 'tsx']) {
        const url = `https://raw.githubusercontent.com/microsoft/vscode/${vscodeTag}/src/${module}.${extension}`;
        try {
            const text = await fetchText(url);
            const target = path.join(cacheDir, `${module}.${extension}`);
            fs.mkdirSync(path.dirname(target), { recursive: true });
            fs.writeFileSync(target, text);
            return text;
        } catch (error) {
            if (!String(error.message).startsWith('404')) throw error;
        }
    }
    throw new Error(`source file not found for ${module}`);
}

async function pool(items, limit, worker) {
    const results = new Array(items.length);
    let next = 0;
    async function run() {
        while (next < items.length) {
            const index = next++;
            results[index] = await worker(items[index]);
        }
    }
    await Promise.all(Array.from({ length: Math.min(limit, items.length) }, run));
    return results;
}

const errors = [];
const monaco = monacoCalls();
const entries = Object.entries(index.keys ?? {}).flatMap(([english, list]) =>
    list.map(entry => ({ english, ...entry })));
const sharedModules = [...new Set(entries
    .filter(entry => entry.source === 'vscode-shared-ui')
    .map(entry => entry.module))];

await pool(sharedModules, 8, async module => {
    try {
        const source = await loadSharedSource(module);
        const calls = parseLocalize(source);
        for (const entry of entries) {
            if (entry.source !== 'vscode-shared-ui' || entry.module !== module) continue;
            const actual = calls.get(entry.key);
            if (actual === undefined)
                errors.push(`${module}::${entry.key}: no localize() call found`);
            else if (actual !== entry.english)
                errors.push(`${module}::${entry.key}: expected ${JSON.stringify(entry.english)}, got ${JSON.stringify(actual)}`);
        }
    } catch (error) {
        errors.push(error.message);
    }
});

for (const entry of entries.filter(entry => entry.source === 'vscode-monaco')) {
    const actual = monaco.get(`${entry.module}::${entry.key}`);
    if (actual === undefined)
        errors.push(`${entry.module}::${entry.key}: no Monaco localize() call found`);
    else if (actual !== entry.english)
        errors.push(`${entry.module}::${entry.key}: expected ${JSON.stringify(entry.english)}, got ${JSON.stringify(actual)}`);
}

if (errors.length) {
    console.error(`VS Code English source check failed (${errors.length}):`);
    for (const error of errors) console.error(`- ${error}`);
    process.exitCode = 1;
} else {
    console.log(`VS Code English source OK: ${entries.length} entries ` +
        `(${entries.filter(entry => entry.source === 'vscode-monaco').length} Monaco, ` +
        `${entries.filter(entry => entry.source === 'vscode-shared-ui').length} shared UI, ` +
        `${sharedModules.length} source files).`);
}
