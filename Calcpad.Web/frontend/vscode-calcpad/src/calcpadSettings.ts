import * as vscode from 'vscode';
import * as path from 'path';
import * as fs from 'fs';
import {
    CalcpadSettings,
    CalcpadExtras,
    getDefaultSettings,
    getDefaultExtras,
    getDefaultSettingsBlob,
    deserializeSettingsBlob,
    serializeSettingsBlob,
    buildApiSettings,
    getExtraString,
    getExtraBool,
    getExtraNumber,
    getExtraObject,
    coerceWriteMode,
    writesAllowed,
} from 'calcpad-frontend';
import type { WriteMode } from 'calcpad-frontend';

export type { CalcpadSettings, CalcpadExtras };

const SETTINGS_DIR_NAME = 'settings';
const DEFAULT_PRESET_NAME = 'default';
// The one file that reflects live user state. Written on every edit,
// read at boot. Preset files (default.json, <name>.json) are read-only
// source-of-truth snapshots — the settings editor never writes to them.
const ACTIVE_SETTINGS_FILE = 'active-settings.json';
const ACTIVE_PRESET_KEY = 'calcpad-active-preset';

// The only key that stays as a workspace configuration entry, because
// contributes.keybindings clauses read `config.calcpad.enableFormattingHotkeys`
// and there is no way to expose our JSON-backed value to a `when` clause.
const HOTKEYS_WORKSPACE_KEY = 'enableFormattingHotkeys';
const HOTKEYS_EXTRA_KEY = 'formattingHotkeys';

let _outputChannel: vscode.OutputChannel | undefined;

function getOutputChannel(): vscode.OutputChannel {
    if (!_outputChannel) {
        _outputChannel = vscode.window.createOutputChannel('CalcpadCE Settings');
    }
    return _outputChannel;
}

/**
 * On-disk layout under `<globalStorage>/settings/`:
 *   active-settings.json  — live state, written on every edit
 *   default.json          — pristine defaults, refreshed on every activation
 *   <name>.json           — user-created presets, never written by the editor
 *
 * The "active preset" name is remembered in globalState purely for the
 * dropdown label — it doesn't gate writes. Any edit lands in
 * active-settings.json regardless of which preset was last loaded.
 */
export class CalcpadSettingsManager {
    private static instance: CalcpadSettingsManager;
    private _settings: CalcpadSettings;
    private _extras: CalcpadExtras;
    private _activePresetName: string = DEFAULT_PRESET_NAME;
    private _onDidChangeSettings = new vscode.EventEmitter<CalcpadSettings>();
    public readonly onDidChangeSettings = this._onDidChangeSettings.event;
    private _context?: vscode.ExtensionContext;
    private _localServerUrl?: string;
    /** Resolves once the on-disk config has been loaded. */
    public readonly ready: Promise<void>;

    private constructor(context?: vscode.ExtensionContext) {
        this._settings = getDefaultSettings();
        this._extras = getDefaultExtras();
        if (context) {
            this._context = context;
        }
        this.ready = this.loadFromDisk();
    }

    public static getInstance(context?: vscode.ExtensionContext): CalcpadSettingsManager {
        if (!CalcpadSettingsManager.instance) {
            CalcpadSettingsManager.instance = new CalcpadSettingsManager(context);
        }
        if (context && !CalcpadSettingsManager.instance._context) {
            CalcpadSettingsManager.instance._context = context;
            // Re-run load with context now available (initial constructor call
            // may have skipped disk access if context was missing).
            CalcpadSettingsManager.instance.loadFromDisk();
        }
        return CalcpadSettingsManager.instance;
    }

    public getSettings(): CalcpadSettings {
        return { ...this._settings };
    }

    public getExtras(): CalcpadExtras {
        return { ...this._extras };
    }

    public getExtra(key: string, defaultValue: string = ''): string {
        return getExtraString(this._extras, key, defaultValue);
    }

    public getExtraBool(key: string, defaultValue: boolean): boolean {
        return getExtraBool(this._extras, key, defaultValue);
    }

    public getWriteMode(): WriteMode {
        return coerceWriteMode(this.getExtra('writeMode'));
    }

    /** Whether a render with these flags may run `#write`/`#append`. */
    public mayWrite(forPrint: boolean, enableUi = false): boolean {
        return writesAllowed(this.getWriteMode(), forPrint, enableUi);
    }

