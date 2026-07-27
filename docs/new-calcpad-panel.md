# The CalcpadCE Panel

> The panel described here is the same in the [VS Code extension](new-vscode-extension.md), the [desktop app](new-desktop-app.md), and the web editor — it is built from one shared set of components. Where a host behaves differently, it is called out below.

The **CalcpadCE panel** is the tabbed sidebar that sits beside the editor.
It shows what your document defines, lets you insert symbols and snippets, controls the calculation and plot settings, and drives export.
Because every CalcpadCE frontend embeds the same panel, the tabs and settings are identical everywhere; only how you open it differs:

- **VS Code** — click the **CalcpadCE** icon in the activity bar. The view title bar has **CalcpadCE: Run Preview** (re-render) and **Stop Server** buttons.
- **Desktop app** — **View → Toggle Sidebar**.

## Views

The panel has two top-level views, switched from the icons at its top:

- **CalcpadCE** — the tabbed working view (below). This is the default.
- **Files** — opens a folder and shows a file tree so you can browse and open `.cpd` files without leaving the panel. Includes *Open Folder*, *Collapse All*, and a *Show all files* toggle.

## Panel tabs

The **CalcpadCE** view is organized into tabs:

| Tab | What it does |
|-----|--------------|
| **Insert** | Searchable palette of symbols, built-in functions, and snippets. Click an item to insert it at the cursor. Includes an **Insert Image** button and a Symbol Palette. |
| **TOC** | Live table of contents built from your document headings. Click a heading to jump to that line. |
| **Settings** | All calculation, plot, unit, theme, editor, and linter settings, plus named configurations. See [Settings](new-settings.md). |
| **Variables** | Everything defined in the document — macros, variables, functions, and custom units — with types and signatures. Click an entry to insert it; each is searchable. |
| **Metadata** | Form-based editor for the [metadata comment](new-metadata-comments.md) at the cursor — descriptions, parameter/return types, per-file settings, lint-ignore, and no-print markers. |
| **PDF** | Header/footer, page size, and layout options applied when you export to PDF. |
| **Formatting** | Prettify options and the **Prettify Document** button. See [Formatting](#formatting-prettify). |
| **Export** | **Save HTML…**, **Save Word…**, and per-plot / ZIP-all image export from any plots produced by the document. See [Export](#export). |
| **Errors** | Full list of calculation errors from the engine, each linking to its source line. |

### Insert

A searchable palette grouped by category.
Typing filters the list; clicking an item inserts it at the cursor.
Function and snippet entries insert with placeholders.
The **Symbol Palette** section is the same set of symbols reachable via quick-typing (`~a` + space → `α`).
An **Insert Image** button opens a file picker and inserts an `<img>` tag with a relative path.

### Variables

Lists everything the current document defines, grouped and counted:

- **Macros** — with parameters and defaults
- **Variables** — with inferred type
- **Functions** — with signature and return type
- **Custom Units** — with their definition

Entries are scoped to the active document (and its `#include` files).
Click any entry to insert its name at the cursor.

### Metadata

A form-based editor for the [metadata comment](new-metadata-comments.md) on the line at the cursor — no hand-editing of JSON required.
Put the cursor on a definition (or an existing metadata comment) and the tab shows exactly the fields that apply: a description for any definition, parameter/return types for functions and macros, and per-file settings, lint-ignore, and no-print markers on generic lines.
**Apply** writes the comment (creating one above the definition if none exists); **Reset** re-reads the current one.
See [Metadata Comments](new-metadata-comments.md) for the full format.

### TOC

A live outline of the headings in your document.
Selecting a heading scrolls the editor to that line.
It rebuilds as you edit.

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

See [PDF Export](new-pdf-export.md) for more information about the options.

### Export

Renders the current document through the backend and offers several save actions:

| Button | Result |
|--------|--------|
| **Save HTML…** | Saves a standalone `.html` of the rendered report. |
| **Save Word…** | Converts the report to a Word `.docx`. |

Below those, the **Plots** section lists every plot the document emits, each with a thumbnail, filename, and size:

| Button | Result |
|--------|--------|
| **Refresh** | Re-runs the document and re-lists plots. Triggered automatically by a manual **Run Preview**. |
| **Save…** (per plot) | Writes that plot to disk in its native format (PNG or SVG, depending on the **Vector Graphics** setting). |
| **Download all (ZIP)** | Bundles every plot in one archive. |

### Errors

Lists every error the calculation engine reports, each with its source line and a link that jumps there.
This is the reliable place to see errors that occur inside `#hide` blocks, which are omitted from the rendered preview.

## Settings

The **Settings** tab is the single place to control the calculation engine and the editor — Math, Plot, Units, Server, themes, Editor features, Library, Linter, Diagnostics, and named configurations.
See [Settings](new-settings.md) for the full reference.

## See also

- [Settings](new-settings.md)
- [Using the VS Code Extension](new-vscode-extension.md)
- [Using the Desktop App](new-desktop-app.md)
- [PDF Export](new-pdf-export.md) · [Linter](new-linter.md) · [Table of Contents](new-table-of-contents.md)
