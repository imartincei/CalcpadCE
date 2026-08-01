# Settings

> Part of the [CalcpadCE Panel](new-calcpad-panel.md), shared across the [VS Code extension](new-vscode-extension.md), the [desktop app](new-desktop-app.md), and the web editor.

The **Settings** tab is the single place to control the calculation engine and the editor.
Editing settings here keeps them in sync with the host and the server — in VS Code, do **not** use VS Code's own settings editor for these.

## Math

| Setting | Values | Meaning |
|---------|--------|---------|
| **Decimals** | 0–15 | Decimal places shown in results. |
| **Angle Units** | Radians / Degrees / Gradians | Trigonometric angle setting. |
| **Complex Numbers** | on/off | Enable complex-number arithmetic. |
| **Substitute Variables** | on/off | Substitute variable values into the output. |
| **Format Equations** | on/off | *Professional* (on) renders equations in stacked math form; *Inline* (off) renders them on a single line. |
| **Zero Small Matrix Elements** | on/off | Show very small matrix/vector values as `0` instead of using scientific notation. |
| **Show Hidden Output** | on/off | Ignore `#hide` so suppressed content is rendered anyway. For debugging. |
| **Max Output Count** | 5–100 | Maximum number of rows/columns shown for large matrices and vectors. |

## Plot

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

## Units

- **Default Input Length Unit** — `m` / `cm` / `mm`. Used for `%u` placeholders in input forms.
- **Non-Metric Units** — **UK (Imperial)** or **US Customary**. Selects the definition of bare unit names that differ between the two systems (`gal`, `ton`, `cwt`, `pt`, `qt`, `bbl`, `tonf`, `therm`, …). This lives on `Settings.IsUs` and is unified across the WPF app, the CLI, and the web/desktop/VS Code hosts.

## PDF Export

Your default page setup for [PDF export](new-pdf-export.md).

| Setting | Values | Meaning |
|---------|--------|---------|
| **Paper size** | Letter / Legal / Tabloid / Ledger / A0–A6 | Page size. |
| **Orientation** | Portrait / Landscape | Page orientation. |
| **Top / Bottom / Left / Right margin** | a length with a unit, e.g. `2cm`, `0.5in`, `12mm` | Page margins. A bare number is rejected — the renderer would read it as pixels. |
| **Page numbers** | on/off | "Page *n* of *m*" in the footer. |
| **Date** | on/off | The timestamp in the header. |
| **Document title** | text | Header title. Empty falls back to the file name. |
| **Timestamp format** | .NET format string, e.g. `M/d/yyyy h:mm tt` | How the header timestamp is written. |

**Reset PDF Settings** restores just this section, leaving everything else alone.

Any document can override these for itself with a `pdf` [metadata comment](new-metadata-comments.md#pdf-export-settings), merged key by key over what you set here — so a report that must print A4 landscape carries that with it, and still follows your defaults for everything it doesn't mention.

## Server

**Remote Server URL** — the address used when the host is configured to talk to a remote CalcpadCE server rather than a local one.

## Preview theme

- **Theme** — System / Light / Dark for the rendered preview.
- **Dark Mode Background** — the background color used in dark mode (default `#1e1e1e`), with a **Reset** button.

## Color theme

**Color Theme** — the syntax-highlighting theme, defaulting to *System* with the available dark and light themes grouped in the list.

## Editor Font

Desktop app only.
Pick the Monaco editor's font family from:

- **JuliaMono** (bundled default) or **System Default**.
- Any additional `.woff2`/`.woff`/`.ttf`/`.otf` files dropped into the desktop app's *fonts folder*. Use **Open Fonts Folder** to reveal it, drop your fonts in, then reopen the Font Family picker to pick them up.

## Editor features

- **Enable Quick Typing** — `~`-prefixed shortcuts expand to symbols (e.g. `~a` → `α`, `~'` → `′`).
- **Comment Format** — Auto (detect `#md` on/off) / HTML / Markdown; controls what the formatting hotkeys emit.
- **Enable Formatting Hotkeys** — the Ctrl+B / Ctrl+I / Ctrl+1–6 … bindings.
- **Sync Preview to Cursor Line** — scroll the preview to follow the line the cursor is on.
- **Auto-Run Preview** *(default on)* — when off, the preview only re-renders when the preview panel is first opened or a manual **Run Preview** is triggered (**Ctrl+Alt+X**, the ▶ Run button, the editor context menu, or the Server → Refresh menu in the desktop app). Turn this off for large documents where every keystroke re-render is too costly.
- **Open #UI Documents in Input Mode** *(default on)* — a document declaring `#UI` controls opens as its input form the first time you open it. The mode you switch to afterwards sticks; a later tab switch never brings the form back. Desktop app and VS Code only. See [UI Mode](new-ui-mode.md).
- **Apply #UI Values in Preview** *(default off)* — Preview normally renders the document's own values; turn this on and it renders the values entered into the input form instead, while still showing `#pre` and `#post` together. For tracking down an error that only appears once a form is filled in. Exports of the Preview variant are unaffected — they always use the document's own values. See [UI Mode](new-ui-mode.md).

## Linter

**Minimum Severity** — Error / Warning / Information (all).
The lowest severity surfaced as a diagnostic.

## Diagnostics

- **Open Logs Folder** — opens the folder holding server logs and the most recent crash dump.
- **Max Output Lines (per channel)** *(web/desktop)* — 10–100000, default 1000. Number of lines retained in each Output panel channel before older lines are dropped. Lower values reduce memory use and keep the UI responsive when logs are noisy.

## Named configurations

The **Configuration** section lets you keep more than one named set of settings — for example a metric configuration with 3 decimals and an imperial one in degrees — and switch between them:

- **Active Config** — pick the configuration to apply.
- **Save current settings as** — type a name and click **Save** to store the current settings under it.
- **Open Settings Folder** — reveal where configurations are stored.
- **Reset to Default** — restore the default settings.

Configurations persist between sessions in the VS Code extension and the desktop app.
(The pure browser build does not yet mirror named configs.)

## See also

- [The CalcpadCE Panel](new-calcpad-panel.md)
- [Linter](new-linter.md) · [PDF Export](new-pdf-export.md)