    /** The "Apply #UI Values in Preview" setting: Preview renders entered values, not declared ones. */
    public previewAppliesUiOverrides(): boolean {
        return this.getExtraBool('previewUiOverrides', false);
    }

    public getExtraNumber(key: string, defaultValue: number): number {
        return getExtraNumber(this._extras, key, defaultValue);
    }

    public getExtraObject<T>(key: string, defaultValue: T): T {
        return getExtraObject(this._extras, key, defaultValue);
    }

    /** Persist an extra to active-settings.json (string, boolean, number, or object). */
    public setExtra(key: string, value: unknown): void {
        if (value === null || value === undefined) {
            this._extras[key] = '';
        } else if (typeof value === 'object') {
            this._extras[key] = JSON.stringify(value);
        } else {
            this._extras[key] = String(value);
        }
        if (key === HOTKEYS_EXTRA_KEY) {
            void this.mirrorFormattingHotkeys();
        }
        void this.saveActiveSettings();
    }

    /**
     * Returns the effective server URL for API calls.
     * Uses the local server URL if a bundled server is running,
     * otherwise falls back to the user-configured remote URL.
     */
    public getServerUrl(): string {
        return this._localServerUrl ?? this._settings.server.url;
    }

    /** Returns the user-configured remote server URL (always from settings, ignores local server). */
    public getRemoteServerUrl(): string {
        return this._settings.server.url;
    }

    public updateSettings(newSettings: Partial<CalcpadSettings>): void {
        this._settings = { ...this._settings, ...newSettings };
        void this.saveActiveSettings();
        this._onDidChangeSettings.fire(this._settings);
    }

    /**
     * Sets the local server URL (from the running bundled server).
     * Ephemeral — not persisted.
     */
    public setLocalServerUrl(url: string): void {
        this._localServerUrl = url;
        this._onDidChangeSettings.fire(this._settings);
    }

    /** Load the pristine `default` preset into active-settings. */
    public async resetSettings(): Promise<void> {
        await this.loadPreset(DEFAULT_PRESET_NAME);
    }

    public async getApiSettings(): Promise<unknown> {
        const apiSettings = buildApiSettings(this._settings);

        const outputChannel = getOutputChannel();
        outputChannel.appendLine('API settings being sent:');
        outputChannel.appendLine(`  Local Server URL: ${this._localServerUrl ?? '(none)'}`);
        outputChannel.appendLine(`  Remote Server URL: ${this._settings.server.url}`);
        outputChannel.appendLine(`  Effective Server URL: ${this.getServerUrl()}`);

        return apiSettings;
    }

    // ---- Preset file I/O ----

    /** Name of the most recently loaded/saved preset (dropdown label). */
    public getActivePresetName(): string {
        return this._activePresetName;
    }

    /**
     * Enumerate presets in the settings folder. `default` is always first;
     * `active-settings.json` is not a preset and is excluded.
     */
    public async listPresets(): Promise<string[]> {
        const dir = this.getSettingsDir();
        if (!dir) return [DEFAULT_PRESET_NAME];
        try {
            const entries = await fs.promises.readdir(dir, { withFileTypes: true });
            const names = entries
                .filter(e => e.isFile() && e.name.toLowerCase().endsWith('.json'))
                .map(e => e.name.slice(0, -'.json'.length))
                .filter(n => n !== ACTIVE_SETTINGS_FILE.replace(/\.json$/i, ''));
            const rest = names.filter(n => n !== DEFAULT_PRESET_NAME).sort();
            return [DEFAULT_PRESET_NAME, ...rest];
        } catch {
            return [DEFAULT_PRESET_NAME];
        }
    }

    /**
     * Save current in-memory settings as a preset. Rejects the reserved
     * `default` name and filename-illegal characters. Also updates the
     * active-preset label to the saved name.
     */
    public async savePreset(rawName: string): Promise<{ ok: true } | { ok: false; message: string }> {
        const name = (rawName ?? '').trim();
        if (!name) return { ok: false, message: 'Config name cannot be empty.' };
        if (name.toLowerCase() === DEFAULT_PRESET_NAME) {
            return { ok: false, message: 'The "default" preset is protected and cannot be overridden.' };
        }
        if (!this.isValidPresetName(name)) {
            return { ok: false, message: 'Config name contains invalid characters.' };
        }
        const presetPath = this.getPresetPath(name);
        if (!presetPath) return { ok: false, message: 'Settings folder is unavailable.' };
        const blob = serializeSettingsBlob(this._settings, this._extras);
        try {
            await fs.promises.mkdir(path.dirname(presetPath), { recursive: true });
            await fs.promises.writeFile(presetPath, JSON.stringify(blob, null, 4), 'utf8');
            getOutputChannel().appendLine(`[Settings] savePreset -> "${name}" file="${presetPath}"`);
        } catch (err) {
            const msg = err instanceof Error ? err.message : String(err);
            return { ok: false, message: `Failed to write preset: ${msg}` };
        }
        await this.setActivePresetName(name);
        return { ok: true };
    }

