# How It Works

The software is quick and easy to use.
Just follow these simple steps:

1. **Enter** text and formulas into the editor.
2. The **results pane** beside it renders a professionally formatted HTML **report** as you type.
   Open it with the **CalcpadCE Preview** button in VS Code, or toggle it with **Ctrl+P** in the desktop app.
3. **Export** the report to **PDF**, **Word** or **HTML** from the panel's **Export** tab.
   See [Exports](new-exports.md).

The [linter](new-linter.md) checks the document while you write, and marks problems in the editor before the report is calculated.

The results pane can show the same document in several modes:

- **Preview** - the whole document, calculated with the default values written in it.
  This is the recommended mode to write a CalcpadCE file in.
- **Input** - an input form.
  Inputs declared with the `#UI` keyword are rendered as text boxes, drop-downs, radio buttons, checkboxes, or editable grids.
  Change a value and the document recalculates.
  This mode is helpful to simplify inputs and for users who are not comfortable editing the CalcpadCE file.
  It makes clear which are the required input parameters, and the person filling in the form never edits the CalcpadCE file.
- **Report** - the printable report, calculated with the values entered into the input form.
- **Unwrapped** - the CalcpadCE file with macros and `#include` files expanded, for debugging.

See [UI mode](new-ui-mode.md) for how to build input forms.
See [the CalcpadCE editor](new-calcpadce-editor.md) and [the CalcpadCE panel](new-calcpad-panel.md) for a tour of the interface.
