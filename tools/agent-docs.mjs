import { mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { pathToFileURL } from 'node:url';
import { repoRoot, resolveVersion } from './version.mjs';

/** Pages from the mkdocs `nav`, in order, with their group. */
export function parseNav(mkdocsYml) {
    const lines = mkdocsYml.split(/\r?\n/);
    const start = lines.findIndex(l => /^nav:\s*$/.test(l));
    if (start === -1) throw new Error('mkdocs.yml has no nav');
    const pages = [];
    let group = '';
    for (const line of lines.slice(start + 1)) {
        if (/^\S/.test(line)) break;
        const item = line.match(/^(\s*)-\s+(.*?)\s*$/);
        if (!item) continue;
        const entry = item[2];
        const titled = entry.match(/^(.+?):\s*(\S+\.md)$/);
        if (titled) pages.push({ file: titled[2], title: titled[1], group });
        else if (/\.md$/.test(entry)) pages.push({ file: entry, title: '', group });
        else if (entry.endsWith(':')) group = entry.slice(0, -1);
    }
    return pages;
}

const imageAlt = alt => (alt?.trim() ? `[image: ${alt.trim()}]` : '');

/** Makes a docs page readable as plain Markdown: no images, admonitions as blockquotes. */
export function transformPage(markdown) {
    const text = markdown
        .replace(/<img\b[^>]*>/gi, tag => imageAlt(tag.match(/\balt\s*=\s*"([^"]*)"/i)?.[1]))
        .replace(/!\[([^\]]*)\]\([^)]*\)/g, (_, alt) => imageAlt(alt));
    const lines = text.split(/\r?\n/);
    const out = [];
    for (let i = 0; i < lines.length; i++) {
        const head = lines[i].match(/^(!!!|\?\?\?\+?)\s+(\w+)(?:\s+"([^"]*)")?\s*$/);
        if (!head) {
            out.push(lines[i]);
            continue;
        }
        const label = head[3] || head[2][0].toUpperCase() + head[2].slice(1);
        const body = [];
        while (i + 1 < lines.length && (lines[i + 1].trim() === '' || /^( {4}|\t)/.test(lines[i + 1]))) {
            body.push(lines[++i]);
        }
        const trailing = [];
        while (body.length && body.at(-1).trim() === '') trailing.unshift(body.pop());
        out.push(`> **${label}**`);
        for (const line of body) out.push(line.trim() === '' ? '>' : `> ${line.replace(/^( {4}|\t)/, '')}`);
        out.push(...trailing);
    }
    return out.join('\n');
}

function fileTitle(file) {
    const stem = file.replace(/\.md$/, '');
    if (stem === 'index') return 'Overview';
    const words = stem.replace(/^new-/, '').replace(/-/g, ' ');
    return words[0].toUpperCase() + words.slice(1);
}

/** Copies the nav's pages into `outDir`, replacing whatever was there, with a manifest. */
export function stageAgentDocs({ outDir, version, docsDir = resolve(repoRoot, 'docs'), mkdocs = resolve(repoRoot, 'mkdocs.yml') }) {
    const pages = parseNav(readFileSync(mkdocs, 'utf8')).map(page => {
        const source = readFileSync(resolve(docsDir, page.file), 'utf8');
        return { ...page, title: page.title || fileTitle(page.file), text: transformPage(source) };
    });
    const topics = new Set();
    for (const page of pages) {
        if (page.file.includes('/')) throw new Error(`Nested docs page ${page.file} is not supported`);
        const topic = page.file.replace(/\.md$/, '');
        if (topics.has(topic)) throw new Error(`Duplicate docs page ${page.file}`);
        topics.add(topic);
    }
    rmSync(outDir, { recursive: true, force: true });
    mkdirSync(outDir, { recursive: true });
    const manifest = { version, pages: [] };
    for (const page of pages) {
        writeFileSync(resolve(outDir, page.file), page.text);
        manifest.pages.push({
            topic: page.file.replace(/\.md$/, ''),
            file: page.file,
            title: page.title,
            group: page.group,
            bytes: Buffer.byteLength(page.text),
        });
    }
    writeFileSync(resolve(outDir, 'manifest.json'), `${JSON.stringify(manifest, null, 2)}\n`);
    return manifest;
}

if (import.meta.url === pathToFileURL(process.argv[1] ?? '').href) {
    const outDir = process.argv[2];
    if (!outDir) {
        console.error('Usage: node tools/agent-docs.mjs <output folder>');
        process.exit(1);
    }
    const manifest = stageAgentDocs({ outDir: resolve(outDir), version: resolveVersion() });
    console.log(`Staged ${manifest.pages.length} docs pages for ${manifest.version} in ${outDir}`);
}
