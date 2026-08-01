# VS Code Extension

> Calcpad.Web only. The standalone WPF desktop application for Windows is separate and unaffected.

The **CalcpadCE VS Code extension** turns Visual Studio Code into a full CalcpadCE authoring environment.
You write `.cpd` files with syntax highlighting, autocomplete, and inline error checking, then render them to a live HTML report and export to PDF, Word, or HTML — all driven by the same calculation engine as the rest of CalcpadCE.

For the CalcpadCE language itself, start with **[Writing Math](writing-math.md)** and the **[Quick Reference](quick-reference.md)**.

## Requirements

- **Visual Studio Code** 1.82 or newer.
- **.NET runtime** on your PATH. The extension bundles a local CalcpadCE server that it launches automatically; it needs `dotnet` to run it. Verify with `dotnet --info` in a terminal.
- A **Chromium-based browser** (Chrome, Edge, Chromium) for PDF export. See [PDF Export](new-pdf-export.md).

## Installing the extension

The extension is distributed as a `.vsix` package (it is not yet on the Marketplace).

1. Obtain the `vscode-calcpad-<version>.vsix` file, or build it yourself from `Calcpad.Web/frontend/vscode-calcpad` with `npm run package:vsix`.
2. In VS Code, open the **Extensions** view (**Ctrl+Shift+X**).
3. Click the **⋯** menu at the top of the view → **Install from VSIX…** and select the file.
4. Reload the window if prompted.

Alternatively, from a terminal: `code --install-extension vscode-calcpad-<version>.vsix`.

## Your first document

1. Create a new file and save it with a **`.cpd`** extension (for example `beam.cpd`). VS Code detects the `calcpad` language automatically. Compiled **`.cpdz`** worksheets are binary, so they are not edited as text — opening one gives you its input form in a dedicated editor, and **Save** writes the values you entered back into the file. To produce one from a document you are editing, run **CalcpadCE: Save As Compiled Worksheet…**.
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
The bundled server starts automatically the first time you render — the first render may take a moment while it spins up.

## The editor

### Syntax highlighting

`.cpd` files get semantic highlighting: numbers, units, operators, variables, functions, macros, keywords, commands, file paths, and embedded HTML/Markdown/CSS/JS/SVG in comments are each colored distinctly.
Highlighting updates per line as you type, for both dark and light themes.

The extension also sets a few editor defaults for `.cpd` files so the language behaves predictably:

- The monospace font is **JuliaMono** (falls back to Cascadia Code / Consolas). Run **CalcpadCE: Install JuliaMono Font** from the Command Palette to install it if the glyphs look wrong.
- **Enter always inserts a newline** — it never accepts a suggestion. Press **Tab** to accept a completion instead.
- Bracket matching is always on; bracket-pair colorization is off (CalcpadCE colors brackets itself).

### Autocomplete

As you type, the completion list offers:

- **Your own symbols first** — variables, functions, macros, and custom units defined in the current document (and its `#include` files) are prioritized above built-ins.
- **Built-in functions** with snippet placeholders — accept one and press **Tab** to jump between arguments.
- **Setting keys** (`decimals`, `degrees`, `complex`, `units`, `colorScale`, …) where a setting is expected.
- **[Metadata keys](new-metadata-comments.md)** inside an HTML-comment block placed directly above a definition. The **Properties** panel tab edits these through a form.

### Quick-type symbols

Type `~` followed by a key and press **space** to insert a Greek letter or math symbol.
For example `~a` + space → `α`, `~p` + space → `π`, `~S` + space → `Σ`.
The full set is shown in the **Insert** tab's Symbol Palette.
Toggle this with the `quickTyping` setting in the CalcpadCE **Settings** tab.

### Operator replacement and auto-indent

Typing ASCII operators auto-converts them to Unicode: `<=` → `≤`, `>=` → `≥`, `!=` → `≠`.
Block keywords indent automatically: `#if` / `#else` / `#end if`, `#for` / `#end for`, and `#def` / `#end def`.

### Formatting hotkeys

When the cursor is in a `.cpd` file, these hotkeys wrap the selection in HTML or Markdown markup.
Whether HTML or Markdown is emitted depends on the `commentFormat` setting (`auto` / `html` / `markdown`).

| Keybinding | Effect |
|------------|--------|
| **Ctrl+B** | Bold |
| **Ctrl+I** | Italic |
| **Ctrl+U** | Underline |
| **Ctrl+=** | Subscript |
| **Ctrl+Shift+=** | Superscript |
| **Ctrl+1** … **Ctrl+6** | Headings 1–6 |
| **Ctrl+L** | Paragraph |
| **Ctrl+R** | Line break |
| **Ctrl+Shift+L** | Bulleted list |
| **Ctrl+Shift+N** | Numbered list |
| **Ctrl+Q** | Toggle `'` comment prefix |
| **Ctrl+Shift+Q** | Uncomment |
| **Ctrl+Shift+V** | Paste as comment (each line prefixed with `'`) |

On macOS use **Cmd** instead of **Ctrl**.
Turn the whole set off with the `enableFormattingHotkeys` setting if it conflicts with your other bindings.

## Navigating symbols

The extension provides IDE-grade navigation across variables, functions, macros, and custom units — including across `#include` files:

| Action | Trigger |
|--------|---------|
| **Go to Definition** | **Ctrl+Click** or **F12** — jumps to the first assignment |
| **Find All References** | **Shift+Alt+F12**, or right-click → *Find All References* |
| **Rename Symbol** | **F2** — renames occurrences in the current document only (not across `#include` files) |
| **Hover** | Point at a symbol for its signature, type, source file, and docs |

