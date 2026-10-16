## 🛡️ Installation Note (Windows SmartScreen)

Because CalcpadCE is a new, community-driven project, Windows SmartScreen flags the installer as an unrecognized app.
This is normal for new open-source software.
To proceed, simply click "More info" and then "Run anyway".

You can skip the SmartScreen warning by installing CalcpadCE via [winget](https://learn.microsoft.com/en-us/windows/package-manager/winget/).
This is also the easiest way to install CalcpadCE.
Simply open a Windows terminal and run:

`winget install -e --id Imartincei.CalcpadCE`

CalcpadCE can be updated via winget after this release's PR in [microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs/pulls?q=is%3Apr+calcpadce) is merged:

`winget update calcpadce`

## Downloads

| File | OS | Arch | .NET Runtime |
| ---- | -- | ---- | ------------ |
| 🪟 **[CalcpadCE-setup-${VERSION}.exe](https://github.com/${REPO}/releases/download/v${VERSION}/CalcpadCE-setup-${VERSION}.exe)** | **Windows Installer** | **64-bit** | **Auto-installed** |
| 🪟 [CalcpadCE-portable-${VERSION}.zip](https://github.com/${REPO}/releases/download/v${VERSION}/CalcpadCE-portable-${VERSION}.zip) | Windows Portable | 64-bit | Included |
| 🐧 [CalcpadCE.${VERSION}.linux-x64.deb](https://github.com/${REPO}/releases/download/v${VERSION}/CalcpadCE.${VERSION}.linux-x64.deb) | Debian / Ubuntu | x86 | Auto-installed |
| 🐧 [CalcpadCE.${VERSION}.linux-arm64.deb](https://github.com/${REPO}/releases/download/v${VERSION}/CalcpadCE.${VERSION}.linux-arm64.deb) | Debian / Ubuntu | ARM | Auto-installed |
| 🐧 [CalcpadCE.${VERSION}.linux-x64.rpm](https://github.com/${REPO}/releases/download/v${VERSION}/CalcpadCE.${VERSION}.linux-x64.rpm) | CentOS / Fedora | x86 | Auto-installed |
| 🐧 [CalcpadCE.${VERSION}.linux-arm64.rpm](https://github.com/${REPO}/releases/download/v${VERSION}/CalcpadCE.${VERSION}.linux-arm64.rpm) | CentOS / Fedora | ARM | Auto-installed |
| 🐧 [CalcpadCE-${VERSION}-linux-x64.tar.bz2](https://github.com/${REPO}/releases/download/v${VERSION}/CalcpadCE-${VERSION}-linux-x64.tar.bz2) | Portable Linux | x86 | Included |
| 🐧 [CalcpadCE-${VERSION}-linux-arm64.tar.bz2](https://github.com/${REPO}/releases/download/v${VERSION}/CalcpadCE-${VERSION}-linux-arm64.tar.bz2) | Portable Linux | ARM | Included |
