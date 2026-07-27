# CalcpadCE Desktop App

The **CalcpadCE Desktop App** is a native application for Windows and Linux that wraps the same editor you get in the browser and bundles the calculation engine inside it.
You get the full editor — multi-tab editing, syntax highlighting, autocomplete, live preview, and the CalcpadCE sidebar — plus native file dialogs, a native menu bar, and drag-and-drop, with no browser to open and no server to set up.

For the CalcpadCE language itself, start with **[Writing Math](writing-math.md)** and the **[Quick Reference](quick-reference.md)**.

## Installing

The app ships as a per-platform download:

| Platform | Format |
|----------|--------|
| Windows | Portable `.zip` build (no install for beta) |
| Linux | AppImage (run directly, no install for beta) |

The calculation engine and its fonts and templates are bundled inside the app — you do **not** need .NET installed separately.
On Linux, the `.AppImage` includes what it needs to run; the `.deb` package expects WebKitGTK to already be present on the system.

For PDF export you need a **Chromium-based browser** (Chrome, Edge, or Chromium) installed on the system.
On Linux the app will tell you which package to install if none is found — see [PDF Export](new-pdf-export.md).

## Your first document

1. Launch the app. It opens with an empty **Untitled-1** tab.
2. Type a calculation, for example:

   ```calcpad
   "Cantilever tip deflection
   P = 5kN
   L = 3m
   E = 200GPa
   I = 8.5e-5m^4
   δ = P·L^3/(3·E·I)
   ```

3. The **preview pane** renders your report live as you type. Toggle it from **View → Toggle Preview**.
4. Save with **Ctrl+S** (or **File → Save**). A native save dialog lets you choose the location.

## Working with tabs

The app uses tabs so you can keep several `.cpd` documents open at once.

| Action | Shortcut |
|--------|----------|
| New tab | **Ctrl+T** or **Ctrl+N**, or the **`+`** button on the tab strip |
| Open a file | **File → Open…**, or drag a file onto the window |
| Close tab | **Ctrl+W**, the **✕** on the tab, or middle-click the tab |
| Next / previous tab | **Ctrl+Tab** / **Ctrl+Shift+Tab** |
| Jump to tab 1–8 | **Ctrl+1** … **Ctrl+8** |
| Jump to last tab | **Ctrl+9** |

How tabs behave:

- **Cursor and scroll position are remembered** per tab and restored when you switch back.
- **Unsaved changes** are marked with a dot on the tab; undoing back to the last saved state clears it.
- **Opening an already-open file** activates its existing tab instead of duplicating it.
- **Opening a file from an empty Untitled tab** replaces it in place rather than stacking a new tab.
- **Closing a tab with unsaved changes** prompts Save / Don't Save / Cancel. Quitting the app walks through every unsaved tab; cancel any prompt to abort the quit.
- Hover, definitions, references, the linter, the preview, and the TOC are all scoped to the active tab, so symbols and errors never bleed between unrelated documents.

## Opening files

There are several ways to open documents:

- **File → Open…** — native file picker.
- **Drag and drop** — drop one or more files onto the editor; each opens in its own tab. Dropping plain text (e.g. from a browser) opens it as a new untitled tab.
- **Files tab** in the sidebar — open a folder and browse its tree.
- **Recent files** — tracked automatically and available from the File menu.

## The editor

- **Syntax highlighting** for numbers, units, operators, variables, functions, macros, keywords, commands, and embedded HTML/Markdown in comments.
- **Autocomplete** that prioritizes your own symbols over built-ins, with snippet placeholders for function arguments.
- **Quick-type symbols** — `~a` + space → `α`, `~p` + space → `π`, etc.
- **Operator replacement** — `<=` → `≤`, `>=` → `≥`, `!=` → `≠`.
- **Auto-indentation** for `#if` / `#for` / `#def` blocks.
- **Go to Definition**, **Find All References**, **Rename**, and **hover** for symbols, including across `#include` files.

Two key behaviors worth knowing:

- **Enter always inserts a newline** — it never accepts a suggestion. Press **Tab** to accept a completion.
- **Tab accepts suggestions** and triggers completion on a partial word.

These are the same editor features as the [VS Code extension](new-vscode-extension.md), so anything you learn in one carries over to the other.

## The CalcpadCE sidebar

Toggle the sidebar with **View → Toggle Sidebar**.
It has a **Files** view and a **CalcpadCE** view; the CalcpadCE view is split into tabs (Insert, TOC, Settings, Variables, PDF, Formatting, Export, Errors).

The sidebar is the same across every CalcpadCE front end — see **[The CalcpadCE Panel & Settings](new-calcpad-panel.md)** for a full walkthrough of each tab, including Prettify options and the Export buttons.

## Live preview

The preview pane renders your report live and re-renders as you type.
From **View** you can:

