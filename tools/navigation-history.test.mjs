import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import ts from '../Calcpad.Web/frontend/calcpad-web/node_modules/typescript/lib/typescript.js';
import { test } from 'node:test';
import { NavigationHistory, navigationDirection } from '../Calcpad.Web/frontend/calcpad-web/src/editor/navigation-history.ts';

const location = (lineNumber, tabId = 'a', groupId = 'g0') => ({
    groupId, tabId, position: {lineNumber, column: 1},
});

test('history restores jumps across documents and groups, then branches after new navigation', () => {
    const history = new NavigationHistory();
    const visits = [location(1), location(4), location(30), location(8, 'b'), location(9, 'b', 'g1')];
    for (const visit of visits) history.record(visit);
    assert.deepEqual(history.go(-1), visits[3]);
    assert.deepEqual(history.go(-1), visits[2]);
    assert.deepEqual(history.go(-1), visits[1]);
    assert.equal(history.go(-1), undefined);
    assert.deepEqual(history.go(1), visits[2]);
    history.record(location(60));
    assert.equal(history.go(1), undefined);
    assert.deepEqual(history.go(-1), visits[2]);
});

test('explicit nearby jumps remain separate and closed tabs are skipped', () => {
    const history = new NavigationHistory();
    history.record(location(1));
    history.record(location(3), true);
    history.record(location(5, 'b'));
    history.record(location(7, 'c'));
    history.remove('g0', 'b');
    assert.deepEqual(history.go(-1), location(3));
    assert.deepEqual(history.go(-1), location(1));
    assert.deepEqual(history.go(1), location(3));
    history.remove('g0', 'a');
    assert.deepEqual(history.go(1), location(7, 'c'));
});

test('nearby movement is merged while a move of ten lines creates a stop', () => {
    const history = new NavigationHistory();
    history.record(location(1));
    for (let line = 2; line <= 20; line++) history.record(location(line));
    assert.equal(history.canGo(-1), false);
    history.record(location(29));
    assert.equal(history.canGo(-1), false);
    history.record(location(39));
    assert.deepEqual(history.go(-1), location(29));
});

test('same-line jumps update the column without adding a stop', () => {
    const history = new NavigationHistory();
    history.record(location(1));
    history.record(location(5), true);
    const moved = {...location(5), position: {lineNumber: 5, column: 8}};
    history.record(moved, true);
    assert.deepEqual(history.go(-1), location(1));
    assert.deepEqual(history.go(1), moved);
});

test('nearby movement preserves forward history and a new jump replaces it', () => {
    const history = new NavigationHistory();
    for (const line of [1, 20, 40]) history.record(location(line));
    history.go(-1);
    history.record(location(23));
    assert.deepEqual(history.go(1), location(40));
    assert.deepEqual(history.go(-1), location(23));
    history.record(location(25), true);
    assert.equal(history.canGo(1), false);
    assert.deepEqual(history.go(-1), location(23));
});

test('selections are compared by their first line and restored with their direction', () => {
    const history = new NavigationHistory();
    const selection = {selectionStartLineNumber: 5, selectionStartColumn: 2, positionLineNumber: 35, positionColumn: 4};
    const selected = {...location(35), position: {lineNumber: 35, column: 4}, selection};
    history.record(location(1));
    history.record(selected);
    assert.equal(history.canGo(-1), false);
    history.record(location(50));
    assert.deepEqual(history.go(-1), selected);
});

test('history retains the most recent fifty stops', () => {
    const history = new NavigationHistory();
    for (let line = 1; line <= 601; line += 10) history.record(location(line));
    let first;
    let steps = 0;
    while (history.canGo(-1)) {
        first = history.go(-1);
        steps++;
    }
    assert.equal(steps, 49);
    assert.deepEqual(first, location(111));
});

test('shortcuts match VS Code on each platform without capturing other modifier combinations', () => {
    const key = overrides => ({key: '-', code: 'Minus', ctrlKey: false, altKey: false,
        shiftKey: false, metaKey: false, ...overrides});
    assert.equal(navigationDirection(key({key: 'ArrowLeft', altKey: true}), 'Win32'), -1);
    assert.equal(navigationDirection(key({key: 'ArrowRight', altKey: true}), 'Win32'), 1);
    assert.equal(navigationDirection(key({ctrlKey: true, altKey: true}), 'Linux x86_64'), -1);
    assert.equal(navigationDirection(key({ctrlKey: true, shiftKey: true, key: '_'}), 'Linux x86_64'), 1);
    assert.equal(navigationDirection(key({ctrlKey: true}), 'MacIntel'), -1);
    assert.equal(navigationDirection(key({ctrlKey: true, shiftKey: true, key: '_'}), 'MacIntel'), 1);
    assert.equal(navigationDirection(key({key: 'ArrowLeft', altKey: true, shiftKey: true}), 'Win32'), 0);
    assert.equal(navigationDirection(key({key: 'ArrowLeft', altKey: true, ctrlKey: true}), 'Win32'), 0);
    assert.equal(navigationDirection(key({metaKey: true}), 'MacIntel'), 0);
    assert.equal(navigationDirection(key({ctrlKey: true, altKey: true, shiftKey: true}), 'Linux'), 0);
});

const dataModule = source => `data:text/javascript;base64,${Buffer.from(source).toString('base64')}`;
const editorSource = readFileSync(new URL('../Calcpad.Web/frontend/calcpad-web/src/editor/navigation-history.ts', import.meta.url), 'utf8');
const serviceSource = readFileSync(new URL('../Calcpad.Web/frontend/calcpad-web/src/services/navigation-history.ts', import.meta.url), 'utf8');
const transpile = source => ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.ESNext } }).outputText;
const serviceModule = transpile(serviceSource)
    .replace("'../editor/navigation-history'", JSON.stringify(dataModule(transpile(editorSource))))
    .replace(/import\('@tauri-apps\/api\/(?:core|event|window)'\)/g, 'Promise.resolve(globalThis.navigationHistoryTest)');
const { createDesktopNavigationHistory } = await import(dataModule(serviceModule));

test('desktop navigation accepts the next shortcut after restoration while the previous command is still returning', async t => {
    const started = Promise.withResolvers();
    const returned = Promise.withResolvers();
    const restored = Promise.withResolvers();
    const commands = [];
    let listener;
    globalThis.navigationHistoryTest = {
        invoke(command, args) {
            commands.push({ command, args });
            if (commands.length === 1) {
                started.resolve();
                return returned.promise;
            }
            return Promise.resolve();
        },
        listen(_event, handler) { listener = handler; },
        getCurrentWindow: () => ({ label: 'main' }),
    };
    t.after(() => {
        delete globalThis.navigationHistoryTest;
    });
    const history = await createDesktopNavigationHistory({
        confirm: async () => true,
        restore() { restored.resolve(); return true; },
    });

    const first = history.go(-1);
    await started.promise;
    const restoration = listener({ payload: { id: 1, location: {} } });
    await restored.promise;
    const second = history.go(1);
    returned.resolve();
    await Promise.all([first, restoration, second]);

    assert.deepEqual(commands.map(({ command }) => command), [
        'navigate_history', 'complete_navigation', 'navigate_history',
    ]);
    assert.equal(commands[2].args.direction, 1);
});
