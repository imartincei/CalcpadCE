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
tens of thousands of files across nine components, and clearing each one in the
Browse UI is not proportionate to a project this size. **We deliberately do not
clear every resource.** Undecided findings are expected and are not a defect.

So the notices file is built in two halves:

| half | source | tool |
|---|---|---|
| the bulk — several hundred permissive dependencies | declared metadata | `generate-notices.sh` from `inventory.csv` |
| the exceptions — copyleft, pass-through, mis-declared | FOSSology scan results | hand-written into `notices-preamble.md` |

```bash
./harvest.sh              # resolve dependencies -> corpus/inventory.csv
./fetch-licences.sh       # licence texts for packages that ship none
./generate-notices.sh     # preamble + inventory -> THIRD-PARTY-NOTICES.txt
./verify-notices.sh <artifact>...   # assert a built artifact carries it
```

`generate-notices.sh` copies `notices-preamble.md` verbatim, then appends the
component listing grouped by licence. Edit the preamble, never the generated
half. Rows the harvesters classify as not distributed (npm optional peers,
cargo build-dependencies) are dropped — see [Not distributed](#not-distributed).
Anything still genuinely unverified lands in a "Held back pending verification"
section instead of shipping silently; that section appearing means act on it.
It **exits non-zero** if the result still contains a `TODO` or a held-back
section: an obligation that is described but not discharged must not ship.

### The file has to reach users

Generating it is only half the job. `verify-notices.sh` unpacks a built
artifact and fails if `THIRD-PARTY-NOTICES.txt` is not inside it; every
release workflow runs it before uploading.

The file is named `.txt`, not `.md`, on purpose. `pull-request.yml` and
`push-to-main.yml` both set `paths-ignore: '**/*.md'`, and GitHub honours no
negation inside it — as a `.md` file, a commit that only regenerated the
notices would run no build and no guard, precisely on the commits where it
matters. It also keeps the file clear of the extension's `*.md` ignore rule.

Re-run the FOSSology scan when dependencies change and diff the findings. A
clean diff means nothing new needs a human.

## Quick start

Needs `docker`, plus the `dotnet` SDK, `npm` and `cargo` on the host — the
harvest resolves each closure with the real package managers.

**Use the SDK `global.json` pins**, not merely a compatible one. The patch level decides which packages resolve standalone and which collapse into the shared framework: on 10.0.111 the server's closure carries `Microsoft.Extensions.{DependencyInjection,Logging,Options,Primitives}`
as separate packages, and on 10.0.112 it does not, because the newer runtime pack subsumes them. It also fixes the runtime pack versions the notices record. Harvest with the wrong SDK and the file describes a build nobody ships.

```bash
cd tools/license-scan

./harvest.sh                  # resolve + download every dependency (~5 min)
./fetch-licences.sh           # pull missing licence texts from upstream repos
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
  on this repo it has already found them: DOMPurify's dual licence inside
  `monaco-editor`, SkiaSharp's vendored native stack, the stripped
  `jspreadsheet`/`jsuites` banners, the MPL crates, and the
  vendored-but-uncompiled libdbus C.

That list is finite and now written down in [Findings](#findings). 
Re-run the scan when dependencies change and diff
the findings; a clean diff means nothing new needs review.

The UI is at **<http://localhost:8081/repo/>**

## Findings

**Real, and missed entirely by declared metadata:**

- **DOMPurify vendored inside `monaco-editor`.** Lives at
  `monaco-editor/esm/vs/base/browser/dompurify/dompurify.js` and is compiled
  into the shipped `editor.main.js`. `monaco-editor`'s `package.json` declares
  MIT and nothing else. DOMPurify is dual Apache-2.0 OR MPL-2.0; **CalcpadCE
  elects Apache-2.0**, recorded in `notices-preamble.md`, so it is attribution
  only and not a file-level copyleft obligation.
- **`monaco-editor` carries its own third-party notice file**
  (CC-BY-4.0, Unicode, W3C-20150513). It is compiled into the shipped
  `editor.main.js`, so this is a pass-through obligation: its notices must be
  reproduced in yours.
  `typescript` ships an equivalent `ThirdPartyNoticeText.txt` and looks like the
  same case, but **it is not an obligation and should not be re-raised**: the
  package is never redistributed (see [Not distributed](#not-distributed)), and
  a notice obligation follows distribution, not resolution. It stays out of the
  notices file for that reason, not by oversight.
- **SkiaSharp's `THIRD-PARTY-NOTICES.txt`** (identical in all three
  `SkiaSharp.NativeAssets.*` packages) lists FTL, LGPL-2.1, MPL-1.1, IJG,
  libpng, zlib and ICU. You ship `libSkiaSharp.so` / `.dll` as prebuilt
  binaries, so you inherit whatever is linked into them, and reproducing that
  file is mandatory. **Nothing LGPL-2.1 is linked in** — see
  [LGPL-2.1 in SkiaSharp](#lgpl-21-in-skiasharp).
- `typescript` shows up in both npm production trees, but only because `vue`
  declares it as an **optional peer dependency** — `npm ci --omit=dev` installs
  those. It is a devDependency everywhere it is declared and never reaches a
  bundle — confirmed against real build output, see
  [Not distributed](#not-distributed).

**GPL-2.0-or-later / AFL-2.1 in the Rust tree — present but not shipped:**

The desktop scan reports 199 `GPL-2.0-or-later` and 194 `AFL-2.1` findings. Every
one traces to the **libdbus C source vendored inside `libdbus-sys` 0.2.7**, which
itself declares only `Apache-2.0/MIT` — another declared-vs-actual mismatch.

It does not ship. `libdbus-sys` resolves with features `default, pkg-config` on
both Linux targets, **not** `vendored`, so its build script links the system
`libdbus-1` and never compiles the bundled C.

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

### Packages that ship no licence file

45 components declared a licence but shipped no licence file, leaving no
copyright line to reproduce — and for MIT and BSD the copyright line is the
part that actually has to travel with the copy.

The manifest is a dead end here. Every NuGet package points `licenseUrl` at
`https://licenses.nuget.org/<spdx>`, which serves the generic template reading
`Copyright (c) <year> <copyright holders>` — a literal placeholder that adds
nothing to the SPDX id already in the table.

The `.nuspec` `<repository>` element is the good route, and it is better than a
version tag: it names the real source repo *and* the exact commit the package
was built from, so the text pins to that build.

```xml
<repository type="git" url="https://github.com/dotnet/dotnet"
            commit="b0f34d51fccc69fd334253924abd8d6853fad7aa" />
```

`fetch-licences.sh` resolves each package that way, falling back to the
project URL, and tries refs in order: build commit, `v<version>`, `<version>`,
`<name>-v<version>`, then the default branch. It follows vanity redirects, so
SkiaSharp's `go.microsoft.com/fwlink/?linkid=868515` resolves to
`mono/SkiaSharp`. Results cache under `corpus/licences-fetched/` with a
manifest of every URL used, keeping `generate-notices.sh` offline.

`REF_PINS` in `fetch_licences.py` overrides that order for the two vendored npm
bundles, and a pin is exclusive — no fallback to `HEAD`. Neither repo tags the
version its bundle declares (`jspreadsheet/ce` stops tagging at `4.15.0`,
`jsuites/jsuites` at `v5.13.3`), so both were resolving to the default branch,
which is a moving target: `jsuites` master is now **6.4.1** while the bundle
shipped is 5.13.5, and jspreadsheet-ce 6 moved to a commercial licence. Each is
pinned to the newest commit in the shipped line, with the `LICENSE` blob there
verified byte-identical to what was fetched:

| bundle | version | pinned commit |
|---|---|---|
| jspreadsheet-ce | 5.0.4 | `fe71a3f0` — introduced 5.0.4, 2025-08-26 |
| jsuites | 5.13.5 | `8de9382f` — last 5.x commit, 2025-10-27 |

`jsuites` has no commit whose `package.json` reads 5.13.5 at all; master goes
5.13.4 → 6.0.0, so 5.13.5 was published to npm without a matching commit.
`8de9382f` is the nearest point in the 5.x line. Neither `LICENSE` has changed
since 2024 (`ce` 2024-12-18, `jsuites` 2024-09-02), so the pin loses nothing —
it only stops a future relicence being picked up silently. Re-derive both pins
when the bundles are next refreshed.

That took the gap from 45 components to 1, and SkiaSharp now carries its real
`Copyright (c) 2015-2016 Xamarin, Inc. / 2017-2018 Microsoft Corporation`
rather than nothing. The one left is `selectors` 0.36.1 — `servo/stylo` carries
no licence file at all and declares MPL-2.0 only in `Cargo.toml`. It is already
covered in the preamble's MPL section.

#### Hardcoded repository URLs — check these before a release

Some packages name no repository anywhere: not in the manifest, not on the
registry. For those the URL is **hardcoded** in `OVERRIDES` at the top of
`fetch_licences.py`, which means nothing upstream will tell us when it goes
stale. Re-check each one before cutting a release:

| Package | Hardcoded URL | Why |
|---|---|---|
| `libappindicator-sys` | https://github.com/tauri-apps/libappindicator-rs | no `repository` in the crate manifest, and none on crates.io |

A hardcoded URL fails in two directions and only one is loud. If the repo moves
or the tag disappears the fetch reports `MISS` and the component drops back into
"shipping no licence text", which is visible. But if upstream *starts* declaring
its own repository, or the crate changes hands, the override silently keeps
winning and we would go on reproducing a licence from the wrong project. Drop an
entry as soon as the package declares its own.

Re-run after any dependency bump: a new version means a new commit, and the
copyright line can legitimately change between releases.

### Not distributed

28 rows resolve into the dependency tree but ship in nothing. Verified against
the artifacts `build-desktop.sh` actually produces, not inferred from metadata,
so `harvest.sh` marks them `not-shipped:` and the notices file omits them.

**26 cargo build-dependencies.** Because `build-desktop.sh` passes `--target`,
cargo splits host artifacts from target artifacts, and that split is the proof:

```bash
cd Calcpad.Web/frontend/calcpad-desktop/src-tauri
# versioned crate sources compiled FOR THE TARGET (linked into the binary)
cat target/<triple>/release/deps/*.d | tr ' ' '\n' \
  | grep -oE 'registry/src/[^/]+/[^/]+/' | sed 's#.*/\([^/]*\)/$#\1#' | sort -u
# the same under target/release/deps/ is host-only: build scripts, never linked
```

All 26 appear only in the host set. `cargo tree --edges normal` agrees on all
three triples. Watch the three name collisions — `toml`, `toml_datetime` and
`winnow` each have a *different* version that genuinely is linked and is
listed; compare versions, not bare names.

**2 npm `typescript` rows.** Neither bundle contains a TypeScript compiler:
`grep -r createSourceFile\|createProgram\|'Debug Failure'` over
`calcpad-web/dist/` and `vscode-calcpad/dist/extension.js` returns nothing, and
no `ts.worker` chunk is emitted. The `typescript-*.js` and `tsMode-*.js` chunks
in `dist/assets/` are monaco-editor 0.52.2's own language support (8K and 24K —
the compiler is ~9MB), not the npm package. The extension is packaged
`--no-dependencies` with `node_modules/**` in `.vscodeignore`, so the on-disk
peer copy is not redistributed either.

Re-run both checks after any dependency bump that could move a build-dependency
into the normal graph.

## Components

`harvest.sh` stages one archive per shippable artifact, so each gets its own
notices file:

| component | source |
|---|---|
| `calcpad-core`, `calcpad-openxml`, `calcpad-highlighter` | libraries |
| `calcpad-server`, `pycalcpad` | apps |
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

`Calcpad.Tests` and `Calcpad.Cli` are deliberately excluded, as neither is shipped
in a release. The CLI is still built, but only to render the documentation's
`.cpd` examples, so nothing it resolves is ever redistributed.

**Runtime packs are different, and depend on how the component is published.**
`build-desktop.sh` calls `sync-bundled-server.mjs` without
`--framework-dependent`, so the sidecar under `src-tauri/binaries/` is a
**self-contained** publish — the whole .NET runtime is redistributed inside the
installer, and its notices are therefore mandatory.

`SELF_CONTAINED` in `harvest.sh` names the components that ship a runtime and
maps each to the `deps.json` the build produced. For those, `collect_nuget`
also walks `downloadDependencies` — runtime and host packs resolve there, *not*
in `libraries`, so relaxing a prefix filter alone would have found nothing — and
cross-checks every resolved version against that `deps.json`, aborting on
disagreement.
