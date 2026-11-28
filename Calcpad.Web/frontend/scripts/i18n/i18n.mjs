/**
 * CalcpadCE translation pipeline.
 *
 *   node scripts/i18n/i18n.mjs              update generated files
 *   node scripts/i18n/i18n.mjs --check      verify generated files
 *   node scripts/i18n/i18n.mjs --locale=pt-BR
 *   node scripts/i18n/i18n.mjs --verbose
 *
 * Phases:
 *   1. extract the pinned VS Code language-pack subsets
 *   2. import exact shared-UI matches into the locale catalogs
 *   3. generate the Monaco NLS maps
 *   4. validate provenance
 *
 * Data stays in scripts/i18n/data; generated output stays in the frontend tree.
 */
import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const frontendRoot = path.resolve(here, '../..');
const dataDir = path.join(here, 'data');
const messagesDir = path.join(frontendRoot, 'calcpad-frontend/src/i18n/messages');
const communityDir = path.join(frontendRoot, 'calcpad-frontend/src/i18n/community');
const enFile = path.join(messagesDir, 'en.ts');
const webRoot = path.join(frontendRoot, 'calcpad-web');
const monacoDir = path.join(webRoot, 'src/editor/monaco-nls');
const cacheDir = path.join(webRoot, 'node_modules/.cache/calcpad-monaco-nls');
const monacoMapsDir = path.join(cacheDir, 'maps');
const monacoOverridesDir = path.join(cacheDir, 'overrides');

const VSCODE_TAG = '1.96.2';
const VSCODE_LOC_TAG = 'v1.96.2024121109';
const VSCODE_LOC_COMMIT = '110df563b65cd2a1f9d8b58e3bdcadce90427f6d';
const localesFile = path.join(frontendRoot, 'calcpad-frontend/src/i18n/locales.json');
const localeConfig = readJson(localesFile);
const LOCALES = localeConfig.locales;
const CATALOG_PACKS = LOCALES
    .filter(locale => locale.pack && locale.catalog)
    .map(locale => ({ output: locale.tag, folder: locale.pack }));
const MONACO_PACKS = LOCALES
    .filter(locale => locale.pack && locale.monaco)
    .map(locale => ({ output: locale.tag, folder: locale.pack }));
const SOURCES = ['vscode-monaco', 'vscode-shared-ui'];

const args = process.argv.slice(2);
const check = args.includes('--check');
const verbose = args.includes('--verbose');
const monacoOnly = args.includes('--monaco-cache');
const only = args.find(argument => argument.startsWith('--locale='))?.split('=')[1];
const unknown = args.filter(argument => !['--check', '--verbose', '--monaco-cache'].includes(argument) && !argument.startsWith('--locale='));
if (unknown.length) {
    console.error(`Unknown option: ${unknown.join(', ')}`);
    process.exit(1);
}

const knownLocales = new Set(LOCALES.map(locale => locale.tag));
if (only && !knownLocales.has(only)) {
    console.error(`Unknown locale: ${only}`);
    process.exit(1);
}

let failed = false;

function fail(message) {
    console.error(message);
    failed = true;
}

function readJson(file) {
    return JSON.parse(fs.readFileSync(file, 'utf8'));
}

