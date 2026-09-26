"""Generate the machine-produced half of THIRD-PARTY-NOTICES.txt.

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

# Second branch catches pass-through notices (THIRD-PARTY-NOTICES.TXT,
# ThirdPartyNoticeText.txt) -- the keyword is not at the start of the basename.
LICENSE_FILE = re.compile(
    r"(?i)(^|/)("
    r"(licen[cs]e|copying|notice|unlicense)([.\-_][\w.\-]*)?"
    r"|third[.\-_]?party[.\-_]?notice[\w.\-]*"
    r")$"
)
COPYRIGHT_LINE = re.compile(r"(?im)^.*copyright.*$")
MAX_LICENSE_BYTES = 200_000

# Lines that mention copyright but are licence body -- disclaimers, grant
# clauses, redistribution conditions -- rather than an attribution. Subtractive
# on purpose: over-reproducing is harmless, dropping a real notice is not, so
# each phrase must be licence prose that cannot appear in an attribution line.
BOILERPLATE = re.compile(r"""(?ix)
    shall\s+be\s+included
  | above\s+copyright
  | licensor\s+shall\s+mean
  | grant\s+of\s+copyright\s+license
  | copyright\s+license\s+to\s+reproduce
  | \[\s*name\s+of\s+copyright\s+owner\s*\]
  | designated\s+in\s+writing\s+by\s+the\s+copyright\s+owner
  | submitted\s+to\s+licensor
  | copyright\s+owner\s+that\s+is\s+granting
  | for\s+the\s+purposes\s+of\s+this\s+definition
  | your\s+own\s+copyright\s+statement
  | printed\s+page
  | no\s+event\s+shall
  | be\s+liable\s+for\s+any
  | this\s+software\s+is\s+provided
  | are\s+disclaimed
  | specifically\s+disclaims
  | list\s+of\s+conditions
  | permission\s+notice\s+appear
  | except\s+as\s+contained\s+in\s+this\s+notice
  | written\s+authorization\s+of\s+the\s+copyright\s+holder
  | all\s+copyright,\s+patent
  | copyright\s+notice\s+that\s+is\s+included
  | ^copyright\s+and\s+permission\s+notice$
  | subject\s+to\s+the\s+terms\s+and\s+conditions
  | copyright\s+holders?\s+(be\s+liable|and\s+contributors)
  | redistributions\s+(of\s+source|in\s+binary)
  | ^"copyright"\s+line
  | including\s+copyright\s+notices
  | to\s+the\s+extent\s+possible\s+under\s+law
  | licensor's\s+copyright\s+notice
  | wipo\s+copyright
  | copyright\s+notices\s+of\s+the\s+unaltered
  | shall\s+mean\s+the\s+copyright
  | disclaims\s+(all\s+)?(warranties|copyright)
  | neither\s+the\s+name\s+of\s+the\s+copyright\s+holder
  | authorization\s+of\s+the\s+copyright\s+holder
  | provided\s+by\s+the\s+copyright\s+holders\s+under
  | additional\s+accurate\s+notices\s+of\s+copyright
  | copyright\s+doctrines
  | by\s+copyrighted\s+interfaces
  | procedures\s+for\s+copyrights
  | references\s+to\s+the\s+internet\s+society
  | copyright\s+and\s+related\s+rights
  | ^\d+\.\s*copyrights?\.?$
  | exercises\s+copyright
  | existing\s+copyright\s+or\s+license\s+notices
  | add\s+your\s+own\s+copyright\s+notice
  | copyright\s+notice\s+may\s+not\s+be\s+removed
  | end\s+of\s+copyright\s+notice
  | complete\s+copyright\s+grant
  | is\s+itself\s+copyrighted
  | through\s+a\s+copyright\s+statement
  | under\s+this\s+license\s+or\s+copyright\s+law
  | otherwise\s+stated\s+in\s+writing\s+the\s+copyright
  | neither\s+icot
  | leave\s+the\s+copyright\s+notice
  | removing\s+the\s+copyright\s+notice
""")

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
        if 10 < len(line) < 200 and not BOILERPLATE.search(line):
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
        w("Components using this text:\n\n")
        for name in names:
            w(f"  - {name}\n")
        w("\n")
        if group["copyrights"]:
            # Extracted lines and the body stay unindented: the file claims to
            # reproduce them verbatim.
            w("Copyright notices:\n\n")
            for line in sorted(group["copyrights"], key=str.lower):
                w(f"{line}\n")
            w("\n")
        w("Licence text:\n\n")
        w(group["text"].replace("\r\n", "\n").rstrip() + "\n")

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
