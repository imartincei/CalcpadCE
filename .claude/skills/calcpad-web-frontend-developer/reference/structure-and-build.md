# Project Structure & Build Reference

## Full Project Structure

```
Calcpad.Web/frontend/
├── calcpad-frontend/               # Shared TypeScript library
│   ├── src/
│   │   ├── index.ts                # Barrel exports (all public API)
│   │   ├── api/
│   │   │   └── client.ts           # CalcpadApiClient (fetch-based HTTP client)
│   │   ├── services/
│   │   │   ├── definitions.ts      # Variable/macro/function definitions extraction
│   │   │   ├── linter.ts           # Debounced linting
│   │   │   ├── highlight.ts        # Semantic token type mapping
│   │   │   ├── snippets.ts         # Autocomplete data
│   │   │   ├── cpdz.ts             # Compiled worksheet encode/decode
│   │   │   ├── headings.ts         # TOC heading extraction from source
│   │   │   ├── ui-overrides.ts     # #UI control values keyed by data-ui-var
│   │   │   ├── preview-diagnostics.ts, preview-limits.ts, scroll-anchor.ts
│   │   │   ├── paths.ts, image-utils.ts, html-body.ts, plot-extract.ts, zip-writer.ts
│   │   │   ├── base64-truncate.ts, crash-report.ts
│   │   │   └── message-bridge/     # postMessage protocol between host and webview
│   │   ├── text/
│   │   │   ├── auto-indent.ts      # Auto-indentation logic (#if/#for blocks)
│   │   │   ├── operators.ts        # Operator replacement (>= → ≥, <= → ≤)
│   │   │   ├── quick-type.ts       # Quick-type shortcuts (~a → α, ~b → β)
│   │   │   ├── comment-formatting.ts  # Bold/italic/heading/sub/super in comments
│   │   │   ├── metadata-comment.ts # pdf / settings / ui metadata comments
│   │   │   ├── directives.ts, ui-directive.ts, path-roots.ts
│   │   │   └── snippet-insert.ts, completion-format.ts
│   │   ├── types/
│   │   │   ├── api.ts              # API request/response interfaces + enums
│   │   │   ├── interfaces.ts       # ILogger, IFileSystem abstractions
│   │   │   ├── pdf-settings.ts     # PdfSettings interface + defaults
│   │   │   ├── settings.ts         # CalcpadSettings + CalcpadSettingsBlob
│   │   │   ├── catalog.ts          # Insert/snippet catalog types
│   │   │   ├── snippets.ts         # Snippet/InsertItem types
│   │   │   └── ui.ts               # UI component types
│   │   ├── defaults/               # Generated settings defaults (see generate-settings-defaults.mjs)
│   │   └── vue/
│   │       ├── components/         # CalcpadApp.vue + the sidebar tabs (Insert, Settings,
│   │       │                       #   Export, Files, Formatting, Metadata, Toc, Variables, Errors)
│   │       ├── services/           # Vue messaging service
│   │       ├── styles/
│   │       └── types/
│   ├── package.json                # Peer dep: vue ^3.5.0
│   └── tsconfig.json
│
├── calcpad-web/                    # Web editor (Vite + Vue 3 + Monaco)
│   ├── src/
│   │   ├── main.ts                 # Entry point, bootstrap
│   │   ├── App.vue                 # Main layout (sidebar + editor + bottom panel)
│   │   ├── editor/
│   │   │   ├── setup.ts            # registerCalcpadLanguage(), createCalcpadEditor()
│   │   │   ├── language.ts         # Monarch tokenizer grammar
│   │   │   ├── semantic-tokens.ts  # SemanticTokensProvider (server-based)
│   │   │   ├── completions.ts      # CompletionItemProvider (snippets + symbols)
│   │   │   ├── include-completions.ts  # File path completion for #include / #read
│   │   │   ├── diagnostics.ts      # Linting → Monaco markers integration
│   │   │   ├── hover.ts, builtin-docs.ts   # Hover docs
│   │   │   ├── references.ts       # Go-to-definition / find-references / rename
│   │   │   ├── format-document.ts, formatting-commands.ts
│   │   │   ├── auto-indent.ts, operator-replacer.ts, quick-type.ts
│   │   │   ├── editor-group.ts, bridge.ts
│   │   │   ├── theme.ts, app-theme.ts, vscode-variables.css
│   │   │   ├── workers.ts          # Web Worker setup for Monaco
│   │   │   └── index.ts            # Editor module barrel
│   │   ├── tabs/                   # Web-editor-specific sidebar tabs
│   │   ├── services/
│   │   │   ├── message-bridge.ts   # IPC for web environment
│   │   │   ├── tauri-bridge.ts     # IPC for Tauri desktop (uses @tauri-apps/api)
│   │   │   ├── server-manager.ts   # Server discovery / health
│   │   │   └── active-editor.ts
│   │   └── styles/
│   │       └── app.css             # Global styles
│   ├── vite.config.ts              # Dev proxy to VITE_SERVER_URL, default :9420
│   ├── package.json                # monaco-editor ^0.52.0, vue ^3.5.0
│   └── tsconfig.json
│
├── calcpad-desktop/                # Tauri desktop wrapper
│   ├── src-tauri/
│   │   ├── src/lib.rs              # Rust shell: window, menu, sidecar spawn, events
│   │   ├── src/main.rs             # Rust entry
│   │   ├── tauri.conf.json         # Window, bundle targets, sidecar externalBin, sign command
│   │   ├── capabilities/           # Plugin capability grants
│   │   ├── icons/                  # App icons for each platform
│   │   └── binaries/               # Staged Calcpad.Server sidecar (.gitkeep only in repo)
│   ├── stage-sidecar.sh / .ps1     # Publish Calcpad.Server → src-tauri/binaries/
│   ├── build-desktop.sh / .ps1     # Full bundle (stage + tauri build)
│   └── package.json                # devDep: @tauri-apps/cli
│
└── vscode-calcpad/                 # VS Code extension
    ├── src/
    │   ├── extension.ts            # Main extension entry (activate/deactivate)
    │   ├── adapters.ts             # VS Code API adapters
    │   ├── calcpadCompletionProvider.ts     # IntelliSense completions
    │   ├── calcpadDefinitionProvider.ts     # Go to Definition
    │   ├── calcpadDefinitionsService.ts     # Symbol extraction service
    │   ├── calcpadIncludeCompletionProvider.ts # #include file path completion
    │   ├── calcpadInsertManager.ts          # Snippets/insertion UI
    │   ├── calcpadReferenceProvider.ts      # Find References
    │   ├── calcpadRenameProvider.ts         # Rename Symbol
    │   ├── calcpadSemanticTokensProvider.ts # Semantic highlighting
    │   ├── calcpadServerLinter.ts           # Linter integration
    │   ├── calcpadServerManager.ts          # Server process lifecycle
    │   ├── calcpadSettings.ts               # Settings manager (JSON under globalStorage, presets)
    │   ├── calcpadHoverProvider.ts          # Hover documentation
    │   ├── calcpadIncludeLinkProvider.ts    # Ctrl+click an #include path
    │   ├── calcpadCompiledEditorProvider.ts # .cpdz custom editor
    │   ├── calcpadLocationResolver.ts       # Maps server locations to VS Code URIs
    │   ├── baseServerManager.ts             # Port/token/lock-file logic shared with the desktop host
    │   ├── dotnetRuntimeManager.ts          # Resolves or installs the .NET runtime
    │   ├── downloadVerification.ts          # Checksum verification for downloads
    │   ├── previewFrame.ts                  # Preview webview shell
    │   ├── autoIndenter.ts, operatorReplacer.ts, quickTyper.ts
    │   ├── installFont.ts
    │   ├── calcpadVueUIProvider.ts          # Webview panel (Vue sidebar)
    │   ├── commentFormatter.ts              # Formatting hotkeys
    │   └── imageInserter.ts                 # Insert Image command
    ├── CalcpadVuePanel/                     # Vue sidebar webview
    │   └── main.ts
    ├── package.json                # Extension manifest (commands, keybindings, settings, themes)
    ├── rollup.config.js            # Extension bundler
    └── tsconfig.json
```

