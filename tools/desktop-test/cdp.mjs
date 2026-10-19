// Drives the test app's webview: Chrome DevTools Protocol on Windows (WebView2), WebKit's remote
// inspector on Linux (WebKitGTK). launch.ps1 / launch.sh open the port.
//   node cdp.mjs eval "<js>"        evaluate in the main window, awaiting promises; prints the value
//   node cdp.mjs eval-file <file>   same, reading the expression from a file (avoids shell quoting)
//   node cdp.mjs menu <id>          fire a native menu item, e.g. export-pdf, restart-server
//   node cdp.mjs output             the Output panel's text and the status bar
//   node cdp.mjs targets
// Env: CDP_PORT (9223), CDP_TIMEOUT ms (20000). A native modal dialog can stall evaluation;
// dismiss it with ui.ps1 first.
import { readFileSync } from 'node:fs';

setTimeout(() => { console.log('CDP TIMEOUT'); process.exit(2); }, +(process.env.CDP_TIMEOUT || 20000));
const port = process.env.CDP_PORT || 9223;
const [, , cmd, arg] = process.argv;
const webkit = process.platform === 'linux';

const expressions = {
    eval: () => arg,
    'eval-file': () => readFileSync(arg, 'utf8'),
    menu: () => `window.__TAURI_INTERNALS__.invoke('plugin:event|emit_to', {
        target: { kind: 'WebviewWindow', label: 'main' }, event: 'menu-click', payload: { id: ${JSON.stringify(arg)} },
    }).then(() => 'menu ${arg} fired')`,
    // Shows the Output panel's current channel (App by default).
    output: () => `(() => {
        if (!document.querySelector('.output-list')) document.querySelector('.status-output')?.click();
        return new Promise(r => setTimeout(() => {
            const server = document.querySelector('.status-server');
            const rows = [...document.querySelectorAll('.output-row')].map(row =>
                row.querySelector('.output-level')?.innerText + ' ' + row.querySelector('.output-message')?.innerText);
            r('SERVER STATUS: ' + (server ? server.innerText.trim() + ' — ' + server.title : 'connected') + '\\n' + rows.join('\\n'));
        }, 400));
    })()`,
};

// Page targets as { title, url, socket }.
async function listTargets() {
    if (!webkit) {
        const pages = (await (await fetch(`http://127.0.0.1:${port}/json`)).json()).filter(t => t.type === 'page');
        return pages.map(t => ({ title: t.title, url: t.url, socket: t.webSocketDebuggerUrl }));
    }
    const html = await (await fetch(`http://127.0.0.1:${port}/`)).text();
    return [...html.matchAll(/class="targetname">(.*?)<\/div><div class="targeturl">(.*?)<\/div>.*?(\/socket\/\d+\/\d+\/WebPage)/g)]
        .map(([, title, url, path]) => ({ title, url, socket: `ws://127.0.0.1:${port}${path}` }));
}

// Resolves { value } or { exception }.
function evaluate(socket, expression) {
    const ws = new WebSocket(socket);
    let id = 0;
    const pending = new Map();
    let send = (method, params) => ws.send(JSON.stringify({ id: ++id, method, params }));
    const call = (method, params) => new Promise(resolve => { pending.set(id + 1, resolve); send(method, params); });
    const settle = (m) => { pending.get(m.id)?.(m); pending.delete(m.id); };

    return new Promise(resolve => {
        const run = async () => {
            if (!webkit) {
                const { result } = await call('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true });
                return resolve(result?.exceptionDetails
                    ? { exception: result.exceptionDetails.exception?.description ?? result.exceptionDetails.text }
                    : { value: result?.result?.value });
            }
            // WebKit has no awaitPromise on evaluate, so the result is wrapped and awaited separately.
            const wrapped = `Promise.resolve(eval(${JSON.stringify(expression)}))`;
            const evaluated = (await call('Runtime.evaluate', { expression: wrapped })).result;
            if (evaluated?.wasThrown) return resolve({ exception: evaluated.result?.description });
            const awaited = (await call('Runtime.awaitPromise', { promiseObjectId: evaluated.result.objectId, returnByValue: true })).result;
            resolve(awaited?.wasThrown ? { exception: awaited.result?.description } : { value: awaited?.result?.value });
        };

        ws.onmessage = e => {
            const m = JSON.parse(e.data);
            if (!webkit) return settle(m);
            // WebKit addresses the page through a target, announced once the socket opens.
            if (m.method === 'Target.targetCreated' && m.params.targetInfo.type === 'page' && id === 0) {
                const targetId = m.params.targetInfo.targetId;
                send = (method, params) => {
                    const n = ++id;
                    ws.send(JSON.stringify({
                        id: n, method: 'Target.sendMessageToTarget',
                        params: { targetId, message: JSON.stringify({ id: n, method, params }) },
                    }));
                };
                run();
            } else if (m.method === 'Target.dispatchMessageFromTarget') {
                settle(JSON.parse(m.params.message));
            }
        };
        if (!webkit) ws.onopen = run;
    });
}

const targets = await listTargets();
if (targets.length === 0) {
    console.log(`no page targets on port ${port}`);
    process.exit(2);
}
if (cmd === 'targets') {
    targets.forEach((t, i) => console.log(i, t.title, t.url));
    process.exit(0);
}
if (!expressions[cmd]) {
    console.log('usage: node cdp.mjs eval|eval-file|menu|output|targets [arg]');
    process.exit(2);
}

const { value, exception } = await evaluate(targets[0].socket, expressions[cmd]());
if (exception !== undefined) console.log('EXCEPTION', exception);
else console.log(typeof value === 'string' ? value : JSON.stringify(value ?? null, null, 1));
process.exit(0);
