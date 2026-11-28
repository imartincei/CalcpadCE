# Monaco NLS maps

Generated from the VS Code language packs pinned in
`Calcpad.Web/frontend/scripts/i18n/i18n.mjs` and registered in
`Calcpad.Web/frontend/calcpad-frontend/src/i18n/locales.json`:

- `vscode-language-pack-cs`
- `vscode-language-pack-de`
- `vscode-language-pack-es`
- `vscode-language-pack-fr`
- `vscode-language-pack-it`
- `vscode-language-pack-ja`
- `vscode-language-pack-ko`
- `vscode-language-pack-pl`
- `vscode-language-pack-pt-BR`
- `vscode-language-pack-ru`
- `vscode-language-pack-tr`
- `vscode-language-pack-zh-hans`
- `vscode-language-pack-zh-hant`

The pseudo-locale pack `vscode-language-pack-qps-ploc` is intentionally excluded.

Source: [microsoft/vscode-loc](https://github.com/microsoft/vscode-loc) tag
`v1.96.2024121109`, commit `110df563b65cd2a1f9d8b58e3bdcadce90427f6d`.
License: MIT, see [LICENSE.md](LICENSE.md).

Do not edit the JSON files by hand. Regenerate them with:

```sh
cd Calcpad.Web/frontend
node scripts/i18n/i18n.mjs
```

The Vite plugin in `calcpad-web/vite.config.ts` matches the `(module, key)`
pairs of Monaco's `localize()` calls and falls back to English when an entry
is missing.

Every entry is generated from the pinned pack; see `SOURCES.json`.
None of the entries are hand-written.

Community overrides live in `overrides/<locale>.json` and are declared in
`SOURCES.json` with `replaces: vscode`.
Monaco maps record `vscodeSource: vscode-monaco`; shared UI entries use `vscode-shared-ui`.
The Vite plugin merges them over the generated map at build time.