function readCatalog(text) {
    const declaration = /\bconst\s+\w+\s*(?::[^=]+)?=\s*\{/.exec(text)
        ?? /\bexport\s+default\s*\{/.exec(text);
    if (!declaration) return {};
    const start = text.indexOf('{', declaration.index);
    let depth = 0;
    let quote = null;
    let escaped = false;
    let lineComment = false;
    let blockComment = false;
    for (let index = start; index < text.length; index++) {
        const char = text[index];
        const next = text[index + 1];
        if (lineComment) {
            if (char === '\n') lineComment = false;
            continue;
        }
        if (blockComment) {
            if (char === '*' && next === '/') {
                blockComment = false;
                index++;
            }
            continue;
        }
        if (quote) {
            if (escaped) {
                escaped = false;
                continue;
            }
            if (char === '\\') {
                escaped = true;
                continue;
            }
            if (char === quote) quote = null;
            continue;
        }
        if (char === "'" || char === '"' || char === '`') {
            quote = char;
            continue;
        }
        if (char === '/' && next === '/') {
            lineComment = true;
            index++;
            continue;
        }
        if (char === '/' && next === '*') {
            blockComment = true;
            index++;
            continue;
        }
        if (char === '{') depth++;
        else if (char === '}') {
            depth--;
            if (depth === 0) {
                const raw = text.slice(start, index + 1).replace(/,\s*}/g, '}');
                return JSON.parse(raw);
            }
        }
    }
    return {};
}

async function fetchWithRetry(url) {
    let lastError
    for (let attempt = 1; attempt <= 3; attempt++) {
        try {
            const response = await fetch(url, { signal: AbortSignal.timeout(30_000) })
            if (response.ok) return response
            lastError = new Error(`${response.status} ${response.statusText}`)
            if (response.status < 500) break
        } catch (error) {
            lastError = error instanceof Error ? error : new Error(String(error))
        }
        if (attempt < 3) await new Promise(resolve => setTimeout(resolve, attempt * 1000))
    }
    throw new Error(`failed to download ${url}: ${lastError?.message ?? lastError}`)
}

async function loadFullPack(pins) {
    fs.mkdirSync(cacheDir, { recursive: true });
    const cacheFile = path.join(cacheDir, `${pins.pack}-${pins.locCommit}.json`);
    if (!fs.existsSync(cacheFile)) {
        const url = `https://raw.githubusercontent.com/microsoft/vscode-loc/${pins.locCommit}/i18n/${pins.pack}/translations/main.i18n.json`;
        const response = await fetchWithRetry(url);
        fs.writeFileSync(cacheFile, await response.text());
    }
    const contents = JSON.parse(fs.readFileSync(cacheFile, 'utf8')).contents;
    if (!contents || typeof contents !== 'object')
        throw new Error(`invalid language pack: ${cacheFile}`);
    return contents;
}

function selectedCatalogPacks() {
    return CATALOG_PACKS.filter(pack => !only || pack.output === only);
}

function selectedMonacoPacks() {
    return MONACO_PACKS.filter(pack => !only || pack.output === only);
}

function localeSeed(pack) {
    return {
        vscode: {
            tag: VSCODE_TAG,
            locTag: VSCODE_LOC_TAG,
            locCommit: VSCODE_LOC_COMMIT,
            pack: pack.folder,
        },
        contents: {},
    };
}

async function extractLangpacks() {
    const packs = selectedCatalogPacks();
    if (!packs.length) return;
    const index = readJson(path.join(dataDir, 'vscode.index.json'));
    for (const pack of packs) {
        const locale = pack.output;
        const file = path.join(dataDir, `vscode.langpack.${locale}.json`);
        if (!fs.existsSync(file)) {
            if (check) {
                fail(`VS Code ${locale}: missing ${path.relative(frontendRoot, file)}; run:\n  node scripts/i18n/i18n.mjs --locale=${locale}`);
                continue;
            }
            fs.writeFileSync(file, JSON.stringify(localeSeed(pack), null, 2) + '\n');
        }
        const current = readJson(file);
        const pins = current.vscode;
        if (!pins?.pack || !pins?.locCommit) {
            fail(`VS Code ${locale}: missing vscode.pack or vscode.locCommit in ${path.basename(file)}`);
            continue;
        }
        let full;
        try {
            full = await loadFullPack(pins);
        } catch (error) {
            fail(`VS Code ${locale}: ${error.message}`);
            continue;
        }
        const contents = {};
        const errors = [];
        for (const [english, entries] of Object.entries(index.keys ?? {})) {
            for (const { source, module, key } of entries) {
                const value = full[module]?.[key];
                if (typeof value !== 'string' || !value) {
                    errors.push(`"${english}": ${module}::${key} is missing from ${pins.pack}`);
                    continue;
                }
                (contents[source] ??= {});
                (contents[source][module] ??= {});
                contents[source][module][key] = value;
            }
        }
        if (errors.length) {
            console.error(`VS Code ${locale}:`);
            for (const error of errors) console.error(`- ${error}`);
            failed = true;
            continue;
        }
        const ordered = {};
        for (const source of Object.keys(current.contents ?? {})) {
            for (const module of Object.keys(current.contents[source])) {
                for (const key of Object.keys(current.contents[source][module])) {
                    const value = contents[source]?.[module]?.[key];
                    if (value === undefined) continue;
                    (ordered[source] ??= {});
                    (ordered[source][module] ??= {});
                    ordered[source][module][key] = value;
                }
            }
        }
        for (const source of Object.keys(contents).sort()) {
            for (const module of Object.keys(contents[source]).sort()) {
                for (const key of Object.keys(contents[source][module]).sort()) {
                    if (ordered[source]?.[module]?.[key] !== undefined) continue;
                    (ordered[source] ??= {});
                    (ordered[source][module] ??= {});
                    ordered[source][module][key] = contents[source][module][key];
                }
            }
        }
        const entries = Object.values(ordered).reduce(
            (sum, modules) => sum + Object.values(modules).reduce((count, keys) => count + Object.keys(keys).length, 0),
            0,
        );
        const output = JSON.stringify({ vscode: pins, contents: ordered }, null, 2) + '\n';
        if (check) {
            const committed = fs.existsSync(file) ? fs.readFileSync(file, 'utf8') : '';
            if (committed !== output) {
                fail(`VS Code ${locale}: language-pack subset is out of date; run:\n  node scripts/i18n/i18n.mjs --locale=${locale}`);
            } else {
                console.log(`VS Code ${locale}: ${entries} entries for ${Object.keys(index.keys ?? {}).length} English keys, up to date.`);
            }
        } else {
            fs.writeFileSync(file, output);
            console.log(`VS Code ${locale}: extracted ${entries} entries for ${Object.keys(index.keys ?? {}).length} English keys.`);
        }
    }
}

function readEnglish() {
    return fs.existsSync(enFile) ? readCatalog(fs.readFileSync(enFile, 'utf8')) : {};
}

function readCommunity(locale) {
    const file = path.join(communityDir, `${locale}.ts`);
    if (!fs.existsSync(file)) return { ui: {}, monaco: {} };
    const data = readCatalog(fs.readFileSync(file, 'utf8'));
    return { ui: data.ui ?? {}, monaco: data.monaco ?? {} };
}

function readCommunityOrigins(locale) {
    const file = path.join(communityDir, `${locale}.provenance.json`);
    if (!fs.existsSync(file)) return {};
    return readJson(file).ui ?? {};
}

function blameCommunity(locale) {
    const file = path.join(communityDir, `${locale}.ts`);
    const relative = path.relative(frontendRoot, file).replace(/\\/g, '/');
    const output = execFileSync('git', ['blame', '--line-porcelain', '--', relative], {
        cwd: frontendRoot,
        encoding: 'utf8',
    });
    const origins = {};
    let commit = null;
    for (const line of output.split('\n')) {
        const header = /^([0-9a-f]{40}) \d+ (\d+)(?: \d+)?$/.exec(line);
        if (header) {
            commit = header[1];
            continue;
        }
        if (!commit || !line.startsWith('\t')) continue;
        const match = /^\s*"([^"]+)"\s*:/.exec(line.slice(1));
        if (match && match[1] !== 'ui' && match[1] !== 'monaco' && !match[1].includes('::'))
            origins[match[1]] = commit.slice(0, 8);
        commit = null;
    }
    return origins;
}