## Build Commands

### Shared Library
```bash
cd Calcpad.Web/frontend/calcpad-frontend
npm run build     # Compile TypeScript to dist/
npm run watch     # Watch mode
```

### Web Editor
```bash
cd Calcpad.Web/frontend/calcpad-web
npm run dev       # Vite dev server on :5173 (proxies /api to VITE_SERVER_URL, default :9420)
npm run build     # Production build to dist/
npm run preview   # Preview production build
```

### Desktop App
```bash
cd Calcpad.Web/frontend/calcpad-desktop
./stage-sidecar.sh                # (First run / after backend changes) publish Calcpad.Server → src-tauri/binaries/
npm run dev                       # tauri dev (hot-reload Vue + rebuild Rust on change)
./build-desktop.sh                # Full bundle (stage + tauri build → src-tauri/target/release/bundle/)
```

### VS Code Extension
```bash
cd Calcpad.Web/frontend/vscode-calcpad
npm run compile      # Rollup build
npm run watch        # Watch mode (Rollup + Vue)
npm run build:vue    # Build Vue webview panel
npm run sync-server  # Publish + copy the backend into bin/ (also :slim, :debug)
npm run package      # Build everything and produce the .vsix (also :full, :lite, :vsix)
```

## External Dependencies

### calcpad-frontend
| Package | Purpose |
|---------|---------|
| vue ^3.5.0 | Peer dependency for Vue components |
| typescript ^5.9.0 | TypeScript compiler |

### calcpad-web
| Package | Version | Purpose |
|---------|---------|---------|
| monaco-editor | ^0.52.0 | Code editor |
| vue | ^3.5.39 | UI framework |
| @tauri-apps/api | ^2 | Desktop bridge (loaded dynamically only when window.__TAURI_INTERNALS__ is defined) |
| @tauri-apps/plugin-* | ^2 | fs, dialog, process, clipboard-manager, shell, store — same conditional-load pattern |
| vite | ^8.1.3 | Build tool / dev server |
| @vitejs/plugin-vue | ^5.0.0 | Vue SFC support |

### calcpad-desktop
| Package | Purpose |
|---------|---------|
| @tauri-apps/cli ^2 | Tauri CLI (`tauri dev` / `tauri build`) |

### vscode-calcpad
| Package | Purpose |
|---------|---------|
| calcpad-frontend | Shared library (file: link) |
| vue ^3.5.0 | Webview UI |
| rollup ^4.53.0 | Extension bundler |
| vite ^7.3.0 | Vue panel builder |
