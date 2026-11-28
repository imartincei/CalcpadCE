// One-time codemod for data-driven labels that are consumed through t()/tt().
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const frontendRoot = path.resolve(here, '../..');
const messageIds = JSON.parse(fs.readFileSync(path.join(here, 'message-ids.json'), 'utf8'));

const targets = [
    { file: 'calcpad-frontend/src/types/settings.ts', props: ['label', 'detail'] },
    { file: 'calcpad-frontend/src/types/pdf-settings.ts', props: ['label', 'detail'] },
    { file: 'calcpad-frontend/src/text/ui-directive.ts', props: ['label', 'detail'] },
    { file: 'calcpad-frontend/src/services/keybindings.ts', props: ['label'], formatArgs: true },
    { file: 'calcpad-frontend/src/services/setting-actions.ts', props: ['label'], extraArgs: true },
    { file: 'calcpad-frontend/src/services/hotkeys.ts', props: [], tuples: true, validateReturn: true },
    { file: 'calcpad-frontend/src/vue/components/CalcpadFilesTab.vue', props: ['relative', 'absolute'] },
    { file: 'calcpad-frontend/src/vue/components/CalcpadInsertTab.vue', props: ['Function', 'Macro', 'Variable', 'Unit'] },
    { file: 'calcpad-frontend/src/vue/components/CalcpadExportTab.vue', props: ['label', 'detail'] },
    { file: 'calcpad-web/src/services/agent-chat.ts', props: ['label', 'hint'] },
    { file: 'calcpad-web/src/components/DesktopTitleBar.vue', props: ['label', 'title'] },
];

function decode(raw, quote) {
    try {
        if (quote === '"') return JSON.parse('"' + raw + '"');
        if (quote === "'") return JSON.parse('"' + raw.replace(/\\'/g, "'") + '"');
        return raw.replace(/\\`/g, '`').replace(/\\\$/g, '$');
    } catch {
        return raw;
    }
}

let total = 0;
for (const target of targets) {
    const file = path.join(frontendRoot, target.file);
    let text = fs.readFileSync(file, 'utf8');
    let changed = 0;
    const before = text;

    if (target.props.length) {
        const property = new RegExp(
            `(\\b(?:${target.props.join('|')})\\s*:\\s*)(['"\`])((?:(?!\\2)[^\\\\]|\\\\.)*)\\2`,
            'g',
        );
        text = text.replace(property, (match, head, quote, raw) => {
            if (quote === '`' && raw.includes('${')) return match;
            const english = decode(raw, quote);
            const id = messageIds[english];
            if (!id) return match;
            changed++;
            return `${head}'${id}'`;
        });
    }

    if (target.formatArgs) {
        text = text.replace(/format\((\s*'[^']*'\s*,\s*)'([^']+)'\)/g, (match, head, english) => {
            const id = messageIds[english];
            if (!id) return match;
            changed++;
            return `format(${head}'${id}')`;
        });
    }

    if (target.extraArgs) {
        text = text.replace(/extra\('([^']+)'/g, (match, english) => {
            const id = messageIds[english];
            if (!id) return match;
            changed++;
            return `extra('${id}'`;
        });
    }

    if (target.tuples) {
        text = text.replace(/\[('([^']+)')\s*,\s*'([^']+)'\]/g, (match, whole, key, english) => {
            const id = messageIds[english];
            if (!id) return match;
            changed++;
            return `[${whole}, '${id}']`;
        });
    }

    if (target.validateReturn) {
        const english = 'A hotkey needs Ctrl, Alt or {meta}, or a function key';
        const id = messageIds[english];
        if (id && text.includes(`return '${english}'`)) {
            text = text.replace(`return '${english}'`, `return '${id}'`);
            changed++;
        }
    }

    if (text !== before) {
        fs.writeFileSync(file, text);
        total += changed;
        console.log(`${target.file}: ${changed}`);
    }
}
console.log('replacements', total);
