// One-off inventory for the message-ID migration. It scans the frontend
// sources, the generated Chinese catalog, and the known dynamic lookup sites,
// then writes a draft ID map for review. It does not modify the runtime.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const frontendRoot = path.resolve(here, '../..');
const outputDir = path.join(here, 'drafts');
const roots = [
    'calcpad-frontend/src',
    'calcpad-web/src',
    'vscode-calcpad/src',
].map(dir => path.join(frontendRoot, dir));

const AREA_RULES = [
    [/CalcpadSettingsTab\.vue$/, 'settings'],
    [/SettingRow\.vue$/, 'settings'],
    [/CalcpadInsertTab\.vue$/, 'insert'],
    [/CalcpadMetadataTab\.vue$/, 'metadata'],
    [/CalcpadExportTab\.vue$/, 'export'],
    [/CalcpadFilesTab\.vue$/, 'files'],
    [/FilesFolderSection\.vue$/, 'files'],
    [/CalcpadFileInfoSection\.vue$/, 'fileInfo'],
    [/CalcpadTocTab\.vue$/, 'toc'],
    [/CalcpadVariablesTab\.vue$/, 'variables'],
    [/CalcpadFormattingTab\.vue$/, 'formatting'],
    [/CalcpadUiOverridesSection\.vue$/, 'uiOverrides'],
    [/CalcpadSearchField\.vue$/, 'search'],
    [/CalcpadApp\.vue$/, 'app'],
    [/HotkeyRecorder\.vue$/, 'hotkeys'],
    [/AgentChatPanel\.vue$/, 'agentChat'],
    [/AgentDiffModal\.vue$/, 'agentDiff'],
    [/DesktopTitleBar\.vue$/, 'desktop'],
    [/FileConflictDialog\.vue$/, 'fileConflict'],
    [/calcpad-web\/src\/App\.vue$/, 'app'],
    [/calcpad-web\/src\/main\.ts$/, 'editor'],
    [/i18n\/menu\.ts$/, 'menu'],
    [/i18n\/index\.ts$/, 'common'],
    [/scripts\/i18n\/data\/vscode\.index\.json$/, 'common'],
    [/calcpad-web\/src\/editor\//, 'editor'],
    [/calcpad-frontend\/src\/services\//, 'service'],
    [/calcpad-frontend\/src\/api\//, 'api'],
];

const SHARED_WORDS = new Set([
    'add', 'apply', 'cancel', 'clear', 'close', 'copy', 'delete', 'description',
    'edit', 'error', 'file', 'files', 'find', 'insert', 'language', 'name',
    'next', 'none', 'off', 'on', 'open', 'output', 'preview', 'previous',
    'remove', 'reset', 'save', 'search', 'settings', 'stop', 'title', 'type',
    'warning',
]);

function walk(directory, output = []) {
    if (!fs.existsSync(directory)) return output;
    for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
        const full = path.join(directory, entry.name);
        if (entry.isDirectory()) walk(full, output);
        else if (/\.(ts|vue)$/.test(entry.name) && !entry.name.endsWith('.d.ts'))
            output.push(full);
    }
    return output;
}

function skipSource(file) {
    const relative = path.relative(frontendRoot, file).replace(/\\/g, '/');
    return relative.includes('/i18n/community/')
        || relative.includes('/i18n/messages/')
        || relative.endsWith('/i18n/legacy-keys.generated.ts')
        || relative.endsWith('/i18n/locales.generated.ts');
}

function decode(raw, quote) {
    try {
        if (quote === '"') return JSON.parse('"' + raw + '"');
        if (quote === "'") return JSON.parse('"' + raw.replace(/\\'/g, "'") + '"');
        return raw.replace(/\\`/g, '`').replace(/\\\$/g, '$');
    } catch {
        return raw;
    }
}

function areaFor(file) {
    const relative = path.relative(frontendRoot, file).replace(/\\/g, '/');
    for (const [pattern, area] of AREA_RULES) {
        if (pattern.test(relative)) return area;
    }
    const base = path.basename(file, path.extname(file));
    const cleaned = base.replace(/^Calcpad/, '').replace(/[^A-Za-z0-9]+/g, '');
    return cleaned ? cleaned[0].toLowerCase() + cleaned.slice(1) : 'common';
}

function slugify(text) {
    const trimmed = text.trim();
    const suffix = trimmed.endsWith(':')
        ? ' label'
        : trimmed.endsWith('?')
            ? ' question'
            : trimmed.endsWith('!')
                ? ' exclamation'
                : trimmed.endsWith('…') || trimmed.endsWith('...')
                    ? ' ellipsis'
                    : '';
    const normalized = (text + suffix)
        .normalize('NFKD')
        .replace(/[\u0300-\u036f]/g, '')
        .replace(/['’]/g, '')
        .replace(/\be\.g\.\s*/gi, 'example ')
        .replace(/&/g, ' and ')
        .replace(/#/g, ' hash ')
        .replace(/%/g, ' percent ')
        .replace(/[^A-Za-z0-9]+/g, ' ')
        .trim();
    if (!normalized) return 'message';
    const words = normalized.split(/\s+/).slice(0, 8);
    return words
        .map((word, index) => index === 0
            ? word.toLowerCase()
            : word[0].toUpperCase() + word.slice(1).toLowerCase())
        .join('')
        .slice(0, 64);
}

function add(map, text, file, kind) {
    if (!text || !text.trim()) return;
    const relative = path.relative(frontendRoot, file).replace(/\\/g, '/');
    const area = areaFor(file);
    const entry = map.get(text) ?? { en: text, areas: new Set(), files: new Set(), kinds: new Set() };
    entry.areas.add(area);
    entry.files.add(relative);
    entry.kinds.add(kind);
    map.set(text, entry);
}

function addCatalog(map, text) {
    if (!text || !text.trim()) return;
    const entry = map.get(text) ?? { en: text, areas: new Set(), files: new Set(), kinds: new Set() };
    entry.catalog = true;
    entry.kinds.add('catalog');
    map.set(text, entry);
}

function addData(map, text, file) {
    if (!text || !text.trim()) return;
    const relative = path.relative(frontendRoot, file).replace(/\\/g, '/');
    const area = areaFor(file);
    const entry = map.get(text) ?? { en: text, areas: new Set(), files: new Set(), kinds: new Set() };
    entry.areas.add(area);
    entry.files.add(relative);
    entry.kinds.add('data');
    map.set(text, entry);
}

function extractFromFile(file, map, dynamic) {
    const source = fs.readFileSync(file, 'utf8');
    const lines = source.split('\n');
    const literal = /\b(t|tt|translate)\s*\(\s*(?:'((?:[^'\\]|\\.)*)'|"((?:[^"\\]|\\.)*)"|`((?:[^`\\]|\\.)*)`)/g;
    let match;
    while ((match = literal.exec(source))) {
        const quote = match[2] !== undefined ? "'" : match[3] !== undefined ? '"' : '`';
        const raw = match[2] ?? match[3] ?? match[4] ?? '';
        if (quote === '`' && raw.includes('${')) continue;
        add(map, decode(raw, quote), file, match[1]);
    }
    const call = /\b(t|tt|translate)\s*\(\s*([A-Za-z_$][^)]*)/g;
    while ((match = call.exec(source))) {
        const line = source.slice(0, match.index).split('\n').length;
        dynamic.push({
            expression: `${match[1]}(${match[2].trim()}`,
            file: path.relative(frontendRoot, file).replace(/\\/g, '/'),
            line,
            sourceLine: lines[line - 1]?.trim() ?? '',
        });
    }
}

function addConstant(map, text, file, note) {
    add(map, text, file, 'constant');
    const entry = map.get(text);
    if (entry) entry.note = entry.note ? `${entry.note}; ${note}` : note;
}

function catalogKeys() {
    const file = path.join(frontendRoot, 'calcpad-frontend/src/i18n/messages/zh.ts');
    const source = fs.readFileSync(file, 'utf8');
    return [...source.matchAll(/^\s*"((?:[^"\\]|\\.)*)"\s*:/gm)]
        .map(match => JSON.parse('"' + match[1] + '"'));
}

function menuKeys() {
    const file = path.join(frontendRoot, 'calcpad-frontend/src/i18n/menu.ts');
    const source = fs.readFileSync(file, 'utf8');
    const block = source.slice(source.indexOf('MENU_KEYS'), source.indexOf(']'));
    return [...block.matchAll(/'((?:[^'\\]|\\.)*)'/g)].map(match => decode(match[1], "'"));
}

const map = new Map();
const dynamic = [];
const files = roots.flatMap(root => walk(root)).filter(file => !skipSource(file));
for (const file of files) extractFromFile(file, map, dynamic);
const uniqueDynamic = [];
const seenDynamic = new Set();
for (const item of dynamic) {
    const wrapper = item.expression.startsWith('t(key')
        || item.expression.startsWith('translate(key')
        || item.sourceLine.includes('function t(')
        || item.sourceLine.includes('const t =')
        || item.sourceLine.includes('function translate(');
    if (wrapper) continue;
    const key = `${item.file}::${item.expression}`;
    if (seenDynamic.has(key)) continue;
    seenDynamic.add(key);
    uniqueDynamic.push(item);
}

const catalog = catalogKeys();
const catalogSet = new Set(catalog);
for (const key of catalog) addCatalog(map, key);

for (const file of files) {
    const source = fs.readFileSync(file, 'utf8');
    const strings = /(?:'((?:[^'\\]|\\.)*)'|"((?:[^"\\]|\\.)*)"|`((?:[^`\\]|\\.)*)`)/g;
    let match;
    while ((match = strings.exec(source))) {
        const quote = match[1] !== undefined ? "'" : match[2] !== undefined ? '"' : '`';
        const raw = match[1] ?? match[2] ?? match[3] ?? '';
        if (quote === '`' && raw.includes('${')) continue;
        const text = decode(raw, quote);
        if (catalogSet.has(text)) addData(map, text, file);
    }
}

for (const key of menuKeys()) addConstant(map, key, path.join(frontendRoot, 'calcpad-frontend/src/i18n/menu.ts'), 'MENU_KEYS');

const indexPath = path.join(frontendRoot, 'scripts/i18n/data/vscode.index.json');
const index = JSON.parse(fs.readFileSync(indexPath, 'utf8'));
for (const key of Object.keys(index.keys ?? {})) addConstant(map, key, indexPath, 'VS_CODE_INDEX');

const filesTab = path.join(frontendRoot, 'calcpad-frontend/src/vue/components/CalcpadFilesTab.vue');
const filesSource = fs.readFileSync(filesTab, 'utf8');
for (const match of filesSource.matchAll(/(?:relative|absolute):\s*'((?:[^'\\]|\\.)*)'/g))
    addConstant(map, decode(match[1], "'"), filesTab, 'REFERENCE_LABELS');

const insertTab = path.join(frontendRoot, 'calcpad-frontend/src/vue/components/CalcpadInsertTab.vue');
const insertSource = fs.readFileSync(insertTab, 'utf8');
for (const match of insertSource.matchAll(/(?:Function|Macro|Variable|Unit):\s*'((?:[^'\\]|\\.)*)'/g))
    addConstant(map, decode(match[1], "'"), insertTab, 'SNIPPET_TYPE_LABELS');
addConstant(map, 'Auto', path.join(frontendRoot, 'calcpad-frontend/src/i18n/index.ts'), 'LOCALE_OPTIONS');

const entries = [];
const usedIds = new Map();
for (const entry of map.values()) {
    const areas = [...entry.areas].sort();
    const shared = areas.length > 1 && SHARED_WORDS.has(slugify(entry.en).toLowerCase());
    const area = shared ? 'common' : areas[0] ?? 'catalog';
    let id = `${area}.${slugify(entry.en)}`;
    let suffix = 2;
    while (usedIds.has(id) && usedIds.get(id) !== entry.en) {
        id = `${area}.${slugify(entry.en)}${suffix}`;
        suffix++;
    }
    usedIds.set(id, entry.en);
    const flags = [];
    if (areas.length > 1) flags.push(shared ? 'shared' : 'multiArea');
    if (entry.en.length > 80) flags.push('long');
    if (entry.catalog && !entry.kinds.has('t') && !entry.kinds.has('tt') && !entry.kinds.has('translate') && !entry.kinds.has('constant') && !entry.kinds.has('data'))
        flags.push('unused');
    if (suffix > 2) flags.push('collision');
    entries.push({
        id,
        en: entry.en,
        areas,
        files: [...entry.files].sort(),
        kinds: [...entry.kinds].sort(),
        note: entry.note ?? null,
        flags,
    });
}

entries.sort((a, b) => a.id.localeCompare(b.id, 'en'));

const byArea = {};
for (const entry of entries) {
    const area = entry.id.split('.')[0];
    (byArea[area] ??= []).push(entry);
}

const counts = {
    entries: entries.length,
    areas: Object.keys(byArea).length,
    dynamic: uniqueDynamic.length,
    multiArea: entries.filter(entry => entry.flags.includes('multiArea') || entry.flags.includes('shared')).length,
    long: entries.filter(entry => entry.flags.includes('long')).length,
    unused: entries.filter(entry => entry.flags.includes('unused')).length,
};

fs.mkdirSync(outputDir, { recursive: true });
fs.writeFileSync(path.join(outputDir, 'message-ids.draft.json'), JSON.stringify({
    convention: '<area>.<component>.<element>',
    counts,
    entries,
    dynamic: uniqueDynamic,
}, null, 2) + '\n');

const lines = [
    '# Draft message IDs',
    '',
    'Generated by `scripts/i18n/propose-ids.mjs`. Review only; no runtime files use these IDs yet.',
    '',
    `- Entries: **${counts.entries}**`,
    `- Areas: **${counts.areas}**`,
    `- Dynamic call sites: **${counts.dynamic}**`,
    `- Multi-area/shared: **${counts.multiArea}**`,
    `- Long strings: **${counts.long}**`,
    `- Unused (only in the catalog, not in source/data): **${counts.unused}**`,
    '',
    '## Dynamic call sites',
    '',
    'These cannot be migrated by a literal replacement; the data source must return an ID or the call site needs a mapping.',
    '',
    '| File | Line | Expression |',
    '|---|---:|---|',
    ...uniqueDynamic.map(item => `| ${item.file} | ${item.line} | \`${item.expression.replace(/\|/g, '\\|')}\` |`),
    '',
    '## Entries by area',
    '',
];
for (const area of Object.keys(byArea).sort()) {
    lines.push(`### ${area}`, '', '| ID | English | Source | Flags |', '|---|---|---|---|');
    for (const entry of byArea[area]) {
        const source = entry.files.length ? entry.files.join('<br>') : 'catalog';
        const flags = entry.flags.length ? entry.flags.join(', ') : '';
        const english = entry.en.replace(/\|/g, '\\|').replace(/\n/g, ' ');
        lines.push(`| \`${entry.id}\` | ${english} | ${source} | ${flags} |`);
    }
    lines.push('');
}
fs.writeFileSync(path.join(outputDir, 'message-ids.draft.md'), lines.join('\n').replace(/\n+$/, '') + '\n');

console.log(JSON.stringify(counts, null, 2));
console.log(`\nWrote ${path.relative(frontendRoot, path.join(outputDir, 'message-ids.draft.json'))}`);
console.log(`Wrote ${path.relative(frontendRoot, path.join(outputDir, 'message-ids.draft.md'))}`);
