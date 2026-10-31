// Regenerates calcpad-web/src/editor/monaco-zh-map.json.
//
// Monaco's npm ESM build inlines English and has no NLS index table, so the map is
// derived by pairing the English fallback literals in the min build with the zh-cn
// NLS array shipped by the same package (same index = same message).
import fs from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const here = path.dirname(fileURLToPath(import.meta.url))
const frontendRoot = path.resolve(here, '..')
const webRoot = path.join(frontendRoot, 'calcpad-web')
const monacoRoot = [
    path.join(webRoot, 'node_modules/monaco-editor'),
    path.join(frontendRoot, 'node_modules/monaco-editor'),
].find(candidate => fs.existsSync(candidate))

if (!monacoRoot) {
    console.error('monaco-editor is not installed; run npm ci in calcpad-web first')
    process.exit(1)
}

const min = fs.readFileSync(path.join(monacoRoot, 'min/vs/editor/editor.main.js'), 'utf8')
const callPattern = /\(\s*(\d{1,4})\s*,\s*"((?:[^"\\]|\\.)*)"/g
const english = new Map()
let match
while ((match = callPattern.exec(min))) {
    const index = Number(match[1])
    if (english.has(index)) continue
    try {
        english.set(index, JSON.parse(`"${match[2]}"`))
    } catch {
        // Skip literals with escapes JSON does not accept.
    }
}

const zhSource = fs.readFileSync(path.join(monacoRoot, 'min/vs/nls.messages.zh-cn.js'), 'utf8')
delete globalThis._VSCODE_NLS_MESSAGES
const previousDefine = globalThis.define
globalThis.define = (_deps, factory) => factory()
try {
    new Function(zhSource)()
} finally {
    globalThis.define = previousDefine
}
const zh = globalThis._VSCODE_NLS_MESSAGES
if (!Array.isArray(zh)) {
    console.error('could not read the zh-cn NLS array')
    process.exit(1)
}

// One English string can occur at several indices. Keep it only when every index
// agrees on the translation; otherwise the English text would silently pick one
// context's wording, so leave those in English.
const byEnglish = new Map()
const ambiguous = new Set()
for (const index of [...english.keys()].sort((a, b) => a - b)) {
    const source = english.get(index)
    const target = zh[index]
    if (typeof target !== 'string' || !target || target === source) continue
    const existing = byEnglish.get(source)
    if (existing === undefined) byEnglish.set(source, target)
    else if (existing !== target) ambiguous.add(source)
}
for (const source of ambiguous) byEnglish.delete(source)

const map = {}
for (const source of [...byEnglish.keys()].sort()) map[source] = byEnglish.get(source)

const output = path.join(webRoot, 'src/editor/monaco-zh-map.json')
fs.writeFileSync(output, JSON.stringify(map))
console.log(`wrote ${Object.keys(map).length} entries to ${path.relative(frontendRoot, output)}`)
if (ambiguous.size) {
    console.log(`skipped ${ambiguous.size} ambiguous string(s): ${[...ambiguous].sort().join(' | ')}`)
}
