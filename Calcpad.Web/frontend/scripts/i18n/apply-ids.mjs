// One-time migration helper: turns the reviewed draft into the message-ID
// sources. Run it once, then edit community files and en.ts by hand.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const frontendRoot = path.resolve(here, '../..');
const messagesDir = path.join(frontendRoot, 'calcpad-frontend/src/i18n/messages');
const communityDir = path.join(frontendRoot, 'calcpad-frontend/src/i18n/community');
const draftFile = path.join(here, 'drafts/message-ids.draft.json');
const draft = JSON.parse(fs.readFileSync(draftFile, 'utf8'));

function readObject(text) {
    const start = text.indexOf('{');
    const end = text.lastIndexOf('}');
    if (start < 0 || end < 0) return {};
    return JSON.parse(text.slice(start, end + 1).replace(/,\s*}$/, '}'));
}

function quote(value) {
    return JSON.stringify(value);
}

function sortedObject(object, keyOf = entry => entry[0]) {
    return Object.fromEntries(Object.entries(object).sort((a, b) => keyOf(a).localeCompare(keyOf(b), 'en')));
}

const entries = draft.entries;
const englishToId = {};
const idToEnglish = {};
for (const entry of entries) {
    if (englishToId[entry.en] && englishToId[entry.en] !== entry.id)
        throw new Error(`duplicate English: ${entry.en}`);
    if (idToEnglish[entry.id] && idToEnglish[entry.id] !== entry.en)
        throw new Error(`duplicate ID: ${entry.id}`);
    englishToId[entry.en] = entry.id;
    idToEnglish[entry.id] = entry.en;
}

const messageIds = sortedObject(englishToId);
fs.writeFileSync(path.join(here, 'message-ids.json'), JSON.stringify(messageIds, null, 2) + '\n');

const enLines = Object.entries(idToEnglish)
    .sort(([a], [b]) => a.localeCompare(b, 'en'))
    .map(([id, english]) => `    ${quote(id)}: ${quote(english)},`);
fs.writeFileSync(path.join(messagesDir, 'en.ts'), `// English source. IDs are stable; see scripts/i18n/MESSAGE-IDS.md.
const en: Record<string, string> = {
${enLines.join('\n')}
}

export default en
`);

const legacyLines = Object.entries(englishToId)
    .sort(([, a], [, b]) => a.localeCompare(b, 'en'))
    .map(([english, id]) => `    ${quote(english)}: ${quote(id)},`);
fs.writeFileSync(path.join(frontendRoot, 'calcpad-frontend/src/i18n/legacy-keys.generated.ts'), `// Temporary English -> ID bridge for the message-ID migration.
// Generated from scripts/i18n/message-ids.json; remove when all call sites use IDs.
const legacyKeys: Record<string, string> = {
${legacyLines.join('\n')}
}

export default legacyKeys
`);

const zhCatalog = readObject(fs.readFileSync(path.join(messagesDir, 'zh.ts'), 'utf8'));
const zhProvenance = JSON.parse(fs.readFileSync(path.join(messagesDir, 'provenance.zh.json'), 'utf8'));
const zhUi = {};
const zhOrigins = {};
for (const [english, value] of Object.entries(zhCatalog)) {
    const id = englishToId[english];
    if (!id) throw new Error(`zh key has no ID: ${english}`);
    zhUi[id] = value;
    const origin = zhProvenance.keys?.[english]?.origin;
    if (!origin) throw new Error(`zh key has no origin: ${english}`);
    zhOrigins[id] = origin;
}
const sortedZh = sortedObject(zhUi);
fs.mkdirSync(communityDir, { recursive: true });
fs.writeFileSync(path.join(communityDir, 'zh.ts'), `// Community translations for zh. Edit this file; the generated catalog is rebuilt by the i18n script.
import type { CommunityCatalog } from '../types'

const community: CommunityCatalog = {
    "ui": {
${Object.entries(sortedZh).map(([id, value]) => `        ${quote(id)}: ${quote(value)},`).join('\n')}
    },
    "monaco": {},
}

export default community
`);
fs.writeFileSync(
    path.join(communityDir, 'zh.provenance.json'),
    JSON.stringify({ ui: sortedObject(zhOrigins) }, null, 2) + '\n',
);

const indexFile = path.join(here, 'data/vscode.index.json');
const index = JSON.parse(fs.readFileSync(indexFile, 'utf8'));
const ids = {};
for (const english of Object.keys(index.keys ?? {})) {
    const id = englishToId[english];
    if (!id) throw new Error(`VS Code index key has no ID: ${english}`);
    ids[english] = id;
}
fs.writeFileSync(indexFile, JSON.stringify({ vscode: index.vscode, ids, keys: index.keys }, null, 2) + '\n');

const localesFile = path.join(frontendRoot, 'calcpad-frontend/src/i18n/locales.json');
const locales = JSON.parse(fs.readFileSync(localesFile, 'utf8'));
const zh = locales.locales.find(locale => locale.tag === 'zh');
if (!zh) throw new Error('zh locale not found');
zh.catalog = true;
fs.writeFileSync(localesFile, JSON.stringify(locales, null, 2) + '\n');

console.log(`message IDs: ${Object.keys(messageIds).length}`);
console.log(`zh community entries: ${Object.keys(zhUi).length}`);
console.log(`VS Code index IDs: ${Object.keys(ids).length}`);
