# Metadata Comments

Sometimes you want CalcpadCE to store information about your document without that note showing up in the printed report.
This is what **metadata comments** are used for.
These are HTML comments that CalcpadCE reads to pull information from.
You can either write write these manually, or the [Properties tab](new-calcpad-panel.md#properties) in the CalcpadCE panel fills them in for you.

## What they look like

A metadata comment is a normal CalcpadCE comment (it starts with `'` or `"`) with an HTML comment storing a JSON string `<!--{ … }-->`:

```text
'<!--{"desc": "Cross-sectional area"}-->
A(b; h) = b·h
```

Because it's an HTML comment, none of it appears in the HTML output.

Here are some considerations for using them:

- Notes about a definition go on the line **directly above** it.
- The whole comment has to stay on **one line** unless you use _ line separators.
- If the text inside is not valid JSON, CalcpadCE just ignores it and the linter points it out.
- Toggling line wrapping with **Alt+Z** can make these easier to read fully or take up less space.

## Documenting a definition

Put this on the line above a variable, function, macro, or custom unit:

| Field | What it's for |
|-------|---------------|
| **Description** | A sentence explaining what the definition is. |
| **Parameter types** | The kind of value each input expects. Functions take `value`, `vector`, `matrix`, or `any`; macros use CalcpadCE's token names (this is work-in-progress). |
| **Parameter descriptions** | A short note for each input, in order. |
| **Return type** | What a function gives back: `value`, `vector`, `matrix`, or `any`. |

```text
'<!--{"desc": "Second moment of area of a rectangle", "paramTypes": ["value", "value"], "paramDesc": ["width", "height"], "returnType": "value"}-->
I(b; h) = b*h^3/12
```

Filling in parameter and return types helps the [linter](new-linter.md) catch places where the function is called with the wrong kind of value.
It also populates text when hovering over the name of the variable, function, macro, or custom unit in the code editor.

## PDF export settings

A document can pin its own PDF page setup, so it prints the same way wherever it's opened:

```text
'<!--{"pdf": {"format": "A4", "orientation": "landscape", "marginTop": "2cm"}}-->
```

Each key you set overrides the matching option on the [Settings tab](new-settings.md#pdf-export); everything you leave out keeps whatever the app is configured with.

The **Properties** tab has a picker for these, so you don't have to remember the names, and the linter flags an unknown key or a margin without a unit.
If more than one comment sets the same key, the last one wins.
See [Exports → PDF export](new-exports.md#pdf-export) for additional information.

## Quieting the linter

If the [linter](new-linter.md) flags something that isn't actually a problem, you can silence it for a stretch of the document.
Wrap those lines between a `LintIgnore` and an `EndLintIgnore` marker, listing the warning codes to hide (or leave the list empty to hide/unhide everything).

The **Properties** tab has a picker for the codes, so you don't have to memorize them.
See [Suppressing diagnostics](new-linter.md#suppressing-diagnostics-lint-ignore) for additional details.

## See also

- [The CalcpadCE Panel & Settings](new-calcpad-panel.md)
- [Linter and Diagnostics](new-linter.md) — the lint-ignore markers
- [Exports](new-exports.md#pdf-export) — page setup and print visibility
- [Using the VS Code Extension](new-vscode-extension.md)
