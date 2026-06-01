import * as esbuild from 'esbuild';

const watch = process.argv.includes('--watch');

/** @type {import('esbuild').BuildOptions} */
const extensionConfig = {
  entryPoints: ['src/extension.ts'],
  bundle: true,
  outfile: 'dist/extension.js',
  external: ['vscode'],
  format: 'cjs',
  platform: 'node',
  target: 'node18',
  sourcemap: true,
  logLevel: 'info'
};

/** @type {import('esbuild').BuildOptions} */
const webviewConfig = {
  entryPoints: ['media/main.ts'],
  bundle: true,
  outfile: 'media/main.js',
  format: 'iife',
  platform: 'browser',
  target: 'es2020',
  sourcemap: true,
  logLevel: 'info'
};

if (watch) {
  const ctxExt = await esbuild.context(extensionConfig);
  const ctxWeb = await esbuild.context(webviewConfig);
  await Promise.all([ctxExt.watch(), ctxWeb.watch()]);
  console.log('esbuild watching...');
} else {
  await Promise.all([esbuild.build(extensionConfig), esbuild.build(webviewConfig)]);
}
