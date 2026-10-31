const typescript = require('@rollup/plugin-typescript');
const commonjs = require('@rollup/plugin-commonjs');
const resolve = require('@rollup/plugin-node-resolve');
const json = require('@rollup/plugin-json');
const fs = require('fs');
const path = require('path');

// pdf.js for the PDF preview, read at runtime and inlined into the preview frame.
const copyPdfjs = () => ({
  name: 'copy-pdfjs',
  writeBundle() {
    const src = path.dirname(require.resolve('pdfjs-dist/legacy/build/pdf.min.mjs'));
    const dest = path.join(__dirname, 'dist', 'pdfjs');
    fs.mkdirSync(dest, { recursive: true });
    for (const file of ['pdf.min.mjs', 'pdf.worker.min.mjs']) {
      fs.copyFileSync(path.join(src, file), path.join(dest, file));
    }
  }
});

// Async so the ESM version helper can be imported: the version the webview is built with.
module.exports = async () => ({
  input: 'src/extension.ts',
  output: {
    file: 'dist/extension.js',
    format: 'cjs',
    sourcemap: true,
    exports: 'auto',
    intro: `const __CALCPAD_VERSION__ = ${JSON.stringify((await import('../../../tools/version.mjs')).resolveVersion())};`
  },
  external: ['vscode'],
  plugins: [
    copyPdfjs(),
    json(),
    resolve({
      preferBuiltins: true
    }),
    commonjs(),
    typescript({
      tsconfig: './tsconfig.json',
      compilerOptions: {
        module: 'esnext',
        outDir: null
      },
      sourceMap: true,
      inlineSources: false
    })
  ]
});
