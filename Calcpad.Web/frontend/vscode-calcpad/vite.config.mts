import { resolveVersion } from '../../../tools/version.mjs';
import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import { resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

// Get directory name in ESM context
const __dirname = fileURLToPath(new URL('.', import.meta.url))

export default defineConfig({
  plugins: [vue()],
  // Suppress CJS deprecation warning for VS Code extension context
  logLevel: 'warn',
  build: {
    outDir: 'out/CalcpadVuePanel',
    lib: {
      entry: resolve(__dirname, 'src/CalcpadVuePanel/main.ts'),
      name: 'CalcpadVuePanel',
      fileName: 'main',
      formats: ['iife'] // IIFE format for VS Code webview
    },
    rollupOptions: {
      external: [], // Bundle everything for webview isolation
      output: {
        globals: {},
        // Don't hash filenames for easier webview integration
        entryFileNames: 'main.js',
        chunkFileNames: '[name].js',
        assetFileNames: '[name].[ext]'
      }
    },
    cssCodeSplit: false, // Bundle all CSS into one file
    minify: 'terser',
    sourcemap: true,
    target: 'es2020' // Compatible with modern VS Code webview
  },
  resolve: {
    alias: {
      '@': resolve(__dirname, 'src/CalcpadVuePanel'),
      '@calcpad-vue': resolve(__dirname, '../calcpad-frontend/src/vue')
    }
  },
  define: {
    'import.meta.env.VITE_APP_VERSION': JSON.stringify(resolveVersion()),
    // Vue.js production optimizations
    __VUE_PROD_DEVTOOLS__: false,
    __VUE_OPTIONS_API__: true,
    __VUE_PROD_HYDRATION_MISMATCH_DETAILS__: false,
    // Define process.env for webview compatibility
    'process.env': JSON.stringify({
      NODE_ENV: 'production'
    })
  },
  // CSS is automatically extracted in lib mode
})