import * as vscode from 'vscode';
import * as fs from 'fs';
import * as path from 'path';
import { applyEdits, modify, parse } from 'jsonc-parser';
import type { FormattingOptions, ParseError } from 'jsonc-parser';
import {
    DEFAULT_KEYBINDINGS,
    KEYBINDING_COMMANDS,
    RUN_SETTING_HOTKEY_COMMAND,
    SETTING_KEYBINDING_PREFIX,
    normalizeHotkey,
    toVSCodeKey,
} from 'calcpad-frontend';
import type { HotkeyBinding } from 'calcpad-frontend';

// VS Code can't bind keys at runtime, so the `keybindings` setting is mirrored into keybindings.json.

interface KeybindingEntry {
    key: string;
    command: string;
    when?: string;
    args?: unknown;
}

const SETTING_HOTKEY_WHEN = "editorLangId == 'calcpad' || editorLangId == 'plaintext' || focusedView == 'calcpadVueUI'";
const mac = process.platform === 'darwin';

const MANAGED_COMMANDS = new Set([
    ...KEYBINDING_COMMANDS.map(c => c.vscodeCommand),
    RUN_SETTING_HOTKEY_COMMAND,
]);

/** The profile's keybindings.json, beside the globalStorage folder. */
export function keybindingsPath(context: vscode.ExtensionContext): string {
    return path.join(path.dirname(path.dirname(context.globalStorageUri.fsPath)), 'keybindings.json');
}

function vscodeKey(raw: string): string | null {
    const hotkey = normalizeHotkey(raw, mac);
    return hotkey ? toVSCodeKey(hotkey, process.platform) : null;
}

function contributedWhen(context: vscode.ExtensionContext): Map<string, string | undefined> {
    const out = new Map<string, string | undefined>();
    const contributed = context.extension.packageJSON?.contributes?.keybindings;
    if (Array.isArray(contributed)) {
        for (const kb of contributed) if (kb?.command && !out.has(kb.command)) out.set(kb.command, kb.when);
    }
    return out;
}

function desiredEntries(context: vscode.ExtensionContext, keybindings: Record<string, string>): KeybindingEntry[] {
    const when = contributedWhen(context);
    const entries: KeybindingEntry[] = [];
    for (const command of KEYBINDING_COMMANDS) {
        const current = keybindings[command.id] ?? '';
        const fallback = DEFAULT_KEYBINDINGS[command.id] ?? '';
        if (normalizeHotkey(current, mac) === normalizeHotkey(fallback, mac)) continue;
        const removed = fallback ? vscodeKey(fallback) : null;
        if (removed) entries.push({ key: removed, command: '-' + command.vscodeCommand });
        const added = current ? vscodeKey(current) : null;
        if (added) {
            const condition = when.get(command.vscodeCommand);
            entries.push({ key: added, command: command.vscodeCommand, ...(condition ? { when: condition } : {}) });
        }
    }
    for (const [id, raw] of Object.entries(keybindings)) {
        if (!id.startsWith(SETTING_KEYBINDING_PREFIX) || !raw) continue;
        const key = vscodeKey(raw);
        if (key) {
            entries.push({
                key,
                command: RUN_SETTING_HOTKEY_COMMAND,
                when: SETTING_HOTKEY_WHEN,
                args: { id: id.slice(SETTING_KEYBINDING_PREFIX.length) },
            });
        }
    }
    return entries;
}

const isManaged = (entry: unknown): boolean => {
    const command = (entry as KeybindingEntry | null)?.command;
    return typeof command === 'string' && MANAGED_COMMANDS.has(command.replace(/^-/, ''));
};

/** Rewrites the managed entries in keybindings.json, touching the file only when they change. */
export async function syncKeybindings(
    context: vscode.ExtensionContext,
    keybindings: Record<string, string>,
    log: (message: string) => void,
): Promise<void> {
    const file = keybindingsPath(context);
    let text = '';
    try {
        text = await fs.promises.readFile(file, 'utf8');
    } catch {
        // No file yet.
    }
    const errors: ParseError[] = [];
    const existing = parse(text || '[]', errors, { allowTrailingComma: true });
    if (errors.length) {
        log(`[Keybindings] ${file} has syntax errors; not updated.`);
        return;
    }
    if (!Array.isArray(existing)) {
        log(`[Keybindings] ${file} is not a JSON array; not updated.`);
        return;
    }
    const desired = desiredEntries(context, keybindings);
    const current = existing.filter(isManaged);
    if (JSON.stringify(current) === JSON.stringify(desired)) return;
    if (desired.length === 0 && current.length === 0) return;

    const formatting: FormattingOptions = { insertSpaces: true, tabSize: 4, eol: '\n' };
    let next = text.trim() ? text : '[\n]\n';
    for (let i = existing.length - 1; i >= 0; i--) {
        if (isManaged(existing[i])) next = applyEdits(next, modify(next, [i], undefined, { formattingOptions: formatting }));
    }
    for (const entry of desired) {
        next = applyEdits(next, modify(next, [-1], entry, { formattingOptions: formatting, isArrayInsertion: true }));
    }
    try {
        await fs.promises.mkdir(path.dirname(file), { recursive: true });
        await fs.promises.writeFile(file, next, 'utf8');
        log(`[Keybindings] Updated ${desired.length} CalcpadCE entries in ${file}`);
    } catch (err) {
        log(`[Keybindings] Could not write ${file}: ${err instanceof Error ? err.message : String(err)}`);
    }
}

function platformKey(kb: Record<string, unknown>): string | undefined {
    const specific = mac ? kb.mac : process.platform === 'win32' ? kb.win : kb.linux;
    const key = typeof specific === 'string' ? specific : kb.key;
    return typeof key === 'string' ? key : undefined;
}

/** Hotkeys bound by other extensions and the user's own keybindings, for the conflict check. */
export async function collectVSCodeHotkeys(context: vscode.ExtensionContext): Promise<HotkeyBinding[]> {
    const out: HotkeyBinding[] = [];
    const add = (raw: string | undefined, label: string) => {
        const hotkey = raw ? normalizeHotkey(raw, mac) : null;
        if (hotkey) out.push({ hotkey, label });
    };
    for (const ext of vscode.extensions.all) {
        if (ext.id === context.extension.id) continue;
        const contributed = ext.packageJSON?.contributes?.keybindings;
        if (!Array.isArray(contributed)) continue;
        const name = ext.packageJSON?.displayName ?? ext.id;
        for (const kb of contributed) {
            if (kb && typeof kb.command === 'string') add(platformKey(kb), `${kb.command} (${name})`);
        }
    }
    try {
        const user = parse(await fs.promises.readFile(keybindingsPath(context), 'utf8'));
        if (Array.isArray(user)) {
            for (const kb of user) {
                if (!kb || typeof kb.command !== 'string' || kb.command.startsWith('-') || isManaged(kb)) continue;
                add(typeof kb.key === 'string' ? kb.key : undefined, `${kb.command} (keybindings.json)`);
            }
        }
    } catch {
        // No user keybindings.
    }
    return out;
}
