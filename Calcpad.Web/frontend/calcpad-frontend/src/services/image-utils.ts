/**
 * Pure, cross-platform image helpers shared by the web bridge, the Tauri
 * bridge, and the VS Code extension. No DOM/Node/VS Code imports — anything
 * platform-specific (clipboard access, file dialogs) lives in the consumer.
 */

export const IMAGE_EXTENSIONS = ['png', 'jpg', 'jpeg', 'gif', 'webp', 'svg'] as const;

/** How an inserted image is stored: inlined, or copied to disk (desktop only). */
export type ImageStorageMode = 'base64' | 'imagesFolder' | 'customPath';

/** Raw image chosen from a file dialog, before it is embedded or saved. */
export interface PickedImage {
    data: Uint8Array;
    mimeType: string;
    filename: string;
}

export const IMAGE_MIME_TYPES = [
    'image/png',
    'image/jpeg',
    'image/gif',
    'image/webp',
    'image/svg+xml',
] as const;

/** Accepts either a full filename (`foo.PNG`) or a bare extension (`png`). */
export function mimeFromExtension(filenameOrExt: string): string {
    const ext = filenameOrExt.toLowerCase().split('.').pop() ?? '';
    switch (ext) {
        case 'png':  return 'image/png';
        case 'jpg':
        case 'jpeg': return 'image/jpeg';
        case 'gif':  return 'image/gif';
        case 'webp': return 'image/webp';
        case 'svg':  return 'image/svg+xml';
        default:     return 'image/png';
    }
}

export function isImageExtension(ext: string): boolean {
    return (IMAGE_EXTENSIONS as readonly string[]).includes(ext.toLowerCase());
}

/** Wraps an image src in the CalcPad comment line convention: `'<img src="…">`. */
export function buildImageCommentLine(src: string): string {
    return `'<img src="${src}">`;
}

/** Base64-encode bytes using `btoa`. Available in both browsers and Node ≥ 16. */
export function bytesToBase64(bytes: ArrayBuffer | Uint8Array): string {
    const view = bytes instanceof Uint8Array ? bytes : new Uint8Array(bytes);
    let binary = '';
    const chunkSize = 0x8000;
    for (let i = 0; i < view.length; i += chunkSize) {
        const chunk = view.subarray(i, Math.min(i + chunkSize, view.length));
        binary += String.fromCharCode.apply(null, Array.from(chunk));
    }
    return btoa(binary);
}
