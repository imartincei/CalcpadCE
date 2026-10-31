import assert from 'node:assert/strict';
import { spawn, execFileSync } from 'node:child_process';
import { once } from 'node:events';
import { mkdtempSync, openSync, closeSync, writeFileSync } from 'node:fs';
import { createServer } from 'node:net';
import { createInterface } from 'node:readline';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { pause, until, invoke } from './test-app.mjs';

export { pause, until, invoke };

async function unusedPort() {
    const server = createServer();
    server.listen(0, '127.0.0.1');
    await once(server, 'listening');
    const port = server.address().port;
    await new Promise(resolve => server.close(resolve));
    return port;
}

export class LinuxTestApp {
    directory = fileURLToPath(new URL('.work/linux-tests/', import.meta.url));
    executable = path.join(this.directory, 'app/bin/calcpad-linux-tests');
    isWayland = process.env.CALCPAD_TEST_BACKEND === 'wayland';
    supportsWindowActivation = !this.isWayland || execFileSync('pkg-config', ['--modversion', 'gtk+-3.0'],
        {encoding: 'utf8'}).trim().localeCompare('3.24.35', 'en', {numeric: true}) >= 0;

    async start() {
        this.profile = mkdtempSync(path.join(this.directory, 'profile-'));
        const port = await unusedPort();
        const nativePort = await unusedPort();
        this.endpoint = `http://127.0.0.1:${port}`;
        const log = openSync(path.join(this.profile, 'driver.log'), 'w');
        this.driver = spawn('tauri-driver', ['--port', String(port), '--native-port', String(nativePort)], {
            detached: true,
            stdio: ['ignore', log, log],
            env: {
                ...process.env,
                XDG_DATA_HOME: path.join(this.profile, 'data'),
                XDG_CONFIG_HOME: path.join(this.profile, 'config'),
                XDG_CACHE_HOME: path.join(this.profile, 'cache'),
            },
        });
        if (this.isWayland) {
            this.keyboard = spawn('wtype', ['-s', '600000'], {stdio: ['ignore', 'ignore', log]});
            this.pointer = spawn('python3', [fileURLToPath(new URL('wayland-pointer.py', import.meta.url))], {
                stdio: ['pipe', 'pipe', log],
            });
            this.pointerReader = createInterface({input: this.pointer.stdout});
            const [ready] = await once(this.pointerReader, 'line', {signal: AbortSignal.timeout(10000)});
            assert.equal(ready, 'ready');
        }
        closeSync(log);
        await until(async () => {
            if (this.driver.exitCode !== null) throw new Error(`tauri-driver exited: ${this.driver.exitCode}`);
            try { return (await fetch(`${this.endpoint}/status`)).ok; }
            catch { return false; }
        }, 'Linux WebDriver ready');
        const session = await this.request('/session', {capabilities: {alwaysMatch: {
            browserName: 'wry', 'tauri:options': {application: this.executable},
        }}});
        this.session = session.sessionId;
        await this.request(`/session/${this.session}/timeouts`, {script: 20000});
        const main = await this.window('main');
        assert.equal(await this.evaluate(main, invoke('plugin:app|identifier')), 'org.calcpad-ce.linux-tests');
        assert.equal(await this.evaluate(main, invoke('use_system_tab_drag')), this.isWayland);
        await this.observeDrags(main);
        return main;
    }

    async request(route, body, method = body === undefined ? 'GET' : 'POST') {
        const response = await fetch(this.endpoint + route, {
            method,
            headers: {'Content-Type': 'application/json'},
            body: body === undefined ? undefined : JSON.stringify(body),
            signal: AbortSignal.timeout(30000),
        });
        const result = await response.json();
        if (!response.ok) throw new Error(`${route}: ${JSON.stringify(result.value)}`);
        return result.value;
    }

    async evaluate(handle, expression) {
        await this.request(`/session/${this.session}/window`, {handle});
        const result = await this.request(`/session/${this.session}/execute/async`, {
            script: `const done = arguments[arguments.length - 1];
                Promise.resolve().then(() => eval(${JSON.stringify(expression)}))
                    .then(value => done({value}), error => done({error: String(error.stack ?? error)}));`,
            args: [],
        });
        if (result.error) throw new Error(result.error);
        return result.value;
    }

    async windows() {
        const handles = await this.request(`/session/${this.session}/window/handles`);
        const windows = new Map();
        for (const handle of handles) {
            const state = await this.evaluate(handle, `({
                label: window.__TAURI_INTERNALS__?.metadata.currentWindow.label,
                ready: !!document.querySelector('.tab-drop-marker'),
            })`);
            if (state.ready) windows.set(state.label, handle);
        }
        return windows;
    }

    window(label) {
        return until(async () => (await this.windows()).get(label), `${label} ready`);
    }

