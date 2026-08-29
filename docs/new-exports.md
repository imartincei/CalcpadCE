# Exports

Everything CalcpadCE writes out of a document is described here: reports as PDF, HTML or Word, the plot images a document produces, the files `#write` and `#append` write, and the two portable formats.

Every export runs through the same calculation engine as the preview, so the output matches what you see on screen.
Exported files don't carry the line numbers or error boxes the on-screen views use for navigation.

## Export variants

An export captures one of the four renderings.
A plain "export to PDF" gives you the report.

| Variant | Contents | Formats |
| --- | --- | --- |
| **Report** *(default)* | `#pre` hidden, `#post` shown, entered `#UI` values applied | PDF, HTML, Word |
| **Preview** | `#pre` and `#post` both shown, using the document's default `#UI` values. Gives the values entered into the input form when **Apply `#UI` Values in Preview** is on | PDF, HTML, Word |
| **Input form** | Form view for inserting `#UI` input values, `#post` hidden, UI overrides applied instead of the document's defaults | PDF, HTML |
| **Unwrapped** | The source with macros and `#include`s expanded | PDF, HTML |

See [UI Mode](new-ui-mode.md) for `#pre`/`#post` and the input form, and [Settings](new-settings.md) for **Apply `#UI` Values in Preview**.

A compiled `.cpdz` worksheet exports the same way a `.cpd` does.

## PDF export

PDF output is print-ready and matches the on-screen preview, with configurable page size, margins, and header/footer content.

Options come from two places, and the document takes priority:

- **Your defaults** live in the **PDF Export** section of the [Settings tab](new-settings.md#pdf-export). They apply to every document you export.
- **A document can override any of them for itself** with a `pdf` [metadata comment](new-metadata-comments.md#pdf-export-settings), written for you by the [Properties tab](new-calcpad-panel.md#properties). This is how a report that has to print A4 landscape is able to print that way on every machine.

The two are merged key by key: a document that sets only `marginTop` keeps your usual paper size and title.

### Browser requirement

PDF export renders the report using a **Chromium-based browser** (Google Chrome, Microsoft Edge, or Chromium).
The app looks for one already installed on your system and uses it if it finds one.

If it can't find one — or the one it finds won't launch — the desktop app and the VS Code extension offers to download a compatible browser.
You can either install a browser yourself (recommended if disk space is a concern) or accept a one-time download of a private headless Chromium (~180 MB) that CalcpadCE uses only for exports.
Once downloaded it is reused, so you are asked only once.
Declining just cancels that export.

On Linux, if no browser is found, the app shows you the exact package to install for your distribution:

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
- **Header and footer** — The title, the timestamp, and the page number can each be set or hidden.

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

This is part of CalcpadCE's [visibility directive system](new-visibility-directives.md).

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

The input form and unwrapped never write, and which `#UI` values (default or overrides) reach the file depends on the preview mode and settings.

## Portable exports

Beyond saving a document as `.cpd`, CalcpadCE can produce two self-contained outputs meant to be handed to someone else: a compiled worksheet, which runs anywhere but keeps its source locked, and a portable package, which stays readable and editable.

### Save As Compiled Worksheet

Compiling produces a `.cpdz` from the document you are working on.
It is a separate output rather than a rename: the file you have open keeps its own name and stays editable, so you can keep working on the `.cpd` and re-compile whenever you need a new copy to hand out.

A compiled worksheet is fully portable: everything the document  depends on (#include, #read, and images) is written into it, so it runs with nothing beside it.

#read data and images are capped at 20 MB in total (10 MB each), since it is all bundled in the .cpdz (which takes more system resources than reading from a file).
The export will fail with a warning if this limit is exceeded.

If a referenced file cannot be read, i.e. from a missing `.csv` or `#include`, the export is stopped and an error is given.

`#write` and `#append` will write next to the compiled file when it runs instead of the original write path. 
Duplicate filenames get -1, -2 appended to the filename to keep them separate.

Opening a compiled worksheet gives you the input form with the source locked.
Values you enter can still be saved back into it: in the desktop app it saves like any other file, and in VS Code a compiled worksheet opens in its own editor where **Save** writes the entered values back.

If the recipient has to read or edit the calculation rather than just fill it in, export a portable package instead.

### Export Portable Package

A portable package is the middle ground between a `.cpd`, which may rely on environment-specific paths, and a `.cpdz`, which runs anywhere but cannot be read or edited.
It is a `.zip` holding the document as text beside a folder of everything it references, with each path rewritten to reach it there:

```
calc.zip
    calc.cpd
    calc.cpd.refs/
        logo.png
        library.cpd
        loads.csv
```

Unzip it anywhere and open the `.cpd`, and it renders as it did in its original environment. This is very useful for archiving or backing up calculations that depend on library files that may change in the future.
- An `#include`d file is packed with its own references, which are rewritten as well.
- Images given as a web address or as inline data are left alone: they already resolve anywhere.
- `#write` and `#append` will write next to the `.cpd` file when it runs instead of the original write path.
- Duplicate filenames get -1, -2 appended to the filename to keep them separate.
- A `{project}`, `{library}` or `{user}` reference (see [Path root tokens](new-includes.md#path-root-tokens-project-and-library)) is resolved against your own declared roots and packed like any other reference.

If a referenced file cannot be read, i.e. from a missing `.csv` or `#include`, the export is stopped and an error is given.

## Troubleshooting

| Symptom | Fix |
|---------|-----|
| PDF export fails or times out | Install a Chromium browser (see the table above) and verify the target file is not locked. In the desktop app, **Server → Show Server Log** shows the underlying error. |
| Images missing in the PDF | Use paths the app can read; local images are embedded automatically before export. |
| A compile or portable export is refused | It names the file it could not read, the size ceiling it hit, or the unresolved `{project}`/`{library}` root. Save the document first if it is untitled. |

## See also

- [The CalcpadCE Panel](new-calcpad-panel.md#export) — where the Export tab sits
- [Settings](new-settings.md#pdf-export) — every PDF option and the **Write files** behavior
- [Metadata Comments](new-metadata-comments.md#pdf-export-settings) — per-document PDF overrides
- [UI Mode](new-ui-mode.md) · [Includes and File Reads](new-includes.md) · [Visibility Directives](new-visibility-directives.md)
- [Using the Desktop App](new-desktop-app.md) · [Using the VS Code Extension](new-vscode-extension.md)
