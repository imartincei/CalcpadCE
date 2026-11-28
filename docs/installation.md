# Installation

CalcpadCE comes in two editions that share the same editor, side panel and calculation engine:

| Edition | Platforms | Download |
| ------- | --------- | -------- |
| [Desktop app](new-desktop-app.md) | Windows, Linux | Installer or portable build from the [Releases page](https://github.com/imartincei/CalcpadCE/releases/latest) |
| [VS Code extension](new-vscode-extension.md) | Windows, Linux, macOS | `.vsix` package from the [Releases page](https://github.com/imartincei/CalcpadCE/releases/latest) |

See [Selecting a CalcpadCE Deployment](new-selecting-calcpadce-deployment.md) to help decide which one suits you.

## Desktop app

On Windows, the easiest way to install the desktop app is with [winget](https://learn.microsoft.com/en-us/windows/package-manager/winget/).
Open a terminal and run:

```powershell
winget install -e --id Imartincei.CalcpadCE
```

Alternatively, download the setup program, the `.msi` installer or the portable `.zip` build from the Releases page.
Linux packages are available for Debian (`.deb`), Fedora (`.rpm`), and Arch.

See [Desktop App → Installing](new-desktop-app.md#installing) for the runtimes each package needs and how to install for one user or for everyone.

## VS Code extension

The extension runs in Visual Studio Code or VS Codium on any platform VS Code supports, including macOS.
It needs the .NET 10 runtime, which VS Code offers to install if it is missing.

See [VS Code Extension → Installing the extension](new-vscode-extension.md#installing-the-extension) for the steps.

## PDF export

Both editions render PDF exports with a **Chromium-based browser** (Chrome, Edge or Chromium).
If none is found, CalcpadCE offers to download one that it uses only for exports.
See [Exports → Browser requirement](new-exports.md#browser-requirement).

## Other editors

Syntax highlighting definitions for [Notepad++, Sublime Text and CudaText](https://github.com/imartincei/CalcpadCE/tree/main/Setup/Syntax-for-external-editors) are also available.
They only color the code, so you still need one of the editions above to calculate and preview the results.
