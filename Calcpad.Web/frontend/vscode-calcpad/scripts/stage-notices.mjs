// vsce packages only what is under the extension root, and .vscodeignore blocks
// `../**`, so the notices have to be copied in before packaging.
import { copyFileSync, existsSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const extRoot = dirname(dirname(fileURLToPath(import.meta.url)));
const repoRoot = join(extRoot, '..', '..', '..');

for (const [src, dest] of [
    ['THIRD-PARTY-NOTICES.txt', 'THIRD-PARTY-NOTICES.txt'],
]) {
    const from = join(repoRoot, src);
    if (!existsSync(from)) {
        console.error(`[stage-notices] missing ${from} -- run tools/license-scan/generate-notices.sh`);
        process.exit(1);
    }
    copyFileSync(from, join(extRoot, dest));
    console.log(`[stage-notices] ${dest}`);
}
