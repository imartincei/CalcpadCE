# UI Mode

> Calcpad.Web only (web editor, desktop app, and VS Code extension). Not available in the standalone WPF desktop application for Windows.

UI mode turns a worksheet into a **fill-in form**. Mark the inputs of your calculation with the
`#UI` keyword, switch the results pane to input mode, and those lines are rendered as text
boxes, drop-downs, radio buttons, checkboxes or editable grids instead of plain results. Type a
new value and the whole document recalculates.

The same file is still an ordinary worksheet. Anywhere the document is rendered as a document —
the preview, a report, a PDF, a Word export — the `#UI` keyword changes nothing about how the
line looks. What it does carry across is the *values*: a report renders the numbers that were
entered into the form. You write one document, and it doubles as its own input form.

```text
#UI L = 6m
#UI q = 25kN/m
M = q*L^2/8
```

Preview and Report show three ordinary lines. In Input mode `L` and `q` become text boxes and
`M` follows whatever you type into them.

## Turning it on

A document that declares `#UI` controls exists to be filled in, so the first time you open one
it comes up as its input form — in the desktop app the results pane switches to **Input**, and in
**VS Code** the form panel opens beside the editor without taking the caret out of it. That
happens once per document per session: switch to **Preview** or **Report** and the mode you chose
sticks, however often you come back to the tab. `#UI` lines reached only through an `#include`
are not detected, since finding them would mean reading files on every open. Turn the whole
behaviour off with **Open #UI Documents in Input Mode** on the panel's **Settings** tab.

**VS Code**

| Command | What it does |
|---------|--------------|
| `CalcpadCE: Toggle #UI Input Mode` | Opens (or closes) the input form panel |
| `CalcpadCE: Toggle Report Preview` | Opens the report — beside the form, or on its own |
| `CalcpadCE: Print Report to PDF` | Exports the report as a PDF |
| `CalcpadCE: Save #UI Values to Document` | Writes the values you entered into the file |

Closing the form asks whether to save the values first; declining discards them. The report
preview is not tied to the form: open it on its own to read the print layout beside the editor,
and it stays open when the form closes. While the form or the report has focus, both the
**Print Report to PDF** and the report toggle appear as buttons in the panel's title bar.

**Web editor and desktop app**

The results toolbar — labelled **Results** — has four modes:

| Mode | What it shows |
|------|---------------|
| **Preview** | The document as written: `#pre` and `#post` both rendered, source values |
| **Unwrapped** | The source listing, with macros and includes resolved |
| **Input** | The `#UI` form. Takes over the window and hides the editor |
| **Report** | The print layout: `#pre` hidden, entered `#UI` values applied |

The same modes are in the native menu under **View → Result Mode**. In **Input** the toolbar
also offers **Report** (the report beside the form), **Save values** (once you have changed
something) and **Exit input mode**, which leaves you in **Report**. In **Input** and **Report**
a **Print PDF** button exports the report.

## `#pre` and `#post`

The two directives split a document into the part that is filled in and the part that is read
back. `#pre` is hidden in a report; `#post` is hidden while the form is on screen:

| | `#pre` shown | `#post` shown | Entered `#UI` values applied |
|---|---|---|---|
| **Preview** | yes | yes | no, unless the setting below says so |
| **Input** | yes | no | yes |
| **Report** | no | yes | yes |

So put the parts of the document that are output — result tables, conclusions, long derivations
— inside a `#post` block, and the form stays a short list of inputs. Put instructions for
whoever fills the form in inside `#pre`, and they stay out of the report.

```text
#pre
'<p>Enter the span below.</p>
#end pre
#UI L = 6m
#post
'<p>Span used: 'L'</p>
#end post
```

**Preview** is the mode to write in: it shows everything at once, and it deliberately ignores
entered values so you always see what the document itself says.

The exception is **Apply #UI Values in Preview** on the panel's **Settings** tab, off by
default. Turn it on and Preview renders with the entered values applied, exactly as the form
and the report do, while still showing `#pre` and `#post` together. That combination is what
makes it a debugging view: when a document calculates cleanly as written but errors once the
form is filled in, Preview with this on shows the failing values against the whole document —
the `#pre` instructions, the `#post` results and the source lines the form leaves out. Turn it
back off to see the document's own values again.

## Writing a `#UI` line

```text
#UI [{ JSON properties }] name = value
```

The JSON block is optional. Without it the control type and, for grids, the number of rows and
columns are worked out from the right-hand side.

`#UI` annotates an **assignment**, and only one whose right-hand side is a plain value:

