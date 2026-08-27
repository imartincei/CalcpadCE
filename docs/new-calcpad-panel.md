# The CalcpadCE Panel

> The panel described here is the same in the [VS Code extension](new-vscode-extension.md), the [desktop app](new-desktop-app.md), and the web editor — it is built from one shared set of components. Where a host behaves differently, it is called out below.

The **CalcpadCE panel** is the tabbed sidebar that sits beside the editor.  
It shows what your document defines, lets you insert symbols and snippets, controls the calculation and plot settings, and drives export.  
Because every CalcpadCE frontend embeds the same panel, the tabs and settings are identical everywhere; only how you open it differs:

*   **VS Code** — click the **CalcpadCE** icon in the activity bar. The view title bar has **CalcpadCE: Run Preview** (re-render) and **Stop Server** buttons.
*   **Desktop app** — **View → Toggle Sidebar**.

## Views

The panel has two top-level views, switched from the icons at its top:

*   **CalcpadCE** — the tabbed working view (below). This is the default.
*   **Files** — opens a folder and shows a file tree so you can browse and open `.cpd` files without leaving the panel. Includes _Open Folder_, _Collapse All_, and a _Show all files_ toggle.

## Panel tabs

The **CalcpadCE** view is organized into tabs:

| Tab | What it does |
| --- | --- |
| **Insert** | Searchable palette of symbols, built-in functions, and snippets. Click an item to insert it at the cursor. Includes an **Insert Image** button and a Symbol Palette. |
| **TOC** | Live table of contents built from your document headings. Click a heading to jump to that line. |
| **Settings** | All calculation, plot, unit, PDF export, theme, editor, and linter settings, plus named configurations. See [Settings](new-settings.md). |
| **Variables** | Everything defined in the document — macros, variables, functions, and custom units — with types and signatures. Click an entry to insert it; each is searchable. |
| **Properties** | Form-based editor for the [metadata comment](new-metadata-comments.md) at the cursor — descriptions, parameter/return types, per-file settings, lint-ignore, and per-document PDF export settings. |
| **Formatting** | Prettify options and the **Prettify Document** button. See [Formatting](#formatting-prettify). |
| **Export** | PDF / HTML / Word save actions, grouped by which rendering they capture, plus per-plot and ZIP image exports and the portable formats. See [Export](#export) and [Exports](new-exports.md). |
| **Errors** | Full list of calculation errors from the engine, each linking to its source line. |

While a [`#UI` input form](new-ui-mode.md) (or a compiled `.cpdz` worksheet) is the active document, **Insert**, **Variables**, **Formatting**, **Errors**, and **Properties** go inactive — they act on the document source, which the form doesn't edit.
**TOC**, **Settings**, and **Export** stay available.

### Insert

A searchable palette grouped by category.  
Typing filters the list; clicking an item inserts it at the cursor.  
Function entries insert with placeholders.  
The **Symbol Palette** section is the same set of symbols reachable via quick-typing (`~a` + space → `α`).  
An **Insert Image** button opens a file picker and inserts an `<img>` tag with the selected path.

### Variables

Lists everything the current document defines, grouped and counted:

*   **Macros** — with parameters and defaults
*   **Variables** — with inferred type
*   **Functions** — with signature and return type
*   **Custom Units** — with their definition

Entries are from the active document (and its `#include` files).  
Click any entry to insert its name at the cursor.

### Properties

A form-based editor for the [metadata comment](new-metadata-comments.md), `#settings` directive JSON, and `#UI` directive JSON.  
Put the cursor on JSON field, and the tab shows exactly the fields that apply: a description for any definition, parameter/return types for functions and macros, and per-file settings, lint-ignore, and [PDF export settings](new-metadata-comments.md#pdf-export-settings) on generic lines.  
**Apply** writes the comment (creating one above the definition if none exists); **Reset** re-reads the current one.  
See [Metadata Comments](new-metadata-comments.md), [Settings](new-settings.md), and [UI Mode](new-ui-mode.md) for the full format.

### TOC

A live outline of the headings in your document. Define headings with \<h1\>, \<h2\>, etc. HTML or #, ## in Markdown.
Selecting a heading scrolls the editor to that line.
While a [`#UI` input form](new-ui-mode.md) is open and there is no editor to scroll, it scrolls the form and the report.  

### Formatting (Prettify)

Controls the **Prettify Document** command, which reformats the active file:

*   **Indent style** — Tab or Space
*   **Spaces per level** — used when the indent style is Space
*   **Trim trailing whitespace**

Set your options, then click **Prettify Document**.

### Export

Allows saving results from the CalcpadCE file into various formats. The tab is four collapsible sections — **HTML / PDF / Word**, **Write / Append**, **Plots** and **Portable Exports** — and remembers which ones you left open.

| Section | Buttons |
| --- | --- |
| **HTML / PDF / Word** | Save PDF… · Save Word… · Save HTML…, grouped by which rendering they capture — **Report**, **Preview**, **Input form**, **Unwrapped** |
| **Write / Append** | **Write files** (when `#write`/`#append` run) and **Write to Disk** |
| **Plots** | **Refresh**, a **Save…** per plot, and **Download all (ZIP)** |
| **Portable Exports** | **Save Compiled…** (a `.cpdz` compiled worksheet) and **Export Portable…** (a `.zip` bundled with everything it reads) |

See [Exports](new-exports.md) for what each rendering captures, which formats it offers, how PDF page setup is resolved, and what the two portable formats produce.

### Errors

Lists every error the calculation engine reports, each with its source line and a link that jumps there.  
This is the reliable place to see errors that occur inside `#hide` blocks, which are omitted from the rendered preview.

## Settings

The **Settings** tab is the single place to control the calculation engine and the editor — Math, Plot, Units, Server, themes, Editor features, Linter, Diagnostics, and named configurations.  
See [Settings](new-settings.md) for the full reference.

## See also

*   [Settings](new-settings.md)
*   [Using the VS Code Extension](new-vscode-extension.md)
*   [Using the Desktop App](new-desktop-app.md)
*   [Exports](new-exports.md) · [Linter](new-linter.md) · [Metadata Comments](new-metadata-comments.md)
