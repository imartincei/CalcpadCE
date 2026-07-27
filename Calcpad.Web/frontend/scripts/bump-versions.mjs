#!/usr/bin/env node
// Bumps calcpad-frontend, calcpad-desktop, and vscode-calcpad to the same
// version. Uses the highest current version across the three package.json
// files as the baseline, then applies the requested semver bump.
//
// Also updates:
//   * package-lock.json (root + packages[""] + packages["../calcpad-frontend"] entries)
//   * calcpad-desktop/src-tauri/Cargo.toml (package.version)
//   * calcpad-desktop/src-tauri/Cargo.lock (calcpad-desktop [[package]] entry)
//   * calcpad-desktop/src-tauri/tauri.conf.json (version)
//   * calcpad-desktop/packaging/arch/PKGBUILD (pkgver, and pkgrel back to 1)

import { existsSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const kind = process.argv[2];
const exact = process.argv[3] ?? null;

if (exact !== null) {
    if (!/^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$/.test(exact)) {
        console.error('Exact version must be a valid semver, e.g. 2.1.4 or 2.1.4-beta.3');
        process.exit(1);
    }
} else if (!['patch', 'minor', 'major'].includes(kind)) {
    console.error('Usage: bump-versions.mjs <patch|minor|major> [exact-version]');
    process.exit(1);
}

const here = dirname(fileURLToPath(import.meta.url));
const frontendRoot = resolve(here, '..');
const pkgDirs = ['calcpad-frontend', 'calcpad-desktop', 'vscode-calcpad', 'calcpad-web'];

const parse = v => v.split('.').map(n => parseInt(n, 10));
const cmp = (a, b) => a[0] - b[0] || a[1] - b[1] || a[2] - b[2];

let next;
if (exact !== null) {
    next = exact;
} else {
    const pkgJsonPaths = pkgDirs.map(name => resolve(frontendRoot, name, 'package.json'));
    const versions = pkgJsonPaths.map(p => parse(JSON.parse(readFileSync(p, 'utf8')).version));
    const baseline = versions.reduce((max, v) => (cmp(v, max) > 0 ? v : max));

    let [major, minor, patch] = baseline;
    if (kind === 'patch') patch += 1;
    else if (kind === 'minor') { minor += 1; patch = 0; }
    else { major += 1; minor = 0; patch = 0; }
    next = `${major}.${minor}.${patch}`;
}

const updateJson = (path, mutate) => {
    if (!existsSync(path)) return false;
    const json = JSON.parse(readFileSync(path, 'utf8'));
    const prev = mutate(json);
    writeFileSync(path, JSON.stringify(json, null, 2) + '\n');
    console.log(`${path.replace(frontendRoot + '/', '')}: ${prev} -> ${next}`);
    return true;
};

for (const name of pkgDirs) {
    const dir = resolve(frontendRoot, name);
    updateJson(resolve(dir, 'package.json'), j => {
        const p = j.version; j.version = next; return p;
    });
    updateJson(resolve(dir, 'package-lock.json'), j => {
        const p = j.version;
        j.version = next;
        if (j.packages && j.packages['']) j.packages[''].version = next;
        const frontendEntry = j.packages && j.packages['../calcpad-frontend'];
        if (frontendEntry && frontendEntry.version) frontendEntry.version = next;
        return p;
    });
}

const cargoToml = resolve(frontendRoot, 'calcpad-desktop/src-tauri/Cargo.toml');
if (existsSync(cargoToml)) {
    const src = readFileSync(cargoToml, 'utf8');
    const updated = src.replace(
        /(\[package\][\s\S]*?\nversion\s*=\s*")([^"]+)(")/,
        (_, a, prev, c) => {
            console.log(`calcpad-desktop/src-tauri/Cargo.toml: ${prev} -> ${next}`);
            return a + next + c;
        },
    );
    writeFileSync(cargoToml, updated);
}

const cargoLock = resolve(frontendRoot, 'calcpad-desktop/src-tauri/Cargo.lock');
if (existsSync(cargoLock)) {
    const src = readFileSync(cargoLock, 'utf8');
    const updated = src.replace(
        /(\[\[package\]\]\nname = "calcpad-desktop"\nversion = ")([^"]+)(")/,
        (_, a, prev, c) => {
            console.log(`calcpad-desktop/src-tauri/Cargo.lock: ${prev} -> ${next}`);
            return a + next + c;
        },
    );
    writeFileSync(cargoLock, updated);
}

updateJson(resolve(frontendRoot, 'calcpad-desktop/src-tauri/tauri.conf.json'), j => {
    const p = j.version; j.version = next; return p;
});

// A new upstream version restarts the Arch package revision at 1.
const pkgbuild = resolve(frontendRoot, 'calcpad-desktop/packaging/arch/PKGBUILD');
if (existsSync(pkgbuild)) {
    const src = readFileSync(pkgbuild, 'utf8');
    const updated = src
        .replace(/^(pkgver=)(.*)$/m, (_, a, prev) => {
            console.log(`calcpad-desktop/packaging/arch/PKGBUILD: ${prev} -> ${next}`);
            return a + next;
        })
        .replace(/^pkgrel=.*$/m, 'pkgrel=1');
    writeFileSync(pkgbuild, updated);
}
