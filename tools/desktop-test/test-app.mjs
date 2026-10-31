import { spawn, execFileSync } from 'node:child_process';
import { once } from 'node:events';
import { existsSync, mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { createServer } from 'node:net';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

export const pause = ms => new Promise(resolve => setTimeout(resolve, ms));
export const invoke = (command, args = {}) =>
    `window.__TAURI_INTERNALS__.invoke(${JSON.stringify(command)}, ${JSON.stringify(args)})`;

export async function until(check, description, timeoutMs = 20000) {
    const deadline = Date.now() + timeoutMs;
    while (Date.now() < deadline) {
        const result = await check();
        if (result) return result;
        await pause(100);
    }
    throw new Error(`Timed out: ${description}`);
}

async function unusedPort() {
    const server = createServer();
    server.listen(0, '127.0.0.1');
    await once(server, 'listening');
    const port = server.address().port;
    await new Promise(resolve => server.close(resolve));
    return port;
}

export class TestApp {
    directory = fileURLToPath(new URL('.work/window-tests/', import.meta.url));
    profile = path.resolve(process.env.APPDATA, 'org.calcpad-ce.window-tests');
    fixtures = path.join(this.profile, 'fixtures');
    executable = path.join(this.directory, 'app/calcpad-window-tests.exe');
    desktop = process.env.CALCPAD_TEST_DESKTOP ?? 'CalcpadWindowTests';
    runner = null;
    launches = 0;
    windowStates = [];

    constructor() {
        if (path.dirname(this.profile) !== path.resolve(process.env.APPDATA)
            || path.basename(this.profile) !== 'org.calcpad-ce.window-tests') {
            throw new Error('Unexpected test profile path');
        }
        rmSync(this.profile, {recursive: true, force: true});
        mkdirSync(this.fixtures, {recursive: true});
    }

    async start(args = []) {
        const deadline = Date.now() + 60000;
        this.windowStates = [];
        this.port = await unusedPort();
        this.endpoint = `http://127.0.0.1:${this.port}`;
        this.runner = this.launch(args);
        this.exited = once(this.runner, 'exit');
        try {
            await until(async () => {
                if (this.runner.exitCode !== null || this.runner.signalCode !== null) {
                    throw new Error(`Application launcher exited: ${this.runner.exitCode ?? this.runner.signalCode}`);
                }
                try { return (await fetch(`${this.endpoint}/json`)).ok; }
                catch { return false; }
            }, 'debug port', deadline - Date.now());
            const main = await this.window('main', deadline - Date.now());
            await pause(300);
            return main;
        } catch (error) {
            const log = path.join(this.directory, `launch-${this.launches - 1}.log`);
            throw new Error(`${error.message}\nLast window states: ${JSON.stringify(this.windowStates)}\n${log}\n${existsSync(log) ? readFileSync(log, 'utf8') : 'No application log was created.'}`, {cause: error});
        }
    }

    launch(args) {
        return spawn(path.join(this.directory, 'helpers/deskrun.exe'), [
            this.desktop, '180', '--log', path.join(this.directory, `launch-${this.launches++}.log`),
            '--', this.executable, ...args,
        ], {cwd: path.dirname(this.executable), windowsHide: true, stdio: ['ignore', 'inherit', 'inherit'], env: {
            ...process.env, CALCPAD_TEST_DEBUG_PORT: String(this.port),
        }});
    }

    async relaunch(args = []) {
        const runner = this.launch(args);
        const [code] = await once(runner, 'exit');
        if (code !== 0) throw new Error(`Repeat launch exited with ${code}`);
    }

    async evaluate(page, expression, timeoutMs = 10000) {
        const result = await this.cdp(page, 'Runtime.evaluate', {
            expression, awaitPromise: true, returnByValue: true,
        }, timeoutMs);
        if (result.exceptionDetails) throw new Error(JSON.stringify(result.exceptionDetails));
        return result.result.value;
    }

    async cdp(page, method, params, timeoutMs = 10000) {
        const socket = new WebSocket(page.webSocketDebuggerUrl);
        try {
            return await new Promise((resolve, reject) => {
                const timeout = setTimeout(() => reject(new Error('Evaluation timed out')), timeoutMs);
                socket.onerror = () => {
                    clearTimeout(timeout);
                    reject(new Error('Debugger connection failed'));
                };
                socket.onopen = () => socket.send(JSON.stringify({id: 1, method, params}));
                socket.onmessage = event => {
                    const result = JSON.parse(event.data);
                    if (result.id !== 1) return;
                    clearTimeout(timeout);
                    if (result.error) reject(new Error(JSON.stringify(result.error)));
                    else resolve(result.result);
                };
            });
        } finally { socket.close(); }
    }

    async ctrlClick(page, position) {
        const point = await this.evaluate(page, `(() => {
            const editor = window.calcpadActiveEditor;
            editor.revealPositionInCenter(${JSON.stringify(position)});
            const visible = editor.getScrolledVisiblePosition(${JSON.stringify(position)});
            const bounds = editor.getDomNode().getBoundingClientRect();
            return {x: bounds.left + visible.left + 2, y: bounds.top + visible.top + visible.height / 2};
        })()`);
        await this.cdp(page, 'Input.dispatchMouseEvent', {type: 'mouseMoved', ...point, modifiers: 2});
        await pause(500);
        await this.cdp(page, 'Input.dispatchMouseEvent', {
            type: 'mousePressed', ...point, modifiers: 2, button: 'left', clickCount: 1,
        });
        await this.cdp(page, 'Input.dispatchMouseEvent', {
            type: 'mouseReleased', ...point, modifiers: 2, button: 'left', clickCount: 1,
        });
    }

    async windows() {
        const pages = (await (await fetch(`${this.endpoint}/json`)).json()).filter(page => page.type === 'page');
        const result = new Map();
        this.windowStates = [];
        for (const page of pages) {
            // A page that is still loading or closing may never answer; treat it as not ready.
            const state = await this.evaluate(page,
                `({label: window.__TAURI_INTERNALS__?.metadata.currentWindow.label,
                    ready: !!window.calcpadTabs && !!document.querySelector('.tab-drop-marker'),
                    documentState: document.readyState, text: window.calcpadTabs ? undefined : document.body?.innerText.slice(0, 2000)})`, 3000)
                .catch(error => ({ready: false, error: error.message}));
            this.windowStates.push({url: page.url, ...state});
            if (state.ready) result.set(state.label, page);
        }
        return result;
    }

    window(label, timeoutMs) {
        return until(async () => (await this.windows()).get(label), `${label} ready`, timeoutMs);
    }

    async newWindow(page) {
        const label = await this.evaluate(page, invoke('new_window'));
        const opened = await this.window(label);
        await pause(300);
        return {label, page: opened};
    }

    async close(page, label) {
        await this.evaluate(page,
            `setTimeout(() => { void ${invoke('plugin:window|close', {label})}; }, 50); 'closing'`);
    }

    async closeAndWait(page, label) {
        await this.close(page, label);
        await until(async () => !(await this.windows()).has(label), `${label} closed`);
    }

    async stop(page, label) {
        await this.close(page, label);
        const [code] = await this.exited;
        if (code !== 0) throw new Error(`Application exited with ${code}`);
        this.runner = null;
    }

    geometry(page) {
        return this.evaluate(page, `(async () => ({
            position: await ${invoke('plugin:window|outer_position')},
            size: await ${invoke('plugin:window|inner_size')},
            maximized: await ${invoke('plugin:window|is_maximized')},
        }))()`);
    }

    async setGeometry(page, logicalSize, position) {
        await this.evaluate(page, invoke('plugin:window|set_size', {value: {Logical: logicalSize}}));
        await this.evaluate(page, invoke('plugin:window|set_position', {value: {Physical: position}}));
        await pause(100);
        return this.geometry(page);
    }

    async focus(page, label) {
        const title = `Calcpad window test focus: ${label}`;
        await this.evaluate(page, invoke('plugin:window|set_title', {label, value: title}));
        execFileSync(path.join(this.directory, 'helpers/deskrun.exe'), [this.desktop, 'notify-focus', title], {windowsHide: true});
        await pause(100);
    }

    fixture(name, content = 'a = 1') {
        const file = path.join(this.fixtures, name);
        writeFileSync(file, content);
        return file;
    }

    fileOpen(page, file) {
        return until(async () => this.evaluate(page,
            `window.calcpadTabs.all.some(tab => tab.filePath?.toLowerCase() === ${JSON.stringify(file.toLowerCase())})`),
        `${path.basename(file)} open`);
    }

    async healthy(page) {
        return this.evaluate(page, `(async () => {
            const url = await ${invoke('server_url')};
            if (!url) return false;
            const token = await ${invoke('server_token')};
            return (await fetch(url + '/api/calcpad/health', {headers: {'X-Calcpad-Token': token}})).ok;
        })()`);
    }

    serverProcesses(action) {
        const executable = path.join(this.directory, 'app/Calcpad.Server.exe').replaceAll("'", "''");
        return execFileSync('powershell', ['-NoProfile', '-Command',
            `$servers = @(Get-CimInstance Win32_Process -Filter "Name='Calcpad.Server.exe'" | Where-Object { $_.ExecutablePath -eq '${executable}' }); ${action}`,
        ], {encoding: 'utf8'}).trim();
    }

    async crashAndRecover(page) {
        const oldUrl = await this.evaluate(page, invoke('server_url'));
        this.serverProcesses("if ($servers.Count -ne 1) { throw 'Expected one test backend' }; Stop-Process -Id $servers[0].ProcessId -Force");
        await until(async () => {
            const url = await this.evaluate(page, invoke('server_url'));
            return url && url !== oldUrl && await this.healthy(page);
        }, 'backend recovered');
    }

    dispose() {
        const executable = this.executable.replaceAll("'", "''");
        execFileSync('powershell', ['-NoProfile', '-Command',
            `Get-CimInstance Win32_Process -Filter "Name='calcpad-window-tests.exe'" | Where-Object { $_.ExecutablePath -eq '${executable}' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }`,
        ], {encoding: 'utf8'});
        this.runner?.kill();
    }
}
