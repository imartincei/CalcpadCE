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

## Path root tokens: `{project}` and `{library}`

`{project}` and `{library}` are symbolic roots you can use as a prefix in `#include`, `#read`, `#write`/`#append`, and `<img src="...">`, instead of a path specific to your machine:

```text
#ProjectPath C:/Jobs/1042
#LibraryPath %APPDATA%/Calcpad/lib
#include {library}/steel/aisc.cpd
#read L from {project}/data/loads.csv
#write R to {project}/out/results.csv
'<img src="{library}/logo.png">
```

Each root is declared once, with a directive naming the folder it points at — the same bare-value syntax as `#include`, no `=`:

- `#ProjectPath ...` — the job- or document-specific folder, typically outside version control and different for every recipient.
- `#LibraryPath ...` — a shared folder of reusable `.cpd`/`.txt` files, e.g. a firm-wide function or materials library.

Both directives render nothing, the same as `#include`. A relative value resolves against the folder of the file that declares it; environment variables are expanded in the value using `%VAR%` syntax — on every platform, including macOS/Linux:

```text
#LibraryPath %HOME%/lib/calcpad
```

**Rules, kept deliberately simple:**

- **One `#ProjectPath` and one `#LibraryPath` per document.** A second declaration of the same root is an error.
- **Declare before first use.** A directive must appear above the first line that uses its token, or that line is an error — this also means the document's declared paths can be read once, up front, without worrying about them changing mid-file.
- **The folder has to actually exist.** A value that doesn't resolve to a real folder on disk is an error at the declaration, not deferred to whatever `#include`/`#read` reaches it later.
- A token whose root was never declared is an error, not a silent fallback to something else.

**Reusable modules should use `#local`.** A file meant to be `#include`d elsewhere — a shared library module, say — should wrap its own `#ProjectPath`/`#LibraryPath` in `#local`/`#global`:

```text
' lib/steel.cpd
#local
#LibraryPath ./data
#global
#read Fy from {library}/grades.csv
```

`#local` content is stripped when the file is `#include`d into another document (the same way it always is), so the module's own declaration never reaches — and never clashes with — the including document's. Opened by itself, the module still runs: `#local` sections are only stripped when a file is reached through `#include`.

## Path root token: `{user}`

`{user}` is a third root, usable anywhere `{project}`/`{library}` are — but unlike them, it needs no `#ProjectPath`/`#LibraryPath`-style declaration first. It always expands to the current OS user's home directory: `%USERPROFILE%` on Windows, `$HOME` on macOS/Linux.

```text
#include {user}/calcpad/lib/steel.cpd
#read L from {user}/data/loads.csv
'<img src="{user}/logo.png">
```

It can also appear inside a `#ProjectPath`/`#LibraryPath` value itself:

```text
#LibraryPath {user}/calcpad/lib
```

### Path root tokens and portable export

Neither export form keeps a token. An [exported portable package](working-with-files.md#export-portable-package) and a [compiled `.cpdz` worksheet](working-with-files.md#save-as-compiled-worksheet) both resolve `{project}`, `{library}` and `{user}` against *your* declared roots — the package bundling what it finds beside the document, the compiled worksheet writing it in.

The reason is the same for all three tokens: each names a folder on the machine that wrote the document, and there is no reason to expect the recipient's machine has the same file in the same place. An export that left the token in would only run where the recipient happened to have declared a matching root, which is the opposite of what either format is for. So the root has to be declared here, and its folder has to exist, or the export is refused naming the directive.

What a token *is* for is the document itself. If you want a recipient to resolve a shared library from their own folders, give them the `.cpd` and let them point `#ProjectPath`/`#LibraryPath` wherever their copy lives — one line to change, and nothing duplicated.

## Errors point to the right place

> Calcpad.Web only (web editor, desktop app, and VS Code extension). Not available in the standalone WPF desktop application for Windows.

Even after several layers of includes and macro expansion, error messages and diagnostics point back to the original file and line number — so a problem in a shared file is reported where it actually lives, not at the `#include` line.

## See also

- [Working with Files](working-with-files.md) · [Programming](programming.md)
- [Using the VS Code Extension](new-vscode-extension.md) — path completion for `#include` and `#read`
