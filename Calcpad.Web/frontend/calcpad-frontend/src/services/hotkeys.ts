/**
 * Canonical hotkeys are `Ctrl+Shift+Alt+Meta+Key`: only the held modifiers, in that order.
 * `Mod` means Cmd on macOS and Ctrl elsewhere.
 */

const MODIFIERS = ['Ctrl', 'Shift', 'Alt', 'Meta'] as const;

const CODE_KEYS: Record<string, string> = {
    Backquote: '`', Minus: '-', Equal: '=', BracketLeft: '[', BracketRight: ']',
    Backslash: '\\', Semicolon: ';', Quote: "'", Comma: ',', Period: '.', Slash: '/',
    ArrowUp: 'Up', ArrowDown: 'Down', ArrowLeft: 'Left', ArrowRight: 'Right',
    Home: 'Home', End: 'End', PageUp: 'PageUp', PageDown: 'PageDown',
    Insert: 'Insert', Delete: 'Delete', Enter: 'Enter', Tab: 'Tab', Space: 'Space',
    Escape: 'Escape', Backspace: 'Backspace',
};

// Lower-cased name → canonical key, covering canonical names and VS Code's spellings.
const KEY_ALIASES: Record<string, string> = (() => {
    const out: Record<string, string> = {};
    for (const name of Object.values(CODE_KEYS)) out[name.toLowerCase()] = name;
    Object.assign(out, { esc: 'Escape', return: 'Enter', del: 'Delete', ins: 'Insert', pgup: 'PageUp', pgdn: 'PageDown' });
    return out;
})();

const MODIFIER_ALIASES: Record<string, typeof MODIFIERS[number]> = {
    ctrl: 'Ctrl', control: 'Ctrl', shift: 'Shift', alt: 'Alt', option: 'Alt',
    meta: 'Meta', cmd: 'Meta', command: 'Meta', win: 'Meta', super: 'Meta',
};

export function isMacPlatform(): boolean {
    if (typeof process !== 'undefined' && process.platform) return process.platform === 'darwin';
    return typeof navigator !== 'undefined' && /Mac|iPhone|iPad/.test(navigator.platform);
}

function keyFromCode(code: string): string | null {
    if (/^Key[A-Z]$/.test(code)) return code.slice(3);
    if (/^Digit[0-9]$/.test(code)) return code.slice(5);
    if (/^F([1-9]|1[0-9]|2[0-4])$/.test(code)) return code;
    if (/^Numpad[0-9]$/.test(code)) return code;
    return CODE_KEYS[code] ?? null;
}

function normalizeKeyName(raw: string): string | null {
    const lower = raw.toLowerCase();
    if (/^[a-z0-9]$/.test(lower)) return lower.toUpperCase();
    if (/^f([1-9]|1[0-9]|2[0-4])$/.test(lower)) return lower.toUpperCase();
    if (/^numpad[0-9]$/.test(lower)) return 'Numpad' + lower.slice(6);
    // VS Code scan-code form, e.g. `[KeyK]`.
    const scan = /^\[(\w+)\]$/.exec(raw);
    if (scan) return keyFromCode(scan[1]);
    return KEY_ALIASES[lower] ?? (raw.length === 1 && Object.values(CODE_KEYS).includes(raw) ? raw : null);
}

function compose(mods: Set<string>, key: string): string {
    return [...MODIFIERS.filter(m => mods.has(m)), key].join('+');
}

/** Canonical hotkey for a keydown, or null for a bare modifier or an unmapped key. */
export function hotkeyFromEvent(event: Pick<KeyboardEvent, 'code' | 'ctrlKey' | 'shiftKey' | 'altKey' | 'metaKey'>): string | null {
    const key = keyFromCode(event.code);
    if (!key) return null;
    const mods = new Set<string>();
    if (event.ctrlKey) mods.add('Ctrl');
    if (event.shiftKey) mods.add('Shift');
    if (event.altKey) mods.add('Alt');
    if (event.metaKey) mods.add('Meta');
    return compose(mods, key);
}

/** Parses `ctrl+shift+k`, `Mod+K`, or VS Code's spelling into canonical form; chords keep their first stroke. */
export function normalizeHotkey(raw: string, mac = isMacPlatform()): string | null {
    const first = raw.trim().split(/\s+/)[0];
    if (!first) return null;
    // `+` itself as the key, e.g. `ctrl++`.
    const parts = first.endsWith('++') ? [...first.slice(0, -2).split('+'), '+'] : first.split('+');
    const mods = new Set<string>();
    let key: string | null = null;
    for (const part of parts) {
        const lower = part.toLowerCase();
        if (lower === 'mod') mods.add(mac ? 'Meta' : 'Ctrl');
        else if (MODIFIER_ALIASES[lower]) mods.add(MODIFIER_ALIASES[lower]);
        else if (key === null) key = part === '+' ? '=' : normalizeKeyName(part);
        else return null;
    }
    return key ? compose(mods, key) : null;
}

