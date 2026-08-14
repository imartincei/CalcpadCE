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

Both directives render nothing, the same as `#include`. A relative value resolves against the folder of the file that declares it — so a module reached through `#include` that declares `#ProjectPath .` means *its own* folder, not the including document's.

**Environment variables expand in every path, not just these two directives.** `%VAR%` is expanded — on every platform, including macOS/Linux — in `#include`, `#read`/`#write`/`#append`, `<img src="...">`, and a `#ProjectPath`/`#LibraryPath` value itself, whether or not a path-root token is involved:

```text
#LibraryPath %APPDATA%/Calcpad/lib
#include %APPDATA%/Calcpad/scratch/notes.cpd
```

These are your OS's own environment variables — CalcpadCE doesn't define any, so make sure the one you reference is actually set. `%VAR%` left unmatched (nothing by that name is set) is not expanded and not an error by itself — it just becomes part of a path that then has to resolve, so an unset variable usually surfaces as a plain "file/folder not found" instead.

**Rules, kept deliberately simple:**

- **One `#ProjectPath` and one `#LibraryPath` per document.** A second declaration of the same root is an error.
- **Declare before first use.** An `#include` whose token is used above its declaration is an error, and since includes are expanded before anything else runs, that is where the rule bites. `#read`\`#write` and `<img src>` are resolved after expansion, against the roots the whole document declared, so they are not order-sensitive — but write the declaration first anyway: it is the one order that reads the same as it resolves.
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

## `#UI` overrides and includes

> Calcpad.Web only. See [Saving what was entered](new-ui-mode.md#saving-what-was-entered) for the `uiOverrides` comment itself.

A saved `uiOverrides` comment only takes effect on the **first line of the file that carries it**, and only the first such comment counts — a second one anywhere in the same file is ignored. Both rules are enforced, not just documented:

- **A `uiOverrides` comment inside an included file never reaches the document that includes it.** The host restores saved values by scanning the file *you have open*, before `#include` is expanded, so it never looks inside a file an `#include` brings in. On top of that, the server treats any `uiOverrides` comment in an included file's content as if it sat inside that file's own `#local`/`#global` block: it is stripped out before the included text is spliced in, whether or not the file's author wrapped it in `#local` themselves. A `#UI` control that lives in an included file simply renders with whatever default the source itself declares.
- **Only the includer's own `uiOverrides` comment can override it instead**, by targeting the control's `name:ordinal` key — the same key any `#UI` control gets, whether it was written in the main file or pulled in through an `#include`.
- The [linter](new-linter.md) warns about a `uiOverrides` comment that will not be the one read back: `CPD-3416` when it isn't on the first line, `CPD-3417` when a valid one on the first line is followed by another later in the file, and `CPD-3418` when it shares its comment with another key (`desc`, `pdf`, and so on) — keep `uiOverrides` in a comment by itself so an edit to the other key can't reshape it unpredictably.

This is deliberate, not a gap to close: resolving an included module's own saved values automatically would mean every document that includes it inherits whatever was last typed into the module when it was opened on its own — a hidden coupling between unrelated documents that would be worse than the control just falling back to its declared default.

**To share entered values across several files, write them out as data instead of relying on `#UI`:**

```text
' shared/inputs.cpd — run this file on its own after entering values in its form
#local
shared = [L; q]
#write shared to {project}/shared-inputs.csv
#global
```

`#write` sits inside `#local` so it only runs when `shared/inputs.cpd` is opened by itself — [`#local` content is stripped out of an `#include`](#path-root-tokens-project-and-library), so including this file elsewhere never re-triggers the write.

```text
' consumer.cpd
#read shared from {project}/shared-inputs.csv
#UI L = shared[1]
#UI q = shared[2]
```

Each consuming file `#read`s the numbers back and declares its own `#UI` controls around them, styled and labeled however that file wants. If you don't need `#UI` in the file doing the reading — you only want the numbers — a plain `#read` of the data file is all that's needed, without wrapping anything in `#local`/`#write` at all.

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

Neither export form keeps a token. An [exported portable package](new-portable-export-options.md#export-portable-package) and a [compiled `.cpdz` worksheet](new-portable-export-options.md#save-as-compiled-worksheet) both resolve `{project}`, `{library}` and `{user}` against *your* declared roots — the package bundling what it finds beside the document, the compiled worksheet writing it in.

The reason is the same for all three tokens: each names a folder on the machine that wrote the document, and there is no reason to expect the recipient's machine has the same file in the same place. An export that left the token in would only run where the recipient happened to have declared a matching root, which is the opposite of what either format is for. So the root has to be declared here, and its folder has to exist, or the export is refused naming the directive.

What a token *is* for is the document itself. If you want a recipient to resolve a shared library from their own folders, give them the `.cpd` and let them point `#ProjectPath`/`#LibraryPath` wherever their copy lives — one line to change, and nothing duplicated.

A `#write`/`#append` target counts as absolute for this purpose, the same as a rooted path, so it collapses to a bare filename rather than being refused: `#write R to {project}/out/results.csv` becomes `#write R to results.csv` in either export.

## Errors point to the right place

> Calcpad.Web only (web editor, desktop app, and VS Code extension). Not available in the standalone WPF desktop application for Windows.

Even after several layers of includes and macro expansion, error messages and diagnostics point back to the original file and line number — so a problem in a shared file is reported where it actually lives, not at the `#include` line.

## See also

- [Portable Export Options](new-portable-export-options.md) · [Working with Files](working-with-files.md) · [Programming](programming.md)
- [Using the VS Code Extension](new-vscode-extension.md) — path completion for `#include` and `#read`
