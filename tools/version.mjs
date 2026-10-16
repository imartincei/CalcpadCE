import { execFileSync } from 'node:child_process';
import { appendFileSync, readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { resolve } from 'node:path';

export const repoRoot = fileURLToPath(new URL('../', import.meta.url));

export function propsVersion() {
    return readFileSync(resolve(repoRoot, 'Directory.Build.props'), 'utf8')
        .match(/<Version>([^<]+)<\/Version>/)[1].trim();
}

export function resolveVersion() {
    return process.env.CALCPAD_VERSION || propsVersion();
}

export function snapshotVersion() {
    const revision = execFileSync('git', ['rev-parse', '--short', 'HEAD'], { cwd: repoRoot, encoding: 'utf8' }).trim();
    const [version, metadata] = propsVersion().split('+');
    return `${version}-${revision}${metadata ? `+${metadata}` : ''}`;
}

export function releaseVersion(tag) {
    const version = propsVersion();
    if (tag !== `v${version}`) throw new Error(`Release tag ${tag} must match v${version} from Directory.Build.props`);
    return version;
}

export function archVersion(version) {
    return version.replace(/[-+]/g, '.');
}

export function windowsInstallerVersion(version) {
    return version.split(/[-+]/)[0];
}

export function rpmVersion(version) {
    const [, core, prerelease, metadata] = version.match(/^(\d+\.\d+\.\d+)(?:-([^+]+))?(?:\+(.+))?$/);
    return {
        version: core,
        release: [prerelease ? `0.${prerelease}` : '1', metadata].filter(Boolean).join('.').replace(/-/g, '.'),
    };
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
    const mode = process.argv[2];
    const version = mode === 'snapshot' ? snapshotVersion()
        : mode === 'release' ? releaseVersion(process.argv[3] ?? process.env.GITHUB_REF_NAME)
        : resolveVersion();
    if (process.env.GITHUB_OUTPUT) appendFileSync(process.env.GITHUB_OUTPUT, `version=${version}\n`);
    console.log(version);
}
