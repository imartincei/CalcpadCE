"""Corpus helpers for harvest.sh.

Stages third-party payloads per component and emits their *declared* license
metadata as CSV. Declared metadata is the cross-check, not the answer --
FOSSology's scan of the actual files is.
"""

import csv
import json
import shutil
import sys
import tarfile
import xml.etree.ElementTree as ET
import zipfile
from pathlib import Path

# .NET platform itself, not third-party, and not redistributed for a
# framework-dependent build. Revisit if we ever ship self-contained.
FRAMEWORK_PREFIXES = (
    "microsoft.aspnetcore.app.ref",
    "microsoft.netcore.app.ref",
    "microsoft.netcore.app.runtime.",
    "microsoft.aspnetcore.app.runtime.",
    "microsoft.windowsdesktop.app.ref",
    "netstandard.library",
)


def nuspec_metadata(nupkg: Path) -> tuple[str, str]:
    try:
        with zipfile.ZipFile(nupkg) as z:
            name = next(
                (n for n in z.namelist() if n.endswith(".nuspec") and "/" not in n),
                None,
            )
            if not name:
                return "", ""
            root = ET.fromstring(z.read(name))
    except (zipfile.BadZipFile, ET.ParseError, OSError):
        return "", ""

    # nuspec uses a versioned default namespace; match on local names.
    def find(tag):
        return next((e for e in root.iter() if e.tag.rsplit("}", 1)[-1] == tag), None)

    lic = find("license")
    if lic is not None and (lic.text or "").strip():
        text = lic.text.strip()
        # type="file" means no SPDX id -- the text ships inside the package.
        declared = text if lic.get("type", "expression") == "expression" else f"file:{text}"
    else:
        url = find("licenseUrl")
        declared = (url.text or "").strip() if url is not None else ""

    proj = find("projectUrl")
    return declared, (proj.text or "").strip() if proj is not None else ""


def collect_nuget(assets_path: str, pkgdir: str, dest: str, component: str) -> None:
    assets = json.loads(Path(assets_path).read_text())
    out = csv.writer(sys.stdout)
    dest_dir = Path(dest)

    for key, lib in sorted(assets.get("libraries", {}).items()):
        if lib.get("type") != "package":
            continue
        pkg_id, _, version = key.partition("/")
        lower = pkg_id.lower()
        if lower.startswith(FRAMEWORK_PREFIXES):
            continue

        nupkg = Path(pkgdir) / lower / version / f"{lower}.{version}.nupkg"
        if not nupkg.is_file():
            print(f"  missing nupkg: {key}", file=sys.stderr)
            continue

        shutil.copy2(nupkg, dest_dir / nupkg.name)
        declared, url = nuspec_metadata(nupkg)
        out.writerow([component, "nuget", pkg_id, version, declared, url])


def cargo_reachable(meta: dict) -> dict[str, str]:
    """Package id -> "normal" or "build", for everything the root links or runs.

    Cargo.lock is a flat union of every platform and every dependency kind.
    Dev-dependencies never ship, so they are dropped outright; build-dependencies
    run on the build host rather than in the binary, so they are kept but tagged.
    """
    nodes = {n["id"]: n for n in meta["resolve"]["nodes"]}
    kinds: dict[str, str] = {}
    queue = [(m, "normal") for m in meta.get("workspace_members", [])]

    while queue:
        pid, kind = queue.pop()
        # "normal" outranks "build": a crate reached both ways does ship.
        if kinds.get(pid) == "normal" or (pid in kinds and kind == "build"):
            continue
        kinds[pid] = kind
        for dep in nodes.get(pid, {}).get("deps", []):
            for dk in dep.get("dep_kinds") or [{"kind": None}]:
                if dk.get("kind") == "dev":
                    continue
                nested = "build" if kind == "build" or dk.get("kind") == "build" else "normal"
                queue.append((dep["pkg"], nested))
    return kinds


# Cargo target dirs at the crate root: separate build targets, never linked into
# a dependent, and a third of the files a clearing pass would otherwise wade
# through. Anything deeper (src/tests/) stays -- that can be a compiled module.
CRATE_SKIP_DIRS = ("tests", "benches", "examples", "fuzz", "ci")


def stage_crate(crate: Path, dest_dir: Path) -> None:
    """Unpack a .crate minus its non-library targets.

    Deliberately not a dep-info prune: rustc never reads LICENSE files, and C
    compiled by a build script never appears in its dep-info, so filtering on
    what rustc read would drop exactly the evidence a notices file needs.
    """
    with tarfile.open(crate) as t:
        keep = [
            m for m in t.getmembers()
            if not (len(parts := m.name.split("/")) > 1 and parts[1] in CRATE_SKIP_DIRS)
        ]
        t.extractall(dest_dir, members=keep, filter="data")


