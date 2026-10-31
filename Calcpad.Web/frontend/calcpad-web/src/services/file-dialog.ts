import { invoke } from '@tauri-apps/api/core';
import { open as nativeOpen, save as nativeSave, type OpenDialogOptions, type SaveDialogOptions } from '@tauri-apps/plugin-dialog';
import { platform } from '@tauri-apps/plugin-os';

export async function open(options: OpenDialogOptions): Promise<string | string[] | null> {
    if (platform() !== 'linux') return nativeOpen(options);
    const paths = await invoke<string[] | null>('linux_file_dialog', { options, save: false });
    return options.multiple ? paths : paths?.[0] ?? null;
}

export async function save(options: SaveDialogOptions): Promise<string | null> {
    if (platform() !== 'linux') return nativeSave(options);
    const paths = await invoke<string[] | null>('linux_file_dialog', { options, save: true });
    return paths?.[0] ?? null;
}
