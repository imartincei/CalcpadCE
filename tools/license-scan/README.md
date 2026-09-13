# Generating THIRD-PARTY-NOTICES

FOSSology scans **file contents** for license text and copyright notices. It has
no idea what NuGet or npm are, so it cannot read a `.csproj` and tell you what
you depend on. That gap is what `harvest.sh` fills: it resolves each component's
dependency closure, downloads the actual payloads, and stages them as archives
FOSSology can unpack and scan.

The payoff for that extra step is that FOSSology reads what is *in* the packages
rather than what their metadata *claims*. On this repo that difference is not
academic — see [Findings](#findings).

## What FOSSology is for here

**FOSSology is a discovery tool, not the source of the notices file.** It exists
to answer one question: *where does declared metadata lie?* Scanning several
hundred packages by hand is not possible; scanning them with FOSSology is how
the exceptions in [Findings](#findings) were found at all.

What it is *not* is a thing this project clears exhaustively. The corpus runs to
tens of thousands of files across ten components, and clearing each one in the
Browse UI is not proportionate to a project this size. **We deliberately do not
clear every resource.** Undecided findings are expected and are not a defect.

So the notices file is built in two halves:

| half | source | tool |
|---|---|---|
| the bulk — several hundred permissive dependencies | declared metadata | `generate-notices.sh` from `inventory.csv` |
| the exceptions — copyleft, pass-through, mis-declared | FOSSology scan results | hand-written into `notices-preamble.md` |

```bash
./harvest.sh              # resolve dependencies -> corpus/inventory.csv
./generate-notices.sh     # preamble + inventory -> THIRD-PARTY-NOTICES.md
```

`generate-notices.sh` copies `notices-preamble.md` verbatim, then appends the
component listing grouped by licence. Edit the preamble, never the generated
half. Rows the harvesters could not confirm as shipped (npm optional peers,
cargo build-dependencies) are held in a separate section rather than listed as
distributed.

Re-run the FOSSology scan when dependencies change and diff the findings. A
clean diff means nothing new needs a human.

## Quick start

Needs `docker`, plus the `dotnet` SDK, `npm` and `cargo` on the host — the
harvest resolves each closure with the real package managers.

```bash
cd tools/license-scan

./harvest.sh                  # resolve + download every dependency (~5 min)
docker compose up -d          # start FOSSology (first boot ~2 min)
docker compose restart scheduler   # see "First boot" below -- do not skip

./scan.sh calcpad-core --report readmeoss
```

Web UI: <http://localhost:8081/repo/> — login `fossy` / `fossy`. The port is bound to
loopback only; if you ever expose it, change that password first.

### First boot

On a fresh database the scheduler registers its agents while the schema is still
being written, and one agent loses the race:

```
ERROR agent localhost.spdx2 has been invalidated, removing from agents
```

`spdx2` is the SPDX report generator. It stays disabled until the scheduler
restarts, and nothing warns you — reports just fail. One `docker compose restart
scheduler` after the first boot fixes it permanently. Harmless to re-run.

Verify all 40 agents are up:

```bash
docker compose exec -T db psql -U fossy -d fossology \
  -tAc "select count(*) from agent where agent_enabled;"
```

## The step you cannot skip

**FOSSology's `readmeoss` report only emits licenses a *decision* exists for.**
The `decider` agent auto-concludes the cases where `nomos` and `monk` agree;
everything else waits for a human in the Browse UI. Anything still undecided is
silently absent from the report — no warning, no placeholder.

Concretely, from the first scan of this repo:

| | `calcpad-core` | `calcpad-web-frontend` |
|---|---|---|
| distinct licenses the scanners found | 38 | 18 |
| licenses in the generated `readmeoss` report | **1** (MIT) | **3** |

Shipping that unreviewed export would have produced a notices file asserting
that SkiaSharp and its vendored FreeType/libjpeg/libpng/ICU stack are all MIT.

So the workflow is three steps, not two:

1. `./harvest.sh` + `./scan.sh` — machine does the finding
2. **Browse → the upload → clear each finding** — human confirms or rejects
3. `./scan.sh --only <id> --report readmeoss` — regenerate after clearing

Use `inventory.csv` (written by `harvest.sh`, holds each package's *declared*
license) as the cross-check: any package where the declared license and the
scanned result disagree is exactly where to spend review time.

## Don't clear every file by hand

The corpus is ~14,600 files for the desktop component alone. Clearing that in
the Browse UI is not a realistic job for a small team, and it is not what this
tooling is for.

Split the notices file in two:

- **The bulk** — the several hundred MIT / Apache-2.0 / BSD dependencies.
  Declared metadata is correct for these, and `inventory.csv` already holds it.
  Generate that section from the CSV. Nobody audits whether you derived "MIT"
  from a scan or from a manifest; they check that the component is named, the
  licence is right, and the text is reproduced.
- **The exceptions** — the handful of places where declared metadata *lies*, or
  where an obligation exceeds attribution. Those are what FOSSology is for, and
  on this repo it has already found them: DOMPurify's MPL inside `monaco-editor`,
  SkiaSharp's vendored native stack, the stripped `jspreadsheet`/`jsuites`
  banners, the MPL crates, the vendored-but-uncompiled libdbus C, and the
  LGPL libraries bundled into the AppImage.

That list is finite and now written down in [Findings](#findings). The expensive
part of this exercise is done. Re-run the scan when dependencies change and diff
the findings; a clean diff means nothing new needs review.

## Using the web UI

The UI is at **<http://localhost:8081/repo/>** — the `/repo/` matters. Bare
`:8081` is Apache's default vhost and has nothing to do with FOSSology.

Three menus matter here: **Browse** (uploads and all clearing work), **Jobs →
All Recent Jobs** (scan progress and failures), **Organize → Uploads** (delete
an upload to re-scan it clean). The Browse table loads over AJAX, so it takes a
moment to paint.

Click an upload and you get a file tree with two columns that are the whole
point: **Scanner Results** (what nomos/monk/ojo found) and **Concluded
License** (what a human decided). Reports read the second column only.

To clear a file: click it, and the license view shows the contents on the left
with the matched license text highlighted, so you can see *why* it matched.
Pick the right license or mark it a false positive, then Save.

Do not do this one file at a time — 1,500 MIT files will not finish. Use
**Bulk actions** at the top of the file tree to apply one conclusion to every
file matching a pattern under a subtree, and clear at directory level wherever
a package is uniformly licensed.

Deep links follow `?mod=view-license&upload=<id>&item=<uploadtree_pk>`. The ids
are only stable while the upload exists; re-uploading a component renumbers
them, so re-derive from Browse rather than reusing old links.

When the clearing pass is done, regenerate per upload:

```bash
./scan.sh --only <upload id> --report readmeoss
```

Then diff the result against `inventory.csv`. A package in the inventory with
no license in the report is still unconcluded.

## Findings

All ten components scanned. Not legal advice — this is the queue for whoever
does the clearing pass.

**Confirmed against a released artifact (not inferred):**

`CalcpadCE.7.6.5.linux-x64.deb`, pulled from the public releases endpoint,
contains **862 files and zero third-party attribution**. The only license text in
it is `doc/LICENSE.TXT` — CalcpadCE's own 23-line MIT grant. Yet the package
ships `libSkiaSharp.so`, whose upstream `THIRD-PARTY-NOTICES.txt` covers FTL,
LGPL-2.1, MPL-1.1, IJG, libpng, zlib and ICU. That is a live compliance gap on a
public release, not a hypothetical one.

That artifact is the CLI line, and it is **framework-dependent**: its
`runtimeconfig.json` declares a `Microsoft.NETCore.App 10.0.0` framework
reference and it carries no runtime natives. `FRAMEWORK_PREFIXES` is right for
it.

**The 8.0 desktop bundles are a different story.** Pulled from CI
(`gh api .../actions/artifacts/<id>/zip` — needs auth, 401 anonymously even
though the repo is public):

`CalcpadCE-desktop-8.0.0-beta1-x86_64.deb` — 394 files, **zero attribution**.
It *is* self-contained: `libcoreclr.so`, `libclrjit.so`, `libhostfxr.so`,
`libhostpolicy.so`, `System.Private.CoreLib.dll` and `createdump` all ship. Its
`deps.json` pins the exact runtime to reproduce notices for:

    runtimepack.Microsoft.NETCore.App.Runtime.linux-x64/10.0.12
    runtimepack.Microsoft.AspNetCore.App.Runtime.linux-x64/10.0.12

Note 10.0.12 — not the 10.0.11 ref pack nor a cached 10.0.9. Take framework
notices from the version the artifact names, never from what the host happens to
have.

**The AppImage bundles an entire third-party stack this corpus does not model.**
`CalcpadCE-desktop-8.0.0-beta1-x86_64.AppImage` holds 700 files and **211 shared
objects**: 160 assorted system libraries, 33 GTK, 14 .NET runtime, 3 WebKitGTK,
and `libSkiaSharp.so`. Attribution present: 17 Debian `copyright` files that
`linuxdeploy` happened to copy, plus two font licences — roughly 8% coverage.
`deb`/`rpm` declare these as system dependencies and bundle none of them, so this
is an AppImage-only obligation.

Two items from that stack need a decision:

- **WebKitGTK is bundled.** WebKit is LGPL-2.1 (WebCore) plus BSD
  (JavaScriptCore), so this is a relinking obligation on a confirmed-present
  library — the same class as the SkiaSharp question, but no longer speculative.
  **Escalate this one.**
- **`libdbus-1.so.3` is bundled** (with its Debian `copyright` file). That makes
  the AFL-2.1 / GPL-2.0-or-later dual licence live for the AppImage, and the
  election has to be deliberate. Note this is the distro library, not the
  vendored C in `libdbus-sys` — that one still never compiles.

Also worth checking: only `DejaVuSerif-LICENSE` and `Jost-LICENSE` ship in the
bundle. JuliaMono is absent, so the font list above may be stale for this target.

**Vendored assets shipping without attribution:**

- `jspreadsheet.min.js`, `jsuites.min.js`, `jspreadsheet.min.css` and
  `jsuites.min.css` in `Calcpad.Web/backend/UiAssets/` carry **no license or
  copyright header**. The backend serves them to users. Both projects are MIT,
  and MIT requires the copyright and permission notice travel with the copy —
  so as shipped this is a compliance gap. Fix by restoring the upstream
  banners, or by shipping their license texts alongside.
- Font licenses are in better shape: `Jost-LICENSE`, `JuliaMono-LICENSE` and
  `DejaVuSerif-LICENSE` all ship next to their fonts. DejaVu scans as
  Bitstream-Vera plus public-domain glyphs; both need reproducing in full.
- JuliaMono carries a **Reserved Font Name**, so a modified copy may not keep
  the name. Jost declares no RFN, which is what makes the documented square
  bracket modifications safe. Worth confirming JuliaMono is unmodified.

**MPL-2.0 in the Tauri shell's Rust tree:**

- `cssparser`, `cssparser-macros`, `selectors`, `dtoa-short` and `option-ext`
  all declare **MPL-2.0** and are normal (linked) dependencies on every target.
  All five arrive through `tauri` itself (`tauri-utils` → `dom_query` for the
  first four, `dirs` → `dirs-sys` for `option-ext`), so none can be dropped
  without patching Tauri. Unlike the DOMPurify case below there is no dual
  license to elect: MPL-2.0 is a file-level copyleft, so the notices file must
  name each crate and point at the source for the exact version shipped.

**Real, and missed entirely by declared metadata:**

- **MPL-2.0 — DOMPurify vendored inside `monaco-editor`.** Lives at
  `monaco-editor/esm/vs/base/browser/dompurify/dompurify.js` and is compiled
  into the shipped `editor.main.js`. `monaco-editor`'s `package.json` declares
  MIT and nothing else. DOMPurify is dual Apache-2.0 OR MPL-2.0, so you can
  elect Apache-2.0 — but that election has to be made deliberately and written
  down. Left alone it is a file-level copyleft obligation on a file you ship.
- **`monaco-editor` and `typescript` both carry their own third-party notice
  files** (CC-BY-4.0, Unicode, W3C-20150513). These are pass-through
  obligations: their notices must be reproduced in yours.
- **SkiaSharp's `THIRD-PARTY-NOTICES.txt`** (in all three
  `SkiaSharp.NativeAssets.*` packages) lists FTL, LGPL-2.1, MPL-1.1, IJG,
  libpng, zlib and ICU. You ship `libSkiaSharp.so` / `.dll` as prebuilt
  binaries, so you inherit whatever is linked into them. Reproducing that file
  is mandatory; **confirming whether anything LGPL-2.1 is actually linked in is
  the one item worth escalating**, since that would carry relinking obligations.
- `typescript` shows up in both npm production trees, but only because `vue`
  declares it as an **optional peer dependency** — `npm ci --omit=dev` installs
  those. It is a devDependency everywhere it is declared and never reaches a
  bundle. `harvest.sh` now flags rows like this in the `note` column; do not
  list them without confirming they actually ship.

**GPL-2.0-or-later / AFL-2.1 in the Rust tree — present but not shipped:**

The desktop scan reports 199 `GPL-2.0-or-later` and 194 `AFL-2.1` findings. Every
one traces to the **libdbus C source vendored inside `libdbus-sys` 0.2.7**, which
itself declares only `Apache-2.0/MIT` — another declared-vs-actual mismatch.

It does not ship. `libdbus-sys` resolves with features `default, pkg-config` on
both Linux targets, **not** `vendored`, so its build script links the system
`libdbus-1` and never compiles the bundled C. Bulk-reject that subtree in the
Browse UI rather than clearing it file by file. Two caveats: re-check the feature
set after any dependency bump that could turn `vendored` on, and confirm the
AppImage target is not bundling `libdbus-1.so` (deb/rpm declare it as a system
dependency, so those are fine).

Crates are staged unpacked minus their root `tests/`, `benches/`, `examples/`,
`fuzz/` and `ci/` directories — separate Cargo targets that are never linked into
a dependent. That drops 4,124 files (18,727 → 14,603) with nothing else lost.

It deliberately stops there. `cargo check` dep-info can name the exact set of
`.rs` files rustc compiles (9,822 → 4,263 on the Linux target), but pruning to it
would be wrong: of the 746 license files and 199 native C/C++ sources in the
tree, dep-info contains **0** and **1** respectively. rustc never reads a
`LICENSE`, and C compiled by a build script never reaches its dep-info — so
filtering on "what rustc read" discards precisely the evidence a notices file is
made of. Dep-info is a review hint, not a corpus filter.

Vendored fallbacks that Cargo never builds (see the `libdbus-sys` finding above)
therefore still reach the clearing queue. Feature resolution is the cross-check
for those.

**Likely false positives — still need explicit rejection in the UI:**

- `GPL` on `libSkiaSharp.dll` / `libSkiaSharp.so` — `nomos` string-matching
  inside a binary, almost certainly the FreeType dual-license notice compiled
  in rather than a GPL grant.
- `BSD-3-Clause-No-Military-License` and `WebM` on `base64-vlq.js` and
  `compiler-sfc.*.js` — mis-bucketed standard BSD-3-Clause headers.
- Anything suffixed `-possibility` (`Microsoft-possibility`,
  `IJG-possibility`) — the suffix is FOSSology telling you it is a low
  confidence guess.

## Components

`harvest.sh` stages one archive per shippable artifact, so each gets its own
notices file:

| component | source |
|---|---|
| `calcpad-core`, `calcpad-openxml`, `calcpad-highlighter` | libraries |
| `calcpad-cli`, `calcpad-server`, `pycalcpad` | apps |
| `calcpad-web-frontend`, `vscode-calcpad` | npm, production deps only |
| `calcpad-desktop` | npm production deps **and** the Tauri shell's Rust crates |
| `bundled-assets` | third-party files committed into the repo |

`calcpad-desktop` is the one component that spans two ecosystems, so its
archive has an `npm/` and a `cargo/` subtree. A component may list several
`kind:path` specs joined by `;` in `COMPONENTS`; each stages into its own
subdirectory and writes its own `inventory/<component>.<kind>.csv`.

Crate selection is not just "everything in `Cargo.lock`". That lockfile is a
flat union of every platform and every dependency kind, so `harvest.sh` runs
`cargo metadata --filter-platform` once per triple CI actually builds
(`x86_64-unknown-linux-gnu`, `aarch64-unknown-linux-gnu`,
`x86_64-pc-windows-msvc`) and takes the union of those closures. Dev-dependencies
are dropped; build-dependencies are kept but flagged in the `note` column, as is
any crate that only one platform reaches. That takes 553 lock entries down to
441 shipped crates.

`bundled-assets` covers what no package manager knows about: the vendored
`jspreadsheet`/`jsuites` bundles the backend serves via `BundledUiAssets.cs`,
and the four font trees. Nothing else in this tooling would find them, and they
are all redistributed.

The WPF desktop app is not listed: it is being removed from the product, so its
dependencies are not shipped and do not belong in a notices file. Removing it
also drops `Microsoft.Web.WebView2`, the one component under a proprietary
Microsoft EULA rather than an OSS license.

`Calcpad.Tests` is deliberately excluded — xunit and coverlet are build-time
only and are not redistributed. Add it to `COMPONENTS` in `harvest.sh` if legal
wants the test toolchain covered too. .NET reference and runtime packs are
filtered out for the same reason (`FRAMEWORK_PREFIXES` in `harvest_lib.py`).

**That last filter is currently wrong for the desktop app.**
`build-desktop.sh` calls `sync-bundled-server.mjs` without
`--framework-dependent`, so the sidecar under `src-tauri/binaries/` is a
**self-contained** publish — the whole .NET runtime is redistributed inside the
installer. `FRAMEWORK_PREFIXES` strips exactly those packs from the corpus, so
no scan covers the runtime the desktop bundle ships. Closing this means
harvesting `calcpad-server` a second time with the filter off and the desktop
RIDs pinned; it adds roughly 80 MB of payload per RID.

## Guarding against database corruption

Postgres losing its data on an unclean shutdown has a specific cause here, and
`stop_grace_period` alone does **not** fix it.

**When you run `docker compose stop`**, Compose honours `stop_grace_period`
(set to 5m for the database). Postgres gets SIGINT — "fast shutdown" — and has
time to checkpoint. This path is safe.

**When you shut down or reboot the machine**, Compose is not involved. systemd
stops `docker.service`, and dockerd applies its *own* `shutdown-timeout` to
every container — **default 15 seconds**, and `stop_grace_period` is ignored
entirely. A Postgres mid-checkpoint after a large scan gets SIGKILLed. That is
the corruption.

`/etc/docker/daemon.json` does not exist on this machine, so you are on the
15s default. Fix it (needs root):

```json
{
  "shutdown-timeout": 300
}
```

```bash
sudo systemctl reload docker   # or restart; reload is enough for this key
```

systemd already allows `docker.service` 3 minutes to stop
(`TimeoutStopUSec=3min`), so it is not the binding constraint — dockerd's own
default is. Raise `TimeoutStopUSec` too if you set `shutdown-timeout` above 180.

Also in place, in `docker-compose.yml`:

- `--data-checksums` at initdb, so Postgres **detects** a torn page instead of
  serving corrupt rows silently. Only applies to a freshly created volume.
- `checkpoint_timeout=60s`, so an unclean stop has little WAL to replay.
- `stop_signal: SIGINT` pinned explicitly, so a base image change cannot
  silently downgrade it to a SIGTERM "smart shutdown" that waits for clients,
  blows the grace period, and gets killed mid-checkpoint.
- `fsync`, `full_page_writes`, `synchronous_commit` stated explicitly. These
  are the defaults; the point is that they are now visible and reviewable.

### Re-uploading without losing clearing work

Clearing decisions belong to an upload, so re-harvesting a component and
uploading it again starts from nothing by default. The `reuser` agent is the fix:

```bash
./scan.sh calcpad-core --reuse        # from the previous upload of this component
./scan.sh calcpad-core --reuse 3      # from upload 3 specifically
```

It matches files by **content hash plus filename, not full path**
(`ReuserAgent.php`), so a re-harvest that only relocates files — such as the
`csproj/` / `npm/` / `cargo/` nesting each component now stages into — carries
every decision over. Only genuinely new or changed files come back unconcluded,
which is exactly the set worth reviewing.

Add `"reuse_enhanced":true` to the payload if you also want the slow diff-based
matcher for files whose contents *did* change; for unchanged files it buys
nothing.

`--reuse` is the reason a version bump is cheap: bump a dependency, re-harvest,
re-scan with `--reuse`, and the review queue is just the delta.

### Snapshots

The corpus is reproducible and scans are recomputable. The **clearing decisions
are not** — they exist only in Postgres. Back them up after any real review
session:

```bash
./backup.sh              # live snapshot, safe mid-scan
./backup.sh --quiesce    # stops agents first, strictly consistent
./restore.sh backups/20260905T190000Z
```

`backup.sh` dumps the database *before* archiving the repository volume on
purpose: a repository row only exists after its file is on disk, so a later
archive is a superset of what the dump references. Extra files are harmless;
dangling rows are not.

### If the database will not start

```bash
docker compose logs db          # look for "invalid page in block" -> checksum caught it
./restore.sh backups/<latest>   # DESTROYS current state, restores the snapshot
```

With no snapshot, the scan data is recomputable — `docker compose down
--volumes`, then `./harvest.sh` and `./scan.sh` again. Only the human clearing
work is lost, which is precisely why `backup.sh` exists.