function gitMetadata(commit) {
    const [author, date, ...subject] = execFileSync(
        'git',
        ['show', '-s', '--format=%an%n%ad%n%s', '--date=short', commit],
        { cwd: frontendRoot, encoding: 'utf8' },
    ).trim().split('\n');
    return { author, date, subject: subject.join(' ') };
}

function importCatalogs() {
    const packs = selectedCatalogPacks();
    if (!packs.length) return;
    const index = readJson(path.join(dataDir, 'vscode.index.json'));
    const english = readEnglish();
    for (const catalogPack of packs) {
        const locale = catalogPack.output;
        const packFile = path.join(dataDir, `vscode.langpack.${locale}.json`);
        if (!fs.existsSync(packFile)) {
            fail(`VS Code ${locale}: missing ${path.relative(frontendRoot, packFile)}`);
            continue;
        }
        const pack = readJson(packFile);
        const catalogFile = path.join(messagesDir, `${locale}.ts`);
        const provenanceFile = path.join(messagesDir, `provenance.${locale}.json`);
        const errors = [];
        const selected = {};
        const skipped = [];

        for (const [text, entries] of Object.entries(index.keys ?? {})) {
            const id = index.ids?.[text];
            if (!id) {
                errors.push(`"${text}" has no ID in vscode.index.json`);
                continue;
            }
            if (typeof english[id] !== 'string') {
                errors.push(`"${id}" is missing from en.ts`);
                continue;
            }
            const bySource = Object.fromEntries(SOURCES.map(source => [source, new Set()]));
            for (const { source, module, key } of entries) {
                const value = pack.contents?.[source]?.[module]?.[key];
                if (value && bySource[source]) bySource[source].add(value);
            }
            const all = new Set(SOURCES.flatMap(source => [...bySource[source]]));
            if (all.size === 0) {
                errors.push(`"${text}" (${id}) has no translation in the committed langpack`);
                continue;
            }
            if (all.size === 1) {
                const value = [...all][0];
                const source = SOURCES.find(candidate => bySource[candidate].has(value));
                if (selected[id] && selected[id].value !== value) {
                    errors.push(`"${id}" has conflicting VS Code translations`);
                    continue;
                }
                selected[id] = { value, vscodeSource: source };
                continue;
            }
            const detail = SOURCES
                .filter(source => bySource[source].size)
                .map(source => `${source}: ${[...bySource[source]].join(', ')}`)
                .join('; ');
            skipped.push({ english: text, id, detail });
        }

        const community = readCommunity(locale);
        const origins = readCommunityOrigins(locale);
        const missingOrigins = Object.keys(community.ui).filter(id => !origins[id]);
        if (missingOrigins.length) {
            let blamed = {};
            try {
                blamed = blameCommunity(locale);
            } catch (error) {
                errors.push(`community/${locale}.ts: commit the file before running the i18n script (${error.message})`);
            }
            for (const id of missingOrigins) {
                const origin = blamed[id];
                if (!origin || origin.startsWith('0000000')) {
                    errors.push(`community "${id}" has no origin; commit community/${locale}.ts first`);
                    continue;
                }
                origins[id] = origin;
            }
        }
        const merged = {};
        for (const [id, entry] of Object.entries(selected)) {
            merged[id] = {
                value: entry.value,
                entry: { source: 'vscode', vscodeSource: entry.vscodeSource },
            };
        }
        for (const [id, value] of Object.entries(community.ui)) {
            if (typeof english[id] !== 'string') {
                errors.push(`community "${id}" is missing from en.ts`);
                continue;
            }
            if (typeof value !== 'string' || !value) {
                errors.push(`community "${id}" has an empty value`);
                continue;
            }
            const origin = origins[id];
            if (!origin) {
                errors.push(`community "${id}" has no origin in ${locale}.provenance.json`);
                continue;
            }
            merged[id] = {
                value,
                entry: {
                    source: 'community',
                    ...(id in selected ? { replaces: 'vscode' } : {}),
                    origin,
                },
            };
        }

        const mergedKeys = Object.keys(merged).sort((a, b) => a.localeCompare(b, 'en'));
        const vscodeCount = mergedKeys.filter(key => merged[key].entry.source === 'vscode').length;
        const communityCount = mergedKeys.length - vscodeCount;
        const monacoCount = mergedKeys.filter(
            key => merged[key].entry.source === 'vscode' && merged[key].entry.vscodeSource === 'vscode-monaco',
        ).length;
        const sharedCount = vscodeCount - monacoCount;
        const lines = mergedKeys.map(
            key => `    ${JSON.stringify(key)}: ${JSON.stringify(merged[key].value)},`,
        );
        const catalog = `// Generated by scripts/i18n/i18n.mjs for ${locale} from VS Code ${pack.vscode.tag}
// and ${pack.vscode.pack} ${pack.vscode.locTag}. Community entries come from community/${locale}.ts.
const catalog: Record<string, string> = {
${lines.join('\n')}
}

export default catalog
`;
        let existingProvenance = {};
        if (fs.existsSync(provenanceFile)) {
            try {
                existingProvenance = readJson(provenanceFile);
            } catch (error) {
                errors.push(`${path.basename(provenanceFile)}: ${error.message}`);
            }
        }
        const originsMap = existingProvenance.origins ?? {};
        for (const origin of new Set(Object.values(origins))) {
            if (originsMap[origin]) continue;
            try {
                originsMap[origin] = gitMetadata(origin);
            } catch (error) {
                errors.push(`community origin ${origin}: ${error.message}`);
            }
        }
        const sortedOrigins = Object.fromEntries(
            Object.entries(origins).sort(([a], [b]) => a.localeCompare(b, 'en')),
        );
        const sidecarText = JSON.stringify({ ui: sortedOrigins }, null, 2) + '\n';
        const sidecarFile = path.join(communityDir, `${locale}.provenance.json`);
        if (Object.keys(community.ui).length && check) {
            const currentSidecar = fs.existsSync(sidecarFile) ? fs.readFileSync(sidecarFile, 'utf8') : '';
            if (currentSidecar !== sidecarText)
                errors.push(`community/${locale}.provenance.json is out of date; run:\n  node scripts/i18n/i18n.mjs --locale=${locale}`);
        }
        const provenance = {
            locale,
            vscode: pack.vscode,
            ...(Object.keys(originsMap).length ? { origins: originsMap } : {}),
            keys: Object.fromEntries(mergedKeys.map(key => [key, merged[key].entry])),
        };
        const provenanceText = JSON.stringify(provenance, null, 2) + '\n';

        if (errors.length) {
            console.error(`VS Code ${locale}:`);
            for (const error of errors) console.error(`- ${error}`);
            failed = true;
            continue;
        }

        if (check) {
            const currentCatalog = fs.existsSync(catalogFile) ? fs.readFileSync(catalogFile, 'utf8') : '';
            const currentProvenance = fs.existsSync(provenanceFile) ? fs.readFileSync(provenanceFile, 'utf8') : '';
            if (currentCatalog !== catalog || currentProvenance !== provenanceText) {
                fail(`VS Code ${locale}: catalog is out of date; run:\n  node scripts/i18n/i18n.mjs --locale=${locale}`);
            } else {
                console.log(`VS Code ${locale}: ${mergedKeys.length} keys, up to date.`);
            }
            continue;
        }

        if (Object.keys(community.ui).length) fs.writeFileSync(sidecarFile, sidecarText);
        fs.writeFileSync(catalogFile, catalog);
        fs.writeFileSync(provenanceFile, provenanceText);
        console.log(
            `VS Code ${locale}: ${mergedKeys.length} keys ` +
            `(${monacoCount} vscode-monaco, ${sharedCount} vscode-shared-ui, ${communityCount} community).`,
        );
        if (skipped.length) {
            console.log(`Skipped ${skipped.length} keys with multiple translations:`);
            for (const item of skipped) console.log(`- ${item.english} (${item.id}; ${item.detail})`);
        }
    }
}

