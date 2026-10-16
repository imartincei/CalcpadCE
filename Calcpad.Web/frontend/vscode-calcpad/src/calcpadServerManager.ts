import * as vscode from 'vscode';
import type { ILogger } from 'calcpad-frontend';
import * as path from 'path';
import * as fs from 'fs';
import { BaseServerManager } from './baseServerManager';
import {
    NUGET_HOSTS,
    assertAllowedUrl,
    downloadVerified,
    extractZipToDir,
    fetchJsonFromAllowedHost,
} from './downloadVerification';

const SKIASHARP_VERSION = '3.119.1';

/**
 * NuGet v3 registration leaf. Resolves to the flat-container download URL plus
 * the catalog entry that publishes the package's SHA512 — the v2
 * `/api/v2/package/{id}/{version}` endpoint we used before has no hash at all.
 */
const NUGET_REGISTRATION_BASE = 'https://api.nuget.org/v3/registration5-semver1';

interface ResolvedNupkg {
    url: string;
    /** Base64 SHA512 from the catalog entry's `packageHash`. */
    hash: string;
}

interface PlatformNativeInfo {
    nugetPackage: string;
    rid: string;
    nativeFileName: string;
}

/**
 * NuGet dependencies stripped from the bundled bin/ and downloaded on first activation, same
 * shape and lifecycle as SkiaSharp's native libs: fetch the .nupkg, extract the DLLs we need
 * into bin/, never re-download if they are already present. Versions must stay in lock-step
 * with Calcpad.Server.deps.json, and sync-bundled-server.mjs strips these same DLLs before
 * packaging — keep both lists in sync when bumping a package.
 */
interface ExternalManagedDep {
    /** NuGet package id, used to build the v2 download URL. */
    nugetPackage: string;
    /** Exact version pinned in Calcpad.Server.deps.json. */
    version: string;
    /**
     * DLL filenames (without folder) that must end up in bin/. Resolved by
     * scanning lib/<tfm>/ inside the .nupkg in TFM-preference order.
     */
    dlls: string[];
}

// Mapping derived from Calcpad.Server.deps.json — that's the authoritative
// source for which package each DLL ships in (DocumentFormat.OpenXml splits
// across two packages, etc.). Re-derive after any backend csproj change:
//
//   node -e "const d=require('./Calcpad.Server.deps.json').targets;
//            for (const t of Object.values(d)) for (const [id,info] of Object.entries(t))
//                if (info.runtime) console.log(id, Object.keys(info.runtime));"
const EXTERNAL_MANAGED_DEPS: ExternalManagedDep[] = [
    { nugetPackage: 'DocumentFormat.OpenXml', version: '3.3.0', dlls: ['DocumentFormat.OpenXml.dll'] },
    { nugetPackage: 'DocumentFormat.OpenXml.Framework', version: '3.3.0', dlls: ['DocumentFormat.OpenXml.Framework.dll'] },
    { nugetPackage: 'PuppeteerSharp', version: '21.1.1', dlls: ['PuppeteerSharp.dll'] },
    { nugetPackage: 'WebDriverBiDi', version: '0.0.43', dlls: ['WebDriverBiDi.dll'] },
    {
        // PDFsharp meta-package ships all 9 PdfSharp.* DLLs under one nupkg —
        // single download fans out into every PdfSharp file we strip.
        nugetPackage: 'PDFsharp',
        version: '6.2.0',
        dlls: [
            'PdfSharp.dll',
            'PdfSharp.BarCodes.dll',
            'PdfSharp.Charting.dll',
            'PdfSharp.Cryptography.dll',
            'PdfSharp.Quality.dll',
            'PdfSharp.Shared.dll',
            'PdfSharp.Snippets.dll',
            'PdfSharp.System.dll',
            'PdfSharp.WPFonts.dll',
        ],
    },
];

/** TFM fallback chain. Highest-version-first; first folder containing a DLL wins. */
const TFM_PREFERENCE = ['net10.0', 'net9.0', 'net8.0', 'net7.0', 'net6.0', 'netstandard2.1', 'netstandard2.0'];

