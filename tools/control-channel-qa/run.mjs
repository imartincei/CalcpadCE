#!/usr/bin/env node
// Control-channel QA suite: end-to-end checks for calcpadctl / --ctl.
// Tests the control channel's contract against a running app; it is not the
// calculation-engine or WebDriver desktop-UI suite. See README.md.
import assert from 'node:assert/strict';
import { copyFileSync, existsSync, mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { spawn, spawnSync } from 'node:child_process';
import { tmpdir } from 'node:os';
import { dirname, join, resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = dirname(fileURLToPath(import.meta.url));
const APP = process.env.CALCPAD_DESKTOP ?? '/usr/bin/calcpad-desktop';
const RUNTIME = process.env.CALCPAD_QA_RUNTIME ?? process.env.XDG_RUNTIME_DIR;
const FIX = join(HERE, 'fixtures');
const WORK = process.env.CALCPAD_QA_WORK ?? join(tmpdir(), 'calcpad-control-qa');
const DISPLAY = process.env.CALCPAD_QA_DISPLAY ?? ':97';
if (!RUNTIME) throw new Error('set CALCPAD_QA_RUNTIME (or XDG_RUNTIME_DIR) to the running app runtime dir');
const ENV = { ...process.env, XDG_RUNTIME_DIR: RUNTIME };
const WORK_ROOT = resolve(WORK);
const TMP_ROOT = resolve(tmpdir());
if (WORK_ROOT === TMP_ROOT || !WORK_ROOT.startsWith(TMP_ROOT + sep)) {
    throw new Error(`CALCPAD_QA_WORK must be a directory below ${TMP_ROOT}: ${WORK_ROOT}`);
}

let passed = 0;
let failed = 0;
const failures = [];

function ctl(args, { expectOk = true } = {}) {
    const result = spawnSync(APP, ['--ctl', ...args], { env: ENV, encoding: 'utf8', timeout: 180000 });
    let json = null;
    try { json = JSON.parse(result.stdout); } catch { /* handled below */ }
    if (expectOk) {
        assert.equal(result.status, 0, `command failed: ${args.join(' ')}\n${result.stdout}${result.stderr}`);
        assert.ok(json, `no JSON from: ${args.join(' ')}\n${result.stdout}${result.stderr}`);
        assert.equal(json.ok, true, `not ok: ${args.join(' ')}\n${result.stdout}`);
        return json.result;
    }
    assert.ok(json, `expected JSON error from: ${args.join(' ')}\n${result.stdout}${result.stderr}`);
    assert.equal(json.ok, false, `expected failure: ${args.join(' ')}\n${result.stdout}`);
    return json;
}

function ctlRaw(args) {
    const full = args[0]?.startsWith('--ctl') ? args : ['--ctl', ...args];
    return spawnSync(APP, full, { env: ENV, encoding: 'utf8', timeout: 180000 });
}

const call = (...args) => ctl(args);
const fail = (...args) => ctl(args, { expectOk: false });
const failRaw = (args, status = 2) => {
    const result = ctlRaw(args);
    assert.equal(result.status, status, `expected exit ${status}: ${args.join(' ')}\n${result.stdout}${result.stderr}`);
    return result;
};
const state = () => call('state');
const open = path => call('open', path);
const close = (path, ...extra) => call('close', ...(path ? [path] : []), ...extra);
const read = path => readFileSync(path, 'utf8');
const bytes = path => readFileSync(path);
const exists = path => existsSync(path);

function stripHtml(html) {
    return html
        .replace(/<style[\s\S]*?<\/style>/gi, ' ')
        .replace(/<script[\s\S]*?<\/script>/gi, ' ')
        .replace(/<[^>]*>/g, ' ')
        .replace(/&ensp;|&hairsp;|&nbsp;/g, ' ')
        .replace(/&middot;/g, '·')
        .replace(/&times;/g, '×')
        .replace(/\s+/g, ' ');
}

function signature(path, expected) {
    const data = bytes(path);
    assert.ok(data.length > 0, `empty file: ${path}`);
    if (expected === 'pdf') assert.equal(data.subarray(0, 5).toString(), '%PDF-', `not a PDF: ${path}`);
    if (expected === 'zip' || expected === 'docx') {
        assert.equal(data.subarray(0, 2).toString(), 'PK', `not a ZIP container: ${path}`);
    }
    if (expected === 'png') assert.equal(data.subarray(1, 4).toString(), 'PNG', `not a PNG: ${path}`);
    if (expected === 'html') assert.match(data.toString('utf8', 0, 200), /<!doctype html|<html/i, `not an HTML document: ${path}`);
    if (expected === 'svg') assert.match(data.toString('utf8', 0, 200), /<svg|<\?xml/i, `not an SVG: ${path}`);
    return data;
}

async function test(name, fn) {
    try {
        await fn();
        passed += 1;
        console.log(`PASS control: ${name}`);
    } catch (error) {
        failed += 1;
        failures.push({ name, error });
        console.log(`FAIL control: ${name}\n     ${String(error.message ?? error).split('\n')[0]}`);
    }
}

async function waitFor(check, description, timeoutMs = 30000) {
    const deadline = Date.now() + timeoutMs;
    while (Date.now() < deadline) {
        if (await check()) return;
        await new Promise(resolve => setTimeout(resolve, 250));
    }
    throw new Error(`timed out: ${description}`);
}

function resetWindows() {
    for (const window of call('windows').windows) {
        if (window.label !== 'main') call(`--window=${window.label}`, 'window', 'destroy');
    }
}

function resetTabs() {
    for (let attempt = 0; attempt < 40; attempt += 1) {
        const current = state();
        const tabs = current.groups.flatMap(group => group.tabs);
        if (tabs.length === 1 && !tabs[0].filePath && !tabs[0].dirty) return;
        const file = tabs.find(tab => tab.filePath);
        if (file) {
            close(file.filePath, '--force');
            continue;
        }
        if (current.groups.length > 1) {
            call('menu', 'unsplit-editor');
            continue;
        }
        const untitled = tabs.find(tab => !tab.filePath);
        if (!untitled) throw new Error('could not find a tab to close');
        call('eval', `window.calcpadTabs.activate(${JSON.stringify(untitled.id)})`);
        close(undefined, '--force');
    }
    throw new Error('could not reset tabs');
}

async function waitForApp() {
    for (let attempt = 0; attempt < 120; attempt += 1) {
        const result = spawnSync(APP, ['--ctl', 'ping'], { env: ENV, encoding: 'utf8' });
        if (result.status === 0) return;
        await new Promise(resolve => setTimeout(resolve, 500));
    }
    throw new Error(`app did not answer at ${RUNTIME}`);
}

function ctlAsync(args) {
    return new Promise(resolvePromise => {
        const child = spawn(APP, ['--ctl', ...args], { env: ENV });
        let stdout = '';
        let stderr = '';
        child.stdout.on('data', chunk => { stdout += chunk; });
        child.stderr.on('data', chunk => { stderr += chunk; });
        child.on('close', status => resolvePromise({ status, stdout, stderr }));
    });
}

async function main() {
    console.log('CalcpadCE control-channel QA suite');
    console.log(`  app: ${APP}`);
    console.log(`  runtime: ${RUNTIME}`);
    rmSync(WORK, { recursive: true, force: true });
    mkdirSync(WORK, { recursive: true });
    for (const name of ['basic.cpd', 'plot.cpd', 'ui.cpd', 'include-parent.cpd', 'include-child.cpd', 'write.cpd', 'errors.cpd', 'unicode.cpd', 'editor.cpd', 'ui2.cpd', 'complex.cpd', 'messy.cpd', 'headings.cpd', 'meta.cpd']) {
        copyFileSync(join(FIX, name), join(WORK, name));
    }
    await test('preflight and clean state', async () => {
        await waitForApp();
        resetWindows();
        resetTabs();
        call('menu', 'result-mode:preview');
        if (!state().previewVisible) call('menu', 'toggle-preview');
    });

    await test('ping', () => {
        const p = call('ping');
        assert.ok(typeof p.version === 'string' && p.version.length > 0);
    });

    await test('windows lists main', () => {
        const w = call('windows');
        assert.ok(w.windows.some(window => window.label === 'main'));
    });

    await test('state has QA fields', () => {
        const s = state();
        assert.equal(s.window, 'main');
        assert.ok(s.windowTitle.includes('CalcpadCE'));
        assert.equal(typeof s.sidebarVisible, 'boolean');
        assert.ok(['on', 'off'].includes(s.wordWrap));
        assert.match(s.serverUrl, /^http:\/\/127\.0\.0\.1:\d+$/);
    });

    await test('open basic + content + preview result', () => {
        const basic = join(WORK, 'basic.cpd');
        open(basic);
        const s = state();
        assert.equal(s.activeFile, basic);
        assert.equal(s.activeTitle, 'basic.cpd');
        const c = call('content');
        assert.match(c.content, /sqrt\(a\^2 \+ b\^2\)/);
        call('refresh');
        const r = call('result');
        const text = stripHtml(r.html);
        assert.match(text, /= 5/);
        assert.match(text, /6 m/);
        assert.match(text, /-2/);
        assert.equal(r.resultMode, 'preview');
    });

    await test('set-content + save round trip', () => {
        const basic = join(WORK, 'basic.cpd');
        const content = "' QA edited\nalpha = 7\nalpha^2\n";
        writeFileSync(join(WORK, 'edit.txt'), content);
        const r = call('set-content', join(WORK, 'edit.txt'), basic);
        assert.equal(r.dirty, true);
        call('save');
        assert.match(read(basic), /alpha = 7/);
        assert.equal(state().dirty, false);
        const c = call('content');
        assert.match(c.content, /alpha = 7/);
    });

    await test('save-as without extension', () => {
        const target = join(WORK, 'copia');
        const r = call('save-as', target);
        assert.equal(r.path, `${target}.cpd`);
        assert.ok(exists(`${target}.cpd`));
        assert.equal(state().activeFile, `${target}.cpd`);
        open(join(WORK, 'basic.cpd'));
    });

    await test('reload from disk and dirty guard', () => {
        const copy = join(WORK, 'copia.cpd');
        open(copy);
        writeFileSync(copy, "' from disk\nr = 99\nr\n");
        call('reload');
        assert.match(call('content').content, /from disk/);
        writeFileSync(join(WORK, 'dirty.txt'), "' dirty\nr = 1\n");
        call('set-content', join(WORK, 'dirty.txt'), copy);
        assert.equal(fail('reload').error !== undefined, true);
        call('reload', '--force');
        assert.match(call('content').content, /from disk/);
        assert.equal(state().dirty, false);
        close(copy);
    });

    await test('close by path and dirty guard', () => {
        const copy = join(WORK, 'copia.cpd');
        open(copy);
        writeFileSync(join(WORK, 'dirty2.txt'), "' dirty2\nq = 2\n");
        call('set-content', join(WORK, 'dirty2.txt'), copy);
        const error = fail('close', copy);
        assert.match(error.error, /unsaved changes/);
        call('close', copy, '--force');
        assert.equal(state().groups.flatMap(group => group.tabs).some(tab => tab.filePath === copy), false);
        assert.equal(state().dirty, false);
    });

    await test('export html/pdf/docx (report)', () => {
        open(join(WORK, 'basic.cpd'));
        const html = join(WORK, 'out.html');
        const pdf = join(WORK, 'out.pdf');
        const docx = join(WORK, 'out.docx');
        call('export', 'html', html);
        call('export', 'pdf', pdf);
        call('export', 'docx', docx);
        assert.match(read(html), /CalcpadCE/);
        signature(html, 'html');
        signature(pdf, 'pdf');
        signature(docx, 'docx');
    });

    await test('export variants and guards', () => {
        const variants = ['report', 'preview', 'unwrapped'];
        for (const variant of variants) {
            const target = join(WORK, `variant-${variant}.html`);
            call('export', 'html', target, variant);
            assert.ok(exists(target), `missing HTML variant ${variant}`);
        }
        const input = join(WORK, 'variant-input.html');
        call('export', 'html', input, 'input');
        signature(input, 'html');
        fail('export', 'docx', join(WORK, 'invalid.docx'), 'input');
        failRaw(['export', 'nope', join(WORK, 'nope.bin')]);
    });

    await test('export compiled and portable', () => {
        const cpdz = join(WORK, 'basic.cpdz');
        const zip = join(WORK, 'basic.zip');
        call('export', 'cpdz', cpdz);
        call('export', 'zip', zip);
        assert.ok(bytes(cpdz).length > 0, 'empty compiled worksheet');
        signature(zip, 'zip');
        open(cpdz);
        assert.equal(state().activeFile, cpdz);
        assert.equal(state().resultMode, 'ui');
        open(join(WORK, 'basic.cpd'));
    });

    await test('plots list/save/zip', () => {
        open(join(WORK, 'plot.cpd'));
        const plots = call('plots');
        assert.ok(plots.plots.length >= 1);
        const plot = plots.plots[0];
        const image = join(WORK, `plot.${plot.ext}`);
        const saved = call('save-plot', String(plot.index), image);
        assert.equal(saved.path, image);
        signature(image, plot.ext);
        const zip = join(WORK, 'plots.zip');
        call('save-plots', zip);
        signature(zip, 'zip');
    });

    await test('write/append runs files', () => {
        open(join(WORK, 'write.cpd'));
        const result = call('message', JSON.stringify({ type: 'writeFilesNow' }), '--wait=writeFilesResult');
        assert.equal(result.ok, true);
        assert.ok(exists(join(WORK, 'out.csv')), 'out.csv was not written');
        assert.deepEqual(read(join(WORK, 'out.csv')).trim().split(/\r?\n/), ['1', '2', '3']);
    });

    await test('include chain', () => {
        open(join(WORK, 'include-parent.cpd'));
        call('refresh');
        assert.match(stripHtml(call('result').html), /15/);
    });

    await test('errors are reported without crashing', () => {
        open(join(WORK, 'errors.cpd'));
        const r = call('result');
        assert.match(stripHtml(r.html), /Error/);
        assert.equal(state().dirty, false);
    });

    await test('UI controls and overrides', () => {
        open(join(WORK, 'ui.cpd'));
        const controls = call('message', JSON.stringify({ type: 'getUiControls' }), '--wait=uiControls');
        assert.ok(controls.controls.some(control => control.key === 'x:1'));
        call('message', JSON.stringify({ type: 'updateUiOverrides', overrides: { 'x:1': '6' } }));
        try {
            call('menu', 'result-mode:ui');
            call('refresh');
            const form = call('result');
            assert.match(stripHtml(form.html), /Input x/);
            call('menu', 'result-mode:report');
            call('refresh');
            const report = call('result');
            assert.match(stripHtml(report.html), /36/);
        } finally {
            call('menu', 'result-mode:preview');
        }
    });

    await test('variables through bridge message', () => {
        open(join(WORK, 'ui.cpd'));
        const vars = call('message', JSON.stringify({ type: 'getVariables' }), '--wait=updateVariables');
        assert.ok(vars.data.variables.some(variable => variable.name === 'x'));
        assert.ok(vars.data.variables.some(variable => variable.name === 'y'));
    });

    await test('settings through bridge message', () => {
        const settings = call('message', JSON.stringify({ type: 'getSettings' }), '--wait=settingsResponse');
        assert.ok(settings.settings.math);
        try {
            const updated = {
                ...settings.settings,
                math: { ...settings.settings.math, decimals: 4 },
            };
            call('message', JSON.stringify({ type: 'updateSettings', settings: updated }));
            const after = call('message', JSON.stringify({ type: 'getSettings' }), '--wait=settingsResponse');
            assert.equal(after.settings.math.decimals, 4);
        } finally {
            call('message', JSON.stringify({ type: 'updateSettings', settings: { math: settings.settings.math } }));
        }
        const restored = call('message', JSON.stringify({ type: 'getSettings' }), '--wait=settingsResponse');
        assert.equal(restored.settings.math.decimals, settings.settings.math.decimals);
    });

    await test('menu toggles and split', () => {
        call('menu', 'result-mode:preview');
        if (!state().previewVisible) call('menu', 'toggle-preview');
        if (state().groups.length > 1) call('menu', 'unsplit-editor');
        const before = state();
        call('menu', 'toggle-sidebar');
        assert.notEqual(state().sidebarVisible, before.sidebarVisible);
        call('menu', 'toggle-sidebar');
        call('menu', 'toggle-preview');
        assert.notEqual(state().previewVisible, before.previewVisible);
        call('menu', 'toggle-preview');
        call('menu', 'toggle-word-wrap');
        assert.notEqual(state().wordWrap, before.wordWrap);
        call('menu', 'toggle-word-wrap');
        call('menu', 'split-editor');
        assert.ok(state().groups.length >= 2);
        call('menu', 'unsplit-editor');
        assert.equal(state().groups.length, 1);
        for (const mode of ['unwrapped', 'ui', 'report', 'preview']) {
            call('menu', `result-mode:${mode}`);
            assert.equal(state().resultMode, mode);
        }
    });

    await test('menu new and close-tab', () => {
        const before = state().groups[0].tabs.length;
        call('menu', 'new');
        assert.equal(state().groups[0].tabs.length, before + 1);
        call('menu', 'close-tab');
        assert.equal(state().groups[0].tabs.length, before);
    });

    await test('menu open recent', () => {
        const file = join(WORK, 'unicode.cpd');
        open(file);
        close(file);
        assert.equal(state().activeFile !== file, true);
        call('menu', `open-recent:${file}`);
        assert.equal(state().activeFile, file);
    });

    await test('menu insert/undo/redo', () => {
        const file = join(WORK, 'basic.cpd');
        open(file);
        call('message', JSON.stringify({ type: 'insertText', text: 'undo_marker = 123\n' }));
        assert.match(call('content').content, /undo_marker/);
        call('eval', 'window.calcpadActiveEditor.focus()');
        call('menu', 'undo');
        assert.doesNotMatch(call('content').content, /undo_marker/);
        call('menu', 'redo');
        assert.match(call('content').content, /undo_marker/);
    });

    await test('editor commands', () => {
        const file = join(WORK, 'editor.cpd');
        open(file);
        call('editor', 'goto', '2', '1');
        assert.equal(call('editor', 'info').position.line, 2);
        call('editor', 'select', '1', '1', '1', '4');
        call('editor', 'insert', 'ONE');
        assert.match(call('content').content, /^ONE = 1/);
        call('editor', 'undo');
        assert.match(call('content').content, /^one = 1/);
        call('editor', 'redo');
        assert.match(call('content').content, /^ONE = 1/);
        const found = call('editor', 'find', 'two');
        assert.equal(found.count, 1);
        assert.equal(found.matches[0].startLine, 2);
        const replaced = call('editor', 'replace-all', 'two', 'TWO');
        assert.equal(replaced.replaced, 1);
        assert.match(call('content').content, /TWO = 2/);
        call('editor', 'focus');
    });

    await test('insert tab data and insertText', () => {
        const data = call('message', JSON.stringify({ type: 'getInsertData' }), '--wait=insertDataResponse');
        assert.ok(Array.isArray(data.items) && data.items.length > 0);
        open(join(WORK, 'editor.cpd'));
        call('editor', 'goto', '1', '1');
        call('message', JSON.stringify({ type: 'insertText', text: 'inserted = 1\n' }));
        assert.match(call('content').content, /inserted/);
    });

    await test('headings bridge message', () => {
        open(join(WORK, 'headings.cpd'));
        const headings = call('message', JSON.stringify({ type: 'getHeadings' }), '--wait=updateHeadings');
        assert.ok(Array.isArray(headings.headings));
    });

    await test('prettify document', async () => {
        open(join(WORK, 'messy.cpd'));
        call('message', JSON.stringify({ type: 'prettifyDocument' }));
        await waitFor(() => { const text = call('content').content; return /#for i = 1 : 3\n\s+a=1\n#loop/.test(text) && !/a=1   / .test(text); }, 'prettified content');
    });

    await test('PDF settings bridge message', () => {
        const pdf = call('message', JSON.stringify({ type: 'getPdfSettings' }), '--wait=pdfSettingsResponse');
        assert.ok(pdf.settings && typeof pdf.settings === 'object');
    });

    await test('read directory bridge message', () => {
        const dir = call('message', JSON.stringify({ type: 'readDirectory', path: WORK }), '--wait=folderContents');
        assert.ok(dir.entries.some(entry => entry.name === 'basic.cpd'));
    });

    await test('document metadata bridge messages', () => {
        open(join(WORK, 'meta.cpd'));
        const metadata = call('message', JSON.stringify({ type: 'getDocumentMetadata' }), '--wait=documentMetadata');
        assert.equal(metadata.hasDocument, true);
        assert.equal(metadata.info.title, 'Meta QA');
        call('message', JSON.stringify({ type: 'updateFileInfo', patch: { title: 'Meta QA 2' } }));
        assert.match(call('content').content, /Meta QA 2/);
    });

    await test('multiple UI controls and overrides', () => {
        open(join(WORK, 'ui2.cpd'));
        const controls = call('message', JSON.stringify({ type: 'getUiControls' }), '--wait=uiControls');
        const keys = controls.controls.map(control => control.key);
        assert.ok(keys.includes('a:1'));
        assert.ok(keys.includes('b:1'));
        call('message', JSON.stringify({ type: 'updateUiOverrides', overrides: { 'a:1': '10', 'b:1': '20' } }));
        try {
            call('menu', 'result-mode:report');
            call('refresh');
            assert.match(stripHtml(call('result').html), /30/);
        } finally {
            call('menu', 'result-mode:preview');
        }
    });

    await test('complex mode via settings', () => {
        const settings = call('message', JSON.stringify({ type: 'getSettings' }), '--wait=settingsResponse');
        const original = settings.settings.math;
        try {
            call('message', JSON.stringify({
                type: 'updateSettings',
                settings: { ...settings.settings, math: { ...original, isComplex: true } },
            }));
            open(join(WORK, 'complex.cpd'));
            call('refresh');
            assert.match(stripHtml(call('result').html), /5/);
        } finally {
            call('message', JSON.stringify({ type: 'updateSettings', settings: { math: original } }));
        }
    });

    await test('settings entry and output panel menu actions', () => {
        call('menu', 'go-to-settings');
        const sidebar = call('eval', 'document.querySelector(".sidebar-pane")?.innerText ?? ""').value;
        assert.match(sidebar, /Settings/);
        call('menu', 'refresh');
        call('menu', 'show-server-log');
    });

    await test('multi-window targeting', async () => {
        call('menu', 'new-window');
        await waitFor(() => call('windows').windows.length >= 2, 'second window');
        const windows = call('windows').windows;
        const other = windows.find(window => window.label !== 'main').label;
        const otherState = call(`--window=${other}`, 'state');
        assert.equal(otherState.window, other);
        const mainState = call('--window=main', 'state');
        assert.equal(mainState.window, 'main');
        fail('--window=does-not-exist', 'state');
        call(`--window=${other}`, 'window', 'destroy');
        await waitFor(() => call('windows').windows.length === 1, 'extra window closed');
    });

    await test('window controls', () => {
        call('--window=main', 'window', 'hide');
        assert.equal(call('windows').windows.find(window => window.label === 'main').visible, false);
        call('--window=main', 'window', 'show');
        assert.equal(call('windows').windows.find(window => window.label === 'main').visible, true);
        call('--window=main', 'window', 'minimize');
        call('--window=main', 'window', 'maximize');
        call('--window=main', 'window', 'unmaximize');
    });

    await test('clipboard and menu clipboard actions', () => {
        call('clipboard', 'write', 'abc');
        assert.equal(call('clipboard', 'read').text, 'abc');
        copyFileSync(join(FIX, 'editor.cpd'), join(WORK, 'clip.cpd'));
        open(join(WORK, 'clip.cpd'));
        call('editor', 'focus');
        call('editor', 'select', '1', '1', '1', '4');
        call('menu', 'copy');
        assert.equal(call('clipboard', 'read').text, 'one');
        call('menu', 'cut');
        assert.doesNotMatch(call('content').content, /^one = 1/m);
        call('editor', 'undo');
        assert.match(call('content').content, /^one = 1/m);
        call('menu', 'select-all');
        call('menu', 'copy');
        assert.ok(call('clipboard', 'read').text.length > 0);
        call('clipboard', 'write', 'ZZZ');
        call('editor', 'goto', '2', '1');
        call('menu', 'paste');
        assert.match(call('content').content, /^ZZZtwo = 2/m);
    });

    await test('menu find and replace open their widgets', async () => {
        call('editor', 'focus');
        call('menu', 'find');
        await waitFor(() => call('eval',
            '!!document.querySelector(".find-widget") && getComputedStyle(document.querySelector(".find-widget")).display !== "none"'
        ).value, 'find widget');
        call('editor', 'focus');
        call('menu', 'replace');
        await waitFor(() => call('eval',
            'document.querySelector(".find-widget")?.classList.contains("replaceToggled") === true'
        ).value, 'replace widget');
        call('eval', 'window.calcpadActiveEditor.trigger("control", "closeFindWidget", null)');
    });

    await test('server stop and restart', async () => {
        open(join(WORK, 'basic.cpd'));
        const before = state().serverUrl;
        call('menu', 'stop-server');
        await waitFor(async () => {
            const result = await ctl(['eval',
                `fetch(${JSON.stringify(before)}).then(() => "up").catch(() => "down")`]);
            return result.value === 'down';
        }, 'server down');
        call('menu', 'restart-server');
        await waitFor(async () => {
            const current = state().serverUrl;
            if (!current) return false;
            const result = await ctl(['eval',
                `fetch(${JSON.stringify(current)}).then(() => "up").catch(() => "down")`]);
            return result.value === 'up';
        }, 'server up again', 60000);
    });

    await test('screenshot of the rendered window', () => {
        if (spawnSync('import', ['-version'], { encoding: 'utf8' }).status !== 0) {
            console.log('SKIP screenshot: ImageMagick import is not available');
            return;
        }
        const image = join(WORK, 'screen.png');
        const result = spawnSync('import', ['-display', DISPLAY, '-window', 'root', image], { encoding: 'utf8' });
        assert.equal(result.status, 0, result.stderr);
        signature(image, 'png');
    });

    await test('path with spaces and unicode', () => {
        const name = join(WORK, 'file with space naïve café.cpd');
        writeFileSync(name, "' space path\nsp = 3\nsp\n");
        open(name);
        assert.equal(state().activeFile, name);
        call('save');
        assert.match(read(name), /space path/);
        close(name);
    });

    await test('large content over control channel', () => {
        const file = join(WORK, 'grande.cpd');
        const line = `v${'0'.repeat(40)} = ${'1'.repeat(200)}\n`;
        const content = "' large\n" + line.repeat(1000);
        writeFileSync(join(WORK, 'grande.txt'), content);
        call('set-content', join(WORK, 'grande.txt'), file);
        call('save');
        const c = call('content');
        assert.equal(c.content.length >= content.length, true);
        close(file);
    });

    await test('concurrent state calls', async () => {
        const results = await Promise.all(Array.from({ length: 5 }, () => ctlAsync(['state'])));
        for (const result of results) {
            assert.equal(result.status, 0);
            assert.equal(JSON.parse(result.stdout).ok, true);
        }
    });

    await test('error paths', () => {
        fail('open', join(WORK, 'does-not-exist.cpd'));
        call('menu', 'new');
        fail('save');
        fail('close', join(WORK, 'does-not-exist.cpd'));
        fail('reload');
        fail('content', join(WORK, 'does-not-exist.cpd'));
        failRaw(['set-content', join(WORK, 'does-not-exist.txt')]);
        fail('export', 'html', '/proc/cannot-write.html');
        failRaw(['--ctl-json', '{']);
        failRaw(['open']);
        const noApp = spawnSync(APP, ['--ctl', 'ping'], {
            env: { ...ENV, XDG_RUNTIME_DIR: join(WORK, 'runtime-vazio') },
            encoding: 'utf8',
        });
        assert.notEqual(noApp.status, 0);
        assert.equal(JSON.parse(noApp.stdout).ok, false);
    });

    await test('eval gate and functionality', () => {
        const value = call('eval', 'window.calcpadTabs ? 42 : 0');
        assert.equal(value.value, 42);
    });

    // Leave a clean preview state for the next run.
    call('menu', 'result-mode:preview');
}

main().then(() => {
    console.log(`\nControl-channel checks: ${passed} passed, ${failed} failed`);
    if (failed) {
        for (const failure of failures) console.log(`\n${failure.name}\n${failure.error.stack ?? failure.error}`);
        process.exit(1);
    }
}).catch(error => {
    console.error(error);
    process.exit(1);
});