    async newWindow(handle) {
        const label = await this.evaluate(handle, invoke('new_window'));
        const opened = await this.window(label);
        await this.observeDrags(opened);
        return {label, handle: opened};
    }

    observeDrags(handle) {
        return this.evaluate(handle, `window.testDragEvents = [];
            for (const type of ['pointerdown', 'pointermove', 'pointerup', 'keydown', 'dragstart', 'dragenter', 'dragover', 'drop', 'dragend']) {
                document.addEventListener(type, event => window.testDragEvents.push({
                    type, key: event.key, x: event.clientX, y: event.clientY, target: event.target.className,
                    types: [...(event.dataTransfer?.types ?? [])],
                }), true);
            }`);
    }

    xdotool(...args) {
        return execFileSync('xdotool', args.map(String), {encoding: 'utf8'}).trim();
    }

    sway(...args) {
        const result = JSON.parse(execFileSync('swaymsg', ['-r', ...args], {encoding: 'utf8'}));
        if (Array.isArray(result)) {
            for (const entry of result) assert.notEqual(entry.success, false, JSON.stringify(entry));
        }
        return result;
    }

    swayWindow(predicate) {
        function find(node) {
            if (predicate(node)) return node;
            for (const child of [...node.nodes, ...node.floating_nodes]) {
                const found = find(child);
                if (found) return found;
            }
        }
        return find(this.sway('-t', 'get_tree'));
    }

    async pointerCommand(command) {
        const response = once(this.pointerReader, 'line', {signal: AbortSignal.timeout(10000)});
        this.pointer.stdin.write(JSON.stringify(command) + '\n');
        assert.equal((await response)[0], 'ok');
    }

    async movePointer(x, y) {
        if (this.isWayland) {
            const output = this.sway('-t', 'get_outputs').find(output => output.active);
            await this.pointerCommand({type: 'move', x: Math.round(x), y: Math.round(y),
                width: output.rect.width, height: output.rect.height});
        } else {
            this.xdotool('mousemove', x, y);
        }
    }

    async pointerButton(pressed, button = 1) {
        if (this.isWayland) await this.pointerCommand({type: 'button', pressed, button: {1: 272, 8: 275, 9: 276}[button]});
        else this.xdotool(pressed ? 'mousedown' : 'mouseup', button);
    }

    async keyChord(handle, ...keys) {
        const native = await this.nativeWindow(handle);
        this.focus(native);
        await until(() => this.isWayland ? this.swayWindow(node => node.id === native).focused
            : this.xdotool('getactivewindow') === String(native), 'GTK keyboard window focused');
        if (this.isWayland) {
            execFileSync('wtype', [...keys.slice(0, -1).flatMap(key => ['-M', key]), '-k', keys.at(-1)]);
        } else {
            this.xdotool('key', keys.join('+'));
        }
    }

    async historyMouse(handle, direction, selector = '.monaco-editor') {
        const point = await this.point(handle, selector);
        await this.movePointer(point.x, point.y);
        const button = direction === -1 ? 8 : 9;
        await this.pointerButton(true, button);
        await this.pointerButton(false, button);
    }

    async closeWindow(handle) {
        await this.evaluate(handle, invoke('plugin:window|close'));
        await until(async () => !(await this.request(`/session/${this.session}/window/handles`)).includes(handle), 'GTK window closed');
    }

    focus(native) {
        if (this.isWayland) this.sway(`[con_id=${native}] focus`);
        else this.xdotool('windowactivate', '--sync', native);
    }

    async fileDialog(title) {
        return until(() => {
            if (this.isWayland) return this.swayWindow(node => node.name === title)?.id;
            try { return this.xdotool('search', '--onlyvisible', '--name', `^${title}$`).split('\n')[0]; }
            catch { return null; }
        }, `${title} dialog visible`);
    }

    closeFileDialog(native) {
        if (this.isWayland) this.sway(`[con_id=${native}] kill`);
        else {
            this.focus(native);
            this.xdotool('key', 'Escape');
        }
    }

    async geometry(handle, x, y, width = 1100, height = 750) {
        if (this.isWayland) {
            const native = await this.nativeWindow(handle);
            this.sway(`[con_id=${native}] floating enable, resize set width ${width} px height ${height} px, move position ${x} ${y}`);
            await pause(150);
            return;
        }
        await this.evaluate(handle, `(async () => {
            await ${invoke('plugin:window|set_size', {value: {Logical: {width, height}}})};
            await ${invoke('plugin:window|set_position', {value: {Physical: {x, y}}})};
        })()`);
        await pause(150);
    }

    async nativeWindow(handle) {
        const title = `Calcpad Linux Test ${handle}`;
        await this.evaluate(handle, invoke('plugin:window|set_title', {value: title}));
        if (this.isWayland) return until(() => this.swayWindow(node => node.name === title)?.id, 'Wayland window mapped');
        return this.xdotool('search', '--onlyvisible', '--name', `^${title}$`).split('\n')[0];
    }

