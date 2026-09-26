# Third-Party Notices

CalcpadCE incorporates third-party software. This file reproduces the notices
those components require. CalcpadCE's own licence is in [LICENSE](LICENSE).

---

## Mozilla Public License 2.0

The following Rust crates are licensed under MPL-2.0 and are linked into the
desktop application on every platform. MPL-2.0 is a file-level copyleft; the
source for each is available at the URL given, at the exact version shipped.

| Component | Version | Source |
|---|---|---|
| cssparser | 0.36.0 | https://static.crates.io/crates/cssparser/cssparser-0.36.0.crate |
| cssparser-macros | 0.6.1 | https://static.crates.io/crates/cssparser-macros/cssparser-macros-0.6.1.crate |
| selectors | 0.36.1 | https://static.crates.io/crates/selectors/selectors-0.36.1.crate |
| dtoa-short | 0.3.5 | https://static.crates.io/crates/dtoa-short/dtoa-short-0.3.5.crate |
| option-ext | 0.2.0 | https://static.crates.io/crates/option-ext/option-ext-0.2.0.crate |

A copy of the Mozilla Public License 2.0 is at <https://mozilla.org/MPL/2.0/>.

DOMPurify, vendored inside `monaco-editor` and compiled into the shipped
`editor.main.js`, is dual-licensed Apache-2.0 OR MPL-2.0. **CalcpadCE elects
Apache-2.0**, so it carries no MPL obligation. Licence text:
<https://www.apache.org/licenses/LICENSE-2.0>.

---

## Pass-through notices

These components ship their own third-party notices, which must be reproduced
in full rather than summarised. Each is reproduced verbatim under "Licence
texts" below, alongside the licences of the component that carries it.

- **.NET runtime** — the desktop bundle is a self-contained publish and
  redistributes the whole runtime, so `Microsoft.NETCore.App.Runtime.<rid>`,
  `Microsoft.AspNetCore.App.Runtime.<rid>` and `Microsoft.NETCore.App.Host.<rid>`
  are harvested like any other dependency. The version is taken from the
  bundle's `deps.json`, never restated here.
- **SkiaSharp** — `libSkiaSharp.so` / `.dll` ship as prebuilt binaries, so the
  `SkiaSharp.NativeAssets.*` notices cover whatever is linked into them
  (FreeType, libjpeg, libpng, zlib, ICU and more).
- **monaco-editor** — vendors its own notices (CC-BY-4.0, Unicode,
  W3C-20150513) and is compiled into the shipped `editor.main.js`.

---

## Fonts

- **Jost** — SIL Open Font License 1.1. No Reserved Font Name.
- **JuliaMono** — SIL Open Font License 1.1, with a Reserved Font Name.
- **DejaVu Serif Condensed** — Bitstream Vera Fonts Copyright, plus
  public-domain glyphs.

Licence files ship alongside the fonts.

---

## Bundled UI assets

- **jSpreadsheet CE** and **jSuites** — MIT. The minified bundles in
  `Calcpad.Web/backend/UiAssets/` have had their upstream banners stripped by
  the minifier, so MIT's copyright and permission notice cannot travel inside
  the copy. Their licence texts are therefore reproduced below.
