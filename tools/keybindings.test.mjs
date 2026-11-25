import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';
import {
    hotkeyFromEvent, normalizeHotkey, toPortableHotkey, toVSCodeKey, validateHotkey,
} from '../Calcpad.Web/frontend/calcpad-frontend/src/services/hotkeys.ts';
import { customSnippetFromDefinition, snippetDraftAt, snippetDraftFromText } from '../Calcpad.Web/frontend/calcpad-frontend/src/services/customizations.ts';

const readJson = path => JSON.parse(readFileSync(new URL(path, import.meta.url), 'utf8'));
const defaults = readJson('../Calcpad.Web/frontend/calcpad-frontend/src/defaults/settings.default.json').extras;
const packageJson = readJson('../Calcpad.Web/frontend/vscode-calcpad/package.json');

const VSCODE_COMMANDS = {
    insertSearch: 'vscode-calcpad.showInsert',
    runPreview: 'calcpad.runPreview',
    editMetadata: 'vscode-calcpad.editMetadataProperties',
};

test('package.json keybindings match the settings.default.json defaults', () => {
    for (const [id, hotkey] of Object.entries(defaults.keybindings)) {
        const command = VSCODE_COMMANDS[id] ?? 'vscode-calcpad.' + id;
        const entry = packageJson.contributes.keybindings.find(kb => kb.command === command);
        assert.ok(entry, `${command} has no package.json keybinding`);
        assert.equal(normalizeHotkey(entry.key, false), normalizeHotkey(hotkey, false), `${command} key`);
        assert.equal(normalizeHotkey(entry.mac ?? entry.key, true), normalizeHotkey(hotkey, true), `${command} mac`);
    }
});

test('default quick types are short enough to be typed', () => {
    for (const [key, text] of Object.entries(defaults.quickTypes)) {
        assert.ok(key.length >= 1 && key.length <= 4 && !/[\s~]/.test(key), `~${key}`);
        assert.ok(text, `~${key} has text`);
    }
});

test('hotkeys normalize across spellings and platforms', () => {
    assert.equal(normalizeHotkey('shift+ctrl+k', false), 'Ctrl+Shift+K');
    assert.equal(normalizeHotkey('Mod+Alt+X', true), 'Alt+Meta+X');
    assert.equal(normalizeHotkey('cmd+[KeyB]', true), 'Meta+B');
    assert.equal(normalizeHotkey('ctrl+k ctrl+c', false), 'Ctrl+K');
    assert.equal(normalizeHotkey('ctrl+shift+=', false), 'Ctrl+Shift+=');
    assert.equal(toPortableHotkey('Ctrl+Shift+K', false), 'Mod+Shift+K');
    assert.equal(toPortableHotkey('Shift+Alt+Meta+K', true), 'Mod+Shift+Alt+K');
    assert.equal(normalizeHotkey(toPortableHotkey('Shift+Alt+Meta+K', true), true), 'Shift+Alt+Meta+K');
    assert.equal(toVSCodeKey('Ctrl+Alt+Meta+F5', 'win32'), 'ctrl+alt+win+f5');
});

test('hotkeys come from physical keys and need a command modifier', () => {
    const event = { code: 'KeyQ', ctrlKey: true, shiftKey: true, altKey: false, metaKey: false };
    assert.equal(hotkeyFromEvent(event), 'Ctrl+Shift+Q');
    assert.equal(hotkeyFromEvent({ ...event, code: 'ShiftLeft' }), null);
    assert.equal(validateHotkey('Shift+K') !== null, true);
    assert.equal(validateHotkey('F7'), null);
    assert.equal(validateHotkey('Alt+K'), null);
});

test('functions and macros save as snippets with parameters and metadata', () => {
    const fn = customSnippetFromDefinition('function', {
        name: 'f', params: 'x; y = 2', paramTypes: ['vector', 'any'], paramDescriptions: ['Length'],
        definition: 'x + y', returnType: 'Unknown',
    });
    assert.deepEqual(fn, {
        label: 'f(x; y)',
        insert: 'f(§; §)',
        keywordType: 'Function',
        description: 'f(x; y = 2) = x + y',
        example: 'f(x; y = 2) = x + y',
        parameters: [
            { name: 'x', type: 'vector', description: 'Length' },
            { name: 'y', isOptional: true },
        ],
    });
    const described = customSnippetFromDefinition('function', { name: 'g', params: '', description: 'Area', returnType: 'Value' });
    assert.equal(described.insert, 'g()');
    assert.equal(described.description, 'Area');
    assert.equal(described.returnType, 'Value');
    const macro = customSnippetFromDefinition('macro', { name: 'm$', params: 'a$', definition: 'x = a$\ny = 2' });
    assert.equal(macro.insert, 'm$(§)');
    assert.equal(macro.keywordType, 'Macro');
    assert.equal(macro.example, '#def m$(a$)\nx = a$\ny = 2\n#end def');
    assert.equal(customSnippetFromDefinition('macro', { name: 'n$', definition: '5' }).example, '#def n$ = 5');
    const variable = customSnippetFromDefinition('variable', { name: 'L', type: 'Vector', definition: '[1; 2]' });
    assert.deepEqual([variable.insert, variable.keywordType, variable.returnType, variable.parameters], ['L', 'Variable', 'Vector', undefined]);
    assert.equal(customSnippetFromDefinition('customUnit', { name: 'kip', definition: '1000lbf' }).example, '.kip = 1000lbf');
});

test('editor right-click finds the definition by symbol or by word', () => {
    const defs = {
        macros: [{ name: 'm$', parameters: ['a$'], content: ['a$*2'], description: 'Double' }],
        functions: [{ name: 'f', parameters: ['x'], expression: 'x^2', returnType: 'Value' }],
        variables: [{ name: 'L', expression: '1', type: 'Value' }, { name: 'L', expression: '2', type: 'Vector' }],
        customUnits: [{ name: 'kip', expression: '1000lbf' }],
    };
    assert.equal(snippetDraftAt(defs, { symbolName: 'f', kind: 'function' }, 'f').insert, 'f(§)');
    assert.equal(snippetDraftAt(defs, null, 'm').insert, 'm$(§)');
    assert.equal(snippetDraftAt(defs, null, 'L').returnType, 'Vector');
    assert.equal(snippetDraftAt(defs, null, 'kip').keywordType, 'Unit');
    assert.equal(snippetDraftAt(defs, null, 'nothing'), null);
});

test('selected text saves as a Text snippet named after its first line', () => {
    assert.deepEqual(snippetDraftFromText('  a = 1\r\nb = 2'), { label: 'a = 1', insert: '  a = 1\nb = 2' });
    assert.equal(snippetDraftFromText('x'.repeat(50)).label, 'x'.repeat(39) + '…');
    assert.equal(snippetDraftFromText('  \n '), null);
});
