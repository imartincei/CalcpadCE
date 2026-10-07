import { resolveVersion } from '../../../../tools/version.mjs';
/**
 * Bundle the .NET server into a frontend app's bin directory.
 *
 * Why: vscode-calcpad and calcpad-desktop both ship the .NET server
 * alongside their frontend. Hand-syncing a single Calcpad.Server.dll
 * across the package boundary has been the source of every
 * "PuppeteerSharp not found", "endpoint 404", and "deps.json out of date"
 * bug we've hit. This script publishes the backend with `dotnet publish`,
 * then mirrors the output into the chosen target so the deps.json,
 * runtimeconfig.json, native apphost, and every transitive dependency
 * stay in lock-step across both apps.
 *
 * Flags:
 *   --target=<abs-dir>     Where to mirror the publish output. Default:
 *                          vscode-calcpad/bin/ (script's own consumer).
 *                          For calcpad-desktop, pass
 *                          calcpad-desktop/extensions/server.
 *   --rid=<rid>            Target RID for self-contained publish (default: host RID)
 *   --framework-dependent  Emit a slim, framework-dependent bundle instead of
 *                          a self-contained one. Smaller (~3 MB vs ~80 MB)
 *                          but requires the user to have .NET 10 installed.
 *   --configuration=<c>    Debug | Release (default: Release)
 *   --skip-build           Reuse an existing publish directory; only re-mirror
 *   --keep-skia-natives    Keep the published `runtimes/` tree (SkiaSharp +
 *                          others) instead of expecting the consumer to
 *                          download SkiaSharp natives at runtime. Required
 *                          for calcpad-desktop (no runtime download path).
 *   --strip-external-deps  After mirroring, delete the managed NuGet DLLs that
 *                          the VS Code extension downloads at first activation
 *                          (DocumentFormat.OpenXml, PuppeteerSharp, EF Core,
 *                          PDFsharp). Trims ~20 MB from the VSIX.
 *                          Pair with --framework-dependent for vscode-calcpad;
 *                          do NOT pass for calcpad-desktop.
 *
 * Files matching PRESERVE_PATTERNS in the target are kept across syncs so
 * runtime-downloaded SkiaSharp natives, lock files, and logs aren't wiped.
 */

