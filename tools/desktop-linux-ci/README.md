# Local Linux desktop CI

Runs the `build-desktop-linux` job from `.github/workflows/build-desktop-linux.yml` on your machine.
It runs in an Ubuntu 22.04 container that matches the CI runner, so the host only needs Docker, Node.js, git and rsync.

```bash
tools/desktop-linux-ci/run.sh                  # full job
tools/desktop-linux-ci/run.sh --skip-tests     # build, package, verify
tools/desktop-linux-ci/run.sh --only=test-wayland
tools/desktop-linux-ci/run.sh --shell          # shell in the CI container
```

The steps run in this order: `deps`, `build`, `test-x11`, `test-scale`, `test-wayland`, `arch`, `stage`, `verify`.
Run `run.sh --help` to see all options.

## How it works

- **Source.** Each run copies tracked files and untracked files that aren't ignored into `.work/src`, so uncommitted changes are tested.
  The container's `node_modules`, `bin/obj` and cargo `target` stay there between runs, apart from your own checkout.
- **Image.** `Dockerfile` installs the same apt packages as CI, plus Node 22, the .NET SDK pinned in `global.json`, stable Rust and `tauri-driver`.
  The container runs as your UID, so the files it creates belong to you.
  Use `--rebuild-image` to pick up a newer Rust or new apt updates.
- **Caches.** NuGet, npm and the cargo registry are kept under `.work/home` and `.work/cargo`.
- **Arch package.** This step runs in `archlinux:base-devel` on the host's Docker, the same way CI does it.

Artifacts are written to `.work/src/artifacts/`.
Test diagnostics are written to `.work/src/tools/desktop-test/.work/linux-tests/`.
Use `--clean` to delete everything.

## Differences from CI

- Only the host architecture is built. CI also builds `aarch64`.
- The default version comes from `node tools/version.mjs snapshot`, which uses HEAD's short hash even if the tree has uncommitted changes.
- WebKit's sandbox is turned off with `WEBKIT_DISABLE_SANDBOX_THIS_IS_DANGEROUS=1`, because bubblewrap can't run under Docker's default seccomp profile.
- Sway runs with `--unsupported-gpu`. Otherwise it won't start on hosts that have the Nvidia kernel module loaded, even in headless mode.
- Nothing is uploaded.
