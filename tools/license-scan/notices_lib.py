"""Generate the machine-produced half of THIRD-PARTY-NOTICES.md.

Two inputs: corpus/inventory.csv for what we depend on, and the corpus archives
for the licence texts those packages actually ship. Texts are grouped by body
with the copyright lines stripped, so the MIT body appears once with every
copyright notice listed above it rather than 171 near-identical copies.
"""

import csv
import collections
import glob
import hashlib
import io
import os
import re
import sys
import tarfile
import zipfile

LICENSE_FILE = re.compile(r"(?i)(^|/)(licen[cs]e|copying|notice|unlicense)([.\-_][\w.\-]*)?$")
COPYRIGHT_LINE = re.compile(r"(?im)^.*copyright.*$")
MAX_LICENSE_BYTES = 200_000

CARGO_PATH = re.compile(r"^cargo/(?P<name>.+)-(?P<version>\d[^/]*)/")
NUPKG_PATH = re.compile(r"^csproj/(?P<name>.+?)\.(?P<version>\d[\w.\-+]*)\.nupkg$")


def _npm_key(path):
    # Last node_modules segment wins: a/node_modules/b/LICENSE belongs to b.
    _, _, tail = path.rpartition("node_modules/")
    parts = tail.split("/")
    if not parts:
        return None
    name = "/".join(parts[:2]) if parts[0].startswith("@") else parts[0]
    return ("npm", name.lower()) if name else None


def _package_key(path):
    if path.startswith("cargo/"):
        m = CARGO_PATH.match(path)
        return ("cargo", m["name"].lower()) if m else None
    if "node_modules/" in path:
        return _npm_key(path)
    return None


def scan_corpus(corpus_dir):
    """package key -> list of licence texts it ships."""
    texts = collections.defaultdict(list)

    def take(key, data):
        if key is None or not data or len(data) > MAX_LICENSE_BYTES:
            return
        body = data.decode("utf8", "replace")
        if body.strip():
            texts[key].append(body)

    for archive in sorted(glob.glob(os.path.join(corpus_dir, "*.tar.gz"))):
        with tarfile.open(archive) as tar:
            for member in tar:
                if not member.isfile():
                    continue
                path = member.name.lstrip("./")
                nupkg = NUPKG_PATH.match(path)
                if nupkg:
                    try:
                        blob = io.BytesIO(tar.extractfile(member).read())
                        with zipfile.ZipFile(blob) as z:
                            for info in z.infolist():
                                if info.file_size and LICENSE_FILE.search(info.filename):
                                    take(("nuget", nupkg["name"].lower()), z.read(info))
                    except (zipfile.BadZipFile, OSError, KeyError):
                        pass
                elif LICENSE_FILE.search(path):
                    try:
                        take(_package_key(path), tar.extractfile(member).read())
                    except (OSError, KeyError):
                        pass
    return texts


def load_fetched(corpus_dir):
    """package key -> [(text, url, ref)] fetched from upstream by fetch-licences.sh."""
    out = collections.defaultdict(list)
    man = os.path.join(corpus_dir, "licences-fetched", "manifest.csv")
    if not os.path.exists(man):
        return out
    for row in csv.DictReader(open(man, encoding="utf8")):
        path = os.path.join(corpus_dir, "licences-fetched", row["path"])
        try:
            text = open(path, encoding="utf8").read()
        except OSError:
            continue
        if text.strip():
            out[(row["ecosystem"], row["package"].lower())].append(
                (text, row["url"], row["ref"]))
    return out


def _normalise(text):
    stripped = COPYRIGHT_LINE.sub("", text)
    return re.sub(r"\s+", " ", stripped).strip().lower()


def _copyrights(text):
    out = []
    for match in COPYRIGHT_LINE.finditer(text):
        line = " ".join(match.group(0).split())
        # Skip the boilerplate "...copyright notice shall be included..." clause.
        if 10 < len(line) < 200 and not re.search(r"(?i)shall be included|above copyright", line):
            out.append(line)
    return out


# Verified against a real build: not distributed, so no notice obligation.
def _not_shipped(note):
    return note.lower().startswith("not-shipped:")


# Anything still unverified surfaces in its own section rather than shipping silently.
def _deferred(note):
    return "confirm" in note.lower() or "verify" in note.lower()


def _norm_license(value):
    value = (value or "").strip()
    if not value:
        return "Undeclared"
    if value.startswith("file:"):
        return f"Declared by file ({value[5:]})"
    return value


def load_inventory(path):
    packages, held = {}, []
    for row in csv.DictReader(open(path)):
        name = (row.get("package") or "").strip()
        if not name:
            continue
        note = (row.get("note") or "").strip()
        if _not_shipped(note):
            continue
        if _deferred(note):
            held.append(row)
            continue
        key = (row["ecosystem"], name, row["version"])
        packages.setdefault(key, {**row, "components": set()})["components"].add(row["component"])
    return packages, held