import { execSync } from 'node:child_process';
import { cpSync, existsSync, mkdirSync, readdirSync, readFileSync, rmSync, statSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { arch as nodeArch, platform as nodePlatform } from 'node:os';

const __dirname = dirname(fileURLToPath(import.meta.url));
const VSCODE_DIR = resolve(__dirname, '..');
const BACKEND_DIR = resolve(VSCODE_DIR, '..', '..', 'backend');
const CSPROJ = join(BACKEND_DIR, 'Calcpad.Server.csproj');
const DEFAULT_TARGET_BIN = join(VSCODE_DIR, 'bin');

// Managed NuGet DLLs that the VS Code extension fetches from nuget.org on
// first activation (see EXTERNAL_MANAGED_DEPS in calcpadServerManager.ts).
// When --strip-external-deps is passed we delete these from the synced bin/
// so they don't ride along inside the VSIX. Keep this list aligned with the
// extension's EXTERNAL_MANAGED_DEPS — drift means the extension will either
// ship duplicate DLLs (still works, just bloated) or fail to redownload them
// (the existence check in ensureExternalManagedDeps short-circuits).
const EXTERNAL_DEP_DLLS = [
    'DocumentFormat.OpenXml.dll',
    'DocumentFormat.OpenXml.Framework.dll',
    'PuppeteerSharp.dll',
    'WebDriverBiDi.dll',
    'PdfSharp.dll',
    'PdfSharp.BarCodes.dll',
    'PdfSharp.Charting.dll',
    'PdfSharp.Cryptography.dll',
    'PdfSharp.Quality.dll',
    'PdfSharp.Shared.dll',
    'PdfSharp.Snippets.dll',
    'PdfSharp.System.dll',
    'PdfSharp.WPFonts.dll',
];

// Filenames that the extension generates at runtime (downloaded native libs,
// log files, lock files, …). We never want to wipe these on resync.
const PRESERVE_PATTERNS = [
    /^runtimes$/,                 // SkiaSharp native libs downloaded by ensureNativeLibs
    /^logs$/,                     // FileLogger output
    /^cache$/,                    // Server-side caches
    /^\.calcpad-server\.lock$/,   // Lock file used by tryReuseExistingServer
    /^CalcpadServer-.*\.log$/,    // Per-day server logs
    /^\.gitkeep$/,                // Placeholder used by calcpad-desktop's empty extensions dir
];

function shouldPreserve(name) {
    return PRESERVE_PATTERNS.some(re => re.test(name));
}

function parseArgs(argv) {
    const out = {
        skipBuild: false,
        configuration: 'Release',
        frameworkDependent: false,
        rid: null,
        target: DEFAULT_TARGET_BIN,
        keepSkiaNatives: false,
        stripExternalDeps: false,
    };
    for (const arg of argv.slice(2)) {
        if (arg === '--skip-build') out.skipBuild = true;
        else if (arg === '--framework-dependent') out.frameworkDependent = true;
        else if (arg === '--keep-skia-natives') out.keepSkiaNatives = true;
        else if (arg === '--strip-external-deps') out.stripExternalDeps = true;
        else if (arg.startsWith('--configuration=')) out.configuration = arg.split('=')[1];
        else if (arg.startsWith('--rid=')) out.rid = arg.split('=')[1];
        else if (arg.startsWith('--target=')) out.target = resolve(arg.split('=')[1]);
        else throw new Error(`Unknown flag: ${arg}`);
    }
    return out;
}

function defaultRid() {
    const platform = nodePlatform();
    const arch = nodeArch();
    const archMap = { x64: 'x64', arm64: 'arm64' };
    const a = archMap[arch];
    if (!a) throw new Error(`Unsupported arch for default RID: ${arch}`);
    if (platform === 'linux') return `linux-${a}`;
    if (platform === 'darwin') return `osx-${a}`;
    if (platform === 'win32') return `win-${a}`;
    throw new Error(`Unsupported platform for default RID: ${platform}`);
}

function publishOutputDir(rid, frameworkDependent, configuration) {
    // Match `dotnet publish` default layout: bin/<config>/<tfm>/[rid/]publish
    const tfm = 'net10.0';
    const base = join(BACKEND_DIR, 'bin', configuration, tfm);
    return frameworkDependent
        ? join(base, rid, 'publish-framework-dependent')
        : join(base, rid, 'publish');
}

function run(cmd, cwd) {
    console.log(`> ${cmd}`);
    execSync(cmd, { cwd, stdio: 'inherit' });
}

function sleepSync(ms) {
    // Atomics.wait gives us a blocking sleep without busy-spinning. Falls
    // back to a synchronous ping/sleep shell-out on platforms where
    // SharedArrayBuffer is restricted (shouldn't happen in plain Node).
    try {
        Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, ms);
    } catch {
        if (process.platform === 'win32') {
            try { execSync(`ping 127.0.0.1 -n 1 -w ${ms} > nul`, { stdio: 'ignore' }); } catch { /* best-effort */ }
        } else {
            try { execSync(`sleep ${ms / 1000}`, { stdio: 'ignore' }); } catch { /* best-effort */ }
        }
    }
}

/**
 * Kill any bundled Calcpad.Server process that's currently holding files in
 * `targetBin`. VS Code's extension host can leave the server alive after a
 * debug session ends, and on Windows that leaves every DLL in the bin folder
 * locked, breaking the next sync. The server-manager writes a JSON lock file
 * with the PID; we read that, force-kill the PID, wait briefly for handles to
 * release, then continue.
 */
