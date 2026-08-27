# Settings

The [Settings](new-calcpad-panel.md#settings) tab is the single place to control the calculation engine and the editor.
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
| **Numerical Precision** | 1e-15–1e-2 | Relative precision used by integration, root-finding, and other numerical methods. |
| **Solver Tolerance** | 1e-15–1e-2 | Target tolerance for the iterative (PCG/eigen) solver. |

## Plot

| Setting | Values | Meaning |
|---------|--------|---------|
| **Adaptive Plotting** | on/off | Adaptively sample plotted functions. |
| **Screen Scale Factor** | 0.1–5 | Scale of rendered plots/images. App-level display setting; not part of `#settings`. |
| **Plot Width / Height** | ≥1 (px) | Default size of the plot canvas. |
| **Plot Mesh Step** | ≥0 (px) | Mesh size for surface/map plots; `0` samples automatically. |
| **Image Path** | text | Directory used for generated plot images. |
| **Vector Graphics** | on/off | Emit SVG plots instead of raster images. |
| **Color Scale** | None / Gray / Rainbow / Terrain / VioletToYellow / GreenToYellow / Blues / BlueToYellow / BlueToRed / PurpleToYellow | Palette for 3D/surface plots. |
| **Smooth Scale** | on/off | Smooth the color scale. |
| **Shadows** | on/off | Render shadows on 3D surfaces. |
| **Light Direction** | text | Light direction vector for 3D shading. App-level display setting; not part of `#settings`. |

## Units

- **Default Input Length Unit** — `m` / `cm` / `mm`. Used for `%u` placeholders in input forms.
- **Non-Metric Units** — **UK (Imperial)** or **US Customary**, defaulting to **US Customary**. Selects the definition of bare unit names that differ between the two systems (`gal`, `ton`, `cwt`, `pt`, `qt`, `bbl`, `tonf`, `therm`, …).

## PDF Export

Your default page setup for [PDF export](new-exports.md#pdf-export).

| Setting | Values | Meaning |
|---------|--------|---------|
| **Paper size** | Letter / Legal / Tabloid / Ledger / A0–A6 | Page size. |
| **Orientation** | Portrait / Landscape | Page orientation. |
| **Top / Bottom / Left / Right margin** | a length with a unit, e.g. `2cm`, `0.5in`, `12mm` | Page margins. A bare number is rejected. |
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
- **Open `#UI` Documents in Input Mode** *(default on)* — a document declaring `#UI` controls opens as its input form the first time you open it. The mode you switch to afterwards sticks; a later tab switch never brings the form back. See [UI Mode](new-ui-mode.md).
- **Apply `#UI` Values in Preview** *(default off)* — Preview normally renders the document's own values; turn this on and it renders the values entered into the input form instead, while still showing `#pre` and `#post` together. For tracking down an error that only appears once a form is filled in. It applies to the Preview *export* as well, so a saved Preview PDF, HTML or Word file shows what the Preview pane showed. It also decides which values a Preview render writes when **Write files** lets it write at all — see [Data output](#data-output). See [UI Mode](new-ui-mode.md).

## Data output

**Write files** — when `#write` and `#append` are allowed to run. Lives in the **Export** tab beside the **Write to Disk** button, not here.

"Report" here means the report layout wherever it is rendered — the preview pane switched to **Report** as much as a PDF, Word or HTML report export. Both are the same render, so both write.

| Value | When the files are written |
| --- | --- |
| Preview and Report | On a **Preview** or **Report** render — so the files are rewritten as you type. |
| Report Only *(default)* | On a report render only: the preview pane on **Report**, and a report export. The **Preview** view leaves the files alone. |
| Manual | On no render at all. Only when you press **Write to Disk**. |

The default exists because the preview re-renders as you type, and every render used to truncate and rewrite the document's output files. A suppressed directive still reports itself in the preview, reading *"will be written to"* rather than *"was successfully written to"*, and still reports any error in the directive or the matrix it names — so nothing is hidden until the write runs.

Two views never write, whatever the setting says. The **input form** is for filling in — the report shown beside it is the half of [input mode](new-ui-mode.md) that produces output, and it writes as any other report render does. **Unwrapped** is a code listing, so the document is not calculated for it at all.

### Which `#UI` values get written

A render that writes writes *its own* numbers, and not every render uses the same ones. A report applies the values entered into the input form; Preview normally uses the document's own values instead.

| Render | Writes on | `#UI` values it writes |
| --- | --- | --- |
| **Report** view, on screen | Report Only · Preview and Report | Entered |
| **Report** export (PDF / Word / HTML) | Report Only · Preview and Report | Entered |
| **Preview** view, on screen | Preview and Report | Document's own — unless **Apply `#UI` Values in Preview** is on, then entered |
| **Preview** export (PDF / Word / HTML) | Preview and Report | The same as the Preview view: it follows that setting too |
| **Input form** | never | — |
| **Unwrapped** | never | — |
| **Write to Disk** | on demand | Entered — it runs the report |

This is why **Report Only** is the default rather than *Preview and Report*. On a worksheet with `#UI` controls, a Preview render that writes would overwrite the output file with results computed from the document's declared values, discarding what the report had just written from the entered ones — and it would do it on every keystroke, so the report's numbers would rarely be the ones left on disk. Restricting writes to a report render keeps one answer to "what is in that file": whatever the form was filled in with.

If you do want *Preview and Report* on a worksheet with a form, turn on **Apply `#UI` Values in Preview** ([Editor features](#editor-features)). Preview then computes the entered values, on screen and in an export alike, so every render that writes writes the same numbers.

## Linter

**Minimum Severity** — Error / Warning / Information (all).
The lowest severity surfaced as a diagnostic.

## Diagnostics

- **Open Logs Folder** — opens the folder holding server logs and the most recent crash dump.
- **Max Output Lines (per channel)** *(web/desktop)* — 10–100000, default 1000. Number of lines retained in each Output panel channel before older lines are dropped. Lower values reduce memory use and keep the UI responsive when logs are noisy.
- **Max Preview Size (MB)** — 1–256, default 24. A document that renders to more HTML than this is not shown; the preview shows a **Preview blocked** page giving the render's size and the limit instead. Showing it risks running the app out of memory. PDF, HTML, and Word export are unaffected — they don't go through the preview. Raise it to preview a very large document anyway.
- **Max Preview Console Messages** — 10–100000, default 500. How many console lines one preview render may relay before the rest are dropped, with a final line saying output was suppressed. Lines go to the **Preview Console** output channel in the desktop app and to the **CalcpadCE Webview Console** channel in VS Code. A worksheet whose scripts log in a loop can otherwise flood the channel. Raise it while debugging a script, lower it when a library is noisy. Each line is clipped to 4 KB regardless of this setting, and the count resets on every render.

A single render inlines at most 24 MB of image data — past that the remaining images are left as paths and a warning is logged. 
The HTML-output channel (**HTML Preview Output** in the desktop app, **CalcpadCE Output HTML** in VS Code) keeps the first 256 KB of a render's body, saying how much it left out.

## Named configurations

The **Configuration** section lets you keep more than one named set of settings — for example a metric configuration with 3 decimals and an imperial one in degrees — and switch between them:

- **Active Config** — pick the configuration to apply.
- **Save current settings as** — type a name and click **Save** to store the current settings under it.
- **Open Settings Folder** — reveal where configurations are stored.
- **Reset to Default** — restore the default settings.

Configurations persist between sessions.

## See also

- [The CalcpadCE Panel](new-calcpad-panel.md)
- [Linter](new-linter.md) · [Exports](new-exports.md)