    async point(handle, selector) {
        const native = await this.nativeWindow(handle);
        this.focus(native);
        const rect = this.isWayland ? this.swayWindow(node => node.id === native).rect : null;
        const geometry = rect ? {X: rect.x, Y: rect.y, WIDTH: rect.width, HEIGHT: rect.height}
            : Object.fromEntries(this.xdotool('getwindowgeometry', '--shell', native)
                .split('\n').map(line => line.split('=')));
        const x = Number(geometry.X) + Number(geometry.WIDTH) / 2;
        const y = Number(geometry.Y) + Number(geometry.HEIGHT) / 2;
        await this.movePointer(x - 20, y);
        await this.evaluate(handle, `window.testPointer = null;
            window.testPointerListener = event => {
                window.testPointer = {x: event.clientX, y: event.clientY, scale: devicePixelRatio};
            };
            document.addEventListener('pointermove', window.testPointerListener);`);
        await this.movePointer(x, y);
        await until(() => this.evaluate(handle, 'window.testPointer'), 'GTK pointer calibration');
        await pause(100);
        const pointer = await this.evaluate(handle, `(() => {
            document.removeEventListener('pointermove', window.testPointerListener);
            return window.testPointer;
        })()`);
        const targetRect = await this.evaluate(handle, `(() => {
            const rect = document.querySelector(${JSON.stringify(selector)}).getBoundingClientRect();
            return {x: rect.left + 18, y: rect.top + rect.height / 2};
        })()`);
        return {
            native,
            x: Math.round(x + (targetRect.x - pointer.x) * (this.isWayland ? 1 : pointer.scale)),
            y: Math.round(y + (targetRect.y - pointer.y) * (this.isWayland ? 1 : pointer.scale)),
        };
    }

    async drag(source, tabId, destination, selector, {cancel = false, outside = false, raiseDestination = false} = {}) {
        const from = await this.point(source, `[data-tab-id="${tabId}"]`);
        const to = outside ? {x: 3100, y: 1700} : await this.point(destination, selector);
        this.lastDrag = {from, to};
        this.focus(from.native);
        await this.movePointer(from.x, from.y);
        await this.pointerButton(true);
        try {
            if (this.isWayland) {
                await this.movePointer(from.x + 12, from.y);
                await pause(150);
            }
            if (raiseDestination) {
                if (this.isWayland) this.focus(to.native);
                else this.xdotool('windowraise', to.native);
            }
            for (let step = 1; step <= 12; step++) {
                await this.movePointer(Math.round(from.x + (to.x - from.x) * step / 12),
                    Math.round(from.y + (to.y - from.y) * step / 12));
                await pause(25);
            }
            if (this.isWayland) {
                await pause(100);
                await this.movePointer(to.x + 1, to.y);
                await pause(100);
                await this.movePointer(to.x, to.y);
                await pause(100);
            }
            if (cancel) {
                this.xdotool('key', 'Escape');
                await pause(100);
            }
        } finally {
            await this.pointerButton(false);
        }
        await pause(150);
    }

    async diagnostics(handle) {
        await this.evaluate(handle, '0');
        const screenshot = await this.request(`/session/${this.session}/screenshot`);
        writeFileSync(path.join(this.profile, 'screenshot.png'), Buffer.from(screenshot, 'base64'));
        const state = await this.evaluate(handle, `({title: document.title,
            tabs: window.calcpadTabs.all, errors: window.testErrors,
            position: window.calcpadActiveEditor.getPosition(), focused: document.hasFocus(),
            activeElement: document.activeElement?.tagName,
            pointer: window.testPointer, events: window.testDragEvents?.slice(-40),
            marker: document.querySelector('.tab-drop-marker')?.outerHTML})`);
        state.drag = this.lastDrag;
        state.nativeFocus = this.isWayland ? this.swayWindow(node => node.focused)?.name : this.xdotool('getactivewindow');
        state.windows = {};
        for (const [label, window] of await this.windows()) {
            state.windows[label] = await this.evaluate(window, `({tabs: window.calcpadTabs.all,
                events: window.testDragEvents?.slice(-40), marker: document.querySelector('.tab-drop-marker')?.outerHTML})`);
        }
        writeFileSync(path.join(this.profile, 'state.json'), JSON.stringify(state, null, 2));
    }

    async dispose() {
        if (this.session) await this.request(`/session/${this.session}`, undefined, 'DELETE');
        if (this.driver?.exitCode === null) {
            const exited = once(this.driver, 'exit');
            process.kill(-this.driver.pid, 'SIGTERM');
            await exited;
        }
        this.pointer?.kill();
        this.keyboard?.kill();
    }
}
