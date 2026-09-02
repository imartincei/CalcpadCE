# Web Editor: calcpad-web

Vite + Vue 3 + Monaco editor. Also built into the Tauri desktop app.

## Monaco Integration
```typescript
// Register Calcpad language with Monaco
registerCalcpadLanguage();   // Monarch tokenizer
registerCalcpadTheme();      // calcpad-dark theme

// Create editor instance
const editor = createCalcpadEditor(container, { fontSize: 14 });
```

## Editor Features
| File | Feature |
|------|---------|
| `language.ts` | Monarch tokenizer grammar for Calcpad syntax |
| `semantic-tokens.ts` | Server-based semantic tokens via highlight API |
| `completions.ts` | Autocomplete with snippets and symbol suggestions |
| `diagnostics.ts` | Lint results → Monaco editor markers |
| `theme.ts` | Dark theme color rules for all token types |

## App.vue Layout
- **Sidebar** (optional): Vue component for settings/controls
- **Editor toolbar**: File name, preview toggle, server status indicator
- **Editor**: Monaco editor with Calcpad language
- **Bottom panel**: Problems tab (lint diagnostics) + Output tab
- **Preview** (optional): Rendered HTML output

## Vite Dev Server
```typescript
// vite.config.ts
server: {
    port: 5173,
    proxy: {
        '/api': {
            target: process.env.VITE_SERVER_URL || 'http://localhost:9420',
            changeOrigin: true,
        },
    },
},
```

The backend only listens on 9420 when it was started with `CALCPAD_PORT=9420` (or `--urls`) — otherwise it takes a random port. For dev, either pin the port or point `VITE_SERVER_URL` at whatever the server printed.

## Tauri Build
`tauri.conf.json`'s `build.frontendDist` points at `../../calcpad-web/dist` and `beforeBuildCommand` runs `npm run build` in `calcpad-web` before bundling — no env-var toggle needed.

## Adding a Monaco Editor Feature
1. **Create provider** in `calcpad-web/src/editor/`:
```typescript
import * as monaco from 'monaco-editor';

export class NewFeatureProvider implements monaco.languages.SomeProvider {
    provideXyz(model: monaco.editor.ITextModel, position: monaco.Position) {
        // Implementation
    }
}
```

2. **Register** in editor setup:
```typescript
monaco.languages.registerSomeProvider('calcpad', new NewFeatureProvider());
```
