# Migration Guide

Most changes to CalcpadCE are additive, and documents written for an earlier version keep calculating unchanged.
Changes that alter how existing code is interpreted are listed here. 
These sections also note how to migrate to supported syntax.

## Migrate from 7.x.x

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

Entering a value in the input form is stored in a `uiOverrides` entry on the first line of the CalcpadCE file, so the file keeps its own default values and they stay unchanged when input fields are updated.
You never have to write that entry yourself, but it is plain text if you ever want to edit it.

### Dots are no longer allowed as part of a variable name

A dot after an identifier is now always element access. Names can no longer contain a dot.

Previously the meaning of the dot depended on what happened to be in scope. 
If the name to the left of it already held a vector or a matrix, the dot was element access; in every other case it was absorbed into the name.
This produced unpredictable behavior in certain cases, which is why this change was made.

#### What breaks

Only documents that used a dot inside a variable name. The errors are reported on the part of the name that follows the dot:

| Code | Before | Now |
|------|--------|-----|
| `F.max = 10` | Defines a variable named `F.max` | `Undefined variable or units: "max".` |
| `a.1` where `a` is a scalar | Reads a variable named `a.1` | `Index target must be vector.` |

Function names follow the same rules as variable names, so a definition like `f.g(x) = x` is no longer valid either.

#### How to update

Rename with an underscore or a comma, both of which are still valid variable name characters:

```calcpad
F_max = 10
a,1 = 4
```
