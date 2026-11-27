import test from 'node:test';
import assert from 'node:assert/strict';
import { existsSync, mkdtempSync, readdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { parseNav, stageAgentDocs, transformPage } from './agent-docs.mjs';
import { repoRoot } from './version.mjs';

test('nav pages keep their order, titles and groups', () => {
    const pages = parseNav([
        'site_name: x',
        'nav:',
        '  - Documentation:',
        '    - index.md',
        '    - Writing Math: writing-math.md',
        '  - Quick Reference:',
        '    - quick-reference.md',
        'markdown_extensions:',
        '  - toc',
    ].join('\n'));
    assert.deepEqual(pages, [
        { file: 'index.md', title: '', group: 'Documentation' },
        { file: 'writing-math.md', title: 'Writing Math', group: 'Documentation' },
        { file: 'quick-reference.md', title: '', group: 'Quick Reference' },
    ]);
});

test('every nav page exists in docs', () => {
    const pages = parseNav(readFileSync(resolve(repoRoot, 'mkdocs.yml'), 'utf8'));
    assert.ok(pages.length > 30);
    for (const page of pages) assert.ok(existsSync(resolve(repoRoot, 'docs', page.file)), page.file);
});

test('images become alt text and admonitions become blockquotes', () => {
    const page = transformPage([
        'Click <img src="./media/a.png" alt="" height="20"> to run.',
        '<img src="./media/b.png" alt="Sample1" />',
        '![Plot](media/plot.png)',
        '',
        '!!! note',
        '    First line.',
        '',
        '    Second paragraph.',
        '',
        '??? info "Operators"',
        '    `+` adds.',
        'After.',
    ].join('\n'));
    assert.equal(page, [
        'Click  to run.',
        '[image: Sample1]',
        '[image: Plot]',
        '',
        '> **Note**',
        '> First line.',
        '>',
        '> Second paragraph.',
        '',
        '> **Operators**',
        '> `+` adds.',
        'After.',
    ].join('\n'));
});

test('staging replaces the folder with exactly the nav pages and a manifest', () => {
    const dir = mkdtempSync(join(tmpdir(), 'agent-docs-'));
    try {
        const out = join(dir, 'out');
        stageAgentDocs({ outDir: out, version: '0.0.1' });
        writeFileSync(join(out, 'stale.md'), 'old');
        const manifest = stageAgentDocs({ outDir: out, version: '0.0.2' });
        const files = readdirSync(out).sort();
        assert.deepEqual(files, [...manifest.pages.map(p => p.file), 'manifest.json'].sort());
        assert.equal(JSON.parse(readFileSync(join(out, 'manifest.json'), 'utf8')).version, '0.0.2');
        assert.ok(manifest.pages.some(p => p.topic === 'quick-reference' && p.group === 'Quick Reference'));
        assert.ok(manifest.pages.some(p => p.topic === 'index' && p.title === 'Overview'));
        for (const file of files.filter(f => f.endsWith('.md'))) {
            const text = readFileSync(join(out, file), 'utf8');
            assert.doesNotMatch(text, /^(!!!|\?\?\?)/m, file);
            assert.doesNotMatch(text, /<img\b|\]\(\.?\/?media\//i, file);
        }
    } finally {
        rmSync(dir, { recursive: true, force: true });
    }
});
