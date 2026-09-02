# VS Code Extension: vscode-calcpad

Rollup-bundled extension + Vue webview panel. Depends on calcpad-frontend.

## Key Providers
| Provider | Purpose |
|----------|---------|
| `calcpadCompletionProvider` | IntelliSense with functions, variables, macros, units, snippets |
| `calcpadDefinitionProvider` | Go to Definition for user symbols |
| `calcpadReferenceProvider` | Find All References |
| `calcpadRenameProvider` | Rename Symbol across file |
| `calcpadSemanticTokensProvider` | Server-based semantic highlighting |
| `calcpadIncludeCompletionProvider` | File path completion for `#include` |

## Commands (40+)
Preview, PDF/DOCX export, insert operations, formatting (bold/italic/heading/sub/super), comment toggle, `calcpad.stopServer`, and more. Defined in `package.json` contributes.commands.

## Custom Semantic Token Types
The legend is built from `SEMANTIC_TOKEN_TYPES` exported by **calcpad-frontend**, so the extension and the web editor stay in lockstep with the server's `TokenType` enum — never hardcode the list here. `mapTokenTypeToIndex` converts a server `typeId` to its legend index.

## Extension Settings
Settings are **not** VS Code configuration entries. They live as JSON under the extension's `globalStorage/settings/`, managed by `CalcpadSettingsManager` (`src/calcpadSettings.ts`):

```
<globalStorage>/settings/
    active-settings.json   live state, written on every edit
    default.json           pristine defaults, refreshed on activation
    <name>.json            user-created presets, never written by the editor
```

The active preset's *name* is remembered in `globalState` for the dropdown label only; every edit lands in `active-settings.json` regardless.

`calcpad.enableFormattingHotkeys` is the one real workspace setting, and only because keybinding `when` clauses can read nothing but `config.*`. It is mirrored from the JSON-backed `formattingHotkeys` extra.

## Server Lifecycle
`BaseServerManager` (`src/baseServerManager.ts`) picks a port, generates the `CALCPAD_API_TOKEN`, and spawns the bundled server with `--urls http://localhost:<port>`. A lock file lets a second window adopt an already-running server instead of spawning a second one, and carries the token so the adopting window can authenticate. `CALCPAD_DETACHED=1` is set so the server survives the window that spawned it. Readiness is polled against `/api/calcpad/snippets`.

## Adding a VS Code Command
1. **Define in package.json** contributes.commands:
```json
{ "command": "calcpad.newCommand", "title": "New Command", "category": "Calcpad" }
```

2. **Register in extension.ts**:
```typescript
context.subscriptions.push(
    vscode.commands.registerCommand('calcpad.newCommand', () => {
        // Implementation
    })
);
```