function stopRunningServer(targetBin) {
    const lockPath = join(targetBin, '.calcpad-server.lock');
    if (!existsSync(lockPath)) return;

    let pid = null;
    try {
        const lock = JSON.parse(readFileSync(lockPath, 'utf-8'));
        if (typeof lock.pid === 'number' && Number.isFinite(lock.pid)) pid = lock.pid;
    } catch { /* malformed lock file — fall through to delete */ }

    if (pid !== null) {
        // Probe whether the PID is alive (signal 0 = no-op delivery check).
        let alive = false;
        try { process.kill(pid, 0); alive = true; } catch { /* dead or denied */ }

        if (alive) {
            console.log(`[sync-bundled-server] stopping existing server pid=${pid} (lock file ${lockPath})`);
            try {
                if (process.platform === 'win32') {
                    execSync(`taskkill /F /T /PID ${pid}`, { stdio: 'ignore' });
                } else {
                    process.kill(pid, 'SIGTERM');
                }
            } catch { /* may have exited between probe and kill */ }

            // Poll up to 5 s for the OS to release handles before we try rmSync.
            const deadline = Date.now() + 5000;
            while (Date.now() < deadline) {
                try { process.kill(pid, 0); } catch { break; }
                sleepSync(100);
            }
        }
    }

    try { rmSync(lockPath, { force: true }); } catch { /* ignore */ }
}

function cleanTarget(targetBin) {
    if (!existsSync(targetBin)) {
        mkdirSync(targetBin, { recursive: true });
        return;
    }

    // Drop any server that's still holding files in this directory before we
    // try to delete them. Without this, rmSync fails with EBUSY on Windows.
    stopRunningServer(targetBin);

    for (const entry of readdirSync(targetBin)) {
        if (shouldPreserve(entry)) continue;
        const full = join(targetBin, entry);
        rmSyncWithRetry(full);
    }
}

/**
 * rmSync with a small retry loop. Even after we kill the server PID, Windows
 * can hold antivirus / search-indexer handles on freshly-released files for a
 * few hundred ms. Retrying with backoff covers that window without surfacing
 * a confusing EBUSY to the user.
 */
function rmSyncWithRetry(target) {
    const maxAttempts = 6;
    let lastErr = null;
    for (let attempt = 1; attempt <= maxAttempts; attempt++) {
        try {
            rmSync(target, { recursive: true, force: true });
            return;
        } catch (err) {
            lastErr = err;
            const code = err && err.code;
            if (code !== 'EBUSY' && code !== 'EPERM' && code !== 'ENOTEMPTY') throw err;
            sleepSync(150 * attempt);
        }
    }
    throw lastErr;
}

function copyPublishedTree(src, targetBin, { keepSkiaNatives, rid }) {
    if (!existsSync(src)) {
        throw new Error(`Publish output not found: ${src}\nDid \`dotnet publish\` succeed?`);
    }
    for (const entry of readdirSync(src)) {
        // Skip pdbs (debug symbols).
        if (entry.endsWith('.pdb')) continue;
        // Skip the runtimes/ tree by default — vscode-calcpad downloads
        // SkiaSharp natives at runtime via ensureNativeLibs, so shipping the
        // full .NET runtimes/ folder just bloats the bundle. Calcpad-Desktop
        // has no download path and must keep them (--keep-skia-natives).
        if (entry === 'runtimes' && !keepSkiaNatives) continue;
        const srcPath = join(src, entry);
        const dstPath = join(targetBin, entry);
        cpSync(srcPath, dstPath, { recursive: true });
    }
    // When keeping runtimes, prune sibling-platform SkiaSharp / Playwright
    // assets so we don't ship Linux libs in a Windows bundle.
    if (keepSkiaNatives) {
        pruneForeignRuntimes(join(targetBin, 'runtimes'), rid);
        pruneForeignPlaywright(join(targetBin, '.playwright', 'node'), rid);
    }
}

function pruneForeignRuntimes(runtimesDir, rid) {
    if (!existsSync(runtimesDir)) return;
    // Keep both the precise RID and the OS-only fallback (e.g. linux-x64
    // and linux), since some packages publish under one or the other.
    const osOnly = rid.split('-')[0];
    for (const entry of readdirSync(runtimesDir)) {
        if (entry === rid || entry === osOnly) continue;
        rmSync(join(runtimesDir, entry), { recursive: true, force: true });
    }
}