function getPlatformNativeInfo(): PlatformNativeInfo | null {
    const platform = process.platform;
    const arch = process.arch;

    if (platform === 'linux' && arch === 'x64') {
        return {
            nugetPackage: 'SkiaSharp.NativeAssets.Linux',
            rid: 'linux-x64',
            nativeFileName: 'libSkiaSharp.so'
        };
    } else if (platform === 'win32' && arch === 'x64') {
        return {
            nugetPackage: 'SkiaSharp.NativeAssets.Win32',
            rid: 'win-x64',
            nativeFileName: 'libSkiaSharp.dll'
        };
    } else if (platform === 'win32' && arch === 'arm64') {
        return {
            nugetPackage: 'SkiaSharp.NativeAssets.Win32',
            rid: 'win-arm64',
            nativeFileName: 'libSkiaSharp.dll'
        };
    } else if (platform === 'darwin' && arch === 'x64') {
        return {
            nugetPackage: 'SkiaSharp.NativeAssets.macOS',
            rid: 'osx-x64',
            nativeFileName: 'libSkiaSharp.dylib'
        };
    } else if (platform === 'darwin' && arch === 'arm64') {
        return {
            nugetPackage: 'SkiaSharp.NativeAssets.macOS',
            rid: 'osx-arm64',
            nativeFileName: 'libSkiaSharp.dylib'
        };
    }

    return null;
}

/**
 * VS Code extension server manager that extends the base CalcpadServerManager
 * with SkiaSharp native library management and VS Code progress notifications.
 */
export class CalcpadServerManager extends BaseServerManager implements vscode.Disposable {
    private extensionPath: string;
    private outputChannel: ILogger;

    /**
     * @param outputChannel Server debug channel — receives stdout (verbose server output).
     * @param mainOutputChannel Main extension log — receives stderr only. Falls back to outputChannel if omitted.
     */
    constructor(
        extensionPath: string,
        outputChannel: ILogger,
        dotnetPath: string = 'dotnet',
        mainOutputChannel?: ILogger
    ) {
        super(
            extensionPath,
            outputChannel,
            dotnetPath,
            mainOutputChannel
        );
        this.extensionPath = extensionPath;
        this.outputChannel = outputChannel;
    }

    public override async start(): Promise<void> {
        await this.ensureExternalManagedDeps();
        await this.ensureNativeLibs();
        return super.start();
    }

    /**
     * Ensure the chunky managed NuGet DLLs (DocumentFormat.OpenXml, PuppeteerSharp,
     * PDFsharp …) that we strip from the VSIX before packaging
     * are present in bin/. Same pattern as ensureNativeLibs — download once,
     * cache permanently in bin/, never re-fetch.
     */
    private async ensureExternalManagedDeps(): Promise<void> {
        const binDir = path.join(this.extensionPath, 'bin');
        const missing = EXTERNAL_MANAGED_DEPS.filter(dep =>
            dep.dlls.some(dll => !fs.existsSync(path.join(binDir, dll)))
        );
        if (missing.length === 0) {
            this.outputChannel.appendLine('[ServerManager] All external managed deps already present');
            return;
        }

        this.outputChannel.appendLine(
            `[ServerManager] Missing managed deps: ${missing.map(d => `${d.nugetPackage}/${d.version}`).join(', ')}`
        );

        await vscode.window.withProgress({
            location: vscode.ProgressLocation.Notification,
            title: 'CalcpadCE: Downloading server libraries...',
            cancellable: false
        }, async (progress) => {
            for (let i = 0; i < missing.length; i++) {
                const dep = missing[i];
                progress.report({
                    message: `${dep.nugetPackage} (${i + 1}/${missing.length})`,
                    increment: 100 / missing.length
                });
                await this.downloadManagedDep(dep, binDir);
            }
        });
    }

    private async downloadManagedDep(dep: ExternalManagedDep, binDir: string): Promise<void> {
        const tmpDir = path.join(binDir, '.tmp-nupkg');
        try {
            const extractDir = await this.fetchAndUnzipNupkg(dep.nugetPackage, dep.version, tmpDir);

            for (const dll of dep.dlls) {
                const source = this.findDllInLibFolders(extractDir, dll);
                if (!source) {
                    throw new Error(
                        `${dll} not found in ${dep.nugetPackage}/${dep.version} ` +
                        `(searched lib/${TFM_PREFERENCE.join(',')}/)`
                    );
                }
                const target = path.join(binDir, dll);
                fs.copyFileSync(source, target);
                this.outputChannel.appendLine(`[ServerManager] Installed ${dll} from ${dep.nugetPackage}/${dep.version}`);
            }
        } finally {
            try { fs.rmSync(tmpDir, { recursive: true, force: true }); }
            catch { /* best-effort */ }
        }
    }

