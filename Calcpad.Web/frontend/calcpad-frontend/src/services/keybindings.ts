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
    { id: 'insertSearch', label: 'service.searchInsertItems', vscodeCommand: 'vscode-calcpad.showInsert', editorOnly: false },
    { id: 'runPreview', label: 'editor.runPreview', vscodeCommand: 'calcpad.runPreview', monacoAction: 'calcpad.runPreview', editorOnly: false },
    { id: 'focusPreviewToLine', label: 'editor.focusPreviewToLine', vscodeCommand: 'vscode-calcpad.focusPreviewToLine', monacoAction: 'calcpad.focusPreviewToLine', editorOnly: true },
    { id: 'editMetadata', label: 'editor.editMetadataProperties', vscodeCommand: 'vscode-calcpad.editMetadataProperties', monacoAction: 'calcpad.editMetadata', editorOnly: true },
    format('formatBold', 'service.bold'),
    format('formatItalic', 'service.italic'),
    format('formatUnderline', 'service.underline'),
    format('formatSubscript', 'service.subscript'),
    format('formatSuperscript', 'service.superscript'),
    format('formatHeading1', 'service.heading1'),
    format('formatHeading2', 'service.heading2'),
    format('formatHeading3', 'service.heading3'),
    format('formatHeading4', 'service.heading4'),
    format('formatHeading5', 'service.heading5'),
    format('formatHeading6', 'service.heading6'),
    format('formatParagraph', 'service.paragraph'),
    format('formatLineBreak', 'service.lineBreak'),
    format('formatBulletedList', 'service.bulletedList'),
    format('formatNumberedList', 'service.numberedList'),
    format('toggleComment', 'service.toggleComment'),
    format('uncomment', 'service.uncomment'),
    format('pasteAsComment', 'service.pasteAsComment'),
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
