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
- **[Favorites](new-calcpad-panel.md#insert)** first, marked with ★.
- **[Custom snippets](new-calcpad-panel.md#custom-snippets)** alongside the built-in ones.

## Quick-type symbols

Type `~` followed by a key and press **space** to insert a Greek letter or math symbol.
For example `~a` + space → `α`, `~p` + space → `π`, `~S` + space → `Σ`.
The full set is shown in the **Insert** tab's Symbol Palette.
Toggle this with the `quickTyping` setting in the CalcpadCE **Settings** tab.

| Name | Lowercase | Key | Uppercase | Key |
| ---- | --------- | --- | --------- | --- |
| alpha | α | `~a` | Α | `~A` |
| beta | β | `~b` | Β | `~B` |
| gamma | γ | `~g` | Γ | `~G` |
| delta | δ | `~d` | Δ | `~D` |
| epsilon | ε | `~e` | Ε | `~E` |
| zeta | ζ | `~z` | Ζ | `~Z` |
| eta | η | `~h` | Η | `~H` |
| theta | θ | `~q` | Θ | `~Q` |
| iota | ι | `~i` | Ι | `~I` |
| kappa | κ | `~k` | Κ | `~K` |
| lambda | λ | `~l` | Λ | `~L` |
| mu | μ | `~m` | Μ | `~M` |
| nu | ν | `~n` | Ν | `~N` |
| xi | ξ | `~x` | Ξ | `~X` |
| omicron | ο | `~o` | Ο | `~O` |
| pi | π | `~p` | Π | `~P` |
| rho | ρ | `~r` | Ρ | `~R` |
| sigma | σ | `~s` | Σ | `~S` |
| final sigma | ς | `~j` | | |
| tau | τ | `~t` | Τ | `~T` |
| upsilon | υ | `~u` | Υ | `~U` |
| phi | φ | `~f` | Φ | `~F` |
| chi | χ | `~c` | Χ | `~C` |
| psi | ψ | `~y` | Ψ | `~Y` |
| omega | ω | `~w` | Ω | `~W` |

| Symbol | Key |
| ------ | --- |
| ° | `~0` |
| ′ ″ ‴ ⁗ | `~'` `~"` `~'''` `~''''` |
| ø Ø | `~/o` `~/O` |
| ϕ | `~ff` |
| ‰ ‱ | `~%` `~%%` |
| ℓ | `~len` |

Any Insert tab item can have quick-type keys: click the **~** beside it to add or remove them.

## Custom Snippets

You can right-click variables, custom units, custom functions, or macros and save it as a custom snippet.
Metadata comments for these items will be inserted into the custom snippet form where they exist.
See **[Custom snippets](new-calcpad-panel.md#custom-snippets)** for more information.

## Operator replacement and auto-indent

Typing ASCII operators auto-converts them to Unicode: `<=` → `≤`, `>=` → `≥`, `!=` → `≠`.
Block keywords indent automatically: `#if` / `#else` / `#end if`, `#for` / `#end for`, and `#def` / `#end def` for example.

## Formatting hotkeys

When the cursor is in a `.cpd` file, these hotkeys wrap the selection in HTML or Markdown markup.
Whether HTML or Markdown is emitted depends on the `commentFormat` setting (`auto` / `html` / `markdown`).

| Keybinding | Effect | HTML | Markdown |
| ---------- | ------ | ---- | -------- |
| **Ctrl+B** | Bold | `<strong>…</strong>` | `**…**` |
| **Ctrl+I** | Italic | `<em>…</em>` | `*…*` |
| **Ctrl+U** | Underline | `<ins>…</ins>` | `++…++` |
| **Ctrl+=** | Subscript | `<sub>…</sub>` | `~…~` |
| **Ctrl+Shift+=** | Superscript | `<sup>…</sup>` | `^…^` |
| **Ctrl+1** … **Ctrl+6** | Headings 1–6 | `<h1>…</h1>` … `<h6>…</h6>` | `# …` … `###### …` |
| **Ctrl+L** | Paragraph | `<p>…</p>` | `<p>…</p>` |
| **Ctrl+R** | Line break | `<br/>` | `<br/>` |
| **Ctrl+Shift+L** | Bulleted list | `<ul><li>…</li></ul>` | `- …` |
| **Ctrl+Shift+N** | Numbered list | `<ol><li>…</li></ol>` | `1. …` |
| **Ctrl+Q** | Toggle `'` comment prefix | | |
| **Ctrl+Shift+Q** | Uncomment | | |
| **Ctrl+Shift+V** | Paste as comment (each line prefixed with `'`) | | |

Select some text and press a hotkey to wrap it.
Pressing a heading hotkey on a line that is already a heading changes its level.

On macOS use **Cmd** instead of **Ctrl**.
Rebind any of them in **Settings → [Keyboard Shortcuts](new-settings.md#keyboard-shortcuts)**, or turn the whole set off with the `enableFormattingHotkeys` setting if it conflicts with your other bindings.

## Navigating symbols

The editor provides IDE-grade navigation across variables, functions, macros, and custom units — including across `#include` files:

| Action | Trigger |
| ------ | ------- |
| **Go to Definition** | **Ctrl+Click** or **F12** — jumps to the first assignment |
| **Find All References** | **Shift+Alt+F12**, or right-click → *Find All References* |
| **Rename Symbol** | **F2** — renames occurrences in the current document only (not across `#include` files) |
| **Hover** | Point at a symbol for its signature, type, source file, and docs |

Hovering over a built-in function shows its signature, description, return type, per-parameter documentation, and a runnable example.

Use the mouse Back and Forward buttons to revisit cursor positions across tabs.
Or, use the following keyboard shortcuts:

| Platform | Back | Forward |
| -------- | ---- | ------- |
| Windows | **Alt+Left** | **Alt+Right** |
| Linux | **Ctrl+Alt+-** | **Ctrl+Shift+-** |
| macOS | **Ctrl+-** | **Ctrl+Shift+-** |

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

When the preview runs, its calculation errors join them in the **Problems** panel.
Each entry starts with `[Linter]` or `[Runtime]` to show where it came from.
This includes errors inside `#hide` blocks, which the preview omits.

See **[Linter and Diagnostics](new-linter.md)** for what each code means.

## See also

- [The CalcpadCE Panel](new-calcpad-panel.md) — the shared sidebar and all settings
- [VS Code Extension](new-vscode-extension.md) · [Desktop App](new-desktop-app.md)
- [Includes and File Reads](new-includes.md) · [Linter and Diagnostics](new-linter.md) · [Metadata Comments](new-metadata-comments.md)
- [Writing Math](writing-math.md) · [Quick Reference](quick-reference.md)
