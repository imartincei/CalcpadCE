#!/usr/bin/env node
// WCAG AA contrast gate for the UI tokens and the Monaco editor themes:
// 4.5:1 for text, 3:1 for control borders and icons.
//
// Colours are checked against the surface they actually sit on rather than
// against white, which changes the verdict -- the light inactive tab sits on
// #ececec, where #808080 scores 3.34:1 rather than 3.95:1.
//
// Usage: node scripts/check-contrast.mjs

import { readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const VARS = resolve(root, 'calcpad-web/src/editor/vscode-variables.css');
const THEME = resolve(root, 'calcpad-web/src/editor/theme.ts');
const TEMPLATE = resolve(root, '../backend/template.html');

const TEXT = 4.5;
const NONTEXT = 3.0;

function channel(c) {
    const v = c / 255;
    return v <= 0.04045 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4);
}

function luminance(hex) {
    const h = hex.replace('#', '');
    const [r, g, b] = [0, 2, 4].map(i => parseInt(h.slice(i, i + 2), 16));
    return 0.2126 * channel(r) + 0.7152 * channel(g) + 0.0722 * channel(b);
}

function ratio(fg, bg) {
    const [a, b] = [luminance(fg), luminance(bg)];
    return (Math.max(a, b) + 0.05) / (Math.min(a, b) + 0.05);
}

/** Flatten `fg` at alpha `a` over opaque `bg` so rgba() tokens can be checked. */
function flatten(fg, a, bg) {
    const [f, b] = [fg, bg].map(h => [0, 2, 4].map(i => parseInt(h.replace('#', '').slice(i, i + 2), 16)));
    return '#' + f.map((v, i) => Math.round(v * a + b[i] * (1 - a)).toString(16).padStart(2, '0')).join('');
}

/** Bare `:root` is the dark block; `:root[data-theme="light"]` is the light one. */
function readTokenBlocks() {
    const css = readFileSync(VARS, 'utf8');
    const blocks = {};
    for (const [name, selector] of [['dark', ':root {'], ['light', ':root[data-theme="light"] {']]) {
        const start = css.indexOf(selector);
        if (start < 0) throw new Error(`could not find ${selector} in ${VARS}`);
        const body = css.slice(start + selector.length, css.indexOf('\n}', start));
        const map = {};
        for (const m of body.matchAll(/(--[\w-]+):\s*([^;]+);/g)) map[m[1]] = m[2].trim();
        blocks[name] = map;
    }
    return blocks;
}

function readMonacoThemes() {
    const ts = readFileSync(THEME, 'utf8');
    const out = {};
    for (const [name, marker] of [['dark', 'calcpadDarkTheme'], ['light', 'calcpadLightTheme']]) {
        const start = ts.indexOf(marker);
        if (start < 0) throw new Error(`could not find ${marker} in ${THEME}`);
        const end = ts.indexOf(marker === 'calcpadDarkTheme' ? 'calcpadLightTheme' : '\n// EOF', start + 1);
        const body = ts.slice(start, end < 0 ? undefined : end);
        const rules = [...body.matchAll(/token:\s*'([^']+)'\s*,\s*foreground:\s*'([0-9a-fA-F]{6})'/g)]
            .map(m => [m[1], '#' + m[2]]);
        const bg = body.match(/'editor\.background':\s*'(#[0-9a-fA-F]{6})'/);
        out[name] = { rules, background: bg ? bg[1] : (name === 'dark' ? '#1e1e1e' : '#ffffff') };
    }
    return out;
}

/**
 * The `.code` palette of the unwrapped listing, which must be the Monaco palette verbatim --
 * bare `.code .x` is light, `.dark-theme .code .x` is dark.
 */
