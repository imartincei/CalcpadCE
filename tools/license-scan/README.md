# Generating THIRD-PARTY-NOTICES with FOSSology

FOSSology scans **file contents** for license text and copyright notices. It has
no idea what NuGet or npm are, so it cannot read a `.csproj` and tell you what
you depend on. That gap is what `harvest.sh` fills: it resolves each component's
dependency closure, downloads the actual payloads, and stages them as archives
FOSSology can unpack and scan.

The payoff for that extra step is that FOSSology reads what is *in* the packages
rather than what their metadata *claims*. On this repo that difference is not
academic — see [Findings](#findings).

## Quick start

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
| `calcpad-web-frontend`, `vscode-calcpad`, `calcpad-desktop` | npm, production deps only |
| `bundled-assets` | third-party files committed into the repo |

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
filtered out for the same reason (`FRAMEWORK_PREFIXES` in `harvest_lib.py`);
that assumption breaks if you ever ship self-contained builds.

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
