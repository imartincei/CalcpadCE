# Message-ID migration

Status: in progress.
The catalogs and literal call sites use IDs.
A temporary English-to-ID bridge still covers data-driven labels and error
messages until those sources return IDs directly.
The generated draft is a snapshot of the lookup sites that remain to migrate.

## Target model

- Every UI message has a stable ID.
- English lives only in `calcpad-frontend/src/i18n/messages/en.ts`:
  ```ts
  const en = {
    'common.close': 'Close',
    'dialog.close': 'Close',
    'editor.close': 'Close',
    'sidebar.files': 'Files',
  }
  export default en
  ```
- A locale catalog maps IDs to translations:
  ```ts
  const catalog = {
    'common.close': 'Fechar',
    'editor.close': 'Fechar editor',
  }
  export default catalog
  ```
- Missing IDs fall back to `en` through vue-i18n.
- Call sites use IDs only: `t('editor.close')`.
- The native menu keeps its English lookup:
  `MENU_KEYS` pairs an ID with the English text the Rust menu expects.
  `menuLabels(t)` returns `{ [english]: t(id) }`.
- Monaco keeps its `module::key` keys.
  They are already context-specific and do not need UI message IDs.

## Community contribution

One human-editable file per locale:

```ts
// calcpad-frontend/src/i18n/community/fr.ts
import type { CommunityCatalog } from '../types'

export default {
  ui: {
    'common.close': 'Fermer',
    'editor.close': 'Fermer l’éditeur',
    'sidebar.files': 'Fichiers',
  },
  monaco: {
    'vs/editor/contrib/find/browser/findWidget::label.closeButton': 'Fermer',
  },
} satisfies CommunityCatalog
```

- Contributors edit only `community/<locale>.ts`.
- The generator merges `en.ts`, the VS Code pack, and the community file.
- Generated catalogs, provenance, and Monaco maps are build output.
- An override is automatic:
  a community key that also exists in the generated VS Code set wins, and
  provenance records `source: "community"` and `replaces: "vscode"`.
- The generator writes `overrides/<locale>.json` into the Monaco build cache
  from the `monaco` section and validates each `module::key` against the base
  map.
  `SOURCES.json` declares a community override for every locale.
  The cache lives under `calcpad-web/node_modules/.cache/`, not in Git.
- The `origin` for a community entry is the short commit that last changed its
  line in the community file.
  Commit `community/<locale>.ts` first; the generator derives missing origins
  with `git blame` and writes `community/<locale>.provenance.json`.
  Contributors do not edit the sidecar by hand.

## ID convention

```text
<area>.<component>.<element>
```

Examples:

```text
common.close
common.cancel
common.apply
dialog.close
editor.close
sidebar.files
settings.language
settings.info.usOrUkUnits
metadata.project
insert.customSnippet
variables.search
export.savePdf
menu.openRecent
```

Rules:

- Lowercase, dot-separated, ASCII.
- Reuse one ID when meaning and translation are the same.
- Split IDs when context changes the translation.
  `dialog.close` and `editor.close` can carry different values.
- The English text is not part of the key.
  Rewriting English does not rename IDs.
- IDs are not translated.
- A trailing `:` becomes `Label`, `?` becomes `Question`, and `…` becomes
  `Ellipsis` in the proposed slug.
  These are review hints, not a hard rule.

## Review draft

Regenerate the inventory with:

```sh
cd Calcpad.Web/frontend
node scripts/i18n/propose-ids.mjs
```

Outputs:

- `scripts/i18n/drafts/message-ids.draft.json` — machine-readable inventory.
- `scripts/i18n/drafts/message-ids.draft.md` — tables grouped by area.

The current snapshot has:

- 580 entries.
- 27 areas.
- 25 dynamic lookup sites.
- 83 multi-area or shared entries.
- 41 long strings.
- 61 catalog keys not found in source or data.

Flags to review:

| Flag | Meaning | Action |
|---|---|---|
| `shared` | Common word used in several areas. | Usually keep `common.<word>`. |
| `multiArea` | Same English text in several areas. | Decide one shared ID or split by context. |
| `long` | Text longer than 80 characters. | Rename to a short semantic ID. |
| `unused` | Only in the `zh` catalog, not in source or data. | Remove or find the missing data source. |
| `collision` | The automatic slug collided with another string. | Rename one of them. |

