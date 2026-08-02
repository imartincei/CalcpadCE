// Regenerates the parts of settings.default.json that duplicate a TS-side
// default: `core` (from DEFAULT_CALCPAD_SETTINGS) and `extras.pdfSettings`
// (from DEFAULT_PDF_SETTINGS). Runs as a postbuild step via tsx, so it can
// import the TS sources directly instead of the compiled dist output. The
// rest of `extras` has no TS-side duplicate and is left untouched.

import { readFileSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import { DEFAULT_CALCPAD_SETTINGS } from '../src/types/settings';
import { DEFAULT_PDF_SETTINGS } from '../src/types/pdf-settings';

const root = path.dirname(fileURLToPath(import.meta.url));

for (const relPath of ['../src/defaults/settings.default.json', '../dist/defaults/settings.default.json']) {
    const file = path.join(root, relPath);
    const json = JSON.parse(readFileSync(file, 'utf8'));
    json.core = DEFAULT_CALCPAD_SETTINGS;
    json.extras.pdfSettings = DEFAULT_PDF_SETTINGS;
    writeFileSync(file, JSON.stringify(json, null, 4) + '\n');
}
