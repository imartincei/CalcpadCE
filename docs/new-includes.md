# Includes and File Reads

`#include` and `#read` let you pull in other files, and both can follow chains of files.

## Reusing code with `#include`

`#include` inlines another CalcpadCE file's source into your document at parse time, so you can keep shared constants, functions, and macros in one place and reuse them everywhere:

```text
' top.cpd
#include shared/constants.cpd
#include shared/helpers.cpd
```

An included file can include others in turn, and those can include more — the chain is followed automatically.

- **Circular includes are safe.** If a file ends up including itself (directly or through another file), the repeat is skipped instead of looping forever. Filenames are matched case-insensitively.
- **There's a depth limit.** Include chains can go up to 20 levels deep; beyond that, the include is skipped and a comment is left in its place noting the file that couldn't be included.

## `#include` vs `#read`

Both bring in outside content, but they do different jobs:

| | `#include` | `#read` |
|--------|-----------|---------|
| What it brings in | CalcpadCE source code | Data (CSV, TSV, Excel, JSON) |
| When it happens | At parse time — the source is inlined | At run time — the data is loaded into a variable |
| Result | The included code becomes part of your document | You get a matrix or vector variable to compute with |

## Errors point to the right place

> Calcpad.Web only (web editor, desktop app, and VS Code extension). Not available in the standalone WPF desktop application for Windows.

Even after several layers of includes and macro expansion, error messages and diagnostics point back to the original file and line number — so a problem in a shared file is reported where it actually lives, not at the `#include` line.

## See also

- [Working with Files](working-with-files.md) · [Programming](programming.md)
- [Using the VS Code Extension](new-vscode-extension.md) — path completion for `#include` and `#read`
