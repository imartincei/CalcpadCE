// One-time codemod: replace literal translation keys with stable message IDs.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const frontendRoot = path.resolve(here, '../..');
const messageIds = JSON.parse(fs.readFileSync(path.join(here, 'message-ids.json'), 'utf8'));
const roots = [
    'calcpad-frontend/src',
    'calcpad-web/src',
    'vscode-calcpad/src',
].map(dir => path.join(frontendRoot, dir));

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

function skip(file) {
    const relative = path.relative(frontendRoot, file).replace(/\\/g, '/');
    return relative.includes('/i18n/community/')
        || relative.includes('/i18n/messages/')
        || relative.endsWith('/i18n/legacy-keys.generated.ts')
        || relative.endsWith('/i18n/locales.generated.ts')
        || relative.endsWith('/i18n/use-calcpad-i18n.ts')
        || relative.endsWith('/i18n/keys.ts');
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

const literal = /\b(t|tt|translate)\s*\(\s*(?:'((?:[^'\\]|\\.)*)'|"((?:[^"\\]|\\.)*)"|`((?:[^`\\]|\\.)*)`)/g;
const missing = new Map();
let replacements = 0;
let files = 0;

for (const file of roots.flatMap(root => walk(root)).filter(file => !skip(file))) {
    const source = fs.readFileSync(file, 'utf8');
    let changed = false;
    const output = source.replace(literal, (match, name, single, double, template) => {
        const quote = single !== undefined ? "'" : double !== undefined ? '"' : '`';
        const raw = single ?? double ?? template ?? '';
        if (quote === '`' && raw.includes('${')) {
            const key = `dynamic template: ${raw.slice(0, 80)}`;
            missing.set(key, (missing.get(key) ?? 0) + 1);
            return match;
        }
        const english = decode(raw, quote);
        const id = messageIds[english];
        if (!id) {
            missing.set(english, (missing.get(english) ?? 0) + 1);
            return match;
        }
        if (english === id) return match;
        replacements++;
        changed = true;
        return `${name}('${id}'`;
    });
    if (changed) {
        fs.writeFileSync(file, output);
        files++;
    }
}

console.log(`replaced ${replacements} calls in ${files} files`);
if (missing.size) {
    console.log(`left ${missing.size} keys without IDs:`);
    for (const [key, count] of [...missing].sort((a, b) => b[1] - a[1]).slice(0, 40))
        console.log(`- ${count}x ${JSON.stringify(key).slice(0, 140)}`);
}
