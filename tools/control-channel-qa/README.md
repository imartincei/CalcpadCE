# Control-channel QA suite (`calcpadctl`)

This directory is the **test suite for the desktop control channel**:
`calcpadctl` / `calcpad-desktop --ctl`. It drives a running desktop app through
the control socket and asserts the JSON responses and the files the channel
writes.

It is a test suite, not a feature. Its question is:

> Does the control channel correctly expose the file, editor, menu, window,
> export and bridge operations that the running app already performs?

## What it is not

- **Not the calculation-engine test suite.** Math, units, solver and parser
  correctness belong to `Calcpad.Tests`; this suite uses a few small fixtures
  only to confirm that the channel reaches the engine.
- **Not the desktop UI harness.** Window/GTK/WebDriver behavior belongs to
  `tools/desktop-test`; this suite asserts the control channel's contract, not
  pixel layout or native window-manager behavior.
- **Not a replacement for either of the above.** It is the integration layer
  between a script/AI agent and the live app.

## What it verifies

| Area                     | Checks                                                                                                                                   |
| ------------------------ | ---------------------------------------------------------------------------------------------------------------------------------------- |
| Socket/runtime           | `ping`, `windows`, per-window readiness and targeting, `--window` routing                                                                |
| Files                    | open, content, set-content (file/stdin), save, save-as, reload, close, dirty guards, `--force`, paths with spaces/Unicode, large content |
| Exports                  | HTML/PDF/Word variants, compiled `.cpdz`, portable `.zip`, plot list/save/ZIP, `#write` files                                            |
| Calculation reachability | units, matrices, functions, plots, `#include`, error reporting, complex mode via settings                                                |
| Editor                   | cursor, selection, insert, undo/redo, find, replace-all, read-only guards                                                                |
| Menus                    | new, close, recent, toggles, split/unsplit, result modes, insert, settings entry, server log                                             |
| Bridge messages          | settings, variables, UI controls/overrides, headings, prettify, PDF settings, directory listing, metadata                                |
| Windows/clipboard        | hide/show/minimize/maximize, second-window create/destroy, clipboard read/write, menu copy/cut/paste/select-all                          |
| Lifecycle                | sidecar stop/restart, screenshot, session/log plumbing                                                                                   |
| Errors                   | missing app/socket, invalid JSON, missing arguments, unknown commands/formats, unwritable output, missing files, dirty guards            |

## What it deliberately does not cover

- Native file/image/folder dialogs (the channel exposes path-based commands
  instead, so it can bypass those dialogs without testing the dialogs).
- Drag-and-drop and drag-resize gestures.
- Pixel/layout regression.
- A negative run of the `eval` gate (the gate is checked manually; the suite
  runs with `CALCPAD_CONTROL_EVAL=1`).
- Windows, because the channel is Unix-only in this change.

## Relationship to the other suites

| Suite                             | Owns                                                       |
| --------------------------------- | ---------------------------------------------------------- |
| `Calcpad.Tests`                   | Calculation engine, parser, units, math                    |
| `tools/desktop-test`              | WebDriver/GTK desktop UI behavior, in an isolated instance |
| `tools/control-channel-qa` (this) | `calcpadctl`/`--ctl` contract against a running app        |

## Safety

The suite is destructive inside the profile it targets: it opens and
force-closes tabs, edits and saves fixtures, changes and restores settings,
stops/restarts the sidecar and creates/destroys a second window. Run it only
against a disposable instance, never the real editing session.

## Prerequisites

- Linux with the desktop app built and running.
- The app must be started with `CALCPAD_CONTROL_EVAL=1` for the `eval` helper
  used by a few checks.
- An isolated `XDG_RUNTIME_DIR`, `XDG_DATA_HOME`, `XDG_CONFIG_HOME` and
  `XDG_CACHE_HOME`.
- Optional: Xvfb and ImageMagick for the screenshot check. The test captures
  the display in `CALCPAD_QA_DISPLAY` (default `:97`).

## Running

```bash
# 1. A virtual display (skip the screenshot check on a headless setup)
Xvfb :97 -screen 0 1920x1080x24 &
mkdir -p /tmp/calcpad-qa/runtime
chmod 700 /tmp/calcpad-qa/runtime

# 2. The disposable app, with eval enabled
env DISPLAY=:97 \
    XDG_RUNTIME_DIR=/tmp/calcpad-qa/runtime \
    XDG_DATA_HOME=/tmp/calcpad-qa/data \
    XDG_CONFIG_HOME=/tmp/calcpad-qa/config \
    XDG_CACHE_HOME=/tmp/calcpad-qa/cache \
    CALCPAD_CONTROL_EVAL=1 \
    calcpad-desktop &

# 3. The suite (same binary as the instance when testing a local build)
env CALCPAD_QA_RUNTIME=/tmp/calcpad-qa/runtime \
    CALCPAD_DESKTOP=/usr/bin/calcpad-desktop \
    CALCPAD_QA_DISPLAY=:97 \
    node tools/control-channel-qa/run.mjs
```

The work directory defaults to `$TMPDIR/calcpad-control-qa`; override it with
`CALCPAD_QA_WORK`. It must stay below the system temp directory, and fixtures
are copied there on every run.

Output lines are prefixed with `control:` so the suite is distinguishable from
the engine and desktop-UI suites in shared logs.