    /**
     * Load a preset's data into the live active-settings state. Writes the
     * preset's contents to active-settings.json so the preset itself stays
     * untouched.
     */
    public async loadPreset(name: string): Promise<void> {
        if (!this.isValidPresetName(name)) {
            getOutputChannel().appendLine(`[Settings] loadPreset refused invalid name "${name}"`);
            return;
        }
        getOutputChannel().appendLine(`[Settings] loadPreset("${name}") — was active="${this._activePresetName}"`);
        await this.readPresetInto(name);
        await this.setActivePresetName(name);
        await this.saveActiveSettings();
        await this.mirrorFormattingHotkeys();
        this._onDidChangeSettings.fire(this._settings);
    }

    public async openSettingsFolder(): Promise<void> {
        const dir = this.getSettingsDir();
        if (!dir) return;
        try { await fs.promises.mkdir(dir, { recursive: true }); } catch { /* already exists */ }
        try {
            await vscode.env.openExternal(vscode.Uri.file(dir));
        } catch (err) {
            const msg = err instanceof Error ? err.message : String(err);
            getOutputChannel().appendLine(`[Settings] Failed to open settings folder: ${msg}`);
        }
    }

    // ---- Internal ----

    private getSettingsDir(): string | null {
        if (!this._context) return null;
        return path.join(this._context.globalStorageUri.fsPath, SETTINGS_DIR_NAME);
    }