def emit(inventory_path, corpus_dir, out=sys.stdout):
    packages, held = load_inventory(inventory_path)
    texts = scan_corpus(corpus_dir)
    fetched = load_fetched(corpus_dir)

    by_license = collections.defaultdict(list)
    for (eco, name, version), row in packages.items():
        detail = (row.get("project_url") or "").strip() or (row.get("note") or "").strip()
        by_license[_norm_license(row["declared_license"])].append((name, version, eco, detail))

    w = out.write
    w("\n---\n\n## Components by licence\n\n")
    w(f"{len(packages)} components across {len(by_license)} declared licences. Generated\n")
    w("from `corpus/inventory.csv`; regenerate with `tools/license-scan/generate-notices.sh`.\n\n")
    w("| Licence | Components |\n|---|---:|\n")
    for lic, items in sorted(by_license.items(), key=lambda kv: (-len(kv[1]), kv[0])):
        w(f"| {lic} | {len(items)} |\n")

    for lic, items in sorted(by_license.items(), key=lambda kv: (-len(kv[1]), kv[0])):
        w(f"\n### {lic}\n\n")
        if lic == "Undeclared":
            w("Vendored files with no manifest to declare a licence. Those shipping a\n")
            w("licence file beside them are covered under Fonts above; those marked\n")
            w("*no license file* still need resolving.\n\n")
        w("| Component | Version | Ecosystem | Source / note |\n|---|---|---|---|\n")
        for name, version, eco, detail in sorted(items, key=lambda t: t[0].lower()):
            w(f"| {name} | {version} | {eco} | {detail or '—'} |\n")

    if held:
        w("\n---\n\n## Held back pending verification\n\n")
        w("Resolved into the dependency tree but not confirmed as shipped, so not\n")
        w("listed above. Confirm whether each is distributed, then list or drop it.\n\n")
        w("| Component | Version | Ecosystem | Reason |\n|---|---|---|---|\n")
        seen = set()
        for row in sorted(held, key=lambda r: r["package"].lower()):
            key = (row["ecosystem"], row["package"], row["version"])
            if key in seen:
                continue
            seen.add(key)
            w(f"| {row['package']} | {row['version']} | {row['ecosystem']} | {row['note']} |\n")

    _emit_texts(w, packages, texts, fetched)


def _emit_texts(w, packages, texts, fetched=None):
    # normalised body -> {"text": verbatim, "packages": [...], "copyrights": set}
    groups = {}
    without = []
    upstream = []
    fetched = fetched or {}

    for (eco, name, version), row in sorted(packages.items()):
        # Vendored files have no manifest to declare anything; they are already
        # listed under Undeclared and handled in the preamble.
        if eco == "vendored":
            continue
        key = (eco, name.lower())
        shipped = texts.get(key)
        if not shipped:
            # No licence file in the package -- fall back to the project's own repo.
            got = fetched.get(key)
            if got:
                shipped = [t for t, _, _ in got]
                for _, url, ref in got:
                    upstream.append((name, version, eco, ref, url))
            else:
                without.append((name, version, eco, _norm_license(row["declared_license"]),
                                (row.get("project_url") or "").strip()))
                continue
        for text in shipped:
            digest = hashlib.sha256(_normalise(text).encode()).hexdigest()
            group = groups.setdefault(digest, {"text": text, "packages": set(), "copyrights": set()})
            group["packages"].add(f"{name} {version}")
            group["copyrights"].update(_copyrights(text))

    ordered = sorted(groups.values(), key=lambda g: (-len(g["packages"]), sorted(g["packages"])[0]))
    covered = len({p for g in groups.values() for p in g["packages"]})

    w("\n---\n\n## Licence texts\n\n")
    w(f"{len(ordered)} distinct licence texts, covering {covered} components. Each body\n")
    w("appears once; the copyright notices from every component sharing it are listed\n")
    w("above it. Texts are reproduced verbatim from the packages as shipped, or for\n")
    w("packages that ship none, from the project's own repository as recorded below.\n")

    for i, group in enumerate(ordered, 1):
        names = sorted(group["packages"], key=str.lower)
        w(f"\n### Licence text {i} — {len(names)} component"
          f"{'s' if len(names) != 1 else ''}\n\n")
        w("<details><summary>Components using this text</summary>\n\n")
        for name in names:
            w(f"- {name}\n")
        w("\n</details>\n\n")
        if group["copyrights"]:
            w("Copyright notices:\n\n```\n")
            for line in sorted(group["copyrights"], key=str.lower):
                w(f"{line}\n")
            w("```\n\n")
        w("```\n")
        w(group["text"].replace("\r\n", "\n").rstrip() + "\n")
        w("```\n")

    if upstream:
        w("\n---\n\n## Licence texts taken from the project repository\n\n")
        w("These packages ship no licence file. Their manifest licenceUrl points at a\n")
        w("generic SPDX template with a placeholder copyright line, so the text above\n")
        w("was taken from the project's own repository instead, pinned to the commit\n")
        w("or tag the package was built from where one is recorded.\n\n")
        w("| Component | Version | Ecosystem | Ref | Source |\n|---|---|---|---|---|\n")
        for name, version, eco, ref, url in sorted(set(upstream), key=lambda t: t[0].lower()):
            pin = ref if ref != "HEAD" else "HEAD (untagged)"
            w(f"| {name} | {version} | {eco} | `{pin}` | {url} |\n")

    if without:
        w("\n---\n\n## Components shipping no licence text\n\n")
        w("These declare a licence in their manifest but ship no licence file, so there\n")
        w("is no copyright notice to reproduce. They are listed here under the licence\n")
        w("they declare, with their source, which is the most that can be reproduced.\n\n")
        w("| Component | Version | Ecosystem | Declared licence | Source |\n|---|---|---|---|---|\n")
        for name, version, eco, lic, url in sorted(without, key=lambda t: t[0].lower()):
            w(f"| {name} | {version} | {eco} | {lic} | {url or '—'} |\n")

    print(f"  {len(ordered)} licence texts, {covered} components covered, "
          f"{len({(u[0], u[1]) for u in upstream})} from upstream repos, "
          f"{len(without)} shipping no text", file=sys.stderr)


if __name__ == "__main__":
    emit(sys.argv[1], sys.argv[2])
