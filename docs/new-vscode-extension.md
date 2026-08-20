# VS Code Extension

The **CalcpadCE VS Code extension** turns Visual Studio Code or VS Codium into a full CalcpadCE authoring environment.
You write `.cpd` files with syntax highlighting, autocomplete, and inline error checking.
It renders .cpd code to a live HTML report and can export to PDF, Word, or HTML.
It is all driven by the same calculation engine as the rest of CalcpadCE.

See [Selecting a CalcpadCE Deployment](new-selecting-calcpadce-deployment.md) to help decide if this is the right CalcpadCE deployment for you.

## Requirements

- **Visual Studio Code** 1.82 or newer.
- **.NET runtime 10** (VS Code will offer to install if not installed)
- A **Chromium-based browser** (Chrome, Edge, Chromium) for PDF export. See [PDF Export](new-pdf-export.md).

## Installing the extension

The extension is distributed as a `.vsix` package (it is not yet on the VS Code or Open VSX Marketplace).

1. Obtain the `vscode-calcpad-<version>.vsix` file from the Github releases page.
2. In VS Code, open the **Extensions** view (**Ctrl+Shift+X**).
3. Click the **⋯** menu at the top of the view → **Install from VSIX…** and select the file.
4. Reload the window if prompted.

Alternatively, from a terminal: `code --install-extension <path>/vscode-calcpad-<version>.vsix`.

## Your first document

1. Create a new file and save it with a **`.cpd`** extension (for example `beam.cpd`). VS Code detects the `calcpad` language automatically.
2. Type a calculation, for example:

   ```calcpad
   "Cantilever tip deflection
   P = 5kN
   L = 3m
   E = 200GPa
   I = 8.5e-5m^4
   δ = P·L^3/(3·E·I)
   ```

3. Click the **CalcpadCE Preview** button in the editor's top-right toolbar, or run **CalcpadCE Preview** from the Command Palette (**Ctrl+Shift+P**).

The preview opens in a side column and re-renders every time you edit.
The bundled calculation engine starts automatically the first time you render — the first render may take a moment while it starts up.

 Compiled **`.cpdz`** worksheets open in a dedicated editor, and **Save** writes the values you entered back into the file.

## The editor

The editor is the same across every CalcpadCE front end — see **[The CalcpadCE Editor](new-calcpadce-editor.md)** for syntax highlighting, autocomplete, quick-type symbols, operator replacement, formatting hotkeys, symbol navigation, and path completion for `#include`/`#read` paths.

VS Code specifics:

- Path completion draws on the files in your **workspace** as well as the document's `#ProjectPath`/`#LibraryPath` roots.
- Run **CalcpadCE: Install JuliaMono Font** from the Command Palette if the math glyphs look wrong.
- The full **Ctrl+1** … **Ctrl+6** heading hotkeys are available (the desktop app uses those for tabs).

## The CalcpadCE panel

Click the **CalcpadCE** icon in the activity bar (left edge) to open the panel.
It has **Files** and **CalcpadCE** views, and the CalcpadCE view is organized into tabs (Insert, TOC, Settings, Variables, Formatting, Export, Errors, Properties).
The view title bar has **CalcpadCE: Run Preview** (re-renders the active file and refreshes plots) and **Stop Server** buttons.

The panel is the same across every CalcpadCE front end — see **[The CalcpadCE Panel & Settings](new-calcpad-panel.md)** for a full walkthrough of each tab.

## Live preview

Three preview panels are available, each opening in its own editor column:

| Panel | How to open | What it shows |
|-------|-------------|---------------|
| **HTML Preview** | Preview button in the editor toolbar, or *CalcpadCE Preview* in the Command Palette | The document as written — both `#pre` and `#post`, source values |
| **Unwrapped Preview** | Eye button in the editor toolbar, or *CalcpadCE Preview Unwrapped* | The fully expanded source (macros and includes resolved) — useful for debugging what the engine actually computes |
| **Report Preview** | Book button in the editor toolbar, or *CalcpadCE: Toggle Report Preview* | The print layout — `#pre` hidden and entered `#UI` values applied |

The Report preview opens on its own beside the editor, or alongside the `#UI` input form when that is open, and it keeps its line links so a result traces back to the line that produced it.
With it (or the input form) focused, **CalcpadCE: Print Report to PDF** appears as a button in the panel's title bar.
See **[UI Mode](new-ui-mode.md)**.

All panels:

- Re-render automatically as you type when **Auto-Run Preview** is on (default).
- Follow the `previewTheme` setting (`light` / `dark` / `system`), using `darkBackground` for the dark background color.
- Embed local images so PDF/print output matches the preview.

Right-click a preview → **View Webview Source HTML** to inspect the rendered markup.

### Running on demand (Auto-Run off)