function readListingPalette() {
    const html = readFileSync(TEMPLATE, 'utf8');
    const out = { dark: { rules: [] }, light: { rules: [] } };
    for (const m of html.matchAll(/(\.dark-theme\s+)?\.code\s+\.([\w-]+)\s*\{\s*color:\s*(#[0-9a-fA-F]{6})/g))
        out[m[1] ? 'dark' : 'light'].rules.push([m[2], m[3]]);
    for (const [name, selector] of [['dark', '.dark-theme .code {'], ['light', '.code {']]) {
        const at = html.indexOf(selector);
        const bg = html.slice(at).match(/background-color:\s*(#[0-9a-fA-F]{6})/);
        out[name].background = bg[1];
    }
    return out;
}

const failures = [];
const checks = [];

function check(label, fg, bg, need) {
    if (!/^#[0-9a-fA-F]{6}$/.test(fg) || !/^#[0-9a-fA-F]{6}$/.test(bg)) {
        failures.push(`${label}: unparseable colour (fg=${fg} bg=${bg})`);
        return;
    }
    const r = ratio(fg, bg);
    checks.push({ label, fg, bg, r, need, pass: r >= need });
    if (r < need) failures.push(`${label}: ${r.toFixed(2)}:1 on ${bg} (needs ${need}:1)`);
}

const tokens = readTokenBlocks();

// Foreground token paired with the token naming the surface behind it.
const PAIRS = [
    ['--vscode-foreground', '--vscode-editor-background', TEXT],
    ['--vscode-descriptionForeground', '--vscode-editor-background', TEXT],
    ['--vscode-editor-foreground', '--vscode-editor-background', TEXT],
    ['--vscode-tab-activeForeground', '--vscode-tab-activeBackground', TEXT],
    ['--vscode-tab-inactiveForeground', '--vscode-tab-inactiveBackground', TEXT],
    ['--vscode-input-foreground', '--vscode-input-background', TEXT],
    ['--vscode-dropdown-foreground', '--vscode-dropdown-background', TEXT],
    ['--vscode-menu-foreground', '--vscode-menu-background', TEXT],
    ['--vscode-menu-selectionForeground', '--vscode-menu-selectionBackground', TEXT],
    ['--vscode-button-foreground', '--vscode-button-background', TEXT],
    ['--vscode-button-secondaryForeground', '--vscode-button-secondaryBackground', TEXT],
    ['--vscode-textLink-foreground', '--vscode-editor-background', TEXT],
    ['--vscode-panelTitle-activeForeground', '--vscode-panel-background', TEXT],
    ['--vscode-panelTitle-inactiveForeground', '--vscode-panel-background', TEXT],
    ['--vscode-sideBarSectionHeader-foreground', '--vscode-sideBar-background', TEXT],
    ['--vscode-badge-foreground', '--vscode-badge-background', TEXT],
    ['--vscode-editorWidget-foreground', '--vscode-editorWidget-background', TEXT],
    // Control affordances: borders and focus rings only need 3:1.
    ['--vscode-input-border', '--vscode-editor-background', NONTEXT],
    ['--vscode-checkbox-border', '--vscode-editor-background', NONTEXT],
    ['--vscode-dropdown-border', '--vscode-editor-background', NONTEXT],
    ['--vscode-focusBorder', '--vscode-editor-background', NONTEXT],
    // Container borders (menu, widget, panel) are omitted on purpose: those
    // surfaces are already set apart by their background fill, so the hairline
    // is decorative, unlike an input border.
];

for (const theme of ['dark', 'light']) {
    const t = tokens[theme];
    for (const [fgKey, bgKey, need] of PAIRS) {
        const fg = t[fgKey] ?? tokens.dark[fgKey];
        const bg = t[bgKey] ?? tokens.dark[bgKey];
        if (!fg || !bg) {
            failures.push(`${theme}: missing token ${!fg ? fgKey : bgKey}`);
            continue;
        }
        check(`${theme} ${fgKey}`, fg, bg, need);
    }
}

// The status bar is #007acc in both themes, so these are checked once against
// that blue. The pill darkens it by 35%.
const STATUS_BAR = '#007acc';
const PILL = flatten('#000000', 0.35, STATUS_BAR);
check('statusBar foreground', '#ffffff', STATUS_BAR, TEXT);
check('status pill connected', '#6bd66b', PILL, TEXT);
check('status pill connecting', '#ffcc66', PILL, TEXT);
check('status pill disconnected', '#ffb3b3', PILL, TEXT);
check('status icon lintError', '#ffc9c9', STATUS_BAR, NONTEXT);
check('status icon warning', '#ffd980', STATUS_BAR, NONTEXT);
check('status icon info', '#cfe8ff', STATUS_BAR, NONTEXT);

const monaco = readMonacoThemes();
for (const theme of ['dark', 'light']) {
    const { rules, background } = monaco[theme];
    if (!rules.length) failures.push(`${theme}: parsed no Monaco token rules from theme.ts`);
    for (const [token, fg] of rules) check(`${theme} monaco ${token}`, fg, background, TEXT);
}

// Monaco cannot read CSS variables, so theme.ts repeats the widget/input literals.
const WIDGET_KEYS = [
    ['focusBorder', '--vscode-focusBorder'],
    ['editorWidget.background', '--vscode-editorWidget-background'],
    ['editorWidget.foreground', '--vscode-editorWidget-foreground'],
    ['editorWidget.border', '--vscode-editorWidget-border'],
    ['editorHoverWidget.background', '--vscode-editorWidget-background'],
    ['editorHoverWidget.foreground', '--vscode-editorWidget-foreground'],
    ['editorHoverWidget.border', '--vscode-editorWidget-border'],
    ['editorSuggestWidget.background', '--vscode-editorWidget-background'],
    ['editorSuggestWidget.foreground', '--vscode-editorWidget-foreground'],
    ['editorSuggestWidget.border', '--vscode-editorWidget-border'],
    ['input.background', '--vscode-input-background'],
    ['input.foreground', '--vscode-input-foreground'],
    ['input.border', '--vscode-input-border'],
];
const themeSrc = readFileSync(THEME, 'utf8');
for (const theme of ['dark', 'light']) {
    const marker = theme === 'dark' ? 'calcpadDarkTheme' : 'calcpadLightTheme';
    const body = themeSrc.slice(themeSrc.indexOf(marker));
    for (const [key, cssVar] of WIDGET_KEYS) {
        const m = body.match(new RegExp(`'?${key.replace('.', '\\.')}'?:\\s*'(#[0-9a-fA-F]{6})'`));
        if (!m) {
            failures.push(`${theme} theme.ts: missing colors['${key}']`);
            continue;
        }
        const want = tokens[theme][cssVar] ?? tokens.dark[cssVar];
        if (m[1].toLowerCase() !== want.toLowerCase())
            failures.push(`${theme} theme.ts colors['${key}'] is ${m[1]}, but ${cssVar} is ${want}`);
    }
}

// The unwrapped listing must render the editor's palette, not a near-copy of it.
const listing = readListingPalette();
for (const theme of ['dark', 'light']) {
    const { rules, background } = listing[theme];
    const editor = Object.fromEntries(monaco[theme].rules);
    if (background !== monaco[theme].background)
        failures.push(`${theme} .code background is ${background}, but editor.background is ${monaco[theme].background}`);
    for (const [token, fg] of rules) {
        check(`${theme} listing ${token}`, fg, background, TEXT);
        if (editor[token] === undefined)
            failures.push(`${theme} listing .${token}: no such token in theme.ts`);
        else if (editor[token].toLowerCase() !== fg.toLowerCase())
            failures.push(`${theme} listing .${token} is ${fg}, but theme.ts has ${editor[token]}`);
    }
    for (const token of Object.keys(editor)) {
        if (!rules.some(([t]) => t === token))
            failures.push(`${theme} listing: template.html has no .${token} rule`);
    }
}

const width = checks.reduce((m, c) => Math.max(m, c.label.length), 0);
for (const c of checks) {
    const mark = c.pass ? 'ok  ' : 'FAIL';
    process.stdout.write(`${mark} ${c.label.padEnd(width)}  ${c.r.toFixed(2).padStart(5)}:1  (needs ${c.need})\n`);
}

process.stdout.write(`\n${checks.length} checks, ${failures.length} failing\n`);
if (failures.length) {
    process.stdout.write('\n' + failures.map(f => `  - ${f}`).join('\n') + '\n');
    process.exit(1);
}
