// Publishes a self-contained Calcpad.Cli build into bin/<rid>/ for bundling in the VSIX.
// Usage:  node scripts/publish-cli.mjs [rid]
// Default rid is the current host platform.
import { spawnSync } from 'node:child_process';
import * as path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const extRoot = path.resolve(here, '..');
const csproj = path.resolve(extRoot, '..', 'Calcpad.Cli', 'Calcpad.Cli.csproj');

function hostRid() {
  switch (process.platform) {
    case 'win32':
      return 'win-x64';
    case 'linux':
      return 'linux-x64';
    case 'darwin':
      return process.arch === 'arm64' ? 'osx-arm64' : 'osx-x64';
    default:
      throw new Error(`Unsupported platform: ${process.platform}`);
  }
}

const rid = process.argv[2] || hostRid();
const outDir = path.join(extRoot, 'bin', rid);

const args = [
  'publish',
  csproj,
  '-c',
  'Release',
  '-r',
  rid,
  '--self-contained',
  '-o',
  outDir,
  '-p:PublishSingleFile=false'
];

console.log(`dotnet ${args.join(' ')}`);
const result = spawnSync('dotnet', args, { stdio: 'inherit' });
if (result.status !== 0) {
  process.exit(result.status ?? 1);
}
console.log(`Published Calcpad CLI to ${outDir}`);
