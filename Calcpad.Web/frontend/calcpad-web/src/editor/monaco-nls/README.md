# Monaco NLS cache

The Monaco NLS maps and community overrides are generated at build time and are
not committed.
They live under
`calcpad-web/node_modules/.cache/calcpad-monaco-nls/maps/` and
`calcpad-web/node_modules/.cache/calcpad-monaco-nls/overrides/`.

Source packs: [microsoft/vscode-loc](https://github.com/microsoft/vscode-loc) tag
`v1.96.2024121109`, commit `110df563b65cd2a1f9d8b58e3bdcadce90427f6d`.
License: MIT, see [LICENSE.md](LICENSE.md).

Monaco's ESM build calls `localize('key', 'English')` with a string key, so the
numeric NLS table used by the AMD build is never consulted.
The generated maps carry the `(module, key)` translations from the pinned packs.

Generate or refresh the cache with:

```sh
cd Calcpad.Web/frontend
node scripts/i18n/i18n.mjs --monaco-cache
```

`npm run dev` and `npm run build` run the same command through their `pre*`
scripts.
`SOURCES.json` records the pack pins, the generated map metadata, and the
community override declarations.

Do not edit the cache JSON by hand.
Community Monaco overrides are authored in
`calcpad-frontend/src/i18n/community/<locale>.ts` and the generator writes them
to `overrides/<locale>.json` in the cache.
