# i18n pipeline

One script owns the whole pipeline. It extracts the pinned VS Code pack subsets,
imports exact matches into the locale catalogs, generates the Monaco NLS cache
at build time, and validates provenance.

The locale list is the single source of truth in
`calcpad-frontend/src/i18n/locales.json`. It records each tag, endonym, aliases,
language fallback, VS Code pack folder, and whether the locale gets a shared UI
catalog and a Monaco map. The script generates the typed
`calcpad-frontend/src/i18n/locales.generated.ts` from it.

## Usage

Run from `Calcpad.Web/frontend`, or use the `calcpad-web` package scripts:

```sh
node scripts/i18n/i18n.mjs                 # update generated files and Monaco cache
node scripts/i18n/i18n.mjs --check         # verify catalogs, provenance and Monaco overrides
node scripts/i18n/i18n.mjs --monaco-cache  # refresh only the Monaco cache
node scripts/i18n/i18n.mjs --locale=pt-BR
node scripts/i18n/i18n.mjs --verbose
node scripts/i18n/check-vscode-english.mjs # verify English at each module/key
```

```sh
npm run i18n
npm run i18n:check
```

`--locale` limits extraction, catalog import, and Monaco generation to one
locale. `--check` never writes repository files and exits non-zero when the
catalogs, provenance, or Monaco override keys are stale.
It compares the Monaco cache only when the cache exists; the maps and overrides
are build output.
`--monaco-cache` writes only the build cache under
`calcpad-web/node_modules/.cache/calcpad-monaco-nls/`; the maps and overrides
are not committed.
`check-vscode-english.mjs` verifies the English text at every
`vscode.index.json` module/key.
Monaco entries use the installed editor; shared-UI entries are fetched from the
pinned VS Code source and cached under `node_modules/.cache`.
CI runs this check; no English source file is committed.

## Layout

```
scripts/i18n/
├── i18n.mjs
├── check-vscode-english.mjs
├── MESSAGE-IDS.md
├── README.md
└── data/
    ├── vscode.index.json        English -> VS Code module/key
    └── vscode.langpack.<locale>.json
```

The registry lives in
`calcpad-frontend/src/i18n/locales.json`.
The script writes `calcpad-frontend/src/i18n/locales.generated.ts` and the
locale catalogs next to it.

## Message-ID migration

The catalogs now use stable message IDs.
Some data sources still hold English labels; those labels stay English until
the source returns an ID.
See [`MESSAGE-IDS.md`](MESSAGE-IDS.md) for the convention and the community
file format.

## Rules

- `locales.json` decides which packs are processed.
  Edit it first, then run the script.
- `vscode.langpack.<locale>.json` is extracted from the pinned VS Code pack.
  It is created on first run and must not be edited by hand.
- `calcpad-frontend/src/i18n/messages/<locale>.ts` and
  `provenance.<locale>.json` are generated for every locale with
  `"catalog": true` by merging the pack with `community/<locale>.ts`; do not
  edit generated entries.
- `calcpad-frontend/src/i18n/community/<locale>.ts` is the human-editable
  community source. Its `ui` and `monaco` sections are merged into the generated
  catalogs and maps.
- `community/<locale>.provenance.json` is generated: missing origins come from
  `git blame` on the community `.ts` file.
- `zh` is `"catalog": true`; its shared UI catalog combines the VS Code pack
  with the community file.
  Its Monaco map still comes from `vscode-language-pack-zh-hans`.
- The pseudo-locale pack `vscode-language-pack-qps-ploc` is intentionally
  excluded.
- Generated catalogs are overwritten by the script; edit
  `community/<locale>.ts` instead.
- The VS Code pack is keyed by `module::key`, not by English text.
  `vscode.index.json` is the English bridge.
  Every index entry must point at a real `localize` call with the same English
  text in VS Code at the pinned tag.
- A word can have several translations across modules.
  Only keys with one unique translation are imported; the rest stay English
  until the community decides.
- A Calcpad-only string with no VS Code correspondence does not belong in the
  shared pack; leave it English or add a reviewed community entry.
- `vscodeSource` records `vscode-monaco` or `vscode-shared-ui`.
- Community overrides use `source: "community"` and `replaces: "vscode"`.
  The generator derives both from `community/<locale>.ts`; review happens there.

See `TRANSLATING.md` for the provenance policy.
