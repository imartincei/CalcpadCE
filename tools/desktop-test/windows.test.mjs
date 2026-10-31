import assert from 'node:assert/strict';
import { readFileSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import { describe, test } from 'node:test';
import { TestApp, invoke, until } from './test-app.mjs';

async function dragTab(app, source, tabId, target, beforeId, {cancel = false, outside = false} = {}) {
    await app.setGeometry(source, {width: 1400, height: 700}, {x: 10, y: 10});
    if (target !== source) await app.setGeometry(target, {width: 1400, height: 700}, {x: 10, y: 10});
    const point = await app.evaluate(source, `(() => {
        const rect = document.querySelector('[data-tab-id="${tabId}"]').getBoundingClientRect();
        return {x: rect.left + 20, y: rect.top + rect.height / 2};
    })()`);
    const destination = outside ? null : await app.evaluate(target, `(() => {
        const element = ${beforeId ? `document.querySelector('[data-tab-id="${beforeId}"]')` : "document.querySelector('.tab-new')"};
        const rect = element.getBoundingClientRect();
        return {windowId: window.__TAURI_INTERNALS__.metadata.currentWindow.label,
            x: rect.left + 12, y: rect.top + rect.height / 2};
    })()`);
    // Native hit testing is simulated; pointer capture, DOM events and inter-window delivery are real.
    await app.evaluate(source, invoke('test_set_tab_drag_target', {target: destination}));
    await app.cdp(source, 'Input.dispatchMouseEvent', {type: 'mousePressed', ...point, button: 'left', buttons: 1, clickCount: 1});
    await app.cdp(source, 'Input.dispatchMouseEvent', {type: 'mouseMoved', x: point.x + 10, y: point.y, button: 'left', buttons: 1});
    if (cancel) await app.cdp(source, 'Input.dispatchKeyEvent', {type: 'keyDown', key: 'Escape', code: 'Escape', windowsVirtualKeyCode: 27});
    await app.cdp(source, 'Input.dispatchMouseEvent', {type: 'mouseReleased', x: point.x + 10, y: point.y, button: 'left', buttons: 0, clickCount: 1});
}

describe('desktop windows', {skip: process.platform !== 'win32', concurrency: false}, () => {
    test('Ctrl-click reuses includes and definitions in another window', {timeout: 90000}, async t => {
        const app = new TestApp();
        t.after(() => app.dispose());
        const target = app.fixture('shared definitions.cpd', "' Shared definitions\n\nshared = 42");
        const source = app.fixture('navigation source.cpd', `#include ${target.toUpperCase().replaceAll('\\', '/')}\nshared + 1`);
        const main = await app.start([source]);
        await app.fileOpen(main, source);
        await app.evaluate(main, invoke('plugin:window|set_size', {value: {Logical: {width: 1200, height: 700}}}));
        await until(() => app.evaluate(main, 'window.calcpadActiveEditor.getLayoutInfo().width > 300'), 'editor wide enough for Ctrl-click');
        const second = await app.newWindow(main);
        await app.evaluate(second.page, `window.dispatchEvent(new CustomEvent('calcpad-open-file', {
            detail: {path: ${JSON.stringify(target)}}
        }))`);
        await app.fileOpen(second.page, target);
        await app.evaluate(second.page, `window.calcpadActiveEditor.setValue("' Shared definitions\\n\\nshared = 42\\nunsaved = 7")`);
        const decoy = app.fixture('other worksheet.cpd');
        await app.evaluate(second.page, `window.dispatchEvent(new CustomEvent('calcpad-open-file', {
            detail: {path: ${JSON.stringify(decoy)}}
        }))`);
        await app.fileOpen(second.page, decoy);

        await app.focus(main, 'main');
        await app.ctrlClick(main, {lineNumber: 1, column: 12});
        await until(() => app.evaluate(second.page,
            `window.calcpadTabs.activeTab.filePath === ${JSON.stringify(target)}`), 'include activated in other window');
        assert.equal(await app.evaluate(main, 'window.calcpadTabs.count'), 1);
        assert.equal(await app.evaluate(main, 'window.calcpadTabs.activeTab.filePath'), source);
        assert.equal(await app.evaluate(second.page, 'window.calcpadTabs.activeTab.dirty'), true);
        assert.match(await app.evaluate(second.page, 'window.calcpadActiveEditor.getValue()'), /unsaved = 7/);
        await until(() => app.evaluate(second.page, 'document.hasFocus()'), 'existing webview focused');

        await app.evaluate(second.page, 'window.calcpadActiveEditor.setPosition({lineNumber: 1, column: 1})');
        await app.evaluate(second.page, invoke('plugin:window|minimize'));
        await app.focus(main, 'main');
        await app.ctrlClick(main, {lineNumber: 2, column: 3});
        await until(async () => (await app.evaluate(second.page, 'window.calcpadActiveEditor.getPosition()')).lineNumber === 3,
            'definition position restored in other window');
        assert.equal(await app.evaluate(second.page, invoke('plugin:window|is_minimized')), false);
        assert.equal(await app.evaluate(main, 'window.calcpadTabs.count'), 1);

        await app.evaluate(second.page, `window.calcpadBridge.confirmThreeWay = async () => 'no'`);
        await app.closeAndWait(second.page, second.label);
        await app.focus(main, 'main');
        await app.ctrlClick(main, {lineNumber: 2, column: 3});
        await until(() => app.evaluate(main, 'window.calcpadTabs.count === 2'), 'closed target opens locally');
        assert.equal(await app.evaluate(main, 'window.calcpadActiveEditor.getPosition().lineNumber'), 3);
        await app.stop(main, 'main');
    });

    test('navigation history is shared across windows and survives closed tabs and the primary window', {timeout: 90000}, async t => {
        const app = new TestApp();
        t.after(() => app.dispose());
        const main = await app.start();
        await app.focus(main, 'main');
        const content = Array.from({length: 80}, (_, i) => 'a' + i + ' = 1').join('\n');
        const selection = {selectionStartLineNumber: 5, selectionStartColumn: 4, positionLineNumber: 6, positionColumn: 2};
        const firstId = await app.evaluate(main, `window.calcpadActiveEditor.setValue(${JSON.stringify(content)});
            window.calcpadActiveEditor.setSelection(${JSON.stringify(selection)});
            window.calcpadBridge.confirmThreeWay = async () => 'no';
            window.calcpadTabs.activeId`);
        const second = await app.newWindow(main);
        await app.focus(second.page, second.label);
        const secondId = await app.evaluate(second.page, `window.calcpadActiveEditor.setValue(${JSON.stringify(content)});
            window.calcpadActiveEditor.setPosition({lineNumber: 6, column: 3});
            window.calcpadBridge.confirmThreeWay = async () => 'no';
            window.calcpadTabs.activeId`);
        assert.equal(firstId, secondId, 'tab IDs may be identical in different windows');
        const focusedAt = async (page, line, column, previous) => {
            await until(async () => {
                const [focused, blurred] = await Promise.all([
                    app.evaluate(page, `document.hasFocus()
                        && window.calcpadActiveEditor.getPosition().lineNumber === ${line}
                        && window.calcpadActiveEditor.getPosition().column === ${column}`),
                    previous ? app.evaluate(previous, '!document.hasFocus()') : true,
                ]);
                return focused && blurred;
            }, `window focused at ${line}:${column}`);
        };
        const navigate = async (page, key, target, line, column) => {
            await app.cdp(page, 'Input.dispatchKeyEvent', {type: 'keyDown', key, code: key,
                modifiers: 1, windowsVirtualKeyCode: key === 'ArrowLeft' ? 37 : 39});
            await focusedAt(target, line, column, page === target ? null : page);
            // CDP focuses the recipient even for release events.
            await app.cdp(target, 'Input.dispatchKeyEvent', {type: 'keyUp', key, code: key, modifiers: 1});
        };

        await app.cdp(second.page, 'Input.dispatchMouseEvent', {type: 'mousePressed', x: 200, y: 120, button: 'back', clickCount: 1});
        await focusedAt(main, 6, 2, second.page);
        await app.cdp(main, 'Input.dispatchMouseEvent', {type: 'mouseReleased', x: 200, y: 120, button: 'back', clickCount: 1});
        const restored = await app.evaluate(main, 'window.calcpadActiveEditor.getSelection()');
        for (const [key, value] of Object.entries(selection)) assert.equal(restored[key], value);
        await navigate(main, 'ArrowRight', second.page, 6, 3);
        await navigate(second.page, 'ArrowLeft', main, 6, 2);
        await app.evaluate(main, 'window.calcpadActiveEditor.setPosition({lineNumber: 8, column: 2})');
        // The webview can lose focus before the native focus notification arrives.
        await app.evaluate(main, `document.hasFocus = () => false;
            window.calcpadActiveEditor.setPosition({lineNumber: 60, column: 1});
            window.calcpadActiveEditor.setPosition({lineNumber: 8, column: 2});
            delete document.hasFocus`);
        await navigate(main, 'ArrowRight', second.page, 6, 3);
        await app.evaluate(main, 'window.calcpadActiveEditor.setPosition({lineNumber: 60, column: 1})');
        await navigate(second.page, 'ArrowLeft', main, 8, 2);
        await navigate(main, 'ArrowRight', second.page, 6, 3);

        await app.evaluate(second.page, `const id = window.calcpadTabs.newUntitled(${JSON.stringify(content)}, 'Closed history tab');
            window.calcpadActiveEditor.setPosition({lineNumber: 9, column: 1});
            window.calcpadTabs.close(id)`);
        await navigate(second.page, 'ArrowLeft', main, 8, 2);
        await navigate(main, 'ArrowRight', second.page, 6, 3);

        await app.closeAndWait(main, 'main');
        await until(() => app.evaluate(second.page, `${invoke('primary_window')} .then(label => label === ${JSON.stringify(second.label)})`), 'remaining window is primary');
        await app.evaluate(second.page, 'window.calcpadActiveEditor.setPosition({lineNumber: 30, column: 1})');
        await navigate(second.page, 'ArrowLeft', second.page, 6, 3);
        await navigate(second.page, 'ArrowRight', second.page, 30, 1);
        await app.stop(second.page, second.label);
    });

    test('cursor history merges nearby clicks and API moves while keeping explicit navigation and selections', {timeout: 90000}, async t => {
        const app = new TestApp();
        t.after(() => app.dispose());
        const main = await app.start();
        await app.setGeometry(main, {width: 1400, height: 700}, {x: 10, y: 10});
        await app.evaluate(main, `window.calcpadTabs.newUntitled(Array.from({length: 80}, (_, i) => 'a' + i + ' = 1').join('\\n'));
            window.calcpadActiveEditor.setPosition({lineNumber: 30, column: 1});
            window.calcpadActiveEditor.revealLineInCenter(32)`);
        const navigate = async key => {
            await app.cdp(main, 'Input.dispatchKeyEvent', {type: 'keyDown', key, code: key,
                modifiers: 1, windowsVirtualKeyCode: key === 'ArrowLeft' ? 37 : 39});
            await app.cdp(main, 'Input.dispatchKeyEvent', {type: 'keyUp', key, code: key, modifiers: 1});
        };
        const line = () => app.evaluate(main, 'window.calcpadActiveEditor.getPosition().lineNumber');
        const at = expected => until(async () => await line() === expected, `cursor history at line ${expected}`);
        const point = await app.evaluate(main, `(() => {
            const editor = window.calcpadActiveEditor;
            const rect = editor.getDomNode().getBoundingClientRect();
            const pos = editor.getScrolledVisiblePosition({lineNumber: 32, column: 3});
            return {x: rect.left + pos.left + 1, y: rect.top + pos.top + pos.height / 2};
        })()`);
        for (const type of ['mousePressed', 'mouseReleased']) {
            await app.cdp(main, 'Input.dispatchMouseEvent', {type, ...point, button: 'left', clickCount: 1});
        }
        await at(32);
        await navigate('ArrowLeft');
        await at(1);
        await app.evaluate(main, 'window.calcpadActiveEditor.setPosition({lineNumber: 4, column: 2})');
        await navigate('ArrowRight');
        await at(32);
        await app.evaluate(main, `window.calcpadActiveEditor.setPosition({lineNumber: 35, column: 2});
            window.calcpadBridge.handleMessage({type: 'goToLine', line: 38})`);
        await at(38);
        await app.evaluate(main, "window.calcpadActiveEditor.setPosition({lineNumber: 38, column: 5}, 'code.jump')");
        await navigate('ArrowLeft');
        await at(35);
        await navigate('ArrowRight');
        await at(38);
        assert.deepEqual(await app.evaluate(main, 'window.calcpadActiveEditor.getPosition()'), {lineNumber: 38, column: 5});

        const selection = {selectionStartLineNumber: 38, selectionStartColumn: 5, positionLineNumber: 37, positionColumn: 2};
        await app.evaluate(main, `window.calcpadActiveEditor.setSelection(${JSON.stringify(selection)});
            window.calcpadActiveEditor.setPosition({lineNumber: 60, column: 1})`);
        await navigate('ArrowLeft');
        await at(37);
        const restored = await app.evaluate(main, 'window.calcpadActiveEditor.getSelection()');
        for (const [key, value] of Object.entries(selection)) assert.equal(restored[key], value);
        await app.stop(main, 'main');
    });

    test('cross-file jumps record only the destination position', {timeout: 90000}, async t => {
        const app = new TestApp();
        t.after(() => app.dispose());
        const targetContent = '\n'.repeat(24) + 'target = 42\n';
        const included = app.fixture('history-definitions.cpd', targetContent);
        const source = app.fixture('history-source.cpd', `#include ${included}\n\nresult = target\n`);
        const main = await app.start([source]);
        await app.fileOpen(main, source);
        const state = () => app.evaluate(main, `({file: window.calcpadTabs.activeTab.filePath,
            line: window.calcpadActiveEditor.getPosition().lineNumber,
            column: window.calcpadActiveEditor.getPosition().column})`);
        const jump = async () => {
            await app.evaluate(main, `window.calcpadActiveEditor.setPosition({lineNumber: 3, column: 11});
                window.calcpadTabs.openFile(${JSON.stringify(included)}, ${JSON.stringify(targetContent)}, {lineNumber: 25, column: 1})`);
            await until(async () => {
                const current = await state();
                return current.file === included && current.line === 25;
            }, 'included definition reached');
        };
        const navigate = async key => {
            await app.cdp(main, 'Input.dispatchKeyEvent', {type: 'keyDown', key, code: key,
                modifiers: 1, windowsVirtualKeyCode: key === 'ArrowLeft' ? 37 : 39});
            await app.cdp(main, 'Input.dispatchKeyEvent', {type: 'keyUp', key, code: key, modifiers: 1});
        };

        await jump();
        const destination = await state();
        await navigate('ArrowLeft');
        await until(async () => JSON.stringify(await state()) !== JSON.stringify(destination), 'history moved back');
        assert.deepEqual(await state(), {file: source, line: 3, column: 11});
        await navigate('ArrowRight');
        await until(async () => (await state()).file === included, 'history moved forward');
        assert.deepEqual(await state(), destination);

        await app.evaluate(main, `window.calcpadActiveEditor.setPosition({lineNumber: 7, column: 1});
            window.calcpadTabs.activate(window.calcpadTabs.findByPath(${JSON.stringify(source)}).id)`);
        await jump();
        await navigate('ArrowLeft');
        await until(async () => JSON.stringify(await state()) !== JSON.stringify(destination), 'history moved back from existing tab');
        assert.deepEqual(await state(), {file: source, line: 3, column: 11});
        await app.stop(main, 'main');
    });

    test('navigation history restores positions across tabs and split groups with keyboard and mouse', {timeout: 90000}, async t => {
        const app = new TestApp();
        t.after(() => app.dispose());
        const main = await app.start();
        const first = await app.evaluate(main, `(() => {
            const id = window.calcpadTabs.newUntitled(Array.from({length: 80}, (_, i) => 'a' + i + ' = 1').join('\\n'), 'History A');
            window.calcpadActiveEditor.setPosition({lineNumber: 3, column: 2});
            window.calcpadActiveEditor.setPosition({lineNumber: 30, column: 3});
            return id;
        })()`);
        const state = () => app.evaluate(main, `({tab: window.calcpadTabs.activeId,
            position: window.calcpadActiveEditor.getPosition()})`);
        const keyboard = async (key, code) => {
            await app.cdp(main, 'Input.dispatchKeyEvent', {type: 'keyDown', key, code,
                modifiers: 1, windowsVirtualKeyCode: key === 'ArrowLeft' ? 37 : 39});
            await app.cdp(main, 'Input.dispatchKeyEvent', {type: 'keyUp', key, code, modifiers: 1});
        };
        const mouse = async button => {
            for (const type of ['mousePressed', 'mouseReleased']) {
                await app.cdp(main, 'Input.dispatchMouseEvent', {type, x: 200, y: 120, button, clickCount: 1});
            }
        };
        const at = async (tab, lineNumber, column) => {
            await until(async () => {
                const current = await state();
                return current.tab === tab && current.position.lineNumber === lineNumber && current.position.column === column;
            }, `history at ${tab}:${lineNumber}:${column}`);
        };
        await keyboard('ArrowLeft', 'ArrowLeft');
        await at(first, 3, 2);
        await mouse('forward');
        await at(first, 30, 3);
        await mouse('back');
        await at(first, 3, 2);
        await keyboard('ArrowRight', 'ArrowRight');
        await at(first, 30, 3);

        const previewInput = async expression => {
            await until(() => app.evaluate(main, `(() => {
                const frame = document.querySelector('.preview-frame:not(.preview-frame-back)');
                if (!frame?.srcdoc) return null;
                frame.focus();
                return true;
            })()`), 'history preview ready');
            const {root} = await app.cdp(main, 'DOM.getDocument', {depth: -1, pierce: true});
            const findPreview = node => {
                if (node.nodeName === 'IFRAME' && node.attributes.includes('preview-frame')) return node.frameId;
                for (const child of node.children ?? []) {
                    const frameId = findPreview(child);
                    if (frameId) return frameId;
                }
            };
            const previewFrame = findPreview(root);
            assert.ok(previewFrame, 'front preview frame found');
            const {executionContextId} = await app.cdp(main, 'Page.createIsolatedWorld', {frameId: previewFrame, worldName: 'history-test'});
            const result = await app.cdp(main, 'Runtime.evaluate', {contextId: executionContextId, expression});
            assert.equal(result.exceptionDetails, undefined);
        };
        await previewInput(`document.dispatchEvent(new KeyboardEvent('keydown', {key: 'ArrowLeft', code: 'ArrowLeft', altKey: true, cancelable: true}))`);
        await at(first, 3, 2);
        const previewPoint = await app.evaluate(main, `(() => {
            const rect = document.querySelector('.preview-frame:not(.preview-frame-back)').getBoundingClientRect();
            return {x: rect.left + rect.width / 2, y: rect.top + rect.height / 2};
        })()`);
        for (const type of ['mousePressed', 'mouseReleased']) {
            await app.cdp(main, 'Input.dispatchMouseEvent', {type, ...previewPoint, button: 'forward', clickCount: 1});
        }
        await at(first, 30, 3);
        await keyboard('ArrowLeft', 'ArrowLeft');
        await at(first, 3, 2);
        await previewInput(`['mousedown', 'mouseup', 'auxclick'].forEach(type => document.dispatchEvent(new MouseEvent(type, {button: 4, cancelable: true})))`);
        await at(first, 30, 3);

        const second = await app.evaluate(main, `(() => {
            const id = window.calcpadTabs.newUntitled('b = 2\\nc = 3', 'History B');
            window.calcpadActiveEditor.setPosition({lineNumber: 2, column: 3});
            return id;
        })()`);
        await keyboard('ArrowLeft', 'ArrowLeft');
        await at(first, 30, 3);
        await keyboard('ArrowRight', 'ArrowRight');
        await at(second, 2, 3);
        await app.evaluate(main, invoke('plugin:event|emit_to', {
            target: {kind: 'WebviewWindow', label: 'main'}, event: 'menu-click', payload: {id: 'split-editor'},
        }));
        const linked = await until(() => app.evaluate(main,
            `document.querySelector('[data-tab-group="g1"] [data-tab-id]')?.dataset.tabId`), 'split history group ready');
        await until(async () => (await state()).tab === linked, 'split group active');
        await keyboard('ArrowLeft', 'ArrowLeft');
        await at(second, 2, 3);
        await keyboard('ArrowRight', 'ArrowRight');
        await until(async () => (await state()).tab === linked, 'history returned to split group');
        await app.evaluate(main, "window.calcpadBridge.confirmThreeWay = async () => 'no'");
        await app.stop(main, 'main');
    });

    test('document tabs reorder without losing undo history and cancelled drags leave them in place', {timeout: 90000}, async t => {
        const app = new TestApp();
        t.after(() => app.dispose());
        const main = await app.start();
        const ids = await app.evaluate(main, `(() => {
            const tabs = window.calcpadTabs;
            const scratch = tabs.activeId;
            const ids = ['A', 'B', 'C'].map(title => tabs.newUntitled(title + ' = 1', title));
            tabs.close(scratch);
            tabs.activate(ids[0]);
            window.calcpadActiveEditor.executeEdits('test', [{range: {startLineNumber: 1, startColumn: 6, endLineNumber: 1, endColumn: 6}, text: '2'}]);
            return ids;
        })()`);
        await dragTab(app, main, ids[0], main, ids[2]);
        await until(async () => (await app.evaluate(main, 'window.calcpadTabs.all.map(tab => tab.title).join()')) === 'B,A,C', 'tabs reordered');
        assert.equal(await app.evaluate(main, 'window.calcpadTabs.activeId'), ids[0]);
        assert.equal(await app.evaluate(main, 'window.calcpadTabs.isDirty()'), true);
        await app.evaluate(main, 'window.calcpadActiveEditor.getModel().undo()');
        assert.equal(await app.evaluate(main, 'window.calcpadActiveEditor.getValue()'), 'A = 1');
        assert.equal(await app.evaluate(main, 'window.calcpadTabs.isDirty()'), false);

        await dragTab(app, main, ids[0], main, ids[1], {cancel: true});
        await dragTab(app, main, ids[0], main, null, {outside: true});
        assert.deepEqual(await app.evaluate(main, 'window.calcpadTabs.all.map(tab => tab.title)'), ['B', 'A', 'C']);
        await dragTab(app, main, ids[1], main, null);
        await until(async () => (await app.evaluate(main, 'window.calcpadTabs.all.map(tab => tab.title).join()')) === 'A,C,B', 'tab moved to end');
        await app.stop(main, 'main');
    });

    test('tabs move between windows with unsaved content, paths, cursor positions and recovery drafts', {timeout: 90000}, async t => {
        const app = new TestApp();
        t.after(() => app.dispose());
        const main = await app.start();
        const second = await app.newWindow(main);
        const original = await app.evaluate(main, `(() => {
            window.testPrompts = 0;
            window.calcpadBridge.confirmThreeWay = async () => { window.testPrompts++; return 'cancel'; };
            window.calcpadActiveEditor.setValue('a = 1\\nb = 2');
            window.calcpadActiveEditor.setPosition({lineNumber: 2, column: 3});
            return window.calcpadTabs.activeTab;
        })()`);
        await dragTab(app, main, original.id, second.page, null);
        await until(async () => await app.evaluate(second.page, 'window.calcpadActiveEditor.getValue()') === 'a = 1\nb = 2', 'unsaved document received');
        await until(async () => !(await app.evaluate(main, 'window.calcpadTabs.all')).some(tab => tab.id === original.id), 'source tab removed');
        assert.equal(await app.evaluate(second.page, 'window.calcpadTabs.count'), 1);
        assert.equal(await app.evaluate(second.page, 'window.calcpadTabs.isDirty()'), true);
        assert.equal(await app.evaluate(second.page, 'window.calcpadTabs.activeTab.title'), original.title);
        assert.deepEqual(await app.evaluate(second.page, 'window.calcpadActiveEditor.getPosition()'), {lineNumber: 2, column: 3});
        assert.equal(await app.evaluate(main, 'window.testPrompts'), 0);
        await until(() => app.evaluate(second.page, `(async () => (await ${invoke('draft_list')}).some(draft => draft.filePath === null))()`), 'destination autosave');

        const file = app.fixture('dragged worksheet.cpd', 'saved = 1');
        await app.focus(main, 'main');
        await app.relaunch([file]);
        await app.fileOpen(main, file);
        const fileTab = await app.evaluate(main, 'window.calcpadTabs.activeTab');
        await dragTab(app, main, fileTab.id, second.page, null);
        await app.fileOpen(second.page, file);
        await until(async () => !(await app.evaluate(main, 'window.calcpadTabs.all')).some(tab => tab.filePath === file), 'saved source removed');
        assert.equal(await app.evaluate(second.page, 'window.calcpadTabs.isDirty()'), false);
        assert.equal(readFileSync(file, 'utf8'), 'saved = 1');
        await app.evaluate(second.page, `window.calcpadBridge.confirmThreeWay = async () => 'no';`);
        await app.closeAndWait(main, 'main');
        await app.stop(second.page, second.label);
    });

    test('tabs move between split groups while retaining their document model', {timeout: 90000}, async t => {
        const app = new TestApp();
        t.after(() => app.dispose());
        const main = await app.start();
        const original = await app.evaluate(main, 'window.calcpadTabs.activeId');
        await app.evaluate(main, invoke('plugin:event|emit_to', {
            target: {kind: 'WebviewWindow', label: 'main'}, event: 'menu-click', payload: {id: 'split-editor'},
        }));
        const linked = await until(() => app.evaluate(main,
            `document.querySelector('[data-tab-group="g1"] [data-tab-id]')?.dataset.tabId`), 'split group ready');
        await app.evaluate(main, `document.querySelector('[data-tab-id="${original}"]').dispatchEvent(new MouseEvent('mousedown', {button: 0, bubbles: true}))`);
        await until(async () => await app.evaluate(main, 'window.calcpadTabs.activeId') === original, 'source group active');
        const moved = await app.evaluate(main, `(() => {
            const id = window.calcpadTabs.newUntitled('', 'Moved');
            window.calcpadActiveEditor.executeEdits('test', [{range: {startLineNumber: 1, startColumn: 1, endLineNumber: 1, endColumn: 1}, text: 'moved = 1'}]);
            window.testMovedModel = window.calcpadActiveEditor.getModel();
            return id;
        })()`);
        await dragTab(app, main, moved, main, linked);
        await until(() => app.evaluate(main, `document.querySelector('[data-tab-group="g1"] .tab-title')?.textContent === 'Moved'`), 'tab moved to split group');
        await until(() => app.evaluate(main, `document.querySelector('[data-tab-id="${moved}"]') === null`), 'source tab removed');
        assert.equal(await app.evaluate(main, 'window.calcpadActiveEditor.getModel() === window.testMovedModel'), true);
        await app.evaluate(main, 'window.calcpadActiveEditor.getModel().undo()');
        assert.equal(await app.evaluate(main, 'window.calcpadActiveEditor.getValue()'), '');
        await app.stop(main, 'main');
    });

    test('dropping a tab onto a different open version preserves both documents', {timeout: 90000}, async t => {
        const app = new TestApp();
        t.after(() => app.dispose());
        const file = app.fixture('conflicting worksheet.cpd', 'saved = 1');
        const main = await app.start([file]);
        await app.fileOpen(main, file);
        const second = await app.newWindow(main);
        await app.evaluate(second.page, `window.dispatchEvent(new CustomEvent('calcpad-open-file', {
            detail: {path: ${JSON.stringify(file)}}
        }))`);
        await app.fileOpen(second.page, file);
        await app.evaluate(main, "window.calcpadActiveEditor.setValue('source = 2')");
        await app.evaluate(second.page, "window.calcpadActiveEditor.setValue('destination = 3')");
        const tabId = await app.evaluate(main, 'window.calcpadTabs.activeId');
        await dragTab(app, main, tabId, second.page, null);
        await until(() => app.evaluate(second.page, "document.body.textContent.includes('already has a different version open')"), 'conflict reported');
        assert.equal(await app.evaluate(main, 'window.calcpadActiveEditor.getValue()'), 'source = 2');
        assert.equal(await app.evaluate(second.page, 'window.calcpadActiveEditor.getValue()'), 'destination = 3');
        for (const page of [main, second.page]) {
            assert.equal(await app.evaluate(page, 'window.calcpadTabs.count'), 1);
            assert.equal(await app.evaluate(page, 'window.calcpadTabs.activeTab.filePath'), file);
            assert.equal(await app.evaluate(page, 'window.calcpadTabs.isDirty()'), true);
        }
        assert.equal(readFileSync(file, 'utf8'), 'saved = 1');
        for (const page of [main, second.page]) await app.evaluate(page, "window.calcpadBridge.confirmThreeWay = async () => 'no'");
        await app.closeAndWait(main, 'main');
        await app.stop(second.page, second.label);
    });

    test('title bar actions use the active document and temporary exports open after writing', {timeout: 90000}, async t => {
        const app = new TestApp();
        t.after(() => app.dispose());
        const main = await app.start();
        const file = app.fixture('toolbar worksheet.cpd');
        await app.evaluate(main, `
            window.exportOpened = [];
            window.openCalls = 0;
            window.testErrors = [];
            window.addEventListener('unhandledrejection', event => window.testErrors.push(String(event.reason)));
            window.calcpadBridge.openFile = async () => {
                window.openCalls++;
                return {path: ${JSON.stringify(file)}, content: 'a = 1'};
            };
            window.calcpadBridge.openPathSafe = async path => {
                const bytes = await window.__TAURI_INTERNALS__.invoke('plugin:fs|read_file', {path, options: {}});
                window.exportOpened.push({path, size: new Uint8Array(bytes).byteLength});
            };
            document.querySelector('[aria-label="Open document"]').click();
        `);
        await app.fileOpen(main, file);
        assert.match(await app.evaluate(main, 'document.querySelector(".titlebar-caption").textContent'), /toolbar worksheet/);
        await app.evaluate(main, `window.calcpadActiveEditor.setValue('toolbar = 299')`);
        await until(async () => !await app.evaluate(main, `document.querySelector('[aria-label="Save document"]').disabled`), 'editing enables Save');
        await app.evaluate(main, `document.querySelector('[aria-label="Save document"]').click()`);
        await until(() => readFileSync(file, 'utf8').split(/\r?\n/)[0] === 'toolbar = 299', 'toolbar saves current document');
        await until(() => app.evaluate(main, `document.querySelector('[aria-label="Save document"]').disabled`), 'saved document disables Save');
        await app.evaluate(main, `window.calcpadActiveEditor.setValue('toolbar = 300')`);
        await until(async () => !await app.evaluate(main, `document.querySelector('[aria-label="Save document"]').disabled`), 'editing enables Save');
        await app.evaluate(main, `document.querySelector('[aria-label="Save document"]').click()`);
        await until(() => app.evaluate(main, `document.querySelector('[aria-label="Save document"]').disabled`), 'saved changes disable Save');
        for (const [label, extension, signature] of [['Export to PDF', '.pdf', '%PDF-'], ['Export to Word', '.docx', 'PK']]) {
            await app.evaluate(main, `document.querySelector('[aria-label="${label}"]').click()`);
            const opened = await until(async () => (await app.evaluate(main, 'window.exportOpened')).find(entry => entry.path.endsWith(extension)), `${label} opened`);
            assert.equal(path.basename(opened.path), `toolbar worksheet${extension}`);
            assert.ok(path.resolve(opened.path).startsWith(path.resolve(os.tmpdir()) + path.sep));
            assert.ok(opened.size > 0);
            assert.equal(readFileSync(opened.path).subarray(0, signature.length).toString(), signature);
        }
        assert.equal(await app.evaluate(main, 'window.openCalls'), 1);
        assert.deepEqual(await app.evaluate(main, 'window.testErrors'), []);
        const first = await app.evaluate(main, invoke('create_export_path', {filename: 'same.pdf'}));
        const second = await app.evaluate(main, invoke('create_export_path', {filename: 'same.pdf'}));
        assert.notEqual(path.dirname(first), path.dirname(second));
        await app.stop(main, 'main');
    });

    test('tabs span the editor and results in every mode and custom window controls work', {timeout: 90000}, async t => {
        const app = new TestApp();
        t.after(() => app.dispose());
        const main = await app.start();
        const titlebar = await app.evaluate(main, `(() => {
            const box = selector => document.querySelector(selector).getBoundingClientRect();
            const center = selector => {const b = box(selector); return {x: b.x + b.width / 2, y: b.y + b.height / 2};};
            return {
                bar: center('.desktop-titlebar'), caption: center('.titlebar-caption'),
                logo: center('.titlebar-logo'), menuIcon: center('[aria-label="File menu"] svg'), menuText: center('[aria-label="File menu"] span'),
                menuFont: getComputedStyle(document.querySelector('[aria-label="File menu"]')).fontSize,
                captionFont: getComputedStyle(document.querySelector('.titlebar-caption')).fontSize,
                appFont: getComputedStyle(document.body).fontSize,
            };
        })()`);
        assert.equal(titlebar.menuFont, titlebar.appFont);
        assert.equal(titlebar.captionFont, titlebar.appFont);
        assert.ok(Math.abs(titlebar.caption.x - titlebar.bar.x) < 1);
        assert.equal(titlebar.menuIcon.y, titlebar.logo.y);
        assert.ok(Math.abs(titlebar.menuText.y - titlebar.logo.y) < 1);
        for (const mode of ['preview', 'unwrapped', 'ui', 'report']) {
            const label = {preview: 'Preview', unwrapped: 'Unwrapped', ui: 'Input', report: 'Report'}[mode];
            await app.evaluate(main, `[...document.querySelectorAll('.results-toolbar [aria-label="Result mode"] button')].find(button => button.textContent === '${label}').click()`);
            const boxes = await app.evaluate(main, `(() => {
                const box = selector => document.querySelector(selector).getBoundingClientRect().toJSON();
                return {tabs: box('.document-tabs'), panes: box('.document-panes'), strip: box('.tab-strip'), preview: box('.preview-pane'), toolbar: box('.document-tabs .results-toolbar')};
            })()`);
            assert.equal(boxes.tabs.x, boxes.panes.x, mode);
            assert.equal(boxes.tabs.width, boxes.panes.width, mode);
            assert.ok(boxes.strip.width > 0, mode);
            assert.equal(boxes.strip.right, boxes.toolbar.left, mode);
            assert.equal(boxes.toolbar.y, boxes.strip.y, mode);
            assert.equal(boxes.preview.y, boxes.tabs.bottom, mode);
            assert.ok(boxes.preview.height > 0, mode);
            assert.equal(await app.evaluate(main, `document.querySelector('.results-toolbar [aria-label="Result mode"] [aria-pressed="true"]').textContent`), label);
        }
        await app.evaluate(main, `document.querySelector('[aria-label="Hide the results pane"]').click()`);
        assert.equal(await app.evaluate(main, `document.querySelector('.results-toolbar')`), null);
        await app.evaluate(main, `document.querySelector('[aria-label="Show the results pane"]').click()`);
        assert.equal(await app.evaluate(main, `!!document.querySelector('.document-tabs .results-toolbar')`), true);
        assert.deepEqual(await app.evaluate(main, `[...document.querySelectorAll('.document-tabs [aria-label="Editor actions"] button')].map(button => button.getAttribute('aria-label'))`), ['Refresh preview', 'Split editor down', 'Hide the results pane']);
        const iconSizes = await app.evaluate(main, `['[aria-label="Split editor down"] svg', '[aria-label="Hide the results pane"] svg', '#vue-sidebar [aria-label="Split panel"] svg', '#vue-sidebar [aria-label="Hide the sidebar"] svg'].map(selector => {const rect = document.querySelector(selector).getBoundingClientRect(); return {width: rect.width, height: rect.height};})`);
        for (const size of iconSizes) assert.deepEqual(size, iconSizes[0]);
        await app.evaluate(main, `document.querySelector('[aria-label="Maximize"]').click()`);
        await until(async () => (await app.geometry(main)).maximized, 'custom maximize button');
        await until(() => app.evaluate(main, `!!document.querySelector('[aria-label="Restore"]')`), 'restore label');
        await app.evaluate(main, `document.querySelector('[aria-label="Restore"]').click()`);
        await until(async () => !(await app.geometry(main)).maximized, 'custom restore button');
        await app.evaluate(main, `document.querySelector('[aria-label="Minimize"]').click()`);
        await until(() => app.evaluate(main, invoke('plugin:window|is_minimized')), 'custom minimize button');
        await app.evaluate(main, invoke('plugin:window|unminimize'));
        await app.evaluate(main, `setTimeout(() => document.querySelector('[aria-label="Close window"]').click(), 50)`);
        const [code] = await app.exited;
        assert.equal(code, 0);
        app.runner = null;
    });

    test('Files shares the sidebar tabs and Settings contains formatting and auto-run controls', {timeout: 90000}, async t => {
        const app = new TestApp();
        t.after(() => app.dispose());
        const main = await app.start();
        const file = app.fixture('sidebar worksheet.cpd');
        const sidebarTab = label => `[...document.querySelectorAll('#vue-sidebar .tab-container > .tab')].find(button => button.textContent.trim() === '${label}').click()`;
        assert.equal(await app.evaluate(main, `!!document.querySelector('#vue-sidebar .activity-icons')`), false);
        assert.equal(await app.evaluate(main, `[...document.querySelectorAll('#vue-sidebar .tab-container > .tab')].some(button => button.textContent.trim() === 'Formatting')`), false);
        await app.evaluate(main, `window.calcpadBridge.getOpenedFolder = async () => ${JSON.stringify(path.dirname(file))}; ${sidebarTab('Files')}`);
        await until(() => app.evaluate(main, `[...document.querySelectorAll('#vue-sidebar .tree-label')].some(label => label.title === ${JSON.stringify(file)})`), 'Files tab loads folder');
        await app.evaluate(main, `[...document.querySelectorAll('#vue-sidebar .tree-label')].find(label => label.title === ${JSON.stringify(file)}).closest('.tree-row').click()`);
        await app.fileOpen(main, file);
        await app.evaluate(main, sidebarTab('Settings'));
        await app.evaluate(main, `(() => { const search = document.querySelector('.settings-toolbar input'); search.value = 'prettify'; search.dispatchEvent(new Event('input', {bubbles: true})); window.calcpadActiveEditor.setValue('a = 1   '); })()`);
        assert.equal(await app.evaluate(main, `document.querySelector('.settings-tab .formatting-tab').checkVisibility()`), true);
        await app.evaluate(main, `document.querySelector('.settings-tab .formatting-tab button').click()`);
        await until(() => app.evaluate(main, `window.calcpadActiveEditor.getValue() === 'a = 1'`), 'Prettify in Settings trims whitespace');
        await app.evaluate(main, `(() => { const search = document.querySelector('.settings-toolbar input'); search.value = 'auto-run'; search.dispatchEvent(new Event('input', {bubbles: true})); })()`);
        await app.evaluate(main, `[...document.querySelectorAll('.settings-tab label')].find(label => label.textContent.includes('Auto-Run Preview')).querySelector('input').click()`);
        await until(() => app.evaluate(main, `!!document.querySelector('[aria-label="Editor actions"] [aria-label="Run preview"]')`), 'auto-run off shows Play');
        await app.evaluate(main, `[...document.querySelectorAll('.settings-tab label')].find(label => label.textContent.includes('Auto-Run Preview')).querySelector('input').click()`);
        await until(() => app.evaluate(main, `!!document.querySelector('[aria-label="Editor actions"] [aria-label="Refresh preview"]')`), 'auto-run on shows Refresh');
        assert.equal(await app.evaluate(main, `document.querySelector('[aria-label="File menu"]') && getComputedStyle(document.querySelector('[aria-label="File menu"]')).cursor`), 'default');
        await app.evaluate(main, `window.calcpadBridge.confirmThreeWay = async () => 'no'`);
        await app.stop(main, 'main');
    });

    test('new windows focus their webview when opened from an existing window or a repeat launch', {timeout: 90000}, async t => {
        const app = new TestApp();
        t.after(() => app.dispose());
        const main = await app.start();

        for (const source of ['command', 'launch', 'command']) {
            await app.focus(main, 'main');
            let opened;
            if (source === 'command') {
                opened = await app.newWindow(main);
            } else {
                await app.relaunch();
                const [label, page] = await until(async () => {
                    const windows = await app.windows();
                    return windows.size === 2 && [...windows].find(([label]) => label !== 'main');
                }, 'repeat launch window ready');
                opened = {label, page};
            }

            await until(() => app.evaluate(opened.page, 'document.hasFocus()'), 'new webview focused');
            assert.equal(await app.evaluate(main, 'document.hasFocus()'), false);
            await app.closeAndWait(opened.page, opened.label);
        }

        await app.stop(main, 'main');
    });

    test('repeat launches open windows; files open tabs in the last-focused window', {timeout: 90000}, async t => {
        const app = new TestApp();
        t.after(() => app.dispose());
        const main = await app.start();
        await app.relaunch();
        const second = await until(async () => {
            const windows = await app.windows();
            return windows.size === 2 && [...windows].find(([label]) => label !== 'main');
        }, 'second launch opens a window');
        const [secondLabel, secondPage] = second;

        const title = await app.evaluate(main, 'document.title');
        assert.equal(await app.evaluate(secondPage, 'document.title'), title);
        await until(async () => await app.evaluate(secondPage, invoke('plugin:window|title')) === title, 'native title matches document title');

        const firstFile = app.fixture('first worksheet.cpd');
        await app.focus(secondPage, secondLabel);
        await app.relaunch([firstFile]);
        await app.fileOpen(secondPage, firstFile);
        assert.equal((await app.windows()).size, 2);
        assert.equal(await app.evaluate(main, 'window.calcpadTabs.all.some(tab => tab.filePath)'), false);
        assert.equal(await app.evaluate(secondPage, 'window.calcpadActiveEditor.getValue()'), 'a = 1');
        assert.doesNotMatch(await app.evaluate(secondPage, 'document.title'), /\s\(\d+\)$/);

        const secondFile = app.fixture('second worksheet.CPD', 'b = 2');
        const thirdFile = app.fixture('third worksheet.cpd', 'c = 3');
        await app.focus(main, 'main');
        await app.relaunch([secondFile, thirdFile]);
        await app.fileOpen(main, secondFile);
        await app.fileOpen(main, thirdFile);
        assert.equal((await app.windows()).size, 2);
        assert.equal(await app.evaluate(secondPage, 'window.calcpadTabs.all.filter(tab => tab.filePath).length'), 1);

        await app.closeAndWait(main, 'main');
        const afterClose = app.fixture('after main closes.cpd');
        await app.relaunch([afterClose]);
        await app.fileOpen(secondPage, afterClose);
        assert.equal((await app.windows()).size, 1);
        await app.relaunch();
        const later = await until(async () => {
            const windows = await app.windows();
            return windows.size === 2 && [...windows].find(([label]) => label !== secondLabel);
        }, 'repeat launch after main closed');
        await app.closeAndWait(later[1], later[0]);
        await app.stop(secondPage, secondLabel);
    });

    test('closing windows preserves other buffers and transfers backend recovery and session saving', {timeout: 90000}, async t => {
        const app = new TestApp();
        t.after(() => app.dispose());
        const main = await app.start();
        const second = await app.newWindow(main);
        const third = await app.newWindow(main);
        for (const [page, content] of [[main, 'main = 1'], [second.page, 'secondary = 2'], [third.page, 'third = 3']]) {
            await app.evaluate(page, `window.testPrompts = 0;
                window.calcpadBridge.confirmThreeWay = async () => { window.testPrompts++; return 'no'; };
                window.calcpadActiveEditor.setValue(${JSON.stringify(content)});`);
        }
        await app.evaluate(main, `window.calcpadBridge.confirmThreeWay = async () => { window.testPrompts++; return 'cancel'; };`);
        await app.close(main, 'main');
        await until(async () => await app.evaluate(main, 'window.testPrompts') === 1, 'cancel prompt');
        assert.equal((await app.windows()).size, 3);
        assert.equal(await app.evaluate(second.page, 'window.testPrompts'), 0);
        assert.equal(await app.evaluate(third.page, 'window.testPrompts'), 0);

        await app.evaluate(main, `window.calcpadBridge.confirmThreeWay = async () => 'no';`);
        await app.closeAndWait(main, 'main');
        assert.equal((await app.windows()).size, 2);
        assert.equal(await app.evaluate(second.page, invoke('primary_window')), second.label);
        assert.equal(await app.evaluate(second.page, 'window.calcpadActiveEditor.getValue()'), 'secondary = 2');
        assert.equal(await app.evaluate(third.page, 'window.calcpadActiveEditor.getValue()'), 'third = 3');
        assert.equal(await app.evaluate(second.page, 'window.testPrompts'), 0);
        assert.ok(await app.healthy(second.page));
        await app.crashAndRecover(second.page);
        assert.equal(app.serverProcesses('$servers.Count'), '1');

        const later = await app.newWindow(second.page);
        assert.equal(await app.evaluate(later.page, invoke('primary_window')), second.label);
        assert.ok(await app.healthy(later.page));
        await app.closeAndWait(later.page, later.label);
        await app.closeAndWait(second.page, second.label);
        assert.equal((await app.windows()).size, 1);
        assert.equal(await app.evaluate(third.page, invoke('primary_window')), third.label);
        assert.equal(await app.evaluate(third.page, 'window.testPrompts'), 0);
        await app.crashAndRecover(third.page);

        const file = app.fixture('surviving session.cpd');
        await app.focus(third.page, third.label);
        await app.relaunch([file]);
        await app.fileOpen(third.page, file);
        await app.stop(third.page, third.label);
        assert.equal(app.serverProcesses('$servers.Count'), '0');
        const workspace = JSON.parse(readFileSync(path.join(app.profile, 'workspace.json'), 'utf8'));
        assert.ok(workspace.state.openFiles.includes(file));
    });

    test('main geometry is saved while secondary windows always use defaults', {timeout: 90000}, async t => {
        const app = new TestApp();
        t.after(() => app.dispose());
        let main = await app.start();
        const defaults = await app.geometry(main);
        assert.equal(defaults.maximized, false);
        const position = {x: defaults.position.x + 40, y: defaults.position.y + 40};
        const changedSize = {width: 560, height: 340};
        const secondarySize = {width: 600, height: 360};
        const changed = await app.setGeometry(main, changedSize, position);
        assert.notDeepEqual(changed.size, defaults.size);
        assert.notDeepEqual(changed.position, defaults.position);
        await app.stop(main, 'main');

        const stateFile = path.join(app.profile, '.window-state.json');
        const saved = JSON.parse(readFileSync(stateFile, 'utf8'));
        writeFileSync(stateFile, JSON.stringify({...saved, 'main-2': {...saved.main, maximized: true}}));
        main = await app.start();
        assert.deepEqual(await app.geometry(main), changed);

        const second = await app.newWindow(main);
        assert.deepEqual(await app.geometry(second.page), defaults);

        await app.setGeometry(main, changedSize, position);
        await app.evaluate(main, invoke('plugin:window|maximize'));
        await until(async () => (await app.geometry(main)).maximized, 'window maximized');
        const third = await app.newWindow(main);
        assert.deepEqual(await app.geometry(third.page), defaults);

        await app.setGeometry(second.page, secondarySize, position);
        await app.closeAndWait(second.page, second.label);
        const fourth = await app.newWindow(main);
        assert.deepEqual(await app.geometry(fourth.page), defaults);
        await app.closeAndWait(main, 'main');
        await app.closeAndWait(third.page, third.label);
        await app.setGeometry(fourth.page, secondarySize, position);
        await app.stop(fourth.page, fourth.label);

        main = await app.start();
        assert.deepEqual(await app.geometry(main), changed);
        await app.stop(main, 'main');
    });
});
