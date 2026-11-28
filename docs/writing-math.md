# Writing Math

Enter the math into the editor.
Spacing and indent are maintained automatically.
You can copy text from and to the editor or any external program (e.g. **Word**).

The source code is logically divided into lines, which are numbered automatically.
Each expression should be on a separate line.
By exception, it is possible to have several expressions on the same line, but they must be separated by comments.
When you finish the line, press "**Enter**" to start a new line.
Syntax highlighting is performed automatically.
Different code elements are displayed with different colors depending on their type.
All comments must be enclosed by quotes.
They can include both plain text and **HTML**.
You can use HTML to add pictures, tables and format the report.

The "**=**" symbol does not calculate the answer as on simple calculators.
This is the assignment operator (e.g. "*a* = 4").
If you need to check the equality of two numbers, use the "≡" operator (for example, "*a* ≡ *b*" means: "Is *a* equal to *b*?").
You can type it as `==` and the editor converts it for you.

## The Editor

The editor is the same in the [desktop app](new-desktop-app.md) and the [VS Code extension](new-vscode-extension.md).
It is built on the same code editor as VS Code, so moving the cursor, selecting, deleting, copying and pasting work the way they do in most other programs.
Some of the most useful commands are:

| Command | Shortcut |
| ------- | -------- |
| Undo / Redo | **Ctrl+Z** / **Ctrl+Y** |
| Find | **Ctrl+F**, then **Enter** or **F3** for the next match |
| Find and replace | **Ctrl+H** |
| Add a cursor above / below | **Ctrl+Alt+Up** / **Ctrl+Alt+Down** |
| Toggle line wrapping | **Alt+Z** |

On macOS use **Cmd** instead of **Ctrl**.

See [The CalcpadCE Editor](new-calcpadce-editor.md) for autocomplete, symbol navigation, formatting hotkeys and linting.

## Inserting Symbols

The **Insert** tab of the [CalcpadCE panel](new-calcpad-panel.md#insert) is a searchable palette of symbols, built-in functions and snippets.
Click an item to insert it at the cursor.
Function entries insert with placeholders, so you can press **Tab** to jump between arguments.

Greek letters and other special symbols can also be typed from the keyboard.
Type `~` followed by a key and press **space**: for example, `~a` gives `α` and `~D` gives `Δ`.
See [Quick-type symbols](new-calcpadce-editor.md#quick-type-symbols) for the full list.

The built-in constants *e* ≈ 2.7183, *π* ≈ 3.1416 and *g* ≈ 9.8066 are written as `e`, `π` (`~p`) and `g`.

## Coding Aids

- **Syntax highlighting** colors functions, variables, operators, units, keywords and comments differently.
  Pick a color theme in the [Settings](new-settings.md#color-theme) tab.
- **Auto-indentation** indents the lines inside conditional, loop and macro blocks.
  ASCII operators such as `<=` and `!=` are replaced with `≤` and `≠` as you type.
- **Autocomplete** suggests keywords, units, built-in functions and the variables, functions and macros defined in the document.
  Press **Tab** to accept a suggestion; **Enter** always starts a new line.
- **Bracket matching** highlights the matching opening or closing bracket next to the cursor.
- **Linting** marks errors and warnings at the spot with the problem as you type.
  See [Linter and Diagnostics](new-linter.md).