/** Display form: Cmd/Option on macOS. */
export function formatHotkey(hotkey: string, mac = isMacPlatform()): string {
    if (!mac) return hotkey;
    return hotkey.split('+').map(p => p === 'Meta' ? 'Cmd' : p === 'Alt' ? 'Option' : p).join('+');
}

/** VS Code `keybindings.json` spelling. */
export function toVSCodeKey(hotkey: string, platform: string): string {
    const meta = platform === 'darwin' ? 'cmd' : platform === 'win32' ? 'win' : 'meta';
    return hotkey.split('+').map(p => {
        if (p === 'Ctrl') return 'ctrl';
        if (p === 'Shift') return 'shift';
        if (p === 'Alt') return 'alt';
        if (p === 'Meta') return meta;
        return p.toLowerCase();
    }).join('+');
}

/** Custom hotkeys need Ctrl, Alt or Meta (or a function key), so plain typing is never captured. */
export function validateHotkey(hotkey: string): string | null {
    const parts = hotkey.split('+');
    const key = parts[parts.length - 1];
    if (/^F\d+$/.test(key)) return null;
    if (parts.some(p => p === 'Ctrl' || p === 'Alt' || p === 'Meta')) return null;
    return 'A hotkey needs Ctrl, Alt or {meta}, or a function key';
}

/** Stored form: the platform's primary modifier becomes `Mod`, so a settings file works on any OS. */
export function toPortableHotkey(hotkey: string, mac = isMacPlatform()): string {
    const parts = hotkey.split('+');
    const primary = mac ? 'Meta' : 'Ctrl';
    if (!parts.includes(primary)) return hotkey;
    return ['Mod', ...parts.filter(p => p !== primary)].join('+');
}

export interface HotkeyBinding {
    hotkey: string;
    label: string;
}

// Fixed shortcuts that can't be rebound: desktop app commands and common editor defaults.
const RESERVED_HOTKEY_TABLE: Array<[string, string]> = [
    ['Mod+S', 'Save'], ['Mod+Shift+S', 'Save As'], ['Mod+O', 'Open'], ['Mod+N', 'New Tab'],
    ['Mod+W', 'Close Tab'], ['Mod+Alt+W', 'New Window'], ['Mod+P', 'Toggle Preview'],
    ['Mod+\\', 'Split Editor'], ['Mod+,', 'Open Settings'], ['Mod+E', 'Export PDF'],
    ['Mod+Shift+B', 'Hide the sidebar'], ['Alt+Z', 'Toggle Word Wrap'], ['Mod+D', 'Duplicate Line'],
    ['F5', 'Run Preview'], ['F1', 'Command Palette'],
    ['Mod+C', 'Copy'], ['Mod+X', 'Cut'], ['Mod+V', 'Paste'], ['Mod+A', 'Select All'],
    ['Mod+Z', 'Undo'], ['Mod+Y', 'Redo'], ['Mod+Shift+Z', 'Redo'],
    ['Mod+F', 'Find'], ['Mod+H', 'Replace'], ['Mod+G', 'Go to Line'],
    ['Mod+/', 'Toggle Line Comment'], ['Mod+[', 'Outdent'], ['Mod+]', 'Indent'],
    ['Mod+K', 'Chord prefix'], ['Mod+Shift+K', 'Delete Line'], ['Mod+Shift+P', 'Command Palette'],
    ['Mod+Shift+F', 'Search'], ['Mod+Shift+E', 'Explorer'], ['Mod+J', 'Toggle Panel'],
    ['Mod+Enter', 'Insert Line Below'], ['Mod+Shift+Enter', 'Insert Line Above'],
    ['Alt+Up', 'Move Line Up'], ['Alt+Down', 'Move Line Down'],
    ['Alt+Left', 'Navigate Back'], ['Alt+Right', 'Navigate Forward'],
    ['Mod+Tab', 'Switch Tab'], ['Mod+Shift+Tab', 'Switch Tab'],
    ['Mod+PageUp', 'Previous Tab'], ['Mod+PageDown', 'Next Tab'], ['Alt+F4', 'Close Window'],
];

export function reservedHotkeys(mac = isMacPlatform()): HotkeyBinding[] {
    const out: HotkeyBinding[] = [];
    for (const [keys, label] of RESERVED_HOTKEY_TABLE) {
        const hotkey = normalizeHotkey(keys, mac);
        if (hotkey) out.push({ hotkey, label });
    }
    return out;
}
