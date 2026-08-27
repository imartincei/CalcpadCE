# Exports

Everything CalcpadCE writes out of a document is described here: reports as PDF, HTML or Word, the plot images a document produces, the files `#write` and `#append` write, and the two portable formats meant to be handed to someone else.

Every export runs through the same calculation engine as the preview, so the output matches what you see on screen.
No exported file carries the line numbers or error boxes the on-screen views use for navigation.

## Where exports live

The **Export** tab of the [CalcpadCE panel](new-calcpad-panel.md#export) holds every export in every host, grouped into **HTML / PDF / Word**, **Write / Append**, **Plots**, and **Portable Exports**.
Each host adds its own shortcuts to the same actions:

| Host | Also available from |
| --- | --- |
| **Desktop app** | **File → Export ▸** for each rendering; **File → Save As Compiled Worksheet…** and **File → Export Portable Package…** for the portable formats. Each opens a native save dialog. |
| **VS Code** | **Export CalcpadCE to PDF** in the editor toolbar; **CalcpadCE: Print Report to PDF** as a title-bar button on the report and input-form panels; *CalcpadCE: Save Source HTML…*, *CalcpadCE: Save as Word Document…*, *CalcpadCE: Save As Compiled Worksheet…* and *CalcpadCE: Export Portable Package…* in the Command Palette. |

## Export variants

An export captures one of the four renderings, and **the report is the default** — a plain "export to PDF" gives you the report.

| Variant | Contents | Formats |
| --- | --- | --- |
| **Report** *(default)* | `#pre` hidden, `#post` shown, entered `#UI` values applied | PDF, HTML, Word |
| **Preview** | `#pre` and `#post` both shown, using the document's own `#UI` values — or the values entered into the input form when **Apply `#UI` Values in Preview** is on | PDF, HTML, Word |
| **Input form** | The `#UI` form itself, `#post` hidden, UI overrides applied instead of the document's defaults | PDF, HTML |
| **Unwrapped** | The source with macros and `#include`s expanded | PDF, HTML |

See [UI Mode](new-ui-mode.md) for `#pre`/`#post` and the input form, and [Settings](new-settings.md) for **Apply `#UI` Values in Preview**.

A compiled `.cpdz` worksheet exports the same way a `.cpd` does — the report and every other variant render from the values entered into its form, with the source staying hidden throughout.

## PDF export

PDF output is print-ready and matches the on-screen preview, with configurable page size, margins, and header/footer content.

Options come from two places, and the document takes priority:

- **Your defaults** live in the **PDF Export** section of the [Settings tab](new-settings.md#pdf-export). They apply to every document you export.
- **A document can override any of them for itself** with a `pdf` [metadata comment](new-metadata-comments.md#pdf-export-settings), written for you by the [Properties tab](new-calcpad-panel.md#properties). This is how a report that has to print A4 landscape says so, once, and prints that way on every machine.

The two are merged key by key: a document that sets only `marginTop` keeps your usual paper size and title.

### Browser requirement

PDF export renders the report using a **Chromium-based browser** (Google Chrome, Microsoft Edge, or Chromium).
The app looks for one already installed on your system and uses it if it finds one.

If it can't find one — or the one it finds won't launch — the desktop app and the VS Code extension **ask you first**, rather than downloading anything on their own.
You can either install a browser yourself (recommended, and the dialog shows the exact command for your system) or accept a one-time download of a private headless Chromium (~180 MB) that CalcpadCE uses only for exports.
Once downloaded it is reused, so you are asked only once.
Declining just cancels that export.

Server deployments with no one at the keyboard can opt into the automatic download by setting `AllowChromiumDownload: true` in `appsettings.json` or `ALLOW_CHROMIUM_DOWNLOAD=true` in the environment.

On Linux, if no browser is found the app shows you the exact package to install for your distribution:

| Distribution | Install command |
|--------------|-----------------|
| Arch / CachyOS / Manjaro / EndeavourOS / Garuda | `sudo pacman -S chromium` |
| Debian / Ubuntu / Mint | `sudo apt install chromium` |
| Fedora / RHEL / Rocky / Alma | `sudo dnf install chromium` |
| openSUSE | `sudo zypper install chromium` |
| Alpine | `sudo apk add chromium` |
| macOS | `brew install --cask google-chrome` |
| Windows | Install Microsoft Edge or Google Chrome |

### Page setup

- **Paper size** — Letter, Legal, Tabloid, Ledger, or A0–A6.
- **Orientation** — portrait or landscape.
- **Margins** — set each edge independently. A unit is required: `2cm`, `1.5cm`, `0.5in`, `12mm`.
- **Header and footer** — always drawn. What goes in them is up to you: the title, the timestamp, and the page number can each be set or hidden, and anything you leave blank simply doesn't appear.

Background colors and images are always printed.
Every option, with its accepted values, is listed under [Settings → PDF Export](new-settings.md#pdf-export).

### Excluding sections from the PDF

Wrap sections you want visible on screen but omitted from the report PDF in `#pre` … `#end pre`:

```text
#pre
'These lines are visible in the preview but excluded from the report PDF.
x = 5
y = x + 1
#end pre
'This prints!'
```

This is part of CalcpadCE's [visibility directive system](new-visibility-directives.md), so it also takes an optional condition and nests properly.

## Plots

The **Plots** section of the Export tab lists every plot the document emits, each with a thumbnail, filename, and size:

| Button | Result |
| --- | --- |
| **Refresh** | Re-runs the document and re-lists plots. Triggered automatically by a manual **Run Preview**. |
| **Save…** (per plot) | Writes that plot to disk in its native format (PNG or SVG, depending on the **Vector Graphics** setting). |
| **Download all (ZIP)** | Bundles every plot in one archive. |

## Write / append files

The **Write / Append** section controls the document's own file output:

| Control | Result |
| --- | --- |
| **Write files** | When `#write`/`#append` run: *Preview and Report*, *Report Only* (default), or *Manual*. |
| **Write to Disk** | Runs the document as a report and writes its `#write`/`#append` files, whatever the setting above says. Being a report, it writes the values entered into the input form. |

The input form and Unwrapped never write, and which `#UI` values reach the file depends on the render — see [Settings → Data output](new-settings.md#data-output).

## Portable exports

Beyond saving a document as `.cpd`, CalcpadCE can produce two self-contained outputs meant to be handed to someone else: a compiled worksheet, which runs anywhere but keeps its source locked, and a portable package, which stays readable and editable.

Both are under **Portable Exports** on the Export tab, and both are also in the host menus listed under [Where exports live](#where-exports-live).

### Save As Compiled Worksheet…

Compiling produces a `.cpdz` from the document you are working on.
It is a separate output rather than a rename: the file you have open keeps its own name and stays editable, so you can keep working on the `.cpd` and re-compile whenever you need a new copy to hand out.

A compiled worksheet is fully portable: everything the document depends on is written into it, so it runs with nothing beside it.

* `#include`d files are expanded in place, and macros defined with `#def` are applied — the compiled file has neither.
* Every `#read` is given the file it names to carry, in place of the path: `#read M from table.csv` becomes `#read M from data:text/csv;base64,MSwzCjIsNAo=`. It stays a read, so everything else about it stays too — the type, the separator, the sheet and the range are untouched, and the compiled worksheet reads them exactly as the original did. An Excel workbook is carried the same way, as its own bytes. What a compiled worksheet may carry is capped at 10 MB, per file and in total, since it is all held in memory to be run; past that the compile stops and says so.
* Images referenced by a relative path are embedded as data, including those referenced by an included file. The images one worksheet may carry total 10 MB, the same ceiling and for the same reason as its `#read` data; past that the compile stops and names the image it stopped on. Nothing is dropped silently — a compiled worksheet is the only copy its recipient has.
* A `{project}`/`{library}` reference (see [Path root tokens](new-includes.md#path-root-tokens-project-and-library)) is resolved to your own local path, for every reference kind — a compiled worksheet's source is locked, so there is no way for whoever opens it to add a `#ProjectPath`/`#LibraryPath` of their own.

If a referenced file cannot be read — a missing `.csv`, an `#include` that does not resolve — compiling stops and reports it, rather than writing a worksheet that fails for whoever receives it.
An unsaved document has no folder for relative paths to resolve against, so save it before compiling.

`#write` and `#append` are outputs, not dependencies, so a relative target is left as it is and still writes next to the compiled file when it runs.
An absolute target, however, points at a folder that may not exist on whoever runs the compiled file — so it is rewritten to its bare filename, landing beside the compiled file the same way a relative one does.

Opening a compiled worksheet gives you the input form with the source locked — that is what the format is for.
Values you enter can still be saved back into it: in the desktop app it saves like any other file, and in VS Code a compiled worksheet opens in its own editor where **Save** writes the entered values back.

If the recipient has to read or edit the calculation rather than just fill it in, export a portable package instead.

### Export Portable Package…

A portable package is the middle ground between a `.cpd`, which only runs on the machine it was written on, and a `.cpdz`, which runs anywhere but cannot be read.
It is a `.zip` holding the document as text beside a folder of everything it references, with each path rewritten to reach it there:

```
calc.zip
    calc.cpd
    calc.cpd.refs/
        logo.png
        library.cpd
        loads.csv
```

Unzip it anywhere and open the `.cpd`: it renders as it did for its author, and it is still a document — readable, editable, and re-exportable.

* `#include` stays an `#include`, `#read` stays a `#read` and images stay images. Only their paths change.
* An `#include`d file is packed with its own references, which are rewritten as well.
* Images given as a web address or as inline data are left alone: they already resolve anywhere.
* `#write` and `#append` are outputs, not dependencies, so a relative target is left alone for the same reason as when compiling. An absolute target is rewritten to its bare filename, so the output lands beside wherever the package is unpacked.
* A `{project}`, `{library}` or `{user}` reference (see [Path root tokens](new-includes.md#path-root-tokens-project-and-library)) is resolved against your own declared roots and packed like any other reference. The token names a folder there is no reason to expect the recipient has, so a package that still depended on one would not be portable — which means the root has to be declared, and its folder has to exist, or the export is refused naming the directive. If what you actually want is for the recipient to resolve a shared library from *their* own folders, hand them the `.cpd` itself and have them point `#ProjectPath`/`#LibraryPath` at it; that is a document to be shared, not a package to be unpacked.

The folder is flat, so if two referenced files share a name — however different their folders are, and however deep in the `#include`s they sit — the second and any further one are renamed `name-1.ext`, `name-2.ext` and so on, and every path that pointed at them is rewritten to match.
A reference that cannot be read stops the export, and lists every one.
Two `#write`/`#append` targets that would collapse onto the same filename once rewritten next to the worksheet stop it too, naming both — rename one.
An unsaved document has no folder for relative paths to resolve against, so save it first.

One thing to know when reading the packaged files: a path inside an `#include`d file is written relative to the *document*, not to the included file, because `#include`s are expanded into the document before anything resolves.
That is where the original resolved it from too.

## Troubleshooting

| Symptom | Fix |
|---------|-----|
| PDF export fails or times out | Install a Chromium browser (see the table above) and verify the target file is not locked. In the desktop app, **Server → Show Server Log** shows the underlying error. |
| Images missing in the PDF | Use paths the app can read; local images are embedded automatically before export. |
| A debug section appears in the PDF | Wrap it in `#pre` … `#end pre`. |
| A page setting is ignored | Check for a `pdf` metadata comment in the document — it overrides the Settings tab. The linter flags an unrecognized key or a margin written without a unit. |
| A compile or portable export is refused | It names the file it could not read, the size ceiling it hit, or the unresolved `{project}`/`{library}` root. Save the document first if it is untitled. |

## See also

- [The CalcpadCE Panel](new-calcpad-panel.md#export) — where the Export tab sits
- [Settings](new-settings.md#pdf-export) — every PDF option and the **Write files** behavior
- [Metadata Comments](new-metadata-comments.md#pdf-export-settings) — per-document PDF overrides
- [UI Mode](new-ui-mode.md) · [Includes and File Reads](new-includes.md) · [Visibility Directives](new-visibility-directives.md)
- [Using the Desktop App](new-desktop-app.md) · [Using the VS Code Extension](new-vscode-extension.md)
