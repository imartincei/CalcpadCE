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

## Path root tokens: `<project>` and `<library>`

`<project>` and `<library>` are symbolic roots you can use as a prefix in `#include`, `#read`, `#write`/`#append`, and `<img src="...">`, instead of a path specific to your machine:

```text
#ProjectPath = C:/Jobs/1042
#LibraryPath = %APPDATA%/Calcpad/lib
#include <library>/steel/aisc.cpd
#read L from <project>/data/loads.csv
#write R to <project>/out/results.csv
'<img src="<library>/logo.png">
```

Each root is declared once, with a directive naming the folder it points at:

- `#ProjectPath = ...` — the job- or document-specific folder, typically outside version control and different for every recipient.
- `#LibraryPath = ...` — a shared folder of reusable `.cpd`/`.txt` files, e.g. a firm-wide function or materials library.

Both directives render nothing, the same as `#include`. A relative value resolves against the folder of the file that declares it; environment variables are expanded in the value using `%VAR%` syntax — on every platform, including macOS/Linux:

```text
#LibraryPath = %HOME%/lib/calcpad
```

**Rules, kept deliberately simple:**

- **One `#ProjectPath` and one `#LibraryPath` per document.** A second declaration of the same root is an error.
- **Declare before first use.** A directive must appear above the first line that uses its token, or that line is an error — this also means the document's declared paths can be read once, up front, without worrying about them changing mid-file.
- A token whose root was never declared is an error, not a silent fallback to something else.

**Reusable modules should use `#local`.** A file meant to be `#include`d elsewhere — a shared library module, say — should wrap its own `#ProjectPath`/`#LibraryPath` in `#local`/`#global`:

```text
' lib/steel.cpd
#local
#LibraryPath = ./data
#global
#read Fy from <library>/grades.csv
```

`#local` content is stripped when the file is `#include`d into another document (the same way it always is), so the module's own declaration never reaches — and never clashes with — the including document's. Opened by itself, the module still runs: `#local` sections are only stripped when a file is reached through `#include`.

## Path root token: `<user>`

`<user>` is a third root, usable anywhere `<project>`/`<library>` are — but unlike them, it needs no `#ProjectPath`/`#LibraryPath`-style declaration first. It always expands to the current OS user's home directory: `%USERPROFILE%` on Windows, `$HOME` on macOS/Linux.

```text
#include <user>/calcpad/lib/steel.cpd
#read L from <user>/data/loads.csv
'<img src="<user>/logo.png">
```

It can also appear inside a `#ProjectPath`/`#LibraryPath` value itself:

```text
#LibraryPath = <user>/calcpad/lib
```

### Path root tokens and portable export

An [exported portable package](working-with-files.md#export-portable-package) leaves a `<project>`/`<library>` reference exactly as written by default — the recipient's own `#ProjectPath`/`#LibraryPath` resolves it, so a shared library file isn't duplicated into every package. Two checkboxes on the **Export** tab, both off by default, bundle `<project>` and `<library>` references independently instead — each resolves the token to your own local path and bundles it like any other absolute reference, for a one-off recipient who has no roots declared of their own. The Export tab also shows the document's declared `<project>`/`<library>` paths, read-only.

`<user>` has no such checkbox: a reference through it is always bundled. It always resolves — there is nothing to declare and nothing that can be left undeclared — but only to *your* home directory, and there is no reason to expect a recipient's own home directory holds the same file in the same place, the way a shared `<library>` folder is expected to.

A [compiled `.cpdz` worksheet](working-with-files.md#save-as-compiled-worksheet), by contrast, always resolves `<project>`/`<library>` (and `<user>`) references — its source is locked, so there is no way for a recipient to add a declaration afterwards.

## Errors point to the right place

> Calcpad.Web only (web editor, desktop app, and VS Code extension). Not available in the standalone WPF desktop application for Windows.

Even after several layers of includes and macro expansion, error messages and diagnostics point back to the original file and line number — so a problem in a shared file is reported where it actually lives, not at the `#include` line.

## See also

- [Working with Files](working-with-files.md) · [Programming](programming.md)
- [Using the VS Code Extension](new-vscode-extension.md) — path completion for `#include` and `#read`
