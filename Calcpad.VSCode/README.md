# CalcpadCE Preview (VS Code)

Live, interactive preview for CalcpadCE `.cpd` worksheets — similar to the built-in
Markdown preview. The preview re-renders as you type and supports the interactive
input fields (`?` prompts) and unit selectors with full recalculation.

It renders through the same engine as the desktop app, by talking to a bundled
`Calcpad.Cli` running in a long-lived `--serve` (NDJSON over stdio) mode.

## Usage

- Open a `.cpd` file.
- Run **Calcpad: Open Preview to the Side** (`Ctrl+K V`) or **Calcpad: Open Preview** (`Ctrl+Shift+V`),
  or click the preview icon in the editor title bar.
- Edit the worksheet — the preview updates after a short debounce.
- Toggle **Interactive** / **Final** in the preview toolbar: Interactive shows editable input
  fields that recalculate live; Final shows the read-only calculated output.
- Change an input field or unit selector in the preview — the worksheet recalculates.

## Export

Export the calculated worksheet (using the values currently shown in the preview):

- Toolbar buttons **Export HTML** / **Export DOCX** in the preview.
- Commands **Calcpad: Export to HTML** / **Calcpad: Export to Word (DOCX)** (command palette
  or right-click a `.cpd` in the Explorer).

PDF export is not yet available (it requires bundling `wkhtmltopdf`).

## Settings

| Setting | Default | Description |
| --- | --- | --- |
| `calcpad.preview.debounceMs` | `300` | Delay before re-rendering after an edit. |
| `calcpad.preview.updateOnSaveOnly` | `false` | Only refresh on save. |
| `calcpad.cli.path` | `""` | Override the CLI used for rendering (native exe or `Cli.dll`). |

## Development

```pwsh
cd Calcpad.VSCode
npm install
dotnet build ..\Calcpad.Cli   # provides the dev-fallback Cli.dll
npm run build
# Press F5 to launch the Extension Development Host
```

`npm run build` extracts the worksheet CSS from `Calcpad.Cli/doc/template.html`,
copies jQuery into `media/`, and bundles the extension and webview client with esbuild.

In the Extension Development Host the extension auto-discovers the repo's built
`Calcpad.Cli/bin/<Config>/net10.0/Cli.dll`. For a packaged build, run
`npm run publish-cli` to publish a self-contained CLI into `bin/<rid>/`, then
`vsce package --target <platform>`.

## Limitations (MVP)

- Worksheet fonts fall back to system fonts (no embedded `@font-face`).
- Embedded `<script>` worksheets (e.g. Plotly 3D examples) don't run under the webview CSP.
- `?` input fields inside macros/included modules may not map back to editable inputs.
- PDF export is not bundled yet (needs `wkhtmltopdf`).
