# Translation sources

> The migration from English-text keys to stable message IDs is in progress.
> Catalogs and literal call sites already use IDs; a temporary bridge still
> covers data-driven labels and error messages.
> See `Calcpad.Web/frontend/scripts/i18n/MESSAGE-IDS.md`.

CalcpadCE records where every translation came from.
Provenance is part of the review, not an afterthought.

The English keys are the canonical source.
A translated key has one of three sources.

## Source labels

| Label | Meaning |
|---|---|
| `community` | Translations contributed by the CalcpadCE community, including the initial Chinese catalog and future locales. |
| `vscode` | Reused from the pinned `microsoft/vscode-loc` packs, in Monaco or a shared Calcpad UI surface: Vue sidebar, Tauri menu, webview. |

`vscode` describes the origin, not the surface.
The same source label applies whether the string lands in Monaco, Vue, the native menu, or a webview.
A `vscode` entry also records `vscodeSource`: `vscode-monaco` or `vscode-shared-ui`.

## Where provenance lives

- **Shared UI catalogs:** `Calcpad.Web/frontend/calcpad-frontend/src/i18n/messages/`.
  `sources.json` declares the source labels and the VS Code pack pin.
  `locales.json` next to it lists the tags, endonyms, aliases, and pack folders.
  Each locale catalog has its own `provenance.<locale>.json`; every key has an
  explicit entry and there is no blanket default.
  `origins` records the commit, author and date that last changed the string.
- **Native menu:** the same shared catalogs.
  `Calcpad.Web/frontend/calcpad-frontend/src/i18n/menu.ts` lists the keys the Rust menu uses.
- **Monaco:** `Calcpad.Web/frontend/calcpad-web/src/editor/monaco-nls/SOURCES.json`.
  Every map comes from one pinned pack; entries are generated, not hand-written.
- **.NET messages:** `Calcpad.Core/Messages.PROVENANCE.md`.
- **Docs:** each translated page should declare its source in front matter.

## Per-key entries

```json
"keys": {
  "Files": {
    "source": "community",
    "origin": "3e486ccf"
  },
  "Find": {
    "source": "vscode",
    "vscodeSource": "vscode-shared-ui"
  },
  "Copy": {
    "source": "community",
    "origin": "3e486ccf",
    "vscodeMatch": true
  }
}
```

`origin` for a community key is the short commit that last changed the line, as reported by `git blame`.
A `vscode` entry uses the pack pin in `sources`, records `vscodeSource`, and does not need a commit origin.
`vscodeMatch: true` means the English key and the Chinese value exactly match a pinned VS Code pack.
It is an observation, not proof that the translator copied the pack, so the source stays `community` unless a contributor confirms it.

## Automatic locale

`Auto` uses the current culture first: the OS locale on desktop, the extension
host's locale in VS Code, and the browser language list on the web. If that
culture has no catalog, it falls back to English. The host language and browser
list are only used when the current culture is unavailable.

## Catalog coverage

A catalog may contain only the keys that have been translated.
Missing keys fall back to English; every key that is present must have explicit provenance.
Generated catalogs contain the strings verified against the pinned VS Code packs, marked `vscode`.
`zh.ts` is the community-authored catalog; the others are generated.
Community translations are added as new entries with `source: "community"` and an `origin`.

## Shared UI from VS Code

The shared UI catalogs import exact matches from the pinned VS Code UI packs.
Each pack is keyed by `module::key`, not by English text, so the repo also carries an index:

- `scripts/i18n/data/vscode.index.json` — English string to `module`/`key`.
- `scripts/i18n/data/vscode.langpack.<locale>.json` — pinned pack subset.
- `calcpad-frontend/src/i18n/locales.json` — tag, endonym, aliases, fallback, and pack folder per locale.