| Right-hand side | Accepted | Example |
|-----------------|----------|---------|
| Number, with or without a unit | ✔ | `#UI L = 10m`, `#UI n = 4`, `#UI q = 3kN/m` |
| Vector or matrix literal | ✔ | `#UI v = [1; 2; 3]`, `#UI M = [1; 2 \| 3; 4]` |
| `vector()` / `matrix()` constructor | ✔ | `#UI Z = vector(5)`, `#UI G = matrix(r; c)` |
| An expression | ✘ | `#UI k = 2*E`, `#UI k = max(v)`, `#UI v = [1; sqrt(4)]` |
| A string variable (`name$`) | ✘ | `#UI s$ = ...` |

An expression is rejected because a control **overwrites** the right-hand side with whatever was
entered — the expression would be gone the moment someone used the form. Compute from the
inputs on a following, unannotated line instead.

Exponent notation is not a value here either: `2.5e6` reads as `2.5` with a unit `e6`. Write
`2500000` or `2.5*10^6` on a separate line.

Saved values are matched to controls by variable name, so give each input a name of its own
rather than re-assigning one — see [Editing a document that has saved
values](#editing-a-document-that-has-saved-values).

### Labels

An inline comment on the line labels the control. The comment is display text, so it may
contain its own `=`:

```text
#UI 'Span, 'L = 6m
#UI '2&middot;<i>r</i> ='d = 100mm
```

### Several controls on one line

Comment segments separate assignments, so one `#UI` line can declare more than one control.
They share the line's JSON properties but are saved and overridden separately.

```text
#UI 'b = 'b = 200mm', 'h = 'h = 400mm
```

## JSON properties

| Property | Type | Applies to | Meaning |
|----------|------|-----------|---------|
| `type` | string | all | `entry`, `datagrid`, `dropdown`, `radio`, `checkbox`. Auto-detected when omitted |
| `mode` | string | all | Only `number` is accepted; string inputs are not supported |
| `style` | string | all | CSS class(es) added to the control, **in Input mode only** |
| `reportStyle` | string | all | CSS class(es) added to the line **wherever it is not a control** |
| `rows` | number | datagrid | Grid rows. Auto-detected when omitted |
| `columns` | number | datagrid | Grid columns. Auto-detected when omitted |
| `rowHeaders` | array | datagrid | Row header labels |
| `columnHeaders` | array | datagrid | Column header labels |
| `keys` | array | dropdown, radio | The labels shown to the user |
| `values` | array | dropdown, radio | The values substituted into the calculation, one per key |

`keys` and `values` are both required for a drop-down or radio group, and must be the same
length. Header arrays must not be longer than the grid dimension they label.

## The control types

### `entry` — a text box

The default for a numeric right-hand side. The unit stays in the document beside the box, so
only the number is editable; the box accepts digits, a decimal point and a sign, and rejects
anything else as you type.

```text
#UI L = 10m
#UI {"type": "entry"} W = 5m
```

### `dropdown` — a list

`keys` are shown, `values` are substituted. A value may carry its own unit, so a drop-down can
switch units as well as magnitudes.

```text
#UI {"type": "dropdown", "keys": ["Low", "Medium", "High"], "values": ["1", "2", "3"]} grade = 1
```

### `radio` — a button group

Same `keys`/`values` pairing as a drop-down, laid out as radio buttons.

```text
#UI {"type": "radio", "keys": ["Steel", "Concrete"], "values": ["200GPa", "25GPa"]} E = 200GPa
```

### `checkbox` — a 1/0 toggle

Checked is `1`, unchecked is `0`. Pairs naturally with `#if`.

```text
#UI {"type": "checkbox"} useSteel = 1
```

### `datagrid` — an editable grid

The default whenever the right-hand side is a vector/matrix literal or a `vector()`/`matrix()`
call. `|` separates rows and `;` separates cells, so `[1; 2; 3]` is one row of three.

```text
#UI v = [1; 2; 3]
#UI M = [1; 2; 3 | 4; 5; 6]
#UI Z = vector(5)
#UI G = matrix(3; 4)
```

Sizes computed at run time work too — `matrix(r; c)`, `matrix(len(x); len(y))` — the grid is
sized from the value the line produced.

Declaring `rows` and `columns` explicitly fits the literal to that shape: missing cells become
`0`, extra ones are dropped.

```text
#UI {"type": "datagrid", "rows": 2, "columns": 3, "columnHeaders": ["a", "b", "c"], "rowHeaders": ["r1", "r2"]} T = [0; 0; 0 | 0; 0; 0]
```

The grid's size comes from the directive, so rows and columns cannot be inserted or deleted in
the form. Every cell becomes an element of a matrix literal, so a cell holding text — typed or
pasted in — is put back to `0`.

## Styling with CSS

Two properties attach classes, and they never both apply at once:

- **`style`** — added to the input control itself, and only in Input mode.
- **`reportStyle`** — added to the element wrapping the line, everywhere the line is *not* a
  control: Preview, Report, and every export but the input form.

So `style` is how the form looks, `reportStyle` is how the plain line looks, and you can set
both on one directive to style each independently.

### The base classes

Every control also carries a class of its own, which is what your class combines with:

| Type | Element | Base class |
|------|---------|-----------|
| `entry` | `<input type="text">` | `calcpad-ui-input` |
| `dropdown` | `<select>` | `calcpad-ui-dropdown` |
| `radio` | `<span>` wrapping the buttons | `calcpad-ui-radio`, each button's `<label>` is `calcpad-ui-radio-label` |
| `checkbox` | `<input type="checkbox">` | `calcpad-ui-checkbox` |
| `datagrid` | `<div>` holding the grid | `calcpad-ui-datagrid` |

Write your selector as the base class plus your own class, so it only hits the controls you
marked: `.calcpad-ui-input.highlight`, not `.highlight`. A `reportStyle` class lands on the
line's element, which is a paragraph, so target it as `p.boxed`.

### Where to put the style sheet

Put the CSS in a comment block, and wrap that block in `#val` … `#end val` so the lines are
emitted as written instead of each being wrapped in a paragraph — a `<p>` tag inserted into the
middle of a `<style>` element breaks the rules around it.

```text
#val
'<style>
'  .calcpad-ui-input.highlight { background-color: #eeeeee; border: 1px solid #aaaaaa; }
'  .calcpad-ui-dropdown.primary { font-weight: 600; }
'  .calcpad-ui-radio.compact .calcpad-ui-radio-label { margin-right: 4px; }
'  .calcpad-ui-checkbox.switch { accent-color: #2a8f3f; }
'  .calcpad-ui-datagrid.bordered { border: 2px solid #444444; }
'  p.boxed { border: 1px solid #cccccc; padding: 2px 4px; }
'</style>
#end val
```

Then name the classes on the directives:

```text
#UI {"style": "highlight"} depth = 2m
#UI {"reportStyle": "boxed"} P = 25kN
#UI {"style": "highlight", "reportStyle": "boxed"} q = 3kN/m
#UI {"type": "dropdown", "style": "primary", "keys": ["Low", "High"], "values": ["1", "2"]} g = 1
#UI {"type": "radio", "style": "compact", "keys": ["Steel", "Concrete"], "values": ["200GPa", "25GPa"]} E = 200GPa
#UI {"type": "checkbox", "style": "switch"} flag = 1
#UI {"type": "datagrid", "style": "bordered"} T = [1; 2 | 3; 4]
```

`depth` is highlighted in the form and unremarkable in the report; `P` is boxed in the report
and an ordinary text box in the form; `q` gets both.

Several classes can be listed at once — `"style": "highlight wide"` — and the usual document
style sheet applies too, so anything you can select with CSS you can style here.

### Inside a datagrid

A grid is a third-party widget, so a `style` class reaches its outer container but not the cells,
headers or context menu inside it. Those are styled by a stylesheet that ships with the
application, not from the document — see *Customizing the `#UI` Datagrid* in `DEVELOPER.md`.
Column widths and the grid's overall size are set by the preview script and are not adjustable
from CSS at all.

## Saving what was entered

Values you type live in memory: filling in a form never dirties the file on its own. Saving
them writes them into a metadata comment at the top of the document, which is what restores
them the next time you open it:

```text
'<!--{"uiOverrides":{"L:1":"8","q:1":"30"}}-->
```

The keys are control identities: the variable name, then which declaration of that name it is
(`L:1` is the first `L`), and inside a loop the pass numbers as well (`y:1:2`). A saved value
replaces the right-hand side of its assignment in the form and in the report, so the report
shows the numbers that were entered. Hand-writing an entry works too, and a broader key covers
more controls — `L:1` covers every pass of that declaration, a bare `L` covers every declaration
of the name.

See [Metadata Comments](new-metadata-comments.md) for the comment format itself.

Values do not have to be saved to be exported: an export made while the form is filled in uses
what is currently entered. Saving is what makes them survive closing the document.

### Editing a document that has saved values

Because a saved value is tied to a variable name and to which declaration of that name it is,
**editing the source of a document can move or orphan the values already saved in it**. The
document still calculates correctly — the risk is that a filled-in form comes back with a value
on the wrong control, or with a control reset to what the document itself says.

The ordinal counts the `#UI` declarations of that name in the order the document runs them, so
what matters is not where a line sits in the file but how many declarations of the same name
come before it:

- **Renaming a variable** orphans its value. `L:1` no longer matches anything, the control comes
  up with the document's own value, and the stale entry stays in the metadata comment until it
  is overwritten by the next save.
- **Deleting one of several `#UI` lines that declare the same name** renumbers the ones after it.
  Delete the first of three `L` controls and the old `L:2` and `L:3` values land on what are now
  `L:1` and `L:2` — the values survive, attached to the wrong controls.
- **Inserting a new `#UI` declaration of a name that already exists** shifts every later
  declaration of that name the same way.
- **Moving, editing or deleting lines that declare *other* names** is safe. So is reordering, as
  long as the declarations of one name keep their relative order.

The stable arrangement is therefore **one `#UI` declaration per variable name**. Give each input
its own name instead of re-assigning one, and every key is `name:1`: it cannot be renumbered by
anything you do elsewhere in the document, and only renaming or removing that input affects it.
Where a name genuinely must be declared more than once — the two branches of an `#if`, a control
inside a `#repeat` — the numbering is already stable against branch flips and loop passes, but
adding or removing one of those declarations later will still shift the rest.

If a document's values do end up scrambled, the metadata comment is plain text: fix the keys by
hand, or clear the `uiOverrides` entry to start the form from the document's own values again.

## Exporting

Exports come in variants, one per rendering, and **report is the default** — a plain "Export
PDF" from the menu, the toolbar, or a command gives you the report:

| Variant | Contents | Formats |
|---------|----------|---------|
| **Report** (default) | `#pre` hidden, `#post` shown, entered `#UI` values applied | PDF, HTML, Word |
| **Preview** | `#pre` and `#post` both shown, the document's own values | PDF, HTML, Word |
| **Input form** | The form itself, `#post` hidden, entered values in the controls | PDF, HTML |
| **Unwrapped** | The source listing, macros and includes resolved | PDF, HTML |

The non-default variants live in the **Export** tab of the sidebar, which groups its buttons by
variant, and in the desktop app's **File → Export** submenu. The input form and the unwrapped
listing have no Word form.

Two things to know about the exported files:

- None of them carry line numbers or the error-summary boxes the on-screen views use for
  navigation. Those belong to the screen; a file is read, not clicked through.
- An exported **input form** is static. Its controls render, but nothing is behind them to
  recalculate — it is a picture of the form, useful for printing a blank or filled-in sheet.

## `#UI` in macros, conditions and loops

The keyword works anywhere a normal assignment does.

```text
#def Beam$(span$)
	#UI load = 10kN/m
	M = load*span$^2/8
#end def
Beam$(6m)
```

Inside `#if` only the taken branch renders a control, and flipping the branch does not renumber
the controls that follow:

```text
#UI {"type": "checkbox"} useSteel = 1
#if useSteel ≡ 1
	#UI E = 200GPa
#else
	#UI E = 25GPa
#end if
```

Inside a loop each pass renders its own control, and each is entered separately:

```text
#repeat 3
	#UI y = 2
	y
#loop
```

## Diagnostics

`#UI` problems are reported under `CPD-3415`, by the linter as you type and by the calculation
engine when you run the document:

| Message | Cause |
|---------|-------|
| The `#UI` keyword requires a variable assignment. | The line assigns nothing |
| `#UI` directives do not support expressions. | The right-hand side is computed, not a value |
| String mode is not supported by the `#UI` keyword. | The variable ends with `$`, or `"mode"` is not `number` |
| Improper format for `#UI` keyword. Missing closing brace. | The JSON block is unterminated |
| Improper format for `#UI` keyword. Invalid JSON. | The block is not valid JSON |
| A `#UI` value has the wrong type. | A property was given the wrong kind of value |
| The `#UI` type '…' is not recognized… | `"type"` is not one of the five |
| The `#UI` … requires both keys and values arrays. | A drop-down or radio group is missing one |
| The `#UI` … keys and values arrays must have the same length. | They are paired, so the counts must match |
| The `#UI` … has *n* entries but the grid has *m* … | More headers than rows or columns |

See [Linter and Diagnostics](new-linter.md) for how diagnostics are surfaced.
