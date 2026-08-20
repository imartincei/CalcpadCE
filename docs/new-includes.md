# Includes and File Reads

`#include` and `#read` let you pull in other files, and both can follow chains of files.

## Reusing code with `#include`

`#include` inlines another CalcpadCE file's source into your document at parse time, so you can keep shared constants, functions, and macros in one place and reuse them everywhere:

```text
#include shared/constants.cpd
#include shared/helpers.cpd
```

An included file can include others in turn, and those can include more — the chain is followed automatically.

Include chains can go up to 20 levels deep.

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

- `#ProjectPath ...` — the job- or document-specific folder, useful for referencing files that may change from project to project.
- `#LibraryPath ...` — a shared folder of reusable `.cpd`/`.txt` files, e.g. a firm-wide function or materials library. If you want a project specific library to prevent edits from breaking older files, this is easy to change in a single place.

Both directives render nothing, the same as `#include`. A relative value resolves against the folder of the file that declares it — so a module reached through `#include` that declares `#ProjectPath .` means *its own* folder, not the including document's.

**Environment variables expand in every path, not just these two directives.** `%VAR%` is expanded — on every platform, including macOS/Linux — in `#include`, `#read`/`#write`/`#append`, `<img src="...">`, and a `#ProjectPath`/`#LibraryPath` value itself, whether or not a path-root token is involved:

```text
#LibraryPath %APPDATA%/Calcpad/lib
#include %APPDATA%/Calcpad/scratch/notes.cpd
```

**Rules:**

- **One `#ProjectPath` and one `#LibraryPath` per document.** A second declaration of the same root is an error.
- **Declare before first use.** An `#include` whose token is used above its declaration results in an error
- **The folder has to actually exist.** A value that doesn't resolve to a real folder on disk is an error at the declaration

**Reusable modules should use `#local`.** A file meant to be use elsewhere should wrap its own `#ProjectPath`/`#LibraryPath` in `#local`/`#global`:

```text
#local
#LibraryPath ./data
#global
#read Fy from {library}/grades.csv
```

## `#UI` overrides and includes

A saved `uiOverrides` comment only takes effect on the **first line of the file that carries it**, and only the first such comment counts — a second one anywhere in the same file is ignored.

- **A `uiOverrides` comment inside an included file never reaches the document that includes it.** The calculation engine treats any `uiOverrides` comment in an included file's content as if it sat inside that file's own `#local`/`#global` block: it is stripped out before the included text is spliced in.
- **Only the includer's own `uiOverrides` comment can override it instead**, by targeting the rendered element after the preview is generated.

This is deliberate behavior, as resolving an included module's own saved values automatically would prevent overriding it in the main calculation file.

**To share entered values across several files, write them out as data instead of relying on `#include`:**

**module.cpd:**
```text
#local
#UI sharedData = [1; 2|3; 4]
#write sharedData to {project}/shared-inputs.csv
#global
'To read it automatically from the file calling the module:'
#read M from {project}/shared-inputs.csv
```

`#write` sits inside `#local` so it only runs when `shared/inputs.cpd` is opened by itself — [`#local` content is stripped out of an `#include`](#path-root-tokens-project-and-library), so including this file elsewhere never re-triggers the write.

**calculation.cpd:**

```text
'This reads shared data that in input from a module without triggering the UI datagrid.
#include module.cpd
```

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

## Errors point to the right place

> Calcpad.Web only (web editor, desktop app, and VS Code extension). Not available in the standalone WPF desktop application for Windows.

Even after several layers of includes and macro expansion, error messages and diagnostics point back to the original file and line number — so a problem in a shared file is reported where it actually lives, not at the `#include` line.

## See also

- [Portable Export Options](new-portable-export-options.md) · [Working with Files](working-with-files.md) · [Programming](programming.md)
- [Using the VS Code Extension](new-vscode-extension.md) — path completion for `#include` and `#read`