The IDs are proposals.
The final map will be reviewed before any call site changes.

## Dynamic lookups

The draft lists 25 call sites that read a key from data:

- `t(group.label)` in the export groups.
- `t(REFERENCE_LABELS[kind].relative/absolute)`.
- `t(SNIPPET_TYPE_LABELS[type])`.
- `tt(spec.label)`, `tt(spec.detail)`, `tt(opt.label)` in settings and metadata.
- `tt(SETTING_TOGGLES[id].label)`, `tt(command.label)`, `tt(c.label)`.
- `t(invalid, …)`, `t(conflict.label)` in the hotkey recorder.
- `t(currentMode.label)`, `t(mode.label)`, `t(action.label)` in the web shell.
- `t(error.message, error.params)` in the insert tab.

The table below is the Phase 0 snapshot.
Most of those sites now receive IDs from their data source.
The remaining categories are:

- Other service errors that still return English text to `t(error.message)`.
  Quick-type validation, run-error wrappers, and included-lint wrappers are
  already structured; the server-provided message stays a parameter.
- Agent or server-provided labels and question options.
- Export group details that have no catalog entry yet.

The remaining service errors are the largest risk and should be migrated one
service at a time.

## Current implementation

- `messages/en.ts` is the English source (610 IDs).
- `scripts/i18n/message-ids.json` is the English-to-ID map.
- `messages/<locale>.ts` catalogs are keyed by ID.
- `community/zh.ts` holds the 512 community entries; 38 of them override
  VS Code entries and are marked `replaces: "vscode"` automatically.
- `community/<locale>.provenance.json` caches the origin of each community
  entry. Missing origins are derived from `git blame` on the `.ts` file.
- `legacy-keys.generated.ts` and `useCalcpadI18n()` keep English data labels
  working while the remaining sources migrate.
- Literal calls were codemodded (508 replacements).
- Data labels in settings, PDF, keybindings, setting actions, hotkeys, export
  groups, desktop actions, file/insert constants, `#UI` property catalogs,
  addable fields, and the native menu now carry IDs.
- `en.ts` is used where a host shows a label without a translator: validation
  messages, search indexes, and the VS Code status bar.
- `run-errors.ts` and `included-lint.ts` now add `messageId` and `params` to
  client-generated wrappers; the web translates them and the VS Code host uses
  `en` plus `formatMessage()`.
- Other service errors, agent-provided labels, and a few export details still
  need IDs.
- `scripts/i18n/apply-ids.mjs`, `codemod-ids.mjs`, and `codemod-data-ids.mjs`
  are one-time migration helpers; do not rerun them after the catalogs use IDs.

## Migration phases

1. **Inventory and convention** — done.
2. **Community source and English catalog** — done.
   `en.ts`, `community/zh.ts`, and the legacy bridge exist.
3. **Bulk literal migration** — done for literal `t('…')` calls.
4. **Data-driven migration** — in progress.
   Settings/metadata specs, `#UI` property catalogs, addable fields, export
   groups, keybindings, setting actions, hotkeys, desktop actions, file/insert
   constants, the native menu, quick-type validation, and run/included-lint
   wrappers use IDs.
   Remaining: other service errors, agent-provided labels, and a few export
   details.
5. **Remove the legacy key path** — delete `legacy-keys.generated.ts` and the
   English-key fallback.

`zh` was the first community migration because it already had 512 translated
entries.
Moving them to `community/zh.ts` proved the override and provenance flow with
real data.

## Validation

The generator and `--check` should eventually enforce:

- Every catalog key exists in `en.ts`.
- Every community key exists in `en.ts`.
- Every ID in `vscode.index.json` exists in `en.ts`.
- Every `vscode.index.json` entry points at a real `localize` call at the
  pinned VS Code tag.
- Every catalog key has explicit provenance.
- A community key that replaces a generated key records `replaces: "vscode"`.
- No duplicate IDs and no empty translations.
- `community/<locale>.ts` is type-checked against `CommunityCatalog`.

## Open decisions

- Approve the `<area>.<component>.<element>` convention.
- Decide whether shared words default to `common.*` or to their first area.
- Decide whether long settings descriptions use `settings.info.<setting>`
  (preferred) or a truncated automatic slug.
- Decide whether the `zh` migration lands with the infrastructure or in a
  separate commit.