    /**
     * Walk lib/{tfm}/ folders in TFM-preference order and return the first
     * filesystem path that contains `dllName`. Returns null if nothing matches —
     * callers turn that into a fatal "package layout changed" error.
     */
    private findDllInLibFolders(extractDir: string, dllName: string): string | null {
        const libDir = path.join(extractDir, 'lib');
        if (!fs.existsSync(libDir)) return null;
        for (const tfm of TFM_PREFERENCE) {
            const candidate = path.join(libDir, tfm, dllName);
            if (fs.existsSync(candidate)) return candidate;
        }
        return null;
    }

    /**
     * Download a NuGet package's .nupkg and extract it into a fresh subdir under `tmpDir`,
     * returning the extracted directory. The caller is responsible for cleaning up `tmpDir`.
     */
    private async fetchAndUnzipNupkg(packageId: string, version: string, tmpDir: string): Promise<string> {
        this.outputChannel.appendLine(`[ServerManager] Fetching ${packageId} v${version}`);

        const buffer = await this.downloadVerifiedNupkg(packageId, version);

        // Use a unique subdir per call so concurrent extractions don't clash.
        const slot = path.join(tmpDir, `${packageId}-${version}`);
        fs.mkdirSync(slot, { recursive: true });
        const nupkgPath = path.join(slot, 'package.nupkg');
        fs.writeFileSync(nupkgPath, buffer);

        const extractDir = path.join(slot, 'extracted');
        // Clean slate — a partial extract from a prior failed run can poison
        // both extraction back-ends (Expand-Archive silently no-ops on a
        // populated dir; ZipFile.ExtractToDirectory throws on conflicts).
        fs.rmSync(extractDir, { recursive: true, force: true });
        fs.mkdirSync(extractDir, { recursive: true });

        extractZipToDir(nupkgPath, extractDir);

        // Signed .nupkg files have made Expand-Archive return success with an
        // empty output directory more than once; this check turns that silent
        // failure into a clear error message instead of "DLL not found".
        const libDir = path.join(extractDir, 'lib');
        if (!fs.existsSync(libDir)) {
            throw new Error(
                `Extraction of ${packageId}/${version} produced no lib/ directory at ${libDir}. ` +
                `The .nupkg downloaded successfully but the unzip step appears to have silently failed.`
            );
        }
        return extractDir;
    }

    /**
     * Resolve a pinned package to its flat-container URL and published SHA512.
     * Both the registration leaf and the catalog entry it points at are checked
     * against the NuGet host allow-list before being followed.
     */
    private async resolveNupkg(packageId: string, version: string): Promise<ResolvedNupkg> {
        // The v3 API keys packages by lowercase id and normalized version.
        const id = packageId.toLowerCase();
        const normalizedVersion = version.toLowerCase();
        const what = `${packageId} v${version} metadata`;

        const leaf = await fetchJsonFromAllowedHost<{
            packageContent?: string;
            catalogEntry?: string;
        }>(`${NUGET_REGISTRATION_BASE}/${id}/${normalizedVersion}.json`, NUGET_HOSTS, what);

        if (!leaf.packageContent || !leaf.catalogEntry) {
            throw new Error(`${what}: registration leaf has no packageContent/catalogEntry`);
        }

        const entry = await fetchJsonFromAllowedHost<{
            packageHash?: string;
            packageHashAlgorithm?: string;
        }>(leaf.catalogEntry, NUGET_HOSTS, `${what} (catalog entry)`);

        if (entry.packageHashAlgorithm !== 'SHA512' || !entry.packageHash) {
            throw new Error(
                `${what}: expected a SHA512 packageHash, got ` +
                `'${entry.packageHashAlgorithm ?? 'none'}'. Refusing to download unverifiable content.`
            );
        }

        assertAllowedUrl(leaf.packageContent, NUGET_HOSTS, `${packageId} v${version} download`);
        return { url: leaf.packageContent, hash: entry.packageHash };
    }

