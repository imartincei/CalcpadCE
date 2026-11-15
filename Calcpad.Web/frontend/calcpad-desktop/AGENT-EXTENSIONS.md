# Agent Extensions

The desktop chat panel is provider-neutral.
An agent provider (for example Claude Code) is an extension the app finds at startup; without one, the chat button stays hidden.

The app hosts everything a provider can share:

- The loopback MCP server with the editor tools (`list_open_documents`, `get_document`, `get_selection`, `get_diagnostics`, `open_document`, `propose_edit`, `approve_tool`).
- The diff review for proposed edits, and the per-conversation change tracker with backups, keep and revert.
- Conversation tabs, history, and the chat panel.

## Discovery

Each extension is a folder with an `extension.json` manifest.
The app looks in these places, and the first extension with a given `id` wins:

1. Each folder listed in the `CALCPAD_EXTENSIONS` environment variable, separated like `PATH` (for development).
2. `<app data>/extensions/<folder>`, for example `~/.local/share/org.calcpad-ce.desktop/extensions/claude-code` on Linux or `%APPDATA%\org.calcpad-ce.desktop\extensions\claude-code` on Windows.
3. `<resources>/extensions/<folder>`, for extensions bundled with the app.

The chat panel uses the first agent provider found.

## Manifest

| Field | Meaning |
| --- | --- |
| `id` | Letters, digits, `-` and `_`. Names the provider's data folder, `<app data>/agent/<id>`. |
| `kind` | Must be `"agent"`. |
| `name` | Shown in the panel header and chat toggle. |
| `assistantName` | Optional short name used in prompts such as "Message Claude…". Defaults to `name`. |
| `command` | Adapter executable, relative to the extension folder. On Windows `.exe` is added when there is no extension. |
| `models` | Optional `{ value, label }` list for the model picker. An empty `value` means the provider's default. |
| `permissionModes` | `{ value, label, hint, edits, profile }` list, in Shift+Tab order; the first is the default. |
| `settings` | Optional `{ key, label, placeholder }` text fields shown in the chat settings. |
| `i18n` | Optional `{ locale: { english: translated } }` messages merged into the app's translations. |

`edits` decides what `propose_edit` does in a mode: `review` shows the diff to accept, `auto` applies it after backing up the worksheet, and `blocked` refuses it.
Switching between modes with different `profile` values restarts the adapter once the current turn ends, and the session resumes.

## Protocol

The adapter talks JSON lines over stdio, and its working directory is the active worksheet's folder.
Anything it writes to stderr, and any stdout line that is not an event, goes into the error shown when it exits with a non-zero code.

The first line on stdin is `start`, which carries the MCP bearer token, so it never appears in a command line:

```json
{
  "type": "start",
  "protocol": 1,
  "session": "main-<conversation id>",
  "dataDir": "<app data>/agent/<id>",
  "extensionDir": "<extension folder>",
  "options": { "model": "opus", "permissionMode": "default", "resume": "<session id or null>", "settings": { "executable": "" } },
  "mcp": {
    "name": "calcpad",
    "type": "http",
    "url": "http://127.0.0.1:<port>/mcp",
    "headers": { "Authorization": "Bearer <token>", "X-Calcpad-Window": "main", "X-Calcpad-Conversation": "<id>" },
    "autoApprovedTools": ["list_open_documents", "get_document", "..."],
    "approvalTool": "approve_tool"
  }
}
```

Later lines are commands:

| Command | Fields |
| --- | --- |
| `send` | `text` |
| `interrupt` | |
| `setModel` | `model` (empty means the default) |
| `setPermissionMode` | `mode` |
| `stop` | Stop the agent and exit. The app kills the adapter if it is still running 3 seconds later. |

The adapter should also exit when stdin closes.

Each stdout line is one event, tagged by `kind`, with camelCase fields:

| Event | Fields |
| --- | --- |
| `sessionStarted` | `sessionId`, `model`, `version`, `permissionMode` |
| `textDelta` | `text` |
| `assistantText` | `text` |
| `toolUse` | `id`, `name`, `input` |
| `toolResult` | `toolUseId`, `isError`, `content` |
| `turnComplete` | `isError`, `subtype`, `costUsd`, `error` |
| `status` | `text` |
| `error` | `message` |

The app reports `exited` itself when the adapter process ends.
`sessionId` is passed back as `options.resume` when the conversation restarts.