Run `node scripts/i18n/i18n.mjs` to process every registered langpack, or
`--locale=<tag>` for one. It extracts the pinned subset, writes
`messages/<locale>.ts` and `provenance.<locale>.json`, and regenerates the
Monaco maps.
`--check` verifies the committed output.
No network access or VS Code source checkout is needed at build time.
The script includes only keys with one unique translation; keys with multiple variants stay English until the community decides.
When a key exists in both sources the script uses Monaco first and records `vscode-monaco`; otherwise it records `vscode-shared-ui`.
Community entries live in the same catalog and manifest; the script preserves entries whose provenance says `community` and rewrites only generated `vscode` entries.
Do not edit generated `vscode` entries by hand.
The human review workflow is still being defined, but the mechanical path is: add the key and its `community` provenance, then run the script.
Community overrides of a generated key also set `replaces: "vscode"`.

### Adding a shared UI term

A Calcpad UI string may reuse a VS Code translation only when VS Code has the
same English text at the pinned tag.
CI runs `scripts/i18n/check-vscode-english.mjs` to verify that English text:
Monaco entries against the installed editor, and shared-UI entries against the
pinned VS Code source, cached under `node_modules/.cache`.
No English source file is committed.

1. Find the `localize` call in VS Code at that tag and note its module and key.
2. Add the English text to `scripts/i18n/data/vscode.index.json` with that
   `module` and `key`.
3. Run `node scripts/i18n/i18n.mjs --locale=<tag>`. It copies the pinned
   translation into the subset, then regenerates the catalog and manifest.
   Never edit the subset by hand.

A Calcpad-only string with no matching VS Code text stays English unless a
reviewed community entry is added.

### Why one English word can have several translations

The pack is keyed by `module::key`, not by English text.
The same English word can appear in several modules, and each module may use a different Portuguese word for its context.
`scripts/i18n/data/vscode.index.json` groups those entries by the English text.

| English | `module::key` | Portuguese |
|---|---|---|
| `Preview` | `extensions/browser/extensionEditor::preview` | Visualizar (verb) |
| `Preview` | `userDataProfile/.../userDataProfilesEditorModel::preview` | Visualização (noun) |
| `Reset` | `mergeEditor/.../commands::...` | Redefinir |
| `Reset` | `workbench/electron-sandbox/window::zoomReset` | Reiniciar |
| `Save As...` | `files/browser/fileConstants::saveAs` | Salvar como... |
| `Save As...` | `workingCopy/.../storedFileWorkingCopy::saveAs` | SalvarComo... |

`Save As...` shows why the importer does not pick the first variant: one module contains a concatenated `SalvarComo...`.
When the grouped values differ, the importer skips the key and leaves it English.
That is not missing data; it is waiting for a community decision.
The module/key evidence stays in the index for that decision.

## Community overrides

A community translation can replace a string that came from VS Code.
Record the replaced source in `replaces`.
The only valid value is `vscode`.

In a shared UI catalog, a per-key entry becomes an object:

```json
"keys": {
  "Find": {
    "source": "community",
    "replaces": "vscode",
    "note": "Calcpad uses its own wording here."
  }
}
```

In Monaco, put the replacement in `monaco-nls/overrides/<locale>.json`.
`SOURCES.json` declares the override file and its `replaces`:

```json
"overrides": {
  "zh": {
    "source": "community",
    "replaces": "vscode",
    "path": "overrides/zh.json"
  }
}
```

The generated maps stay untouched.
The Vite plugin merges the overrides at build time.

## Rules

1. Every translated key is `community` or `vscode`.
2. Every shared UI key has an explicit entry; there is no default source.
3. Every `community` shared UI key records its `origin` commit.
4. `vscode` entries record `vscodeSource` plus the pack, tag, commit and license.
5. `vscodeMatch` does not change the source by itself.
6. Do not edit generated Monaco maps by hand; regenerate them.
7. Do not copy VS Code wording blindly; check context and terminology.
8. Machine translation is not a source.
   A machine draft must be reworked and reviewed as a community contribution before merging.
9. Language names are endonyms; never translate them.
10. A catalog may omit keys; missing keys fall back to English. Do not fill gaps with unreviewed text.
11. Run the provenance check before merging.

The check lives at `Calcpad.Web/frontend/scripts/i18n/i18n.mjs --check`.