- **Toggle Preview** — show/hide the pane.
- **Preview Mode: Wrapped** — the normal report view.
- **Preview Mode: Unwrapped** — the fully expanded source, with macros and includes resolved. Useful for debugging what the engine computes.

### Running on demand (Auto-Run off)

By default the preview re-renders continuously as you type.
If you turn **Settings → Auto-Run Preview** off — useful for long-running documents — the preview only re-renders when you:

- Click **▶ Run** on the editor toolbar.
- Press **Ctrl+Alt+X**.
- Right-click in the editor → **Run Preview**.
- Use **Server → Refresh** in the native menu (same shortcut).

A manual run also re-lints the document, refreshes definitions and the table of contents, and rebuilds the Export tab's plot list.

## Splitting the editor

The **Split ⬓** button in the editor toolbar (also **View → Split Editor**) opens a second editor group stacked below the first.
Each group has its own tabs, tab strip, preview, and Problems markers.
Click **Unsplit** (same button) to close the bottom group; any unsaved tabs in it are walked through the save prompt first.
The active group — the one you most recently clicked into — drives the sidebar (Problems, TOC, Variables).

## Errors

**Linter** — CalcpadCE checks your document as you write and flags problems before they're converted to HTML.
Issues are marked in red, yellow, or blue at the spot with the problem, based on severity, and appear in the **Problems** panel with a link to the offending line.
See **[Linter and Diagnostics](new-linter.md)** for the full list of codes.

**Preview errors** — errors from the calculation engine (including inside hidden code) are listed in the **Errors** tab of the sidebar, each with a link to its source line.

## Exporting

Every export uses the app's built-in engine, so the output matches the preview exactly.

| Output | How | Notes |
|--------|-----|-------|
| **PDF** | **File → Export PDF…** | Full-fidelity export via a native save dialog. Requires a Chromium browser — see [PDF Export](new-pdf-export.md). |
| **HTML** | **Save HTML…** on the sidebar's **Export** tab | Native save dialog writes a standalone `.html` report. |
| **Word (.docx)** | **Save Word…** on the sidebar's **Export** tab | Native save dialog writes a `.docx` document. |

Set the document title, timestamp format, page size, and header/footer in the sidebar's **PDF** tab before exporting.
The **Export** tab also has a **Plots** section that lists every plot the document produces, so you can save each one individually or all at once as a ZIP — see [The CalcpadCE Panel & Settings → Export](new-calcpad-panel.md#export).

## The native menu

The menu bar drives the whole app:

- **File** — New Tab · Open… · Save · Save As… · Close Tab · Export PDF… · Quit
- **Edit** — Undo · Redo · Cut · Copy · Paste · Select All · Find · Replace
- **View** — Toggle Sidebar · Toggle Preview · Split Editor · Preview Mode: Wrapped / Unwrapped
- **Server** — Refresh (**Ctrl+Alt+X**) · Show Server Log · Stop Server · Restart Server
- **Help** — Documentation (opens the docs site in your default browser)

## Settings and configurations

All calculation, plot, unit, theme, editor, and linter settings live in the **Settings** tab of the sidebar.
The desktop app also supports **named configurations** — save different sets of settings (e.g. one for metric with 3 decimals, one for imperial with degrees) and switch the active configuration from the Settings tab; configurations persist between sessions.

See **[The CalcpadCE Panel & Settings → Settings](new-calcpad-panel.md#settings)** for the full list, and **[→ Formatting](new-calcpad-panel.md#formatting-prettify)** for the Prettify options.

## The built-in engine

The app runs the calculation engine inside it.
It starts automatically when the app launches and shuts down when you close it — you never launch or configure it yourself.

If calculations stop responding, use the **Server** menu:

- **Refresh** (**Ctrl+Alt+X**) — re-run the active document.
- **Show Server Log** — open the engine's log file to diagnose a problem.
- **Stop Server** / **Restart Server** — cycle the engine.

## Troubleshooting

| Symptom | Fix |
|---------|-----|
| Preview blank or not updating | **Server → Refresh**, then **Server → Restart Server** if needed. Check **Server → Show Server Log** to see messages from the calculation engine. Click **Open Log Folder** in the **Settings** tab to submit logs showing an error as a Github Issue. |
| PDF export fails | Install a Chromium browser. On Linux the app names the package to install — see [PDF Export](new-pdf-export.md). |
| Unsaved work after a crash | The app writes backup copies of unsaved files; reopen them from the Files tab. |

## See also

- [The CalcpadCE Panel & Settings](new-calcpad-panel.md) — the shared sidebar and all settings
- [Using the VS Code Extension](new-vscode-extension.md)
- [PDF Export](new-pdf-export.md) · [Includes and File Reads](new-includes.md) · [Linter and Diagnostics](new-linter.md) · [Table of Contents](new-table-of-contents.md)
- [Writing Math](writing-math.md) · [Quick Reference](quick-reference.md)
