# Migration Guide

Most changes to CalcpadCE are additive, and documents written for an earlier version keep calculating unchanged.
Changes that alter how existing code is interpreted are listed here.
These sections also note how to migrate to supported syntax.

## Migrate from 7.x.x

### `#input` is deprecated

`#input` rendered an input form at a breakpoint and waited for the user to resume.
[UI mode](new-ui-mode.md) replaces it.

#### What breaks

`#input` still parses without errors but has no effect.
The [linter](new-linter.md) flags it as a warning under `CPD-3420`.
It will be removed in a future update.

#### How to update

Declare the inputs with `#UI` and replace `#input` with `#pause` where needed.
In the UI mode input form, `#pause` ends a step that the user moves past with **Next**.
See [`#pause` in UI mode](new-ui-mode.md#pause-in-ui-mode).

### `?{}` Input syntax is deprecated

[UI mode](new-ui-mode.md) replaces the two places a document used to keep its own input values — a `?{6}` field, and the `#{2; 3}` list that fed those fields into an `#include`:

```calcpad
L = ?{6}m
#include beam.cpd #{2; 3}
```

#### What breaks

Both forms still parse without errors, but the values are ignored and no input fields get created.
The [linter](new-linter.md) flags them as warnings — `CPD-3419` for a `?{}` field, `CPD-1103` for the `#include` list.
This syntax will throw an error in a future update instead of being ignored, so it is recommended to migrate to the new #UI syntax.

#### How to update

Declare the input with `#UI` and give it an ordinary value:

```calcpad
#UI L = 6m
```

Entering a value in the input form is stored in a `uiOverrides` entry at the end of the CalcpadCE file, so the file keeps its own default values and they stay unchanged when input fields are updated.
You never have to write that entry yourself, but it is plain text if you ever want to edit it.

### `%u` in comments is removed

A `%u` in a comment used to be replaced with the document's length unit, and a **m** / **cm** / **mm** combo box was added to the input form so the reader could switch every occurrence at once.
The **Default Input Length Unit** setting that drove it, and the `Units` variable holding the matching conversion factor, are removed with it.

#### What breaks

`%u` is no longer substituted and renders literally as `%u` in the output, and no unit combo box is added.
`#settings {"units": "..."}` is now an unknown key, and reading the `Units` variable is an undefined variable error.

#### How to update

Replace each `%u` with the unit the document actually uses.

Where an equation used `Units` as a conversion factor, write the factor itself instead based on what `%u` was replaced with.

This is a temporary loss of functionality.
Dynamic input units are coming in a future version via string variables, where the unit can be picked from a dropdown and assigned to a string variable you can reference anywhere else in the document.
There is also an option to input units with the number by setting "forceUnits:false" or "allowExpression:true", see [UI mode](new-ui-mode.md)

### The report font changed from Georgia Pro to DejaVu Serif Condensed

Georgia Pro could not be redistributed, so it was not ideal as a font that needed shipped over the web.
Output now uses [DejaVu Serif Condensed](https://dejavu-fonts.github.io/), which ships with CalcpadCE and is embedded in every report.

#### What breaks

Only the report appearance changes, nothing about the calculation process is different.
Equations are about 1% wider and lines 3% taller.
DejaVu Serif also has no Light or SemiBold, so bold text and `∑`, `∏`, `∫` render heavier.

#### How to update

To restore the old look where Georgia Pro is installed, insert the **Report Fonts** snippet — Insert panel, under **CSS**:

That snippet also contains examples to change font sizes, so here is a condensed version that just changes the font back if you have it installed already:

```calcpad
#val
'<style>
'  .eq, input[type="text"], table.matrix,
'  .eq small var, .eq small i {
'    font-family: "Georgia Pro", "Century Schoolbook", "Times New Roman", Times, serif;
'  }
'  .nary { font-family: "Georgia Pro Light", "Georgia Pro", serif; font-weight: 300; }
'</style>
#end val
```

### `"` headings render as `<h1>` instead of `<h3>`

A `"Title` line now renders as `<h1>`, so the report outline starts at the top level and leaves room for more levels of subsections below it.
The heading sizes were scaled down to match, so `<h1>` is the size `<h3>` used to be.

| Element | Before | Now |
| ------- | ------ | --- |
| `h1` | 2.1em | 1.4em |
| `h2` | 1.7em | 1.32em |
| `h3` | 1.4em | 1.24em |
| `h4` | 1.2em | 1.16em |
| `h5` | 1.1em | 1.08em |
| `h6` | 1em | 1em |

#### What breaks

Only the report appearance changes.
`"` headings look the same as before, but HTML headings (`'<h1>` to `'<h6>`) and Markdown headings (`#` to `######`) render smaller.
Custom CSS that targeted `h3` to style `"` headings now needs to target `h1`.

#### How to update

There are two ways to get a report to render exactly as before.

**Option 1:** restore the old sizes for every level and convert each `"Title` line to `'<h3>Title</h3>`.
This works for any document, but each `"` heading has to be changed:

```calcpad
#val
'<style>
'  h1 { font-size: 2.1em; }
'  h2 { font-size: 1.7em; }
'  h3 { font-size: 1.4em; }
'  h4 { font-size: 1.2em; }
'  h5 { font-size: 1.1em; }
'  h6 { font-size: 1em; }
'</style>
#end val
```

**Option 2:** keep `h1` at 1.4em and restore the old sizes for the other levels.
No headings need to be edited, but this breaks the table of contents order and only works if the document has no `'<h1>` or `#` headings of its own:

```calcpad
#val
'<style>
'  h2 { font-size: 1.7em; }
'  h3 { font-size: 1.4em; }
'  h4 { font-size: 1.2em; }
'  h5 { font-size: 1.1em; }
'  h6 { font-size: 1em; }
'</style>
#end val
```

### Dots are no longer allowed as part of a variable name

A dot after an identifier is now always element access. Names can no longer contain a dot.

Previously the meaning of the dot depended on what happened to be in scope.
If the name to the left of it already held a vector or a matrix, the dot was element access; in every other case it was absorbed into the name.
This produced unpredictable behavior in certain cases, which is why this change was made.

#### What breaks

Only documents that used a dot inside a variable name. The errors are reported on the part of the name that follows the dot:

| Code | Before | Now |
| ---- | ------ | --- |
| `F.max = 10` | Defines a variable named `F.max` | `Undefined variable or units: "max".` |
| `a.1` where `a` is a scalar | Reads a variable named `a.1` | `Index target must be vector.` |

Function names follow the same rules as variable names, so a definition like `f.g(x) = x` is no longer valid either.

#### How to update

Rename with an underscore or a comma, both of which are still valid variable name characters:

```calcpad
F_max = 10
a,1 = 4
```

### Inverse trig functions return angle units by default

`asin`, `acos`, `atan`, `acsc`, `asec`, `acot`, `atan2` and `phase` now attach the current angle unit to their result (`°`, `rad` or `grad`).
Previously this required `ReturnAngleUnits = 1`, and the result was a unitless number otherwise.
The default is now the **Return Angle Units** setting, which is on.

Two related fixes change results under `#deg` and `#gra`:

- `phase` now follows `#deg`/`#rad`/`#gra` like `atan2` does. It always returned radians before.
- In `A∠φ`, a unitless `φ` is now read in the current angle units. It was always read as radians before, so under `#deg`, `1∠45` was 1∠(45 rad).

#### What breaks

Mixing an inverse trig result with a unitless number is an error:

| Code | Before | Now |
| ---- | ------ | --- |
| `atan(1)` | `45` | `45°` |
| `atan(x) - 1` | a number | `Inconsistent units: "° - unitless".` |
| `phase(1 + 1i)` under `#deg` | `0.785` | `45°` |
| `re(1∠45)` under `#deg` | `0.525` | `0.707` |

Workarounds such as `phase(z)*180/π` now give the wrong answer under `#deg`.

Inverse trig on a diagonal or triangular matrix leaves its off-diagonal zeros unitless, so combining the result with an angle is an error, e.g. `atan(diagonal(3; 1)) + 1°`.
This is a known limitation that will be fixed in a future update.
Use `hp()` matrices, which share one unit across all elements, or set `#settings {"returnAngleUnits": false}` before running these calculations.

#### How to update

Remove manual conversions like `*180/π` after `phase`, and add angle units where a result is combined with unitless numbers, e.g. `atan(x) - 1rad`.

To keep the old unitless results instead, turn off **Return Angle Units** in the [Settings](new-settings.md#math) tab, or add either line at the top of the document:

```calcpad
ReturnAngleUnits = 0
#settings {"returnAngleUnits": false}
```

Write `1∠(45rad)` to keep a radian phasor angle under `#deg`.