Hovering over a built-in function shows its signature, description, return type, per-parameter documentation, and a runnable example.

## Path completion for includes and data files

When you type a path after `#include`, `#read`, `#write`, or `#append`, the extension completes filenames from the workspace and, once the document declares them, from its `#ProjectPath`/`#LibraryPath` roots:

- `#include`: `.cpd`, `.txt`
- `#read` / `#write` / `#append`: `.cpd`, `.txt`, `.csv`, `.tsv`, `.xlsx`, `.xlsm`, `.xls`
- Both `/` and `\` separators work, and it drills into subdirectories.
- Environment variables expand: `%VAR%` on Windows, `$VAR` on macOS/Linux.

Declare `#ProjectPath = ...` and/or `#LibraryPath = ...` at the top of a document and both roots show up in completion at the root level, alongside workspace files, drilling into `<project>/…`/`<library>/…` as you type — an easy way for a team to share a common library without copying it into every project. See [Includes and File Reads](new-includes.md#path-root-tokens-project-and-library) for the directive itself.

## The CalcpadCE panel

Click the **CalcpadCE** icon in the activity bar (left edge) to open the panel.
It has **Files** and **CalcpadCE** views, and the CalcpadCE view is organized into tabs (Insert, TOC, Settings, Variables, PDF, Formatting, Export, Errors).
The view title bar has **CalcpadCE: Run Preview** (re-renders the active file and refreshes plots) and **Stop Server** buttons.

The panel is the same across every CalcpadCE front end — see **[The CalcpadCE Panel & Settings](new-calcpad-panel.md)** for a full walkthrough of each tab.

## Live preview

Three preview panels are available, each opening in its own editor column:

| Panel | How to open | What it shows |
|-------|-------------|---------------|
| **HTML Preview** | Preview button in the editor toolbar, or *CalcpadCE Preview* in the Command Palette | The document as written — both `#pre` and `#post`, source values |
| **Unwrapped Preview** | Eye button in the editor toolbar, or *CalcpadCE Preview Unwrapped* | The fully expanded source (macros and includes resolved) — useful for debugging what the engine actually computes |
| **Report Preview** | Book button in the editor toolbar, or *CalcpadCE: Toggle Report Preview* | The print layout — `#pre` hidden and entered `#UI` values applied |

The Report preview opens on its own beside the editor, or alongside the `#UI` input form when
that is open, and it keeps its line links so a result traces back to the line that produced it.
With it (or the input form) focused, **CalcpadCE: Print Report to PDF** appears as a button in
the panel's title bar. See **[UI Mode](new-ui-mode.md)**.

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
See **[Linter and Diagnostics](new-linter.md)** for what each code means.

For errors that occur inside hidden (`#hide`) regions — which don't appear in the preview — use the **Errors** tab in the sidebar to see the full list with source-line links.

## Exporting

All exports run through the same engine as the preview, so what you see is what you get.

Every export is a **report** by default: `#pre` hidden, `#post` shown, and the values entered
into the `#UI` form applied.

| Output | How |
|--------|-----|
| **PDF** | **Export CalcpadCE to PDF** button in the editor toolbar, or *Export CalcpadCE to PDF*. Requires a Chromium browser — see [PDF Export](new-pdf-export.md). |
| **PDF (report)** | **CalcpadCE: Print Report to PDF** — the same export, also a title-bar button on the report and input-form panels |
| **HTML** | **Save HTML…** on the sidebar's **Export** tab, or *CalcpadCE: Save Source HTML…* |
| **Word (.docx)** | **Save Word…** on the sidebar's **Export** tab, or *CalcpadCE: Save as Word Document…* |

The sidebar's **Export** tab also offers the other renderings, grouped by variant: **Preview**
(PDF, HTML, Word), **Input form** (PDF, HTML) and **Unwrapped** (PDF, HTML). No exported file
carries the line numbers or error boxes the preview panels use for navigation.

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
The server mode (`auto` / `local` / `remote`), URL (default `http://localhost:9420`), and `dotnet` path are configurable if you need a custom setup.

### Output channels for troubleshooting

Four VS Code output channels help diagnose problems (open the Output panel and pick from the dropdown):

| Channel | Shows |
|---------|-------|
| **CalcpadCE Extension** | Extension lifecycle, commands, errors |
| **CalcpadCE Output HTML** | Rendered HTML diagnostics |
| **CalcpadCE Webview Console** | Console messages from preview panels |
| **CalcpadCE Server Debug** | Output from the engine process |

## Troubleshooting

| Symptom | Fix |
|---------|-----|
| Preview never renders / "server not ready" | Click refresh icon in the CalcpadCE panel to try restarting a server that crashed or failed to start. Check the **CalcpadCE Server Debug** output channel. |
| PDF export fails | Install a Chromium browser (Chrome/Edge/Chromium). See [PDF Export](new-pdf-export.md). |
| Formatting hotkeys conflict with other bindings | Disable `enableFormattingHotkeys` in the Settings tab. |

## See also

- [The CalcpadCE Panel & Settings](new-calcpad-panel.md) — the shared sidebar and all settings
- [Using the Desktop App](new-desktop-app.md)
- [PDF Export](new-pdf-export.md) · [Includes and File Reads](new-includes.md) · [Linter and Diagnostics](new-linter.md) · [Table of Contents](new-table-of-contents.md)
- [Writing Math](writing-math.md) · [Quick Reference](quick-reference.md)
