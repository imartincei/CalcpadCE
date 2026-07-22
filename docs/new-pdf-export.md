# PDF Export

> Calcpad.Web only (web editor, desktop app, and VS Code extension). Not available in the standalone WPF desktop application for Windows.

Calcpad.Web can export your report to a print-ready PDF that matches the on-screen preview, with configurable page size, margins, and optional headers, footers, and a letterhead background.

Set the options below in the sidebar's **PDF** tab, then export:

- **Desktop app** — **File → Export PDF…**
- **VS Code** — *CalcPad: Export to PDF*
- **Web editor** — the PDF button on the sidebar's **PDF** tab

## Browser requirement

PDF export renders the report using a **Chromium-based browser** (Google Chrome, Microsoft Edge, or Chromium). The app looks for one already installed on your system; if it can't find one, it downloads a minimal headless build automatically the first time you export.

On Linux, if no browser is found the app shows you the exact package to install for your distribution:

| Distribution | Install command |
|--------------|-----------------|
| Arch / CachyOS / Manjaro / EndeavourOS / Garuda | `yay -S ungoogled-chromium-bin` (or `sudo pacman -S chromium`) |
| Debian / Ubuntu / Mint | `sudo apt install chromium` |
| Fedora / RHEL / Rocky / Alma | `sudo dnf install chromium` |
| openSUSE | `sudo zypper install chromium` |
| Alpine | `sudo apk add chromium` |
| macOS | `brew install --cask google-chrome` |
| Windows | install Microsoft Edge or Google Chrome |

## Page setup

- **Paper size** — Letter, Legal, Tabloid, Ledger, or A0–A6. Default is A4.
- **Orientation** — portrait (default) or landscape.
- **Margins** — set each edge independently, using values like `2cm`, `1.5cm`, or `0.5in`.
- **Scale** — a zoom factor from 0.1 to 2.0 for shrinking or enlarging the content.
- **Background** — colors and background images are printed by default.

## Excluding sections from the PDF (NoPrint)

Wrap sections you want visible on screen but omitted from the PDF in `NoPrintStart` / `NoPrintEnd` markers:

```text
'<!--{"NoPrintStart": true}-->
'These lines are visible in the preview but stripped from the PDF.
debug_x = 5
debug_y = debug_x + 1
'<!--{"NoPrintEnd": true}-->
'This prints!'
```

Good to know:

- The marker lines themselves are removed too.
- Regions can be nested; the outermost pair wins.
- A `NoPrintStart` without a matching `NoPrintEnd` strips everything through the end of the file.
- Property names are matched case-insensitively, and the marker's value doesn't matter — only that the property is present.
- These sections stay visible in the live preview; they're only removed from PDF (and other print) output.
- These markers are one kind of [metadata comment](new-metadata-comments.md); the **Properties** panel tab can insert them for you.

This uses the same comment-marker syntax as [`LintIgnore`](new-linter.md#suppressing-diagnostics-lint-ignore) and per-file settings.

## Options reference

Every option you can set for a PDF export:

| Option | Default | Purpose |
|--------|---------|---------|
| `format` | `Letter` | Paper size |
| `marginTop` | `2cm` | Top margin |
| `marginRight` | `1.5cm` | Right margin |
| `marginBottom` | `2cm` | Bottom margin |
| `marginLeft` | `1.5cm` | Left margin |
| `documentTitle` | — | Title (header, bold) |
| `dateTimeFormat` | — | .NET date/time format string for the timestamp |

## Troubleshooting

| Symptom | Fix |
|---------|-----|
| Export fails or times out | Install a Chromium browser (see the table above). In the desktop app, **Server → Show Server Log** shows the underlying error. |
| Images missing in the PDF | Use paths the app can read; local images are embedded automatically before export. |
| A debug section appears in the PDF | Wrap it in `NoPrintStart` / `NoPrintEnd` markers. |

## See also

- [Using the Desktop App](new-desktop-app.md) · [Using the VS Code Extension](new-vscode-extension.md)
- [The CalcPad Panel & Settings](new-calcpad-panel-and-settings.md)
