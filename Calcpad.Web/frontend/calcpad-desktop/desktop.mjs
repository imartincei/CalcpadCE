import { spawnSync } from 'node:child_process';
import { mkdirSync, rmSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { resolve } from 'node:path';
import { resolveVersion, rpmVersion, windowsInstallerVersion } from '../../../tools/version.mjs';

const directory = fileURLToPath(new URL('.', import.meta.url));
const [command, ...rawArgs] = process.argv.slice(2);
const args = rawArgs.flatMap(argument => /^--(target|bundles)=/.test(argument)
    ? [argument.slice(0, argument.indexOf('=')), argument.slice(argument.indexOf('=') + 1)] : [argument]);
const stagedIndex = args.indexOf('--sidecar-staged');
if (stagedIndex !== -1) args.splice(stagedIndex, 1);
process.env.CALCPAD_VERSION = resolveVersion();
const windows = process.platform === 'win32';
const targetIndex = args.indexOf('--target');
const target = targetIndex === -1 ? '' : args[targetIndex + 1];
const output = resolve(directory, 'src-tauri/target', target, args.includes('--debug') ? 'debug' : 'release');
if (command === 'build') rmSync(resolve(output, 'version.txt'), { force: true });

function run(executable, arguments_) {
    const result = spawnSync(executable, arguments_, { cwd: directory, env: process.env, stdio: 'inherit' });
    if (result.error) throw result.error;
    if (result.status !== 0) process.exit(result.status ?? 1);
}

if (command === 'dev' || (command === 'build' && stagedIndex === -1)) {
    run(windows ? 'powershell' : 'bash', windows
        ? ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', 'stage-sidecar.ps1']
        : ['stage-sidecar.sh']);
}

function tauri(action, options, config = {
    version: process.env.CALCPAD_VERSION,
    ...(windows ? { bundle: { windows: {
        wix: { version: windowsInstallerVersion(process.env.CALCPAD_VERSION) },
    } } } : {}),
}) {
    run(process.execPath, [resolve(directory, 'node_modules/@tauri-apps/cli/tauri.js'), action,
        '--config', `src-tauri/tauri.${windows ? 'windows' : 'linux'}.conf.json`,
        '--config', JSON.stringify(config), ...options]);
}

if (!windows && command === 'build' && !args.includes('--no-bundle')) {
    const bundleIndex = args.indexOf('--bundles');
    const options = [...args];
    let bundles = ['deb', 'rpm'];
    if (bundleIndex !== -1) {
        let end = bundleIndex + 1;
        while (end < options.length && !options[end].startsWith('--')) end++;
        bundles = options.splice(bundleIndex, end - bundleIndex).slice(1).flatMap(value => value.split(','));
    }
    const otherBundles = bundles.filter(bundle => bundle !== 'rpm');
    tauri('build', [...options, ...(otherBundles.length ? ['--bundles', ...otherBundles] : ['--no-bundle'])]);
    if (bundles.includes('rpm')) {
        const packageVersion = rpmVersion(process.env.CALCPAD_VERSION);
        const targetIndex = options.indexOf('--target');
        tauri('bundle', ['--bundles', 'rpm', ...(options.includes('--debug') ? ['--debug'] : []),
            ...(targetIndex === -1 ? [] : ['--target', options[targetIndex + 1]])], {
            version: packageVersion.version,
            bundle: { linux: { rpm: { release: packageVersion.release } } },
        });
    }
} else {
    tauri(command, args);
}

if (command === 'build') {
    mkdirSync(output, { recursive: true });
    writeFileSync(resolve(output, 'version.txt'), `${process.env.CALCPAD_VERSION}\n`);
}