function walk(directory, output = []) {
    for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
        const full = path.join(directory, entry.name);
        if (entry.isDirectory()) walk(full, output);
        else output.push(full);
    }
    return output;
}

function decodeString(raw) {
    try {
        return JSON.parse('"' + raw.replace(/\\'/g, "'") + '"');
    } catch {
        return raw;
    }
}

function scanMonacoCalls(esmRoot) {
    const calls = [];
    const files = walk(esmRoot).filter(file => file.endsWith('.js'));
    const pattern = /\blocalize2?\s*\(\s*(?:'((?:[^'\\]|\\.)*)'|"((?:[^"\\]|\\.)*)")\s*,\s*(?:'((?:[^'\\]|\\.)*)'|"((?:[^"\\]|\\.)*)")/g;
    for (const file of files) {
        const source = fs.readFileSync(file, 'utf8');
        const module = path.relative(esmRoot, file).replace(/\\/g, '/').replace(/\.js$/, '');
        let match;
        while ((match = pattern.exec(source))) {
            calls.push({
                module,
                key: decodeString(match[1] ?? match[2]),
                english: decodeString(match[3] ?? match[4]),
            });
        }
    }
    return calls;
}

function placeholders(text) {
    return [...text.matchAll(/\{(\d+)\}/g)].map(match => match[1]).sort().join(',');
}

function addTranslation(map, module, key, translation, conflicts) {
    const previous = map[module]?.[key];
    if (previous !== undefined && previous !== translation) {
        conflicts.add(`${module}::${key}`);
        return false;
    }
    (map[module] ??= {})[key] = translation;
    return true;
}

function sortMap(map) {
    const sorted = {};
    for (const module of Object.keys(map).sort()) {
        sorted[module] = {};
        for (const key of Object.keys(map[module]).sort()) sorted[module][key] = map[module][key];
    }
    return sorted;
}

async function generateMonacoMaps() {
    const packs = selectedMonacoPacks();
    if (!packs.length) return;
    const monacoRoot = [
        path.join(webRoot, 'node_modules/monaco-editor'),
        path.join(frontendRoot, 'node_modules/monaco-editor'),
    ].find(candidate => fs.existsSync(candidate));
    if (!monacoRoot) {
        fail('monaco-editor is not installed; run npm ci in calcpad-web first');
        return;
    }
    const esmRoot = path.join(monacoRoot, 'esm');
    const calls = scanMonacoCalls(esmRoot);
    console.log(`Monaco NLS maps from vscode-loc ${VSCODE_LOC_TAG} (${VSCODE_LOC_COMMIT.slice(0, 12)})`);
    console.log(`Monaco: ${monacoRoot}`);
    console.log(`Calls: ${calls.length} across ${new Set(calls.map(call => call.module)).size} modules\n`);
    if (!check) {
        fs.mkdirSync(monacoMapsDir, { recursive: true });
        fs.mkdirSync(monacoOverridesDir, { recursive: true });
    }

    for (const pack of packs) {
        let contents;
        try {
            contents = await loadFullPack({ pack: pack.folder, locCommit: VSCODE_LOC_COMMIT });
        } catch (error) {
            fail(`${pack.output}: ${error.message}`);
            continue;
        }
        const map = {};
        const conflicts = new Set();
        const placeholderMismatches = new Set();
        const misses = [];
        let hits = 0;
        let identical = 0;
        for (const call of calls) {
            const translation = contents[call.module]?.[call.key];
            if (typeof translation !== 'string' || !translation) {
                misses.push(call);
                continue;
            }
            if (translation === call.english) {
                identical++;
                continue;
            }
            if (placeholders(translation) !== placeholders(call.english)) {
                placeholderMismatches.add(`${call.module}::${call.key}`);
                continue;
            }
            if (addTranslation(map, call.module, call.key, translation, conflicts)) hits++;
        }
        const sorted = sortMap(map);
        const entries = Object.values(sorted).reduce((sum, module) => sum + Object.keys(module).length, 0);
        const coverage = ((hits / calls.length) * 100).toFixed(1);
        console.log(
            `${pack.output}: ${hits} translated, ${identical} identical to English, ${misses.length} missing ` +
            `(${coverage}% translated, ${entries} entries)`,
        );
        if (conflicts.size)
            console.warn(`  conflicting translations: ${[...conflicts].slice(0, 5).join(', ')}${conflicts.size > 5 ? '…' : ''}`);
        if (placeholderMismatches.size)
            console.warn(`  placeholder mismatches: ${[...placeholderMismatches].slice(0, 5).join(', ')}${placeholderMismatches.size > 5 ? '…' : ''}`);
        const byModule = new Map();
        for (const miss of misses) byModule.set(miss.module, (byModule.get(miss.module) ?? 0) + 1);
        const top = [...byModule].sort((a, b) => b[1] - a[1]).slice(0, 5);
        if (top.length) console.log(`  top misses: ${top.map(([module, count]) => `${module} (${count})`).join(', ')}`);
        if (verbose) {
            for (const miss of misses) console.log(`  ${miss.module} :: ${miss.key} :: ${miss.english}`);
        }

        const serialized = JSON.stringify(sorted, null, 2) + '\n';
        const target = path.join(monacoMapsDir, `${pack.output}.json`);
        if (check) {
            const current = fs.existsSync(target) ? fs.readFileSync(target, 'utf8') : '';
            if (current !== serialized) fail(`  out of date: ${path.relative(frontendRoot, target)}`);
        } else {
            fs.writeFileSync(target, serialized);
            console.log(`  wrote ${path.relative(frontendRoot, target)}`);
        }

        const community = readCommunity(pack.output);
        const overrides = {};
        for (const [key, value] of Object.entries(community.monaco)) {
            const separator = key.indexOf('::');
            const module = separator > 0 ? key.slice(0, separator) : '';
            const messageKey = separator > 0 ? key.slice(separator + 2) : '';
            if (!module || !messageKey || !sorted[module]?.[messageKey]) {
                fail(`${pack.output}: monaco override "${key}" is not in the base map`);
                continue;
            }
            if (typeof value !== 'string' || !value) {
                fail(`${pack.output}: monaco override "${key}" has an empty value`);
                continue;
            }
            (overrides[module] ??= {})[messageKey] = value;
        }
        const sortedOverrides = sortMap(overrides);
        const overrideTarget = path.join(monacoOverridesDir, `${pack.output}.json`);
        const overrideSerialized = JSON.stringify(sortedOverrides, null, 2) + '\n';
        if (check) {
            const current = fs.existsSync(overrideTarget) ? fs.readFileSync(overrideTarget, 'utf8') : '';
            if (current !== overrideSerialized)
                fail(`  out of date: ${path.relative(frontendRoot, overrideTarget)}`);
        } else {
            fs.mkdirSync(path.dirname(overrideTarget), { recursive: true });
            fs.writeFileSync(overrideTarget, overrideSerialized);
            if (Object.keys(sortedOverrides).length)
                console.log(`  wrote ${path.relative(frontendRoot, overrideTarget)}`);
        }
    }
}

function localeRegistrySource() {
    const entries = LOCALES.map(locale => {
        const fields = [
            `"tag": ${JSON.stringify(locale.tag)}`,
            `"aliases": ${JSON.stringify(locale.aliases)}`,
            `"languageFallback": ${JSON.stringify(locale.languageFallback)}`,
        ].join(', ');
        return `    { ${fields} },`;
    }).join('\n');
    const labels = LOCALES
        .map(locale => `    ${JSON.stringify(locale.tag)}: ${JSON.stringify(locale.label)},`)
        .join('\n');
    return `// Generated by scripts/i18n/i18n.mjs from locales.json. Do not edit.
export const LOCALES = [
${entries}
] as const

export type CalcpadLocale = (typeof LOCALES)[number]['tag']

export const SUPPORTED_LOCALES = LOCALES.map(locale => locale.tag) as CalcpadLocale[]

export const LOCALE_LABELS: Record<CalcpadLocale, string> = {
${labels}
}
`;
}

function checkProvenance() {
    const errors = [];
    const registryFile = path.join(frontendRoot, 'calcpad-frontend/src/i18n/locales.generated.ts');
    const registry = localeRegistrySource();
    if (check) {
        const currentRegistry = fs.existsSync(registryFile) ? fs.readFileSync(registryFile, 'utf8') : '';
        if (currentRegistry !== registry)
            errors.push('locales.generated.ts is out of date; run:\n  node scripts/i18n/i18n.mjs');
    } else {
        fs.writeFileSync(registryFile, registry);
    }
    const readJsonSafe = file => {
        try {
            return JSON.parse(fs.readFileSync(file, 'utf8'));
        } catch (error) {
            errors.push(`${path.relative(frontendRoot, file)}: ${error.message}`);
            return {};
        }
    };
    const parseKeys = file => {
        const keys = new Set();
        const source = fs.readFileSync(path.join(messagesDir, file), 'utf8');
        for (const match of source.matchAll(/^\s*"((?:[^"\\]|\\.)*)"\s*:/gm))
            keys.add(JSON.parse('"' + match[1] + '"'));
        return keys;
    };

    const english = readEnglish();
    const messageIds = readJsonSafe(path.join(here, 'message-ids.json'));
    const messageIdValues = new Set(Object.values(messageIds));
    for (const [text, id] of Object.entries(messageIds)) {
        if (typeof id !== 'string' || !id) {
            errors.push(`message-ids.json: "${text}" has no ID`);
            continue;
        }
        if (english[id] !== text)
            errors.push(`message-ids.json: "${text}" -> "${id}" does not match en.ts`);
    }
    for (const [id, text] of Object.entries(english)) {
        if (typeof text !== 'string' || !text)
            errors.push(`en.ts: "${id}" has an empty English value`);
        if (!messageIdValues.has(id))
            errors.push(`en.ts: "${id}" has no entry in message-ids.json`);
    }

    const legacyFile = path.join(frontendRoot, 'calcpad-frontend/src/i18n/legacy-keys.generated.ts');
    if (fs.existsSync(legacyFile)) {
        const legacy = readCatalog(fs.readFileSync(legacyFile, 'utf8'));
        for (const [text, id] of Object.entries(messageIds)) {
            if (legacy[text] !== id)
                errors.push(`legacy-keys.generated.ts: "${text}" does not map to "${id}"`);
        }
        for (const text of Object.keys(legacy)) {
            if (!(text in messageIds))
                errors.push(`legacy-keys.generated.ts: "${text}" is not in message-ids.json`);
        }
    }

    const index = readJsonSafe(path.join(dataDir, 'vscode.index.json'));
    for (const text of Object.keys(index.keys ?? {})) {
        const id = index.ids?.[text];
        if (!id) errors.push(`vscode.index.json: "${text}" has no ID`);
        else if (english[id] !== text)
            errors.push(`vscode.index.json: "${text}" -> "${id}" does not match en.ts`);
    }
    for (const [text, id] of Object.entries(index.ids ?? {})) {
        if (!(text in (index.keys ?? {})))
            errors.push(`vscode.index.json: ID "${id}" belongs to unknown English key "${text}"`);
        if (!english[id])
            errors.push(`vscode.index.json: ID "${id}" is missing from en.ts`);
    }

    for (const pack of CATALOG_PACKS) {
        const locale = pack.output;
        const communityFile = path.join(communityDir, `${locale}.ts`);
        if (!fs.existsSync(communityFile)) continue;
        let community;
        try {
            community = readCommunity(locale);
        } catch (error) {
            errors.push(`${path.relative(frontendRoot, communityFile)}: ${error.message}`);
            continue;
        }
        const origins = readCommunityOrigins(locale);
        for (const id of Object.keys(community.ui ?? {})) {
            if (!english[id])
                errors.push(`community/${locale}.ts: "${id}" is missing from en.ts`);
            if (!origins[id])
                errors.push(`community/${locale}.ts: "${id}" has no origin`);
        }
        for (const [id, value] of Object.entries(community.monaco ?? {})) {
            if (!id.includes('::'))
                errors.push(`community/${locale}.ts: monaco key "${id}" must be module::key`);
            if (typeof value !== 'string' || !value)
                errors.push(`community/${locale}.ts: monaco key "${id}" has an empty value`);
        }
    }

    const sources = readJsonSafe(path.join(messagesDir, 'sources.json'));
    if (sources.vscode?.tag !== VSCODE_LOC_TAG || sources.vscode?.commit !== VSCODE_LOC_COMMIT)
        errors.push('sources.json: vscode tag/commit do not match the pinned release');
    if (sources.vscode?.source !== 'microsoft/vscode-loc')
        errors.push('sources.json: expected source microsoft/vscode-loc');
    if (sources.vscode?.license !== 'MIT')
        errors.push('sources.json: expected MIT license');
    const catalogs = fs.readdirSync(messagesDir)
        .filter(file => file.endsWith('.ts') && file !== 'en.ts')
        .map(file => file.slice(0, -3))
        .sort()
        .map(label => ({
            label,
            file: `${label}.ts`,
            manifest: readJsonSafe(path.join(messagesDir, `provenance.${label}.json`)),
        }));
    for (const catalog of catalogs) {
        if (catalog.manifest.default)
            errors.push(`provenance.${catalog.label}.json: a default source is not allowed`);
    }
    const catalogResults = [];
    for (const catalog of catalogs) {
        const keys = parseKeys(catalog.file);
        const counts = new Map();
        const originCounts = new Map();
        const vscodeSourceCounts = new Map();
        let overrides = 0;
        let vscodeMatches = 0;
        for (const key of keys) {
            if (typeof english[key] !== 'string') {
                errors.push(`${catalog.file}: key "${key}" is missing from en.ts`);
                continue;
            }
            const entry = catalog.manifest.keys?.[key];
            if (!entry) {
                errors.push(`${catalog.file}: key "${key}" has no explicit provenance`);
                continue;
            }
            const source = typeof entry === 'string' ? entry : entry?.source;
            if (!source) {
                errors.push(`${catalog.label}: key "${key}" has no source`);
                continue;
            }
            if (source === 'vscode') {
                const vscodeSource = typeof entry === 'string' ? undefined : entry.vscodeSource;
                if (!['vscode-monaco', 'vscode-shared-ui'].includes(vscodeSource))
                    errors.push(`${catalog.label}: key "${key}" has invalid vscodeSource "${vscodeSource}"`);
                else
                    vscodeSourceCounts.set(vscodeSource, (vscodeSourceCounts.get(vscodeSource) ?? 0) + 1);
            }
            if (typeof entry === 'object' && entry.replaces) {
                if (source !== 'community')
                    errors.push(`${catalog.label}: key "${key}" can only replace from community`);
                if (entry.replaces !== 'vscode')
                    errors.push(`${catalog.label}: key "${key}" replaces unknown source "${entry.replaces}"`);
                overrides++;
            }
            if (typeof entry === 'object' && entry.origin) {
                const origins = catalog.manifest.origins ?? {};
                if (!origins[entry.origin])
                    errors.push(`${catalog.label}: key "${key}" uses undeclared origin "${entry.origin}"`);
                else
                    originCounts.set(entry.origin, (originCounts.get(entry.origin) ?? 0) + 1);
            } else if (source === 'community') {
                errors.push(`${catalog.label}: community key "${key}" has no origin`);
            }
            if (typeof entry === 'object' && entry.vscodeMatch !== undefined) {
                if (typeof entry.vscodeMatch !== 'boolean')
                    errors.push(`${catalog.label}: key "${key}" vscodeMatch must be a boolean`);
                else if (entry.vscodeMatch)
                    vscodeMatches++;
            }
            if (!sources[source]) {
                errors.push(`${catalog.label}: key "${key}" uses undeclared source "${source}"`);
                continue;
            }
            counts.set(source, (counts.get(source) ?? 0) + 1);
        }
        for (const [key, entry] of Object.entries(catalog.manifest.keys ?? {})) {
            if (typeof english[key] !== 'string')
                errors.push(`${catalog.label}: key "${key}" is missing from en.ts`);
            if (!keys.has(key)) errors.push(`${catalog.label}: key "${key}" is not in ${catalog.file}`);
            const source = typeof entry === 'string' ? entry : entry?.source;
            if (!source || !sources[source])
                errors.push(`${catalog.label}: key "${key}" has an undeclared source`);
            if (typeof entry === 'object' && entry.origin) {
                const origins = catalog.manifest.origins ?? {};
                if (!origins[entry.origin])
                    errors.push(`${catalog.label}: key "${key}" has an undeclared origin`);
            }
        }
        catalogResults.push({
            label: catalog.label,
            total: keys.size,
            counts,
            originCounts,
            vscodeSourceCounts,
            overrides,
            vscodeMatches,
        });
    }

    const monacoSources = readJsonSafe(path.join(monacoDir, 'SOURCES.json'));
    if (monacoSources.generator !== '../../../../scripts/i18n/i18n.mjs')
        errors.push('SOURCES.json: generator does not point at scripts/i18n/i18n.mjs');
    if (monacoSources.tag !== VSCODE_LOC_TAG || monacoSources.commit !== VSCODE_LOC_COMMIT)
        errors.push('SOURCES.json: tag/commit do not match the pinned VS Code release');
    if (monacoSources.source !== 'microsoft/vscode-loc')
        errors.push('SOURCES.json: expected source microsoft/vscode-loc');
    if (monacoSources.license !== 'MIT')
        errors.push('SOURCES.json: expected MIT license');

    const packs = new Map(MONACO_PACKS.map(pack => [pack.output, pack.folder]));
    for (const [file, info] of Object.entries(monacoSources.maps ?? {})) {
        const output = file.replace(/\.json$/, '');
        if (!packs.has(output)) {
            errors.push(`SOURCES.json: map "${file}" has no matching generator pack`);
            continue;
        }
        if (packs.get(output) !== info.pack)
            errors.push(`SOURCES.json: pack for "${file}" does not match the generator`);
        if (info.source !== 'vscode')
            errors.push(`SOURCES.json: map "${file}" must be vscode`);
        if (info.vscodeSource !== 'vscode-monaco')
            errors.push(`SOURCES.json: map "${file}" must be vscode-monaco`);
    }
    if (Object.keys(monacoSources.maps ?? {}).length !== packs.size)
        errors.push('SOURCES.json: map count does not match the generator packs');

    let monacoOverrides = 0;
    for (const [locale, info] of Object.entries(monacoSources.overrides ?? {})) {
        if (info.source !== 'community')
            errors.push(`SOURCES.json: override "${locale}" must be community`);
        if (info.replaces !== 'vscode')
            errors.push(`SOURCES.json: override "${locale}" must replace vscode`);
        try {
            monacoOverrides += Object.keys(readCommunity(locale).monaco ?? {}).length;
        } catch (error) {
            errors.push(`community/${locale}.ts: ${error.message}`);
        }
    }

    if (errors.length) {
        console.error('Translation provenance check failed:');
        for (const error of errors) console.error(`- ${error}`);
        failed = true;
        return;
    }
    for (const result of catalogResults) {
        const summary = [...result.counts].map(([source, count]) => `${count} ${source}`).join(', ');
        const vscodeDetail = result.vscodeSourceCounts.size
            ? ` (${[...result.vscodeSourceCounts].map(([source, count]) => `${count} ${source}`).join(', ')})`
            : '';
        console.log(`Shared UI ${result.label} catalog: ${result.total} keys (${summary}; ${result.vscodeMatches} VS Code matches; ${result.overrides} overrides)${vscodeDetail}`);
        if (result.originCounts.size) {
            const topOrigins = [...result.originCounts]
                .sort((a, b) => b[1] - a[1])
                .slice(0, 4)
                .map(([origin, count]) => `${origin} (${count})`)
                .join(', ');
            console.log(`  Origins: ${topOrigins}`);
        }
    }
    console.log('Catalog counts only; missing keys fall back to English; hardcoded UI strings are not measured.');
    console.log(`Monaco: ${Object.keys(monacoSources.maps).length} maps from ${monacoSources.source} ${monacoSources.tag} (vscode; ${monacoOverrides} overrides)`);
    console.log('Provenance OK');
}

console.log(`i18n ${monacoOnly ? 'monaco cache' : check ? 'check' : 'update'}${only ? ` (${only})` : ''}`);
if (monacoOnly) {
    await generateMonacoMaps();
    if (failed) process.exitCode = 1;
    else console.log('i18n: monaco cache updated');
} else {
    await extractLangpacks();
    importCatalogs();
    if (!check) await generateMonacoMaps();
    checkProvenance();
    if (failed) process.exitCode = 1;
    else console.log(check ? 'i18n: OK' : 'i18n: updated');
}
