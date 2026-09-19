"""Fetch licence texts for components that ship none, from their own repository.

A package with no licence file leaves nothing to reproduce but the SPDX id, and
the manifest's licenceUrl is no help -- NuGet points every MIT package at the
generic https://licenses.nuget.org/MIT template, whose copyright line is the
literal placeholder `Copyright (c) <year> <copyright holders>`. The project's
own LICENSE carries the real holder, which is the part MIT actually requires.

Texts are pinned to the tag matching the shipped version where one exists, so a
later upstream change cannot silently rewrite what we claim to have shipped.
Results land in corpus/licences-fetched/ with a manifest recording the exact URL
used, keeping generate-notices.sh offline and reproducible.
"""

import csv
import glob
import io
import os
import re
import sys
import tarfile
import time
import urllib.error
import urllib.request
import zipfile

import notices_lib as N

REPO = re.compile(r"^https?://(github\.com|gitlab\.[\w.-]+)/([^/]+)/([^/?#]+?)(?:\.git)?/?$")
UA = {"User-Agent": "calcpadce-license-scan/1.0"}
TIMEOUT = 20
MAX_BYTES = 200_000

NAMES = [
    "LICENSE", "LICENSE.txt", "LICENSE.md", "LICENSE.TXT",
    "LICENCE", "COPYING", "LICENSE-MIT", "LICENSE-APACHE",
    "MIT-LICENSE", "license", "license.txt", "License.txt",
]

# Repos we had to supply by hand: the package declares a licence but names no
# repository anywhere -- not in its manifest, not on the registry. Re-check each
# before a release; upstream may start publishing one, or move.
OVERRIDES = {
    ("cargo", "libappindicator-sys"): "https://github.com/tauri-apps/libappindicator-rs",
}

_CACHE = {}


NUSPEC_REPO = re.compile(r'<repository([^>]*?)/?>', re.S)
ATTR = re.compile(r'(\w+)\s*=\s*"([^"]*)"')


def nuspec_repos(corpus_dir):
    """nuget package (lowercased) -> (repo url, commit) straight from its .nuspec.

    Beats the manifest projectUrl: it names the actual source repo and the exact
    commit the package was built from, so the text can be pinned to that build.
    """
    out = {}
    for archive in sorted(glob.glob(os.path.join(corpus_dir, "*.tar.gz"))):
        with tarfile.open(archive) as tar:
            for m in tar:
                if not m.isfile() or not m.name.endswith(".nupkg"):
                    continue
                try:
                    with zipfile.ZipFile(io.BytesIO(tar.extractfile(m).read())) as z:
                        ns = next((n for n in z.namelist() if n.endswith(".nuspec")), None)
                        if not ns:
                            continue
                        x = z.read(ns).decode("utf8", "replace")
                        pid = re.search(r"<id>(.*?)</id>", x)
                        rep = NUSPEC_REPO.search(x)
                        if not pid or not rep:
                            continue
                        a = dict(ATTR.findall(rep.group(1)))
                        if a.get("url"):
                            out.setdefault(pid.group(1).lower(), (a["url"], a.get("commit", "")))
                except (zipfile.BadZipFile, OSError, KeyError):
                    pass
    return out


def resolve_repo(url):
    """Follow vanity redirects (go.microsoft.com/fwlink) to a real repo URL."""
    if not url or REPO.match(url):
        return url
    try:
        with urllib.request.urlopen(urllib.request.Request(url, headers=UA), timeout=TIMEOUT) as r:
            return r.url
    except (urllib.error.URLError, urllib.error.HTTPError, OSError):
        return url


def _raw_url(host, owner, repo, ref, path):
    if host == "github.com":
        return f"https://raw.githubusercontent.com/{owner}/{repo}/{ref}/{path}"
    return f"https://{host}/{owner}/{repo}/-/raw/{ref}/{path}"


def _get(url):
    if url in _CACHE:
        return _CACHE[url]
    _CACHE[url] = text = _fetch(url)
    time.sleep(0.05)
    return text


def _fetch(url):
    try:
        with urllib.request.urlopen(urllib.request.Request(url, headers=UA), timeout=TIMEOUT) as r:
            if r.status != 200:
                return None
            body = r.read(MAX_BYTES + 1)
    except (urllib.error.URLError, urllib.error.HTTPError, OSError):
        return None
    if not body or len(body) > MAX_BYTES:
        return None
    text = body.decode("utf8", "replace")
    # A repo without the file can still 200 with an HTML error page.
    if not text.strip() or text.lstrip()[:1] == "<":
        return None
    return text


def _refs(name, version):
    base = version.split("+")[0]
    return [f"v{base}", base, f"{name}-v{base}", f"{name}-{base}", "HEAD"]


def fetch_one(name, version, url, commit=""):
    """-> (ref, [(filename, url, text)]) for the first ref that yields anything."""
    m = REPO.match(url or "")
    if not m:
        return None, []
    host, owner, repo = m.groups()
    refs = ([commit] if commit else []) + _refs(name, version)
    for ref in refs:
        hits = []
        for fn in NAMES:
            raw = _raw_url(host, owner, repo, ref, fn)
            text = _get(raw)
            if text:
                hits.append((fn, raw, text))
        if hits:
            return ref, hits
    return None, []


def main(inventory, corpus_dir):
    packages, _ = N.load_inventory(inventory)
    have = N.scan_corpus(corpus_dir)

    nuspec = nuspec_repos(corpus_dir)

    targets = {}
    for (eco, name, version), row in sorted(packages.items()):
        key = (eco, name.lower())
        if eco == "vendored" or have.get(key) or key in targets:
            continue
        url, commit = nuspec.get(name.lower(), ("", "")) if eco == "nuget" else ("", "")
        url = resolve_repo(url) if url else ""
        if not REPO.match(url):
            url, commit = (row.get("project_url") or "").strip(), ""
            url = resolve_repo(url)
        if not REPO.match(url):
            url, commit = OVERRIDES.get(key, ""), ""
        if REPO.match(url):
            targets[key] = (name, version, url, commit)

    out_dir = os.path.join(corpus_dir, "licences-fetched")
    os.makedirs(out_dir, exist_ok=True)
    rows, misses = [], []

    for (eco, key), (name, version, url, commit) in sorted(targets.items()):
        ref, hits = fetch_one(name, version, url, commit)
        if not hits:
            misses.append((eco, name, version, url))
            print(f"  MISS {name} {version}", file=sys.stderr)
            continue
        for fn, raw, text in hits:
            safe = re.sub(r"[^A-Za-z0-9._-]", "_", f"{eco}__{key}__{fn}")
            path = os.path.join(out_dir, safe)
            with open(path, "w", encoding="utf8") as f:
                f.write(text)
            rows.append([eco, name, version, ref, fn, raw, os.path.basename(path)])
        pinned = "build commit" if ref == commit else ("tag" if ref != "HEAD" else "default branch")
        print(f"  ok   {name} {version} <- {ref} ({pinned}, {len(hits)} file(s))", file=sys.stderr)

    with open(os.path.join(out_dir, "manifest.csv"), "w", newline="", encoding="utf8") as f:
        w = csv.writer(f)
        w.writerow(["ecosystem", "package", "version", "ref", "filename", "url", "path"])
        w.writerows(rows)

    print(f"\n  fetched {len(rows)} file(s) for "
          f"{len({(r[0], r[1]) for r in rows})} component(s); {len(misses)} miss(es)",
          file=sys.stderr)
    return misses


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
