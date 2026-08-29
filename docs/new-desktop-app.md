# CalcpadCE Desktop App

The **CalcpadCE Desktop App** is a native application for Windows.
You get the full editor — multi-tab editing, syntax highlighting, autocomplete, live preview, and the CalcpadCE sidebar.
It uses native file dialogs and menu bar and has offline capabilities.

See [Selecting a CalcpadCE Deployment](new-selecting-calcpadce-deployment.md) to help decide if this is the right CalcpadCE deployment for you.

## Installing

The app ships as a per-platform download:

| Platform | Format |
|----------|--------|
| Windows | Portable `.zip` build (no install for beta) |
| Linux | AppImage (run directly) |
| Linux | Debian `.deb` |
| Linux | Fedora `.rpm` |
| Linux | Arch (compressed package with PKGBUILD) |

The calculation engine and its dependencies are bundled inside the app — you do **not** need .NET installed separately.
The Linux `.AppImage` and Windows Portable builds include what they need to run.
The other Linux options expect WebKitGTK to already be present on the system and may require installing a few other common dependencies.

For PDF export you need a **Chromium-based browser** (Chrome, Edge, or Chromium) installed on the system.
On Linux, the app will tell you which package to install if none is found — see [Exports](new-exports.md#browser-requirement).

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
| Open a file | **Ctrl+O**, or **File → Open…**, or drag a file onto the window |
| Close tab | **Ctrl+W**, the **✕** on the tab, or middle-click the tab |
| Next  tab | **Ctrl+Tab** |

How tabs behave:

- **Cursor and scroll position are remembered** per tab and restored when you switch back.
- **Unsaved changes** are marked with a dot on the tab; undoing back to the last saved state clears it.
- **Opening an already-open file** activates its existing tab instead of duplicating it.
- **Opening a file from an empty Untitled tab** replaces it in place rather than stacking a new tab.
- **Closing a tab with unsaved changes** prompts Save / Don't Save / Cancel. Quitting the app walks through every unsaved tab; cancel any prompt to abort the quit.

## Opening files

There are several ways to open documents:

- **File → Open…** — native file picker.
- **Files tab** in the sidebar — open a folder and browse its tree.
- **Recent files** — tracked automatically and available from the File menu.

Compiled **`.cpdz`** worksheets open the same way and are registered as their own file type.
See [UI Mode](new-ui-mode.md#compiled-cpdz-worksheets).

## The editor

The editor is the same across every CalcpadCE frontend — see **[The CalcpadCE Editor](new-calcpadce-editor.md)**.

Desktop specifics:

- Path completion draws on the folder opened in the **Files** tab as well as the document's `#ProjectPath`/`#LibraryPath` roots.
- JuliaMono is bundled with the app, so math glyphs render consistantly without installing a font.

## The CalcpadCE sidebar

Toggle the sidebar with **View → Toggle Sidebar**.
It has a **Files** view and a **CalcpadCE** view.

The sidebar is the same across every CalcpadCE frontend — see **[The CalcpadCE Panel & Settings](new-calcpad-panel.md)** for a full walkthrough of each tab.

## Live results

The **Results** pane renders your document live and re-renders as you type.
Its toolbar (and **View → Result Mode**) offers four modes — **Preview**, **Unwrapped**, **Input**, and **Report** — each rendering the same thing its matching export captures.
See [Exports → Export variants](new-exports.md#export-variants) for a breakdown of each one.

**View → Toggle Preview** shows and hides the pane.
Drag the border between the editor and the results pane to resize either side.

Right-click a preview → **View Webview Source HTML** to inspect the rendered HTML.

### Running on demand (Auto-Run off)

By default the preview re-renders continuously as you type.
If you turn **Settings → Auto-Run Preview** off, the preview only re-renders when you:

- Click **▶ Run** on the editor toolbar.
- Press **Ctrl+Alt+X**.
- Right-click in the editor → **Run Preview**.
- Use **Server → Refresh** in the menu bar.

A manual run also re-lints the document, writes data, refreshes definitions and the table of contents, and rebuilds the Export tab's plot list.

## Splitting the editor

The **Split ⬓** button in the editor toolbar (also **View → Split Editor**) opens a second editor group stacked below the first.
Each group has its own tabs, tab strip, preview, and Problems markers.
Click **Unsplit** (same button) to close the bottom group; any unsaved tabs in it are walked through the save prompt first.
The active group drives the sidebar.

## Errors

**Linter** — CalcpadCE checks your document as you write and flags problems before they're converted to HTML.
Issues are marked in red, yellow, or blue at the spot with the problem, based on severity, and appear in the **Problems** panel with a link to the offending line.

**Preview errors** — errors from the calculation engine (including inside hidden code) are listed in the **Errors** tab of the sidebar, each with a link to its source line.

See **[The CalcpadCE Editor → Linting](new-calcpadce-editor.md#linting)** and **[Linter and Diagnostics](new-linter.md)** for the full list of codes.

## Exporting

Exporting is the same across every CalcpadCE frontend — see **[Exports](new-exports.md)**.

Desktop specifics:

- Every export is in **File → Export**.
- **File → Save As Compiled Worksheet…** and **File → Export Portable Package…** produce the two handoff formats — see [Exports → Portable exports](new-exports.md#portable-exports).

## The native menu

The menu bar drives the whole app:

- **File** — New Tab · Open… · Save · Save As… · Save As Compiled Worksheet… · Export Portable Package… · Close Tab · Export ▸ · Quit — see [Exports](new-exports.md) for what each export command produces
- **Edit** — Undo · Redo · Cut · Copy · Paste · Select All · Find · Replace
- **View** — Toggle Sidebar · Toggle Preview · Toggle Word Wrap · Split Editor · Result Mode: Preview / Unwrapped / Input / Report
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
- **Stop Server** / **Restart Server** — cycle the calculation engine.

If these do not work and the server shows "Disconnected" in the bottom-right corner, restarting the app typically fixes it. Because the calculation engine is separate from the editor, it is normally possible to save and modify files even if the calculation engine crashes.

## Troubleshooting

| Symptom | Fix |
|---------|-----|
| Preview blank or not updating | **Server → Refresh**, then **Server → Restart Server** if needed. Check **Server → Show Server Log** to see messages from the calculation engine. Click **Open Log Folder** in the **Settings** tab to submit logs showing an error as a Github Issue. |
| PDF export fails | Install a Chromium browser. On Linux the app names the package to install — see [Exports → Troubleshooting](new-exports.md#troubleshooting). |
| Unsaved work after a crash | The app writes backup copies of unsaved files; reopen them from the Files tab. |

## See also

- [The CalcpadCE Editor](new-calcpadce-editor.md) — the shared editor, navigation, and linting
- [The CalcpadCE Panel & Settings](new-calcpad-panel.md) — the shared sidebar and all settings
- [Using the VS Code Extension](new-vscode-extension.md)
- [Exports](new-exports.md) · [Includes and File Reads](new-includes.md) · [Linter and Diagnostics](new-linter.md) · [Table of Contents](new-calcpad-panel.md#toc)
- [Writing Math](writing-math.md) · [Quick Reference](quick-reference.md)
