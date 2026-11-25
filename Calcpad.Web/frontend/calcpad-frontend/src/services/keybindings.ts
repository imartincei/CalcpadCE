import { DEFAULT_KEYBINDINGS, readCustomizations } from './customizations';
import { isMacPlatform, normalizeHotkey, reservedHotkeys } from './hotkeys';
import type { HotkeyBinding } from './hotkeys';

export interface KeybindingCommand {
    id: string;
    label: string;
    vscodeCommand: string;
    /** Monaco action run by the desktop dispatcher; insertSearch is handled by the host. */
    monacoAction?: string;
    /** Fires only while a text editor has focus. */
    editorOnly: boolean;
}

const format = (id: string, label: string): KeybindingCommand =>
    ({ id, label, vscodeCommand: 'vscode-calcpad.' + id, monacoAction: 'calcpad.' + id, editorOnly: true });

/** Rebindable commands; their default hotkeys live in settings.default.json `keybindings`. */
export const KEYBINDING_COMMANDS: KeybindingCommand[] = [
    { id: 'insertSearch', label: 'Search Insert Items', vscodeCommand: 'vscode-calcpad.showInsert', editorOnly: false },
    { id: 'runPreview', label: 'Run Preview', vscodeCommand: 'calcpad.runPreview', monacoAction: 'calcpad.runPreview', editorOnly: false },
    { id: 'focusPreviewToLine', label: 'Focus Preview to Line', vscodeCommand: 'vscode-calcpad.focusPreviewToLine', monacoAction: 'calcpad.focusPreviewToLine', editorOnly: true },
    { id: 'editMetadata', label: 'Edit Metadata Properties', vscodeCommand: 'vscode-calcpad.editMetadataProperties', monacoAction: 'calcpad.editMetadata', editorOnly: true },
    format('formatBold', 'Bold'),
    format('formatItalic', 'Italic'),
    format('formatUnderline', 'Underline'),
    format('formatSubscript', 'Subscript'),
    format('formatSuperscript', 'Superscript'),
    format('formatHeading1', 'Heading 1'),
    format('formatHeading2', 'Heading 2'),
    format('formatHeading3', 'Heading 3'),
    format('formatHeading4', 'Heading 4'),
    format('formatHeading5', 'Heading 5'),
    format('formatHeading6', 'Heading 6'),
    format('formatParagraph', 'Paragraph'),
    format('formatLineBreak', 'Line Break'),
    format('formatBulletedList', 'Bulleted List'),
    format('formatNumberedList', 'Numbered List'),
    format('toggleComment', 'Toggle Comment'),
    format('uncomment', 'Uncomment'),
    format('pasteAsComment', 'Paste as Comment'),
];

export const SETTING_KEYBINDING_PREFIX = 'setting:';
export const RUN_SETTING_HOTKEY_COMMAND = 'vscode-calcpad.runSettingHotkey';

export function keybindingCommand(id: string): KeybindingCommand | undefined {
    return KEYBINDING_COMMANDS.find(c => c.id === id);
}

export function defaultKeybinding(id: string): string {
    return DEFAULT_KEYBINDINGS[id] ?? '';
}

/** Canonical hotkey → binding id, for dispatching a keydown. */
export function buildHotkeyMap(keybindings: Record<string, string>, mac = isMacPlatform()): Map<string, string> {
    const map = new Map<string, string>();
    for (const [id, raw] of Object.entries(keybindings)) {
        const hotkey = raw ? normalizeHotkey(raw, mac) : null;
        if (hotkey && !map.has(hotkey)) map.set(hotkey, id);
    }
    return map;
}

let hotkeyMapCache: { raw: string | undefined; mac: boolean; map: Map<string, string> } | null = null;

/** buildHotkeyMap for the raw `keybindings` extra, cached so a keydown handler can call it freely. */
export function hotkeyMapFromSetting(raw: string | undefined, mac = isMacPlatform()): Map<string, string> {
    if (hotkeyMapCache && hotkeyMapCache.raw === raw && hotkeyMapCache.mac === mac) return hotkeyMapCache.map;
    const keybindings = readCustomizations(key => key === 'keybindings' ? raw : undefined).keybindings;
    const map = buildHotkeyMap(keybindings, mac);
    hotkeyMapCache = { raw, mac, map };
    return map;
}

export type KeybindingConflict =
    | { kind: 'reserved'; label: string }
    | { kind: 'binding'; id: string };

/** What a canonical hotkey would collide with, or null when it is free. */
export function findKeybindingConflict(
    hotkey: string,
    ownId: string,
    keybindings: Record<string, string>,
    extraReserved: HotkeyBinding[] = [],
    mac = isMacPlatform(),
): KeybindingConflict | null {
    const taken = [...reservedHotkeys(mac), ...extraReserved].find(b => b.hotkey === hotkey);
    if (taken) return { kind: 'reserved', label: taken.label };
    for (const [id, raw] of Object.entries(keybindings)) {
        if (id !== ownId && raw && normalizeHotkey(raw, mac) === hotkey) return { kind: 'binding', id };
    }
    return null;
}
