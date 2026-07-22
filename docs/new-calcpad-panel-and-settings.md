# The CalcPad Panel & Settings

> Part of Calcpad.Web. The panel described here is the same in the [VS Code extension](new-vscode-extension.md), the [desktop app](new-desktop-app.md), and the web editor — it is built from one shared set of components (`calcpad-frontend`). Where a host behaves differently, it is called out below.

The **CalcPad panel** is the tabbed sidebar that sits beside the editor. It shows what your document defines, lets you insert symbols and snippets, controls the calculation and plot settings, and drives export. Because every Calcpad front end embeds the same panel, the tabs and settings are identical everywhere; only how you open it differs:

- **VS Code** — click the **CalcPad** icon in the activity bar. The view title bar has **CalcPad: Run Preview** (re-render) and **Stop Server** buttons.
- **Desktop app** — **View → Toggle Sidebar**.

## Views

The panel has two top-level views, switched from the icons at its top:

- **Calcpad** — the tabbed working view (below). This is the default.
- **Files** — opens a folder and shows a file tree so you can browse and open `.cpd` files without leaving the panel. Includes *Open Folder*, *Collapse All*, and a *Show all files* toggle.

## Panel tabs

The **Calcpad** view is organized into tabs:

| Tab | What it does |
|-----|--------------|
| **Insert** | Searchable palette of symbols, built-in functions, and snippets. Click an item to insert it at the cursor. Includes an **Insert Image** button and the Symbol Palette used by quick-typing. |
| **TOC** | Live table of contents built from your document headings. Click a heading to jump to that line. |
| **Settings** | All calculation, plot, unit, theme, editor, and linter settings, plus named configurations. See [Settings](#settings). |
| **Variables** | Everything defined in the document — macros, variables, functions, and custom units — with types and signatures. Click an entry to insert it; each is searchable. |
| **Properties** | Form-based editor for the [metadata comment](new-metadata-comments.md) at the cursor — descriptions, parameter/return types, lint-ignore, and no-print markers — plus document settings, written as a [`#settings` directive](new-metadata-comments.md#document-settings). Shown in hosts that track the editor cursor (currently VS Code). |
| **PDF** | Header/footer, page size, and layout options applied when you export to PDF. |
| **Formatting** | Prettify options and the **Prettify Document** button. See [Formatting](#formatting-prettify). |
| **Export** | **Save HTML…**, **Save Word…**, and per-plot / ZIP-all image export from any plots produced by the document. See [Export](#export). |
| **Errors** | Full list of calculation errors from the engine, each linking to its source line — including errors that occur inside hidden (`#hide`) regions and so never appear in the preview. |

### Insert

A searchable palette grouped by category. Typing filters the list; clicking an item inserts it at the cursor. Function and snippet entries insert with `${N:param}` placeholders you can Tab through. The **Symbol Palette** section is the same set of symbols reachable via quick-typing (`~a` + space → `α`). An **Insert Image** button opens a file picker and inserts an `<img>` tag with a relative path.

### Variables

Lists everything the current document defines, grouped and counted:

- **Macros** — with parameters and defaults
- **Variables** — with inferred type
- **Functions** — with signature and return type
- **Custom Units** — with their definition

Entries are scoped to the active document (and its `#include` files). Click any entry to insert its name at the cursor.

### Metadata

A form-based editor for the [metadata comment](new-metadata-comments.md) on the line at the cursor — no hand-editing of JSON required. Put the cursor on a definition (or an existing metadata comment) and the tab shows exactly the fields that apply: a description for any definition, parameter/return types for functions and macros, and per-file settings, lint-ignore, and no-print markers on generic lines. **Apply** writes the comment (creating one above the definition if none exists); **Reset** re-reads the current one. See [Metadata Comments](new-metadata-comments.md) for the full format. This tab appears in hosts that can track the editor cursor (currently the VS Code extension).

### TOC

A live outline of the headings in your document. Selecting a heading scrolls the editor to that line. It rebuilds as you edit.

### Formatting (Prettify)

Controls the **Prettify Document** command, which reformats the active file:

- **Indent style** — Tab or Space
- **Spaces per level** — used when the indent style is Space
- **Trim trailing whitespace**

Set your options, then click **Prettify Document**.

### PDF

Configures the PDF export before you run it:

- **Header** — document title (defaults to the file name) and a timestamp format (e.g. `M/d/yyyy h:mm tt`)
- **Page Layout** — page size (Letter, …) and orientation

Use **Generate PDF** to export, or **Reset** to restore the defaults.

### Export

Renders the current document through the backend and offers several save actions:

| Button | Result |
|--------|--------|
| **Save HTML…** | Saves a standalone `.html` of the rendered report. |
| **Save Word…** | Converts the report to a Word `.docx` (via Calcpad.OpenXml). |

Below those, the **Plots** section lists every plot the document emits, each with a thumbnail, filename, and size:

| Button | Result |
|--------|--------|
| **Refresh** | Re-runs the document and re-lists plots. Triggered automatically by a manual **Run Preview**. |
| **Save…** (per plot) | Writes that plot to disk in its native format (PNG or SVG, depending on the **Vector Graphics** setting). |
| **Download all (ZIP)** | Bundles every plot in one archive. |

On the desktop app these use native save dialogs; in the browser build they download as blobs. This replaces the WPF "plot output directory" setting — plots are always kept in memory and exported on demand.

### Errors

Lists every error the calculation engine reports, each with its source line and a link that jumps there. This is the reliable place to see errors that occur inside `#hide` blocks, which are omitted from the rendered preview.

## Settings

The **Settings** tab is the single place to control the calculation engine and the editor. Editing settings here keeps them in sync with the host and the server — in VS Code, do **not** use VS Code's own settings editor for these.

### Math

| Setting | Values | Meaning |
|---------|--------|---------|
| **Decimals** | 0–15 | Decimal places shown in results. |
| **Angle Units** | Radians / Degrees / Gradians | Trigonometric angle setting. |
| **Complex Numbers** | on/off | Enable complex-number arithmetic. |
| **Substitute Variables** | on/off | Substitute variable values into the output. |
| **Format Equations** | on/off | *Professional* (on) renders equations in stacked math form; *Inline* (off) renders them on a single line. |
| **Zero Small Matrix Elements** | on/off | Show very small matrix/vector values as `0` instead of using scientific notation. |
| **Max Output Count** | 5–100 | Maximum number of rows/columns shown for large matrices and vectors. |

### Plot

| Setting | Values | Meaning |
|---------|--------|---------|
| **Adaptive Plotting** | on/off | Adaptively sample plotted functions. |
| **Screen Scale Factor** | 0.1–5 | Scale of rendered plots/images. |
| **Image Path** | text | Directory used for generated plot images. |
| **Vector Graphics** | on/off | Emit SVG plots instead of raster images. |
| **Color Scale** | Rainbow / Grayscale / Hot / Cool / Jet / Parula | Palette for 3D/surface plots. |
| **Smooth Scale** | on/off | Smooth the color scale. |
| **Shadows** | on/off | Render shadows on 3D surfaces. |
| **Light Direction** | text | Light direction vector for 3D shading. |

### Units

- **Default Input Length Unit** — `m` / `cm` / `mm`. Used for `%u` placeholders in input forms.
- **Non-Metric Units** — **UK (Imperial)** or **US Customary**. Selects the definition of bare unit names that differ between the two systems (`gal`, `ton`, `cwt`, `pt`, `qt`, `bbl`, `tonf`, `therm`, …). This lives on `Settings.IsUs` and is unified across the WPF app, the CLI, and the web/desktop/VS Code hosts.

### Server

**Remote Server URL** — the address used when the host is configured to talk to a remote Calcpad server rather than a local one.

### Preview theme

- **Theme** — System / Light / Dark for the rendered preview.
- **Dark Mode Background** — the background color used in dark mode (default `#1e1e1e`), with a **Reset** button.

### Color theme

**Color Theme** — the syntax-highlighting theme, defaulting to *System* with the available dark and light themes grouped in the list.

### Editor Font

Desktop app only. Pick the Monaco editor's font family from:

- **JuliaMono** (bundled default) or **System Default**.
- Any additional `.woff2`/`.woff`/`.ttf`/`.otf` files dropped into the desktop app's *fonts folder*. Use **Open Fonts Folder** to reveal it, drop your fonts in, then reopen the Font Family picker to pick them up.

### Editor features

- **Enable Quick Typing** — `~`-prefixed shortcuts expand to symbols (e.g. `~a` → `α`, `~'` → `′`).
- **Comment Format** — Auto (detect `#md` on/off) / HTML / Markdown; controls what the formatting hotkeys emit.
- **Enable Formatting Hotkeys** — the Ctrl+B / Ctrl+I / Ctrl+1–6 … bindings.
- **Sync Preview to Cursor Line** — scroll the preview to follow the line the cursor is on.
- **Auto-Run Preview** *(default on)* — when off, the preview only re-renders when the preview panel is first opened or a manual **Run Preview** is triggered (**Ctrl+Alt+X**, the ▶ Run button, the editor context menu, or the Server → Refresh menu in the desktop app). Turn this off for large documents where every keystroke re-render is too costly.

### Library

**Library Path** — a directory of shared `.cpd`/`.txt` files that appear in `#include` / `#read` path completion, so a team can share a common library. Supports `%ENV%` variables.

### Linter

**Minimum Severity** — Error / Warning / Information (all). The lowest severity surfaced as a diagnostic.

### Diagnostics

- **Open Logs Folder** — opens the folder holding server logs and the most recent crash dump.
- **Max Output Lines (per channel)** *(web/desktop)* — 10–100000, default 1000. Number of lines retained in each Output panel channel before older lines are dropped. Lower values reduce memory use and keep the UI responsive when logs are noisy.

## Named configurations

The **Configuration** section lets you keep more than one named set of settings — for example a metric configuration with 3 decimals and an imperial one in degrees — and switch between them:

- **Active Config** — pick the configuration to apply.
- **Save current settings as** — type a name and click **Save** to store the current settings under it.
- **Open Settings Folder** — reveal where configurations are stored.
- **Reset to Default** — restore the default settings.

Configurations persist between sessions in the VS Code extension and the desktop app. (The pure browser build does not yet mirror named configs.)

## See also

- [Using the VS Code Extension](new-vscode-extension.md)
- [Using the Desktop App](new-desktop-app.md)
- [PDF Export](new-pdf-export.md) · [Linter](new-linter.md) · [Table of Contents](new-table-of-contents.md)
