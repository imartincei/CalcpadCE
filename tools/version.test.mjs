import test from 'node:test';
import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { propsVersion, resolveVersion, snapshotVersion, releaseVersion, archVersion, windowsInstallerVersion, rpmVersion, repoRoot } from './version.mjs';

test('local version comes from props and CI overrides it', () => {
    const previous = process.env.CALCPAD_VERSION;
    delete process.env.CALCPAD_VERSION;
    assert.equal(resolveVersion(), propsVersion());
    process.env.CALCPAD_VERSION = '1.2.3-preview-abcdef0';
    assert.equal(resolveVersion(), '1.2.3-preview-abcdef0');
    if (previous === undefined) delete process.env.CALCPAD_VERSION;
    else process.env.CALCPAD_VERSION = previous;
});

test('snapshots append the checked-out Git revision', () => {
    const revision = execFileSync('git', ['rev-parse', '--short', 'HEAD'], { cwd: repoRoot, encoding: 'utf8' }).trim();
    const [base, metadata] = propsVersion().split('+');
    assert.equal(snapshotVersion(), `${base}-${revision}${metadata ? `+${metadata}` : ''}`);
});

test('release tags must match props even with a CI override', () => {
    const previous = process.env.CALCPAD_VERSION;
    process.env.CALCPAD_VERSION = '0.0.0-mismatch';
    assert.equal(releaseVersion(`v${propsVersion()}`), propsVersion());
    assert.throws(() => releaseVersion('v0.0.0-mismatch'), /must match/);
    if (previous === undefined) delete process.env.CALCPAD_VERSION;
    else process.env.CALCPAD_VERSION = previous;
});

test('Arch represents prerelease and build metadata without forbidden characters', () => {
    assert.equal(archVersion('1.2.3-beta.1-abcdef0+build.2'), '1.2.3.beta.1.abcdef0.build.2');
});

test('Windows installer versions are numeric for stable, prerelease, and snapshot builds', () => {
    for (const version of ['1.2.3', '1.2.3-beta.1', '1.2.3-beta.1-abcdef0+build.2', '1.2.3+build.2']) {
        assert.equal(windowsInstallerVersion(version), '1.2.3');
    }
});

test('RPM keeps prereleases in its release field before stable packages', () => {
    assert.deepEqual(rpmVersion('1.2.3-beta.1-abcdef0+build.2'), { version: '1.2.3', release: '0.beta.1.abcdef0.build.2' });
    assert.deepEqual(rpmVersion('1.2.3'), { version: '1.2.3', release: '1' });
});