    /**
     * A preset name is a filename component, so it may not contain a path
     * separator, a drive letter, a `..`, or anything Windows rejects outright.
     * Applied on read as well as write: `savePreset` validated already, but the
     * `switchConfig` webview message reaches `loadPreset` with a name that never
     * passed through it, and that name is joined straight onto the settings dir.
     */
    private isValidPresetName(name: string): boolean {
        return name.length > 0
            && name.length <= 128
            && name !== '.' && name !== '..'
            && !/[\\/:*?"<>|\x00-\x1f]/.test(name);
    }

    private getPresetPath(name: string): string | null {
        if (!this.isValidPresetName(name)) return null;
        const dir = this.getSettingsDir();
        return dir ? path.join(dir, `${name}.json`) : null;
    }

    private getActiveSettingsPath(): string | null {
        const dir = this.getSettingsDir();
        return dir ? path.join(dir, ACTIVE_SETTINGS_FILE) : null;
    }

    /**
     * Ensure the settings folder exists and `default.json` is fresh from the
     * bundled defaults. Runs on activation so `default` always represents
     * pristine defaults. Never touches active-settings.json.
     */
    private async ensureSettingsFolder(): Promise<void> {
        const dir = this.getSettingsDir();
        if (!dir) return;
        await fs.promises.mkdir(dir, { recursive: true });
        const defaultPath = this.getPresetPath(DEFAULT_PRESET_NAME);
        if (defaultPath) {
            const blob = getDefaultSettingsBlob();
            try {
                await fs.promises.writeFile(defaultPath, JSON.stringify(blob, null, 4), 'utf8');
            } catch (err) {
                const msg = err instanceof Error ? err.message : String(err);
                getOutputChannel().appendLine(`[Settings] Could not refresh default.json: ${msg}`);
            }
        }
    }

    private async setActivePresetName(name: string): Promise<void> {
        this._activePresetName = name;
        if (this._context) {
            await this._context.globalState.update(ACTIVE_PRESET_KEY, name);
        }
    }

    private loadActivePresetName(): string {
        if (!this._context) return DEFAULT_PRESET_NAME;
        const stored = this._context.globalState.get<string>(ACTIVE_PRESET_KEY);
        return (stored ?? '').trim() || DEFAULT_PRESET_NAME;
    }

    /**
     * Boot-time load:
     *   1. Refresh default.json from bundled defaults.
     *   2. Read active-settings.json into memory. If missing, seed it from
     *      the last-active preset (or default) and write it out.
     *   3. Restore active-preset label from globalState.
     */
    private async loadFromDisk(): Promise<void> {
        if (!this._context) {
            getOutputChannel().appendLine(`[Settings] loadFromDisk skipped — no context yet`);
            return;
        }
        await this.ensureSettingsFolder();
        this._activePresetName = this.loadActivePresetName();
        getOutputChannel().appendLine(`[Settings] loadFromDisk — active-preset="${this._activePresetName}"`);

        const loaded = await this.readActiveSettings();
        if (!loaded) {
            // First run (or the file was deleted): seed from the last-active
            // preset, then write it back so subsequent boots skip the seed.
            await this.readPresetInto(this._activePresetName);
            await this.saveActiveSettings();
        }
        await this.mirrorFormattingHotkeys();
    }

    /**
     * Read active-settings.json into `_settings/_extras`. Returns false when
     * the file doesn't exist / is unreadable so the caller can decide how to
     * seed it. On a parse error, leaves in-memory state at defaults and logs.
     */
    private async readActiveSettings(): Promise<boolean> {
        const activePath = this.getActiveSettingsPath();
        if (!activePath) return false;
        try {
            const raw = await fs.promises.readFile(activePath, 'utf8');
            const parsed = JSON.parse(raw);
            const { settings, extras } = deserializeSettingsBlob(parsed);
            this._settings = settings;
            this._extras = extras;
            return true;
        } catch (err) {
            const code = (err as NodeJS.ErrnoException | undefined)?.code;
            if (code !== 'ENOENT') {
                const msg = err instanceof Error ? err.message : String(err);
                getOutputChannel().appendLine(
                    `[Settings] readActiveSettings failed for ${activePath}: ${msg}`
                );
            }
            return false;
        }
    }

    /**
     * Load a preset file (or bundled defaults for "default") into
     * `_settings/_extras`. Does NOT write active-settings.json — callers
     * that need the change persisted must call saveActiveSettings afterward.
     */
    private async readPresetInto(name: string): Promise<void> {
        // "default" always reflects the bundled defaults; skip the disk read
        // so a corrupted default.json can't wedge us at boot.
        if (name === DEFAULT_PRESET_NAME) {
            this._settings = getDefaultSettings();
            this._extras = getDefaultExtras();
            return;
        }
        const presetPath = this.getPresetPath(name);
        if (!presetPath) return;
        try {
            const raw = await fs.promises.readFile(presetPath, 'utf8');
            const parsed = JSON.parse(raw);
            const { settings, extras } = deserializeSettingsBlob(parsed);
            this._settings = settings;
            this._extras = extras;
        } catch (err) {
            const msg = err instanceof Error ? err.message : String(err);
            getOutputChannel().appendLine(
                `[Settings] readPresetInto("${name}") failed, falling back to defaults: ${msg}`
            );
            this._settings = getDefaultSettings();
            this._extras = getDefaultExtras();
        }
    }

    /** Write in-memory state to active-settings.json. */
    private async saveActiveSettings(): Promise<void> {
        const activePath = this.getActiveSettingsPath();
        if (!activePath) return;
        const blob = serializeSettingsBlob(this._settings, this._extras);
        try {
            await fs.promises.mkdir(path.dirname(activePath), { recursive: true });
            await fs.promises.writeFile(activePath, JSON.stringify(blob, null, 4), 'utf8');
        } catch (err) {
            const msg = err instanceof Error ? err.message : String(err);
            getOutputChannel().appendLine(`[Settings] Failed to write ${activePath}: ${msg}`);
        }
    }

    /**
     * Mirror the JSON-backed `formattingHotkeys` extra onto the workspace
     * setting `calcpad.enableFormattingHotkeys`, because keybinding `when`
     * clauses can only reference `config.*` values from workspace/user config.
     */
    private async mirrorFormattingHotkeys(): Promise<void> {
        const desired = this.getExtraBool(HOTKEYS_EXTRA_KEY, true);
        const workspace = vscode.workspace.getConfiguration('calcpad');
        const current = workspace.get<boolean>(HOTKEYS_WORKSPACE_KEY);
        if (current !== desired) {
            try {
                await workspace.update(HOTKEYS_WORKSPACE_KEY, desired, vscode.ConfigurationTarget.Global);
            } catch (err) {
                const msg = err instanceof Error ? err.message : String(err);
                getOutputChannel().appendLine(`[Settings] Could not mirror ${HOTKEYS_WORKSPACE_KEY}: ${msg}`);
            }
        }
    }
}