function pruneForeignPlaywright(playwrightDir, rid) {
    if (!existsSync(playwrightDir)) return;
    for (const entry of readdirSync(playwrightDir)) {
        if (entry === rid) continue;
        rmSync(join(playwrightDir, entry), { recursive: true, force: true });
    }
}

function stripExternalDeps(targetBin) {
    let stripped = 0;
    let bytes = 0;
    for (const dll of EXTERNAL_DEP_DLLS) {
        const p = join(targetBin, dll);
        if (!existsSync(p)) continue;
        try {
            bytes += statSync(p).size;
            rmSync(p, { force: true });
            stripped++;
        } catch (err) {
            console.log(`[sync-bundled-server] could not strip ${dll}: ${err.message}`);
        }
    }
    console.log(`[sync-bundled-server] stripped ${stripped} external NuGet DLLs (${(bytes / 1024 / 1024).toFixed(1)} MB) — extension will redownload on first activation`);
}

/**
 * Locate signtool.exe (Windows SDK). Honors CALCPAD_SIGNTOOL as an explicit
 * override, otherwise picks the newest x64 signtool under the Windows Kits
 * bin folder. Windows-only; returns null elsewhere.
 */
function resolveSigntool() {
    if (process.platform !== 'win32') return null;
    if (process.env.CALCPAD_SIGNTOOL && existsSync(process.env.CALCPAD_SIGNTOOL)) {
        return process.env.CALCPAD_SIGNTOOL;
    }
    const kitsBin = 'C:/Program Files (x86)/Windows Kits/10/bin';
    if (!existsSync(kitsBin)) return null;
    const versions = readdirSync(kitsBin)
        .filter(v => /^\d+\.\d+\.\d+\.\d+$/.test(v))
        .sort((a, b) => {
            const pa = a.split('.').map(Number), pb = b.split('.').map(Number);
            for (let i = 0; i < 4; i++) if (pa[i] !== pb[i]) return pa[i] - pb[i];
            return 0;
        })
        .reverse();
    for (const v of versions) {
        const candidate = join(kitsBin, v, 'x64', 'signtool.exe');
        if (existsSync(candidate)) return candidate;
    }
    const flat = join(kitsBin, 'x64', 'signtool.exe');
    return existsSync(flat) ? flat : null;
}

/**
 * Authenticode-sign the bundled apphost (Calcpad.Server.exe). Off by default:
 * only runs when CALCPAD_SIGN_THUMBPRINT points at a code-signing cert in the
 * current user's store, so CI and contributors without a cert get an unsigned
 * (working) bundle. Signing exists to get past Windows Defender / SmartScreen /
 * corporate EDR reputation blocks, which key off the per-file hash for unsigned
 * binaries (re-firing on every rebuild) but off the publisher for signed ones.
 *
 * The .exe apphost is the only native PE that gets blocked; the managed .dlls
 * load through it, so signing just the apphost is sufficient.
 */
function signApphost(targetBin) {
    const thumbprint = process.env.CALCPAD_SIGN_THUMBPRINT;
    if (!thumbprint) return; // signing disabled — no-op

    if (process.platform !== 'win32') {
        console.log('[sync-bundled-server] CALCPAD_SIGN_THUMBPRINT set but signing is Windows-only — skipping');
        return;
    }
    const exe = join(targetBin, 'Calcpad.Server.exe');
    if (!existsSync(exe)) {
        console.log('[sync-bundled-server] no apphost (Calcpad.Server.exe) present — nothing to sign');
        return;
    }
    const signtool = resolveSigntool();
    if (!signtool) {
        throw new Error('CALCPAD_SIGN_THUMBPRINT is set but signtool.exe was not found. Install the Windows SDK or set CALCPAD_SIGNTOOL to its full path.');
    }
    const ts = process.env.CALCPAD_SIGN_TIMESTAMP_URL || 'http://timestamp.digicert.com';
    console.log(`[sync-bundled-server] signing apphost with cert ${thumbprint}`);
    run(`"${signtool}" sign /sha1 ${thumbprint} /fd SHA256 /tr ${ts} /td SHA256 "${exe}"`, targetBin);
    // Verify is best-effort: a self-signed cert that isn't in the machine trust
    // store won't chain to a trusted root, so /pa verify fails even though the
    // signature was applied correctly. Don't fail the build on that.
    try {
        execSync(`"${signtool}" verify /pa "${exe}"`, { stdio: 'inherit' });
    } catch {
        console.log('[sync-bundled-server] signtool verify failed — expected for a self-signed cert not yet trusted on this machine; signature was still applied');
    }
}

