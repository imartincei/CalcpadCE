// Drives the test app's webview over Chrome DevTools Protocol (launch.ps1 opens the port).
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

const targets = (await (await fetch(`http://127.0.0.1:${port}/json`)).json()).filter(t => t.type === 'page');
if (cmd === 'targets') {
    targets.forEach((t, i) => console.log(i, t.title, t.url));
    process.exit(0);
}

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
if (!expressions[cmd]) {
    console.log('usage: node cdp.mjs eval|eval-file|menu|output|targets [arg]');
    process.exit(2);
}

const ws = new WebSocket(targets[0].webSocketDebuggerUrl);
await new Promise(r => ws.onopen = r);
ws.onmessage = e => {
    const m = JSON.parse(e.data);
    if (m.id !== 1) return;
    const r = m.result;
    if (r?.exceptionDetails) console.log('EXCEPTION', r.exceptionDetails.exception?.description ?? r.exceptionDetails.text);
    else console.log(typeof r?.result?.value === 'string' ? r.result.value : JSON.stringify(r?.result?.value ?? m, null, 1));
    process.exit(0);
};
ws.send(JSON.stringify({ id: 1, method: 'Runtime.evaluate', params: { expression: expressions[cmd](), awaitPromise: true, returnByValue: true } }));