When you turn **Settings → Auto-Run Preview** off, typing no longer re-renders the preview.
Trigger the run yourself via any of:

- **Ctrl+Alt+X** (works whenever a `.cpd` or plaintext editor has focus)
- Right-click in the editor → **CalcpadCE: Run Preview**
- The **CalcpadCE: Run Preview** button in the CalcpadCE sidebar's view title bar
- The *CalcpadCE: Run Preview* command in the Command Palette

Running also re-lints the document, refreshes syntax highlighting, and rebuilds the Export tab's plot list.

## Errors and diagnostics

CalcpadCE errors surface in VS Code's **Problems** panel (**Ctrl+Shift+M**) as standard diagnostics with Error / Warning / Information severities, each with a `CPD-XXXX` code.
Click one to jump to the offending line.
Control how much is shown with the linter minimum-severity setting.

For errors that occur inside hidden (`#hide`) regions — which don't appear in the preview — use the **Errors** tab in the sidebar to see the full list with source-line links.

See **[The CalcpadCE Editor → Linting](new-calcpadce-editor.md#linting)** and **[Linter and Diagnostics](new-linter.md)**.

## Exporting

All exports run through the same engine as the preview, so what you see is what you get.

Every export is a **report** by default: `#pre` hidden, `#post` shown, and the values entered into the `#UI` form applied.

| Output | How |
|--------|-----|
| **PDF** | **Export CalcpadCE to PDF** button in the editor toolbar, or *Export CalcpadCE to PDF*. Requires a Chromium browser — see [PDF Export](new-pdf-export.md). |
| **PDF (report)** | **CalcpadCE: Print Report to PDF** — the same export, also a title-bar button on the report and input-form panels |
| **HTML** | **Save HTML…** on the sidebar's **Export** tab, or *CalcpadCE: Save Source HTML…* |
| **Word (.docx)** | **Save Word…** on the sidebar's **Export** tab, or *CalcpadCE: Save as Word Document…* |

The sidebar's **Export** tab also offers the other renderings, grouped by variant: **Preview** (PDF, HTML, Word), **Input form** (PDF, HTML) and **Unwrapped** (PDF, HTML).
No exported file carries the line numbers or error boxes the preview panels use for navigation.

Set the document title, timestamp format, page size, and header/footer in the **PDF Export** section of the panel's **Settings** tab, or pin them to one document from the **Properties** tab — see [PDF Export](new-pdf-export.md).
The **Export** tab also has a **Plots** section — a thumbnail list of every plot the document emits, each with an individual **Save…** button and a **Download all (ZIP)** button.
See [The CalcpadCE Panel & Settings → Export](new-calcpad-panel.md#export).

## Settings

All CalcpadCE settings — math, plot, units, preview and color themes, editor features, linter severity, and named configurations — live in the **Settings** tab of the CalcpadCE panel, **not** in VS Code's normal settings editor.
Editing them there keeps them in sync with the extension and the server.

See **[The CalcpadCE Panel & Settings → Settings](new-calcpad-panel.md#settings)** for the full list.

## The bundled engine

The extension runs a local CalcpadCE engine to do all conversion and linting.
You don't normally need to think about it:

- It **starts automatically** in the background when the extension activates.
- It **auto-restarts** on a crash (up to 3 retries) before asking you to refresh manually.
- It **shuts down cleanly** when the window closes.

If something goes wrong, use the Stop and Restart buttons on the CalcpadCE panel to stop/restart it.

### Output channels for troubleshooting

Four VS Code output channels help diagnose problems (open the Output panel and pick from the dropdown):

| Channel | Shows |
|---------|-------|
| **CalcpadCE Extension** | Extension lifecycle, commands, errors |
| **CalcpadCE Output HTML** | Rendered HTML in the preview |
| **CalcpadCE Webview Console** | Console messages from preview panels |
| **CalcpadCE Server Debug** | Output from the engine process |

## Troubleshooting

| Symptom | Fix |
|---------|-----|
| Preview never renders / "server not ready" | Click refresh icon in the CalcpadCE panel to try restarting a server that crashed or failed to start. Check the **CalcpadCE Server Debug** output channel. |
| PDF export fails | Verify the file is not locked and a Chromium browser is installed (Chrome/Edge/Chromium). See [PDF Export](new-pdf-export.md). |

## See also

- [The CalcpadCE Editor](new-calcpadce-editor.md) — the shared editor, navigation, and linting
- [The CalcpadCE Panel & Settings](new-calcpad-panel.md) — the shared sidebar and all settings
- [Using the Desktop App](new-desktop-app.md)
- [PDF Export](new-pdf-export.md) · [Includes and File Reads](new-includes.md) · [Linter and Diagnostics](new-linter.md) · [Table of Contents](new-table-of-contents.md)
- [Writing Math](writing-math.md) · [Quick Reference](quick-reference.md)
