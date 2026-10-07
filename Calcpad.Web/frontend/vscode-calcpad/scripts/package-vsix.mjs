import { cpSync, mkdirSync, readFileSync, readdirSync, rmSync, writeFileSync } from 'node:fs';
import { basename, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { createVSIX } from '@vscode/vsce';
import { resolveVersion } from '../../../../tools/version.mjs';

const directory = fileURLToPath(new URL('../', import.meta.url));
const staging = resolve(directory, '.package');
const version = resolveVersion();
rmSync(staging, { recursive: true, force: true });
mkdirSync(staging);
for (const entry of readdirSync(directory)) {
    if (['node_modules', '.package', '.git'].includes(entry) || entry.endsWith('.vsix')) continue;
    cpSync(resolve(directory, entry), resolve(staging, entry), {
        recursive: true,
        filter: source => !['node_modules', '.git'].includes(basename(source)) && !source.endsWith('.vsix'),
    });
}
const manifest = JSON.parse(readFileSync(resolve(staging, 'package.json'), 'utf8'));
manifest.version = version;
delete manifest.scripts;
writeFileSync(resolve(staging, 'package.json'), `${JSON.stringify(manifest, null, 2)}\n`);
await createVSIX({ cwd: staging, packagePath: resolve(directory, `${manifest.name}-${version}.vsix`), dependencies: false });
