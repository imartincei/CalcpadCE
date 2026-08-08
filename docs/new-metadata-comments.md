# Metadata Comments

> Part of Calcpad.Web. The **Properties** tab described here currently appears in the [VS Code extension](new-vscode-extension.md) and the [desktop app](new-desktop-app.md). The comments it writes are understood everywhere — the [linter](new-linter.md), the preview, and [PDF export](new-pdf-export.md) — no matter which editor created them.

Sometimes you want to tell Calcpad something about your document without that note showing up in the printed report — what a function's inputs mean, that a section is a work-in-progress the linter should leave alone, or which page size this particular report has to print at.

A **metadata comment** does exactly that. It looks like an ordinary comment, so it never clutters your output, but Calcpad reads the extra information inside it. You don't have to write these by hand: the **Properties** tab in the CalcPad panel fills them in for you.

## The Properties tab

Open the [CalcPad panel](new-calcpad-panel.md) and switch to the **Properties** tab. Then click a line in your document, and the tab shows a small form for that line:

- **On a definition** (a variable, function, macro, or custom unit) you can add a **description**, and for functions and macros, a **type and description for each parameter** and the **return type**. The parameter rows are filled in to match the definition automatically.
- **On a `#UI` line** you get a form for its JSON properties — control type, style, options, grid size, and so on — instead of hand-writing the `{...}` block, plus a **Saved #UI values** list of every entry already saved for the document. Click an entry to jump to its control, edit its value inline, or **Purge unused** to drop entries that no longer match a control still in the document. See [UI Mode](new-ui-mode.md).
- **Document settings** are always available. Editing them writes a [`#settings` directive](#document-settings) at the top of the file — not a metadata comment — so they take effect during calculation.
- **On any other line** you also get the **lint-ignore** regions and the [**PDF export** settings](#pdf-export-settings).

Fill in what you want and click **Apply**.
If the line doesn't have a metadata comment yet, one is created for you; if it does, it's updated in place.
**Reset** throws away your edits and reloads what's currently there.
Only the fields that make sense for the line are shown — use **Add field** if you want one that's hidden.

## What they look like

A metadata comment is a normal CalcpadCE comment (it starts with `'` or `"`) with an HTML comment storing a JSON string `<!--{ … }-->`:

```text
'<!--{"desc": "Cross-sectional area"}-->
A(b; h) = b·h
```

Because it's an HTML comment, none of it appears in the HTML output.

A few things to know:

- Notes about a definition go on the line **directly above** it.
- The whole comment has to stay on **one line** unless you use _ line separators.
- If the text inside gets garbled, CalcpadCE just ignores it and the linter points it out.
- Toggling line wrapping with **Alt+Z** can make these easier to read fully or take up less space.

## Documenting a definition

Put this on the line above a variable, function, macro, or custom unit:

| Field | What it's for |
|-------|---------------|
| **Description** | A sentence explaining what the definition is. |
| **Parameter types** | The kind of value each input expects. Functions take `value`, `vector`, `matrix`, or `any`; macros use CalcpadCE's token names. |
| **Parameter descriptions** | A short note for each input, in order. |
| **Return type** | What a function gives back: `value`, `vector`, `matrix`, or `any`. |

```text
'<!--{"desc": "Second moment of area of a rectangle", "paramTypes": ["value", "value"], "paramDesc": ["width", "height"], "returnType": "value"}-->
I(b; h) = b·h³/12
```

Filling in parameter and return types also helps the [linter](new-linter.md) catch places where the function is called with the wrong kind of value.

## Document settings

You can pin settings — decimals, angle units, and so on — to a document so it always renders the same way, no matter how the app is configured. Use the `#settings` directive with a JSON object:

```text
#settings {"decimals": 2, "degrees": 1, "units": "cm"}
```

Unlike the other properties on this tab, `#settings` is a real Calcpad directive, not a metadata comment — the engine reads it while calculating. It applies to **every line after it**, so you can change settings partway through a document by adding another `#settings` directive. If you keep one near the top, it configures the whole file; the **Properties** tab writes it there for you. The settings you can set are the ones on the [Settings tab](new-calcpad-panel.md#settings), except **Screen Scale Factor** and **Light Direction**, which are app-level display settings only:

| Setting | Values |
|---------|--------|
| `decimals` | 0–15 decimal places |
| `degrees` | `0` radians · `1` degrees · `2` gradians |
| `complex` | `true` / `false` — complex-number mode |
| `substitute` | `true` / `false` — substitute variable values into the output |
| `formatEquations` | `true` / `false` — stacked math form |
| `zeroSmallMatrixElements` | `true` / `false` — show tiny values as `0` |
| `showHiddenOutput` | `true` / `false` — ignore `#hide` and render hidden content (debugging) |
| `maxOutputCount` | 5–100 rows shown for big matrices/vectors |
| `units` | unit system, e.g. `m`, `cm`, `mm` |
| `isUs` | `true` / `false` — US customary vs. UK imperial for unit names that differ between the two |
| `vectorGraphics` | `true` / `false` — SVG plots instead of images |
| `colorScale` | plot palette (see below) |
| `smoothScale` | `true` / `false` — smooth the color scale |
| `shadows` | `true` / `false` — shadows on 3-D surfaces |
| `adaptivePlot` | `true` / `false` — adaptive plot sampling |
| `plotWidth` · `plotHeight` | plot area size, in pixels |
| `plotStep` | mesh size for surface/map plots, in pixels (`0` = automatic) |
| `precision` | relative precision for numerical methods, `1e-15`–`1e-2` |
| `tol` | target tolerance for the iterative solver, `1e-15`–`1e-2` |

`colorScale` can be `None`, `Gray`, `Rainbow`, `Terrain`, `VioletToYellow`, `GreenToYellow`, `Blues`, `BlueToYellow`, `BlueToRed`, or `PurpleToYellow`.
Anything the app doesn't recognize is ignored.

The plot and solver settings above can also still be set with the older dedicated variables (`PlotWidth`, `PlotHeight`, `PlotStep`, `Precision`, `Tol`, `PlotSVG`, `PlotAdaptive`, `PlotPalette`, `PlotShadows`, `PlotSmooth`) for backwards compatibility. Whichever one — the `#settings` key or the variable assignment — appears later in the file wins.

## PDF export settings

A document can pin its own PDF page setup, so it prints the same way wherever it's opened.
Put a `pdf` object in a metadata comment — near the top is the natural place, though it applies to the whole export no matter where it sits:

```text
'<!--{"pdf": {"format": "A4", "orientation": "landscape", "marginTop": "2cm"}}-->
```

Each key you set overrides the matching option on the [Settings tab](new-settings.md#pdf-export); everything you leave out keeps whatever the app is configured with.
So a document that only cares about its margins can say just that, and still follow your usual paper size.

| Key | Values |
|-----|--------|
| `format` | `Letter`, `Legal`, `Tabloid`, `Ledger`, or `A0`–`A6` |
| `orientation` | `portrait` / `landscape` |
| `marginTop` · `marginRight` · `marginBottom` · `marginLeft` | A length with a unit, e.g. `2cm`, `0.5in`, `12mm` |
| `showPageNumbers` | `true` / `false` — "Page *n* of *m*" in the footer |
| `showDate` | `true` / `false` — the timestamp in the header |
| `documentTitle` | Header title (defaults to the file name) |
| `dateTimeFormat` | [.NET date/time format string](https://learn.microsoft.com/en-us/dotnet/standard/base-types/custom-date-and-time-format-strings), e.g. `M/d/yyyy h:mm tt` |

The **Properties** tab has a picker for these, so you don't have to remember the names, and the linter flags an unknown key or a margin without a unit.
If more than one comment sets the same key, the last one wins.
See [PDF Export](new-pdf-export.md) for the rest of the export story.

## Quieting the linter

If the [linter](new-linter.md) flags something that isn't actually a problem, you can silence it for a stretch of the document.
Wrap those lines between a `LintIgnore` and an `EndLintIgnore` marker, listing the warning codes to hide (or leave the list empty to hide/unhide everything):

```text
'<!--{"LintIgnore": ["CPD-3301"]}-->
prototype_var = 5
'<!--{"EndLintIgnore": []}-->
```

The **Properties** tab has a picker for the codes, so you don't have to memorize them. See [Suppressing diagnostics](new-linter.md#suppressing-diagnostics-lint-ignore) for the details.

## Leaving sections out of the PDF

This is no longer a metadata comment. Wrap the section in `#pre` … `#end pre` instead:

```text
#pre
debug_x = 5
#end pre
```

The section still shows in the preview but is dropped from the PDF.
The old `NoPrintStart` / `NoPrintEnd` markers are gone — see [Excluding sections from the PDF](new-pdf-export.md) and, for the full `#end`/condition syntax these directives share, [New Syntax](new-syntax.md).

## See also

- [The CalcpadCE Panel & Settings](new-calcpad-panel.md)
- [Linter and Diagnostics](new-linter.md) — the lint-ignore markers
- [PDF Export](new-pdf-export.md) — page setup and print visibility
- [Using the VS Code Extension](new-vscode-extension.md)