def collect_cargo(dest: str, component: str, cargo_home: str, *metas: str) -> None:
    dest_dir = Path(dest)
    cache = Path(cargo_home) / "registry" / "cache"
    out = csv.writer(sys.stdout)

    # id -> (package, kinds seen, triples that reach it)
    picked: dict[str, tuple[dict, set[str], set[str]]] = {}
    for meta_path in metas:
        triple = Path(meta_path).stem
        meta = json.loads(Path(meta_path).read_text())
        pkgs = {p["id"]: p for p in meta["packages"]}
        for pid, kind in cargo_reachable(meta).items():
            pkg = pkgs.get(pid)
            # A null source is the local crate itself -- first-party, not a notice.
            if not pkg or not (pkg.get("source") or "").startswith("registry+"):
                continue
            _, seen_kinds, triples = picked.setdefault(pid, (pkg, set(), set()))
            seen_kinds.add(kind)
            triples.add(triple)

    all_triples = {Path(m).stem for m in metas}
    for pkg, kinds, triples in sorted(picked.values(), key=lambda v: v[0]["id"]):
        name, version = pkg["name"], pkg["version"]
        crate = next(cache.glob(f"*/{name}-{version}.crate"), None)
        if crate is None:
            print(f"  missing crate: {name}-{version}", file=sys.stderr)
            continue
        stage_crate(crate, dest_dir)

        # license_file means no SPDX id -- the text ships inside the crate.
        declared = pkg.get("license") or ""
        if not declared and pkg.get("license_file"):
            declared = f"file:{pkg['license_file']}"

        notes = []
        if kinds == {"build"}:
            notes.append("build-dependency only; confirm it is not linked in before listing")
        if triples != all_triples:
            notes.append(f"{', '.join(sorted(triples))} only")

        out.writerow([
            component, "cargo", name, version, declared,
            pkg.get("repository") or pkg.get("homepage") or "", "; ".join(notes),
        ])


def prod_reachable(lockfile: Path) -> set[str] | None:
    """Names reachable from the root's runtime deps without crossing a peer edge.

    `npm ci --omit=dev` still installs optional peer deps of production
    packages -- vue pulls in the whole TypeScript compiler that way. Those are
    present on disk but never bundled, so flagging them keeps the notices file
    from attributing code we do not ship.
    """
    try:
        lock = json.loads(lockfile.read_text())
    except (json.JSONDecodeError, OSError):
        return None
    pkgs = lock.get("packages")
    if not pkgs:
        return None

    def resolve(frm: str, name: str) -> str | None:
        # Node resolution: try nested, then walk up to the hoisted top level.
        prefix = frm
        while True:
            cand = f"{prefix}/node_modules/{name}" if prefix else f"node_modules/{name}"
            if cand in pkgs:
                return cand
            if not prefix:
                return None
            idx = prefix.rfind("/node_modules/")
            prefix = prefix[:idx] if idx > 0 else ""

    seen: set[str] = set()
    queue = [("", d) for d in (pkgs.get("", {}).get("dependencies") or {})]
    while queue:
        frm, name = queue.pop()
        path = resolve(frm, name)
        if not path or path in seen:
            continue
        seen.add(path)
        entry = pkgs.get(path, {})
        for d in (entry.get("dependencies") or {}):
            queue.append((path, d))
        for d in (entry.get("optionalDependencies") or {}):
            queue.append((path, d))
    return {p.rsplit("node_modules/", 1)[-1] for p in seen}


def collect_npm(root: str, component: str) -> None:
    out = csv.writer(sys.stdout)
    modules = Path(root) / "node_modules"
    reachable = prod_reachable(Path(root) / "package-lock.json")

    for manifest in sorted(Path(modules).rglob("package.json")):
        # A real package sits directly under a node_modules dir (or one level
        # deeper when scoped); anything else is a fixture or sub-manifest.
        if manifest.parent.parent.name != "node_modules" and not (
            manifest.parent.parent.parent.name == "node_modules"
            and manifest.parent.parent.name.startswith("@")
        ):
            continue
        try:
            pkg = json.loads(manifest.read_text())
        except (json.JSONDecodeError, OSError):
            continue
        if not pkg.get("name"):
            continue

        lic = pkg.get("license") or pkg.get("licenses") or ""
        if isinstance(lic, list):
            lic = " OR ".join(
                x.get("type", "") if isinstance(x, dict) else str(x) for x in lic
            )
        elif isinstance(lic, dict):
            lic = lic.get("type", "")

        note = ""
        if reachable is not None and pkg["name"] not in reachable:
            note = "installed as optional peer; verify it is bundled before listing"

        out.writerow([
            component, "npm", pkg["name"], pkg.get("version", ""), lic,
            pkg.get("homepage", "") or _repo_url(pkg), note,
        ])


def collect_paths(staged: str, component: str) -> None:
    """Inventory vendored files. Nothing declares a license for these, so the
    declared column stays empty and FOSSology's scan is the only source."""
    out = csv.writer(sys.stdout)
    root = Path(staged)

    for path in sorted(root.rglob("*")):
        if not path.is_file():
            continue
        licenses = sorted(
            p.name for p in path.parent.glob("*")
            if p.is_file() and "license" in p.name.lower()
        )
        out.writerow([
            component, "vendored", str(path.relative_to(root)), "", "", "",
            f"ships: {', '.join(licenses)}" if licenses else "no license file",
        ])


def _repo_url(pkg: dict) -> str:
    repo = pkg.get("repository", "")
    return repo.get("url", "") if isinstance(repo, dict) else str(repo)


if __name__ == "__main__":
    cmd, *rest = sys.argv[1:]
    {
        "collect-nuget": collect_nuget,
        "collect-npm": collect_npm,
        "collect-paths": collect_paths,
        "collect-cargo": collect_cargo,
    }[cmd](*rest)
