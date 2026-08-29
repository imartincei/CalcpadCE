# The CalcpadCE Editor

> The editor described here is the same in the [VS Code extension](new-vscode-extension.md) and the [desktop app](new-desktop-app.md). Both frontends embed the same Monaco editor and language tooling.
> Where a host behaves differently, it is called out below.

## Syntax highlighting

`.cpd` files get semantic highlighting: numbers, units, operators, variables, functions, macros, keywords, commands, file paths, and embedded HTML/Markdown/CSS/JS/SVG in comments are each colored distinctly.
Highlighting updates per line as you type, for both dark and light themes.

A few editor defaults are set for `.cpd` files so the language behaves predictably:

- The default font is **JuliaMono**.
  - **VS Code** — run **CalcpadCE: Install JuliaMono Font** from the Command Palette if you chose not to install when prompted.
  - **Desktop app** — JuliaMono is bundled with the app.
- **Enter always inserts a newline** — it never accepts a suggestion.
  Press **Tab** to accept a completion instead.
- **Tab accepts suggestions** and triggers completion on a partial word.


## Autocomplete

As you type, the completion list offers:

- **Your own symbols first** — variables, functions, macros, and custom units defined in the current document (and its `#include` files) are prioritized above built-ins.
- **Built-in functions** with snippet placeholders — accept one and press **Tab** to jump between arguments.
- **Setting keys** (`decimals`, `degrees`, `complex`, `units`, `colorScale`, …) where a setting is expected.
- **[Metadata keys](new-metadata-comments.md)** inside an HTML-comment block placed directly above a definition.
  The **Properties** panel tab also edits these through a form.

## Quick-type symbols

Type `~` followed by a key and press **space** to insert a Greek letter or math symbol.
For example `~a` + space → `α`, `~p` + space → `π`, `~S` + space → `Σ`.
The full set is shown in the **Insert** tab's Symbol Palette.
Toggle this with the `quickTyping` setting in the CalcpadCE **Settings** tab.

## Operator replacement and auto-indent

Typing ASCII operators auto-converts them to Unicode: `<=` → `≤`, `>=` → `≥`, `!=` → `≠`.
Block keywords indent automatically: `#if` / `#else` / `#end if`, `#for` / `#end for`, and `#def` / `#end def` for example.

## Formatting hotkeys

When the cursor is in a `.cpd` file, these hotkeys wrap the selection in HTML or Markdown markup.
Whether HTML or Markdown is emitted depends on the `commentFormat` setting (`auto` / `html` / `markdown`).

| Keybinding | Effect |
|------------|--------|
| **Ctrl+B** | Bold |
| **Ctrl+I** | Italic |
| **Ctrl+U** | Underline |
| **Ctrl+=** | Subscript |
| **Ctrl+Shift+=** | Superscript |
| **Ctrl+1** … **Ctrl+6** | Headings 1–6 |
| **Ctrl+L** | Paragraph |
| **Ctrl+R** | Line break |
| **Ctrl+Shift+L** | Bulleted list |
| **Ctrl+Shift+N** | Numbered list |
| **Ctrl+Q** | Toggle `'` comment prefix |
| **Ctrl+Shift+Q** | Uncomment |
| **Ctrl+Shift+V** | Paste as comment (each line prefixed with `'`) |

On macOS use **Cmd** instead of **Ctrl**.
Turn the whole set off with the `enableFormattingHotkeys` setting if it conflicts with your other bindings.

## Navigating symbols

The editor provides IDE-grade navigation across variables, functions, macros, and custom units — including across `#include` files:

| Action | Trigger |
|--------|---------|
| **Go to Definition** | **Ctrl+Click** or **F12** — jumps to the first assignment |
| **Find All References** | **Shift+Alt+F12**, or right-click → *Find All References* |
| **Rename Symbol** | **F2** — renames occurrences in the current document only (not across `#include` files) |
| **Hover** | Point at a symbol for its signature, type, source file, and docs |

Hovering over a built-in function shows its signature, description, return type, per-parameter documentation, and a runnable example.

Hover, definitions, references, the linter, the preview, and the TOC are all scoped to the document you are editing, so symbols and errors never bleed between unrelated documents.

## Path completion for includes and data files

When you type a path after `#include`, `#read`, `#write`, or `#append`, the editor completes filenames from the workspace or open folder and, once the document declares them, from its `#ProjectPath`/`#LibraryPath` roots:

- `#include`: `.cpd`, `.txt`
- `#read` / `#write` / `#append`: `.cpd`, `.txt`, `.csv`, `.xlsx`, `.xlsm`
- The completion list expands `%VAR%`, `{user}`, `{project}`, `{library}` as you type.
See [Includes and File Reads](new-includes.md#path-root-tokens-project-and-library).

## Linting

CalcpadCE checks your document as you write and flags problems before they are converted to HTML.
Issues are marked in red, yellow, or blue at the spot with the problem, based on severity, and appear in the **Problems** list with a link to the offending line.
Each carries a `CPD-XXXX` code, and the linter minimum-severity setting controls how much is shown.

- **VS Code** — diagnostics appear in the **Problems** panel (**Ctrl+Shift+M**).
- **Desktop app** — diagnostics appear in the **Problems** panel below the editor.

See **[Linter and Diagnostics](new-linter.md)** for what each code means.

## See also

- [The CalcpadCE Panel](new-calcpad-panel.md) — the shared sidebar and all settings
- [VS Code Extension](new-vscode-extension.md) · [Desktop App](new-desktop-app.md)
- [Includes and File Reads](new-includes.md) · [Linter and Diagnostics](new-linter.md) · [Metadata Comments](new-metadata-comments.md)
- [Writing Math](writing-math.md) · [Quick Reference](quick-reference.md)
