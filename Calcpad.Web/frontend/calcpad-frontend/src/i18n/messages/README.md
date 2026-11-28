# Shared UI catalogs

- `en.ts` is the English source. Keys are stable message IDs; English lives in the values.
- A locale catalog is `<locale>.ts`, with `provenance.<locale>.json`.
  Both are generated for every locale with `"catalog": true`.
- `sources.json` declares the source labels and the VS Code pack pin.
- `../locales.json` is the locale registry; the script generates
  `../locales.generated.ts` from it.
- `community/<locale>.ts` is the human-editable source, with `ui` and `monaco`
  sections. The generator merges it with the pinned pack, derives missing
  origins with `git blame`, and marks overrides with `replaces: "vscode"`.
- `zh.ts` is generated from `community/zh.ts`; do not edit it directly.
- The generator reads `scripts/i18n/data/vscode.index.json` (English to VS Code
  module/key) and `vscode.langpack.<locale>.json` (pinned pack subset).
  A word can have several pack translations because it appears in different
  modules. Only unique translations are imported; multi-variant keys stay
  English until the community decides. See `TRANSLATING.md`.
- Keys missing from a catalog fall back to English.
- Provenance has no default: a community entry records `source: "community"`
  and an `origin`; a `vscode` entry records `vscodeSource`.
- A community entry can add `replaces: "vscode"` when it supersedes a generated
  string.
- Native Tauri menu labels live in the same catalogs; `i18n/menu.ts` lists the
  IDs and filters untranslated entries.
- `common.auto` is the Auto option for locale selection.
  It resolves the current culture first, then the host language, then the
  browser list; a culture without a catalog falls back to English.
- Do not edit generated catalogs, provenance, or Monaco maps by hand.

## ID convention

IDs are lowercase, dot-separated and ASCII: `<area>.<component>.<element>`,
for example `menu.openRecent`.
Reuse one ID when meaning and translation are the same; split IDs when context
changes the translation.
The English text is not part of the key, so rewriting English does not rename
IDs.
IDs are not translated.
Use `Label`, `Question` and `Ellipsis` suffixes for trailing punctuation.

Monaco keys keep their `module::key` form; they are already context-specific
and do not use UI message IDs.

## Adding a message

Add the English text with a new ID to `en.ts`.
Add the translation to `community/<locale>.ts` when one exists.
Then run `node scripts/i18n/i18n.mjs --locale=<locale>` and
`node scripts/i18n/i18n.mjs --check`.
