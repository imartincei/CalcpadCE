# Desktop app testing

Runs an isolated copy of the CalcpadCE desktop app (Tauri) where nothing shows on the developer's screen, and drives it from scripts. The developer can keep working while an agent exercises menus, native dialogs and server failures.

## How it works

- **Hidden desktop.** `deskrun` creates a second Win32 desktop (`WinSta0\CalcpadTest`) and starts processes on it. Windows on that desktop are never shown and can't take focus. Every process it starts, including the server sidecar and WebView2, inherits it.
- **Isolated app copy.** `build.ps1` builds the app with identifier `com.calcpadce.desktop.test` (via `TAURI_CONFIG`) and assembles it with the server in `.work\app`. That gives the copy its own app data folder (`%APPDATA%\com.calcpadce.desktop.test`) and its own single-instance lock. Without this it would share open tabs, drafts and settings with the developer's CalcpadCE, and launching theirs could be forwarded into the hidden one.
- **Webview control.** `launch.ps1` sets `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=9223`, which exposes Chrome DevTools Protocol. `cdp.mjs` evaluates JS in the page, fires native menu items, and reads the Output panel.
- **Native dialogs.** Tauri's dialogs are Win32 TaskDialogs and file pickers, which JS can't reach. `ui.ps1` reads and clicks them with UI Automation. UI Automation only sees its own desktop, so `ui.ps1` re-runs itself on the hidden desktop through `deskrun`.
- **Configuration.** `-Settings <file>` gives the server a different `appsettings.json`. `-Env @{...}` sets environment variables for the app and its server, for example `BROWSER_PATH` to pick the PDF browser.

## Usage

```powershell
cd tools\desktop-test
.\build.ps1 -Stage -Frontend      # first time, or after backend (-Stage) / TS (-Frontend) changes; Rust always rebuilds
.\launch.ps1 [-Settings path\to\appsettings.json] [-Env @{ NAME = 'value' }]
.\launch.ps1 -Stop
```

| Command | Does |
|---|---|
| `node cdp.mjs menu <id>` | Fires a native menu item. The ids are in `calcpad-desktop/src-tauri/src/lib.rs`, for example `export-pdf`, `restart-server` and `stop-server`. |
| `node cdp.mjs eval "<js>"` / `eval-file <file>` | Runs JS in the main window, e.g. `monaco.editor.getModels()[0].setValue('a = 1')`. |
| `node cdp.mjs output` | Shows the server status and the Output panel. |
| `.\ui.ps1 waitdialog <sec>` | Waits for a native dialog, then prints its title, text and buttons. |
| `.\ui.ps1 list` / `tree` | Lists the windows and dialogs, or dumps every element in each dialog. |
| `.\ui.ps1 click <label>` | Clicks a dialog button, e.g. OK, Retry or Cancel. |

Rust-side `eprintln!` output goes to `.work\app-out.txt`. Crash reports go to `%APPDATA%\com.calcpadce.desktop.test\logs`.

## Window regression tests

Run the window tests on Windows:

```powershell
.\window-tests.ps1
```

Use `-Stage` after backend changes, or `-SkipBuild` to reuse the test build.
The tests cover repeated launches, file routing, titles, independent window closing, backend recovery, session saving, and window geometry.
Focus notifications and unsaved-change prompt responses are simulated.

## Linux regression tests

Run the GTK tests on Linux or inside WSL with Linux-installed Node.js, Rust, and .NET.
Install the desktop build dependencies and these additional test tools on Ubuntu:

```bash
sudo apt install webkit2gtk-driver xvfb openbox xdotool x11-utils dbus-x11 sway wtype python3
cargo install tauri-driver --version 2.0.6 --locked
```

From the repository root:

```bash
bash tools/desktop-test/linux-tests.sh
```

Use `--skip-build` to reuse the test build.
Tests use an isolated application profile and a separate display.
They exercise real pointer dragging, tab reordering, cancellation, split groups, and transfers between GTK windows.
Navigation tests cover keyboard shortcuts, mouse side buttons, previews, selections, tabs, split groups, and windows.
Run `GDK_SCALE=2 bash tools/desktop-test/linux-tests.sh --skip-build` to check display scaling.
Run `bash tools/desktop-test/linux-tests.sh --wayland --skip-build` to test native Wayland.
Logs and test diagnostics are saved under `tools/desktop-test/.work/linux-tests/`.
Linux CI tests X11 and Wayland on both supported architectures.

## Windows gotchas

- **No screenshots.** A hidden desktop is never painted, so `CopyFromScreen` returns nothing. Use `ui.ps1` and `cdp.mjs` instead.
- **Keep the desktop alive.** The desktop is destroyed when its last handle closes. A process started there after that dies at startup with no output. `launch.ps1` keeps `deskrun` waiting on the app for this reason.
- **Clicking TaskDialog buttons.** The buttons are `CCPushButton` panes, not Buttons. `ui.ps1 click` posts `BM_CLICK` to them; `InvokePattern.Invoke` can block on a modal that is closing.
- **CDP while a dialog is open.** CDP evaluation can stall while a native modal is open. Dismiss the dialog first. `cdp.mjs` gives up after 20 s.
- **Empty documents.** Export and preview fail early on an empty document, so set some content first.
- **Stopping processes.** `launch.ps1 -Stop` only stops processes running from `.work\`, plus the PDF browser the test server started. It never touches the developer's own CalcpadCE or its PDF browser.
- **Platform.** Windows only.
