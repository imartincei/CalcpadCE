import * as crypto from 'crypto';
import { execSync } from 'child_process';

/** Hosts Microsoft serves .NET runtime archives from. */
export const DOTNET_DOWNLOAD_HOSTS: readonly string[] = Object.freeze([
    'builds.dotnet.microsoft.com',
    'download.visualstudio.microsoft.com',
]);

/** Host serving the NuGet v3 API: registration leaves, catalog entries, and the flat container. */
export const NUGET_HOSTS: readonly string[] = Object.freeze([
    'api.nuget.org',
]);

export interface ExpectedSha512 {
    value: string;
    encoding: 'hex' | 'base64';
}

/**
 * Reject any URL that is not HTTPS on an allow-listed host. Applied to every
 * URL taken from fetched metadata, so a compromised or spoofed feed cannot
 * redirect a download to an arbitrary origin.
 */
export function assertAllowedUrl(url: string, allowedHosts: readonly string[], what: string): URL {
    let parsed: URL;
    try {
        parsed = new URL(url);
    } catch {
        throw new Error(`${what}: '${url}' is not a valid URL`);
    }

    if (parsed.protocol !== 'https:') {
        throw new Error(`${what}: refusing non-HTTPS URL '${url}'`);
    }

    // parsed.host includes the port, so an allow-listed name on an unexpected port is rejected.
    if (!allowedHosts.includes(parsed.host)) {
        throw new Error(
            `${what}: host '${parsed.host}' is not in the download allow-list (${allowedHosts.join(', ')})`
        );
    }

    return parsed;
}

/**
 * Compare a downloaded payload against the SHA512 published in its feed metadata.
 * A malformed expected digest fails the length check rather than passing silently.
 */
export function verifySha512(buffer: Buffer, expected: ExpectedSha512, what: string): void {
    const actual = crypto.createHash('sha512').update(buffer).digest();
    const expectedBytes = Buffer.from(expected.value, expected.encoding);

    if (expectedBytes.length !== actual.length || !crypto.timingSafeEqual(expectedBytes, actual)) {
        throw new Error(
            `${what}: SHA512 mismatch — expected ${expected.value}, got ` +
            `${actual.toString(expected.encoding)}. Refusing to use the download.`
        );
    }
}

/** GET JSON from an allow-listed host. */
export async function fetchJsonFromAllowedHost<T>(
    url: string,
    allowedHosts: readonly string[],
    what: string
): Promise<T> {
    assertAllowedUrl(url, allowedHosts, what);

    const response = await fetch(url);
    if (!response.ok) {
        throw new Error(`${what}: HTTP ${response.status} fetching ${url}`);
    }

    return await response.json() as T;
}

/**
 * Download a binary payload from an allow-listed host and verify its SHA512
 * before returning it. Callers must not write anything to disk until this resolves.
 */
export async function downloadVerified(options: {
    url: string;
    allowedHosts: readonly string[];
    expected: ExpectedSha512;
    what: string;
    log?: (message: string) => void;
}): Promise<Buffer> {
    const { url, allowedHosts, expected, what, log } = options;

    assertAllowedUrl(url, allowedHosts, what);

    const response = await fetch(url, { redirect: 'follow' });
    if (!response.ok) {
        throw new Error(`${what}: download failed with HTTP ${response.status}`);
    }

    // A followed redirect can land on a host we never allow-listed.
    if (response.url && response.url !== url) {
        assertAllowedUrl(response.url, allowedHosts, `${what} (after redirect)`);
    }

    const buffer = Buffer.from(await response.arrayBuffer());
    verifySha512(buffer, expected, what);

    log?.(`${what}: ${(buffer.length / 1024 / 1024).toFixed(1)} MB, SHA512 verified`);
    return buffer;
}

/**
 * Cross-platform .nupkg / .zip extractor. On Windows it calls
 * `[System.IO.Compression.ZipFile]::ExtractToDirectory` via PowerShell, because
 * `Expand-Archive` silently no-ops on signed `.nupkg` files and, unlike it, the .NET API
 * validates entry paths against the destination so a crafted archive cannot escape it.
 *
 * On Linux/macOS it shells out to `unzip`, surfacing stderr so a missing `unzip` is reported as
 * itself. Paths are passed via env vars on Windows so spaces and quotes in the extension path
 * cannot break shell quoting.
 */
export function extractZipToDir(zipPath: string, destDir: string): void {
    if (process.platform === 'win32') {
        try {
            execSync(
                'powershell -NoProfile -Command "Add-Type -AssemblyName System.IO.Compression.FileSystem; ' +
                '[System.IO.Compression.ZipFile]::ExtractToDirectory($env:CALCPAD_ZIP_SRC, $env:CALCPAD_ZIP_DST)"',
                {
                    timeout: 120000,
                    env: { ...process.env, CALCPAD_ZIP_SRC: zipPath, CALCPAD_ZIP_DST: destDir },
                    stdio: ['ignore', 'ignore', 'pipe'],
                }
            );
        } catch (err) {
            const stderr = (err as { stderr?: Buffer }).stderr?.toString().trim() || '';
            throw new Error(`Windows ZIP extract failed: ${stderr || (err as Error).message}`);
        }
    } else {
        try {
            execSync(`unzip -o "${zipPath}" -d "${destDir}"`, {
                timeout: 120000,
                stdio: ['ignore', 'ignore', 'pipe'],
            });
        } catch (err) {
            const stderr = (err as { stderr?: Buffer }).stderr?.toString().trim() || '';
            // ENOENT from execSync's spawn = `unzip` isn't on PATH at all.
            if ((err as { code?: string }).code === 'ENOENT' || /command not found|not recognized/i.test(stderr)) {
                throw new Error(
                    `\`unzip\` not found on PATH. Install it (e.g. \`apt install unzip\` / ` +
                    `\`brew install unzip\`) and reload the window to retry the bundle download.`
                );
            }
            throw new Error(`unzip failed: ${stderr || (err as Error).message}`);
        }
    }
}
