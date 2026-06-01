// Extracts the worksheet CSS from the CLI template and copies jQuery into media/,
// so the webview can render Calcpad output with the same styling as the desktop app.
import * as fs from 'node:fs';
import * as path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const extRoot = path.resolve(here, '..');
const cliDoc = path.resolve(extRoot, '..', 'Calcpad.Cli', 'doc');
const mediaDir = path.join(extRoot, 'media');

fs.mkdirSync(mediaDir, { recursive: true });

// 1. Extract the <style> block from template.html into media/calcpad.css
const templatePath = path.join(cliDoc, 'template.html');
const template = fs.readFileSync(templatePath, 'utf8');
const start = template.indexOf('<style>');
const end = template.indexOf('</style>');
if (start < 0 || end < 0) {
  throw new Error(`Could not find <style> block in ${templatePath}`);
}
const css = template.slice(start + '<style>'.length, end).trim();
const header = '/* Generated from Calcpad.Cli/doc/template.html — do not edit by hand. */\n';
fs.writeFileSync(path.join(mediaDir, 'calcpad.css'), header + css + '\n', 'utf8');
console.log('Wrote media/calcpad.css');

// 2. Copy jQuery
const jquery = 'jquery-3.6.3.min.js';
fs.copyFileSync(path.join(cliDoc, jquery), path.join(mediaDir, jquery));
console.log(`Copied media/${jquery}`);