    /** Download a pinned .nupkg and verify it against the hash NuGet publishes for it. */
    private async downloadVerifiedNupkg(packageId: string, version: string): Promise<Buffer> {
        const resolved = await this.resolveNupkg(packageId, version);

        return await downloadVerified({
            url: resolved.url,
            allowedHosts: NUGET_HOSTS,
            expected: { value: resolved.hash, encoding: 'base64' },
            what: `${packageId} v${version}`,
            log: (message) => this.outputChannel.appendLine(`[ServerManager] ${message}`),
        });
    }

    private async ensureNativeLibs(): Promise<void> {
        const info = getPlatformNativeInfo();
        if (!info) {
            this.outputChannel.appendLine(`[ServerManager] Unsupported platform: ${process.platform}-${process.arch}`);
            throw new Error(`Unsupported platform: ${process.platform}-${process.arch}. Cannot resolve SkiaSharp native library.`);
        }

        const binDir = path.join(this.extensionPath, 'bin');
        const nativeDir = path.join(binDir, 'runtimes', info.rid, 'native');
        const nativeLibPath = path.join(nativeDir, info.nativeFileName);

        if (fs.existsSync(nativeLibPath)) {
            this.outputChannel.appendLine(`[ServerManager] Native lib already present: ${nativeLibPath}`);
            return;
        }

        this.outputChannel.appendLine(`[ServerManager] Native lib missing for ${info.rid}, downloading from NuGet...`);

        await vscode.window.withProgress({
            location: vscode.ProgressLocation.Notification,
            title: 'CalcpadCE: Downloading native libraries...',
            cancellable: false
        }, async (progress) => {
            await this.downloadNativeLib(info, binDir, progress);
        });
    }

    private async downloadNativeLib(
        info: PlatformNativeInfo,
        binDir: string,
        progress: vscode.Progress<{ increment?: number; message?: string }>
    ): Promise<void> {
        this.outputChannel.appendLine(`[ServerManager] Downloading ${info.nugetPackage} v${SKIASHARP_VERSION} from NuGet`);

        progress.report({ message: `Downloading ${info.nugetPackage}...` });

        const buffer = await this.downloadVerifiedNupkg(info.nugetPackage, SKIASHARP_VERSION);

        const tmpDir = path.join(binDir, '.tmp-nupkg');
        fs.mkdirSync(tmpDir, { recursive: true });
        const nupkgPath = path.join(tmpDir, 'package.nupkg');
        fs.writeFileSync(nupkgPath, buffer);

        progress.report({ message: 'Extracting native library...' });

        const extractDir = path.join(tmpDir, 'extracted');
        fs.rmSync(extractDir, { recursive: true, force: true });
        fs.mkdirSync(extractDir, { recursive: true });

        extractZipToDir(nupkgPath, extractDir);

        const ridSearchOrder = [info.rid];
        if (info.rid.startsWith('osx-')) {
            ridSearchOrder.push('osx');
        }

        let sourceFile: string | null = null;
        for (const rid of ridSearchOrder) {
            const candidate = path.join(extractDir, 'runtimes', rid, 'native', info.nativeFileName);
            if (fs.existsSync(candidate)) {
                sourceFile = candidate;
                break;
            }
        }

        if (!sourceFile) {
            const runtimesDir = path.join(extractDir, 'runtimes');
            if (fs.existsSync(runtimesDir)) {
                const contents = fs.readdirSync(runtimesDir);
                this.outputChannel.appendLine(`[ServerManager] Available RIDs in package: ${contents.join(', ')}`);
            }
            throw new Error(`Native lib ${info.nativeFileName} not found in package for RID ${info.rid}`);
        }

        const targetDir = path.join(binDir, 'runtimes', info.rid, 'native');
        fs.mkdirSync(targetDir, { recursive: true });

        const targetPath = path.join(targetDir, info.nativeFileName);
        fs.copyFileSync(sourceFile, targetPath);

        if (process.platform !== 'win32') {
            fs.chmodSync(targetPath, 0o755);
        }

        this.outputChannel.appendLine(`[ServerManager] Native lib installed: ${targetPath}`);
        progress.report({ message: 'Done!' });

        try {
            fs.rmSync(tmpDir, { recursive: true, force: true });
        } catch {
            this.outputChannel.appendLine('[ServerManager] Warning: Could not clean up temp directory');
        }
    }
}
