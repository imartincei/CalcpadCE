# PDF Export

> Calcpad.Web only (web editor, desktop app, and VS Code extension). Not available in the standalone WPF desktop application for Windows.

Calcpad.Web can export your report to a print-ready PDF that matches the on-screen preview, with configurable page size, margins, and header/footer content.

Options come from two places, and the document wins:

- **Your defaults** live in the **PDF Export** section of the sidebar's [**Settings** tab](new-settings.md). They apply to every document you export.
- **A document can override any of them for itself** with a `pdf` [metadata comment](new-metadata-comments.md#pdf-export-settings), written for you by the **Properties** tab. This is how a report that has to print A4 landscape says so, once, and prints that way on every machine.

The two are merged key by key: a document that sets only `marginTop` keeps your usual paper size and title.

Then export:

- **Desktop app** — **File → Export PDF…**
- **VS Code** — *CalcpadCE: Export to PDF*
- **Web editor** — **Report → Save PDF…** on the sidebar's **Export** tab

## Browser requirement

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
| Arch / CachyOS / Manjaro / EndeavourOS / Garuda | `sudo pacman -S chromium` or `yay -S ungoogled-chromium-bin` |
| Debian / Ubuntu / Mint | `sudo apt install chromium` |
| Fedora / RHEL / Rocky / Alma | `sudo dnf install chromium` |
| openSUSE | `sudo zypper install chromium` |
| Alpine | `sudo apk add chromium` |
| macOS | `brew install --cask google-chrome` |
| Windows | Install Microsoft Edge or Google Chrome |

## Page setup

- **Paper size** — Letter, Legal, Tabloid, Ledger, or A0–A6.
- **Orientation** — portrait or landscape.
- **Margins** — set each edge independently. A unit is required: `2cm`, `1.5cm`, `0.5in`, `12mm`.
- **Header and footer** — always drawn. What goes in them is up to you: the title, the timestamp, and the page number can each be set or hidden, and anything you leave blank simply doesn't appear.

Background colors and images are always printed.

## Excluding sections from the PDF

Wrap sections you want visible on screen but omitted from the PDF in `#pre` … `#end pre`:

```text
#pre
'These lines are visible in the preview but excluded from the PDF.
debug_x = 5
debug_y = debug_x + 1
#end pre
'This prints!'
```

This is part of Calcpad's [visibility directive system](new-syntax.md), so it also takes an optional condition and nests properly.
The older `NoPrintStart` / `NoPrintEnd` comment markers no longer exist.

## Options reference

Every option you can set for a PDF export, on the **Settings** tab or in a document's `pdf` [metadata comment](new-metadata-comments.md#pdf-export-settings):

| Option | Default | Purpose |
|--------|---------|---------|
| `format` | `Letter` | Paper size — Letter, Legal, Tabloid, Ledger, A0–A6 |
| `orientation` | `portrait` | `portrait` or `landscape` |
| `marginTop` | `0.75in` | Top margin |
| `marginRight` | `0.5in` | Right margin |
| `marginBottom` | `0.75in` | Bottom margin |
| `marginLeft` | `0.5in` | Left margin |
| `showPageNumbers` | `true` | "Page *n* of *m*" in the footer |
| `showDate` | `true` | The timestamp in the header |
| `documentTitle` | file name | Title (header, bold) |
| `dateTimeFormat` | `M/d/yyyy h:mm tt` | [.NET date/time format string](https://learn.microsoft.com/en-us/dotnet/standard/base-types/custom-date-and-time-format-strings) for the timestamp |

## Troubleshooting

| Symptom | Fix |
|---------|-----|
| Export fails or times out | Install a Chromium browser (see the table above). In the desktop app, **Server → Show Server Log** shows the underlying error. |
| Images missing in the PDF | Use paths the app can read; local images are embedded automatically before export. |
| A debug section appears in the PDF | Wrap it in `#pre` … `#end pre`. |
| A page setting is ignored | Check for a `pdf` metadata comment in the document — it overrides the Settings tab. The linter flags an unrecognized key or a margin written without a unit. |

## See also

- [Using the Desktop App](new-desktop-app.md) · [Using the VS Code Extension](new-vscode-extension.md)
- [The CalcpadCE Panel & Settings](new-calcpad-panel.md)
- [Metadata Comments](new-metadata-comments.md#pdf-export-settings) — per-document page setup