function bundleSizeMb(dir) {
    let total = 0;
    function walk(p) {
        for (const entry of readdirSync(p)) {
            const full = join(p, entry);
            const st = statSync(full);
            if (st.isDirectory()) walk(full); else total += st.size;
        }
    }
    walk(dir);
    return (total / 1024 / 1024).toFixed(1);
}

function main() {
    const args = parseArgs(process.argv);
    const rid = args.rid ?? defaultRid();
    const targetBin = args.target;
    console.log(`[sync-bundled-server] target=${targetBin}`);
    console.log(`[sync-bundled-server] mode=${args.frameworkDependent ? 'framework-dependent' : `self-contained (${rid})`}`);
    console.log(`[sync-bundled-server] configuration=${args.configuration}`);
    if (args.keepSkiaNatives) console.log('[sync-bundled-server] keeping published runtimes/ tree');

    if (!args.skipBuild) {
        if (args.frameworkDependent) rmSync(publishOutputDir(rid, true, args.configuration), { recursive: true, force: true });
        const publishCmd = args.frameworkDependent
            ? `dotnet publish "${CSPROJ}" -c ${args.configuration} -r ${rid} --no-self-contained -o "${publishOutputDir(rid, true, args.configuration)}"`
            : `dotnet publish "${CSPROJ}" -c ${args.configuration} -r ${rid} --self-contained true`;
        run(`${publishCmd} -p:Version=${resolveVersion()} -p:IncludeSourceRevisionInInformationalVersion=false`, BACKEND_DIR);
    } else {
        console.log('[sync-bundled-server] --skip-build given, reusing existing publish output');
    }

    const src = publishOutputDir(rid, args.frameworkDependent, args.configuration);
    console.log(`[sync-bundled-server] copying from ${src}`);

    cleanTarget(targetBin);
    copyPublishedTree(src, targetBin, { keepSkiaNatives: args.keepSkiaNatives, rid });

    if (args.stripExternalDeps) {
        stripExternalDeps(targetBin);
    }

    // Sign the apphost so Defender / SmartScreen / EDR stop blocking it. No-op
    // unless CALCPAD_SIGN_THUMBPRINT is set (see signApphost).
    signApphost(targetBin);

    // Quick sanity check — the server DLL and its deps manifest must be present,
    // and the apphost needs +x on POSIX.
    const requiredFiles = ['Calcpad.Server.dll', 'Calcpad.Server.deps.json', 'Calcpad.Server.runtimeconfig.json'];
    for (const f of requiredFiles) {
        if (!existsSync(join(targetBin, f))) {
            throw new Error(`Sync incomplete: ${f} missing from ${targetBin}`);
        }
    }

    if (process.platform !== 'win32') {
        for (const f of ['Calcpad.Server', 'createdump']) {
            const p = join(targetBin, f);
            if (existsSync(p)) {
                try { execSync(`chmod 0755 "${p}"`); } catch { /* best-effort */ }
            }
        }
    }

    console.log(`[sync-bundled-server] OK — bundle size ${bundleSizeMb(targetBin)} MB`);
}

try {
    main();
} catch (err) {
    console.error(`[sync-bundled-server] FAILED: ${err instanceof Error ? err.message : err}`);
    process.exit(1);
}
